using Auro_OSD.Config;
using Auro_OSD.Ddc;
using Auro_OSD.Hotkeys;
using Auro_OSD.Logging;
using Auro_OSD.Osd;
using Auro_OSD.Settings;
using Auro_OSD.Switching;
using Auro_OSD.Wizard;
using System.Diagnostics;

namespace Auro_OSD.Tray;

internal enum SwitchKind
{
    Dp,
    Hdmi1,
    Hdmi2
}

public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _notify;
    private readonly OsdForm _osd;
    private readonly HotkeyManager _hotkeys = new();
    private readonly object _switchLock = new();
    private AppConfig _config;
    private InputSwitcher _switcher;
    private AutoSwitchService _autoSwitch;
    private bool _busy;
    private SettingsForm? _settingsForm;
    private InputWizard? _wizardForm;
    private ManualInputForm? _manualForm;
    private AutoSwitchForm? _autoSwitchForm;

    public TrayContext()
    {
        _config = ConfigStore.Load();
        AppLogger.Enabled = _config.Log.Enabled;
        _switcher = new InputSwitcher(_config);

        _osd = new OsdForm();
        _ = _osd.Handle;

        _autoSwitch = new AutoSwitchService(_config, PerformAutoSwitch);
        _autoSwitch.CurrentInput = _switcher.ReadInput();

        var icon = AppIcon.Get();
        _notify = new NotifyIcon
        {
            Icon = icon,
            Text = "Auro_OSD",
            Visible = true
        };
        _notify.DoubleClick += (_, _) => OpenSettings();

        RebuildMenu();
        RegisterHotkeys();
        _autoSwitch.Start();
    }

    private void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        AddInputItem(menu, "DP", _config.Labels.DpDisplay, _config.InputMap.Dp1, _config.Labels.Dp, SwitchKind.Dp);
        AddInputItem(menu, "HDMI1", _config.Labels.Hdmi1Display, _config.InputMap.Hdmi1, _config.Labels.Hdmi1, SwitchKind.Hdmi1);
        AddInputItem(menu, "HDMI2", _config.Labels.Hdmi2Display, _config.InputMap.Hdmi2, _config.Labels.Hdmi2, SwitchKind.Hdmi2);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("手动填写输入源", null, (_, _) => OpenManual());
        menu.Items.Add("自动识别输入源", null, (_, _) => OpenWizard());
        menu.Items.Add("自动切换列表", null, (_, _) => OpenAutoSwitch());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());

        var logItem = new ToolStripMenuItem("启用日志") { Checked = _config.Log.Enabled };
        logItem.Click += (_, _) =>
        {
            _config.Log.Enabled = !_config.Log.Enabled;
            AppLogger.Enabled = _config.Log.Enabled;
            logItem.Checked = _config.Log.Enabled;
            ConfigStore.Save(_config);
            if (_config.Log.Enabled)
                AppLogger.Line($"logging enabled -> {AppLogger.FilePath}");
        };
        menu.Items.Add(logItem);
        menu.Items.Add("打开数据目录", null, (_, _) => OpenDataDirectory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        _notify.ContextMenuStrip = menu;
    }

    private void AddInputItem(ContextMenuStrip menu, string caption, string display, uint? value, string? label, SwitchKind kind)
    {
        var item = menu.Items.Add($"{caption}（{display}）", null, (_, _) => RunSwitch(kind));
        item.Enabled = CanSwitch(value, label);
    }

    private static bool CanSwitch(uint? value, string? label) =>
        value.HasValue && !string.IsNullOrWhiteSpace(label);

    private void RegisterHotkeys()
    {
        _hotkeys.Clear();
        RegisterHotkey(_config.Hotkeys.Dp, _config.InputMap.Dp1, _config.Labels.Dp, SwitchKind.Dp);
        RegisterHotkey(_config.Hotkeys.Hdmi1, _config.InputMap.Hdmi1, _config.Labels.Hdmi1, SwitchKind.Hdmi1);
        RegisterHotkey(_config.Hotkeys.Hdmi2, _config.InputMap.Hdmi2, _config.Labels.Hdmi2, SwitchKind.Hdmi2);
    }

    private void RegisterHotkey(string? hotkey, uint? value, string? label, SwitchKind kind)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return;
        if (!CanSwitch(value, label)) return;
        _hotkeys.Register(hotkey!, () => RunSwitch(kind));
    }

    private void RunSwitch(SwitchKind kind)
    {
        lock (_switchLock)
        {
            if (_busy) return;
            _busy = true;
        }

        Task.Run(() =>
        {
            try
            {
                switch (kind)
                {
                    case SwitchKind.Dp:
                        if (_switcher.SwitchToDp() && _config.InputMap.Dp1 is uint dp)
                        {
                            _autoSwitch.CurrentInput = dp;
                            if (_autoSwitch.HasEnabledEntry)
                                ShowPauseDialog();
                        }
                        break;
                    case SwitchKind.Hdmi1 when _config.InputMap.Hdmi1 is uint hdmi1:
                        if (_switcher.SwitchToHdmi(hdmi1, _config.Projection.OnHdmi1))
                            _autoSwitch.CurrentInput = hdmi1;
                        break;
                    case SwitchKind.Hdmi2 when _config.InputMap.Hdmi2 is uint hdmi2:
                        if (_switcher.SwitchToHdmi(hdmi2, _config.Projection.OnHdmi2))
                            _autoSwitch.CurrentInput = hdmi2;
                        break;
                }
            }
            finally
            {
                lock (_switchLock) _busy = false;
            }
        });
    }

    private bool PerformAutoSwitch(uint value, bool isDp)
    {
        lock (_switchLock)
        {
            if (_busy) return false;
            _busy = true;
        }

        try
        {
            if (isDp)
            {
                _switcher.SwitchToDp();
            }
            else
            {
                var mode = value == _config.InputMap.Hdmi1 ? _config.Projection.OnHdmi1 : _config.Projection.OnHdmi2;
                _switcher.SwitchToHdmi(value, mode);
            }

            AppLogger.Line($"switch done value={value} isDp={(isDp ? 1 : 0)}");
            return true;
        }
        finally
        {
            lock (_switchLock) _busy = false;
        }
    }

    private void ShowPauseDialog()
    {
        var wasSuspended = _autoSwitch.Suspended;
        _autoSwitch.Suspended = true;

        try
        {
            _osd.Invoke((Action)(() =>
            {
                using var form = new AutoSwitchPauseForm();
                if (form.ShowDialog() == DialogResult.OK && form.PauseDuration is TimeSpan duration)
                    _autoSwitch.PausedUntil = DateTime.UtcNow + duration;
            }));
        }
        catch
        {
            // ignore
        }
        finally
        {
            _autoSwitch.Suspended = wasSuspended;
        }
    }

    private void IdentifyMonitor(MonitorInfo info)
    {
        try
        {
            var screen = Screen.AllScreens.FirstOrDefault(s =>
                             s.DeviceName.Equals(info.DeviceName, StringComparison.OrdinalIgnoreCase))
                         ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
            _osd.ShowMessage($"目标显示器\n{info.DisplayName}", screen, _config.Osd.DurationMs);
        }
        catch
        {
            // ignore
        }
    }

    private void SaveAndApply()
    {
        ConfigStore.Save(_config);
        ApplyConfig();
        _settingsForm?.RefreshFromConfig();
        _autoSwitch.Refresh();
    }

    private void RunSuspended(Action action)
    {
        var wasSuspended = _autoSwitch.Suspended;
        _autoSwitch.Suspended = true;
        try
        {
            action();
        }
        finally
        {
            _autoSwitch.Suspended = wasSuspended;
        }
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            BringToFront(_settingsForm);
            return;
        }

        var host = new SettingsHost
        {
            OpenWizard = OpenWizard,
            OpenManual = OpenManual,
            OpenAutoSwitch = OpenAutoSwitch,
            SaveAndApply = SaveAndApply,
            IdentifyMonitor = IdentifyMonitor
        };

        _settingsForm = new SettingsForm(_config, host);
        RunSuspended(() => _settingsForm.ShowDialog());
        _settingsForm = null;
        _autoSwitch.CurrentInput = _switcher.ReadInput();
    }

    private void OpenWizard()
    {
        if (_wizardForm is { IsDisposed: false })
        {
            BringToFront(_wizardForm);
            return;
        }

        var owner = _settingsForm is { IsDisposed: false, Visible: true } ? _settingsForm : null;
        _wizardForm = new InputWizard(_config);
        var result = DialogResult.Cancel;
        RunSuspended(() => result = owner != null ? _wizardForm.ShowDialog(owner) : _wizardForm.ShowDialog());
        _wizardForm = null;

        if (result == DialogResult.OK)
            SaveAndApply();

        _autoSwitch.CurrentInput = _switcher.ReadInput();
    }

    private void OpenManual()
    {
        if (_manualForm is { IsDisposed: false })
        {
            BringToFront(_manualForm);
            return;
        }

        var owner = _settingsForm is { IsDisposed: false, Visible: true } ? _settingsForm : null;
        _manualForm = new ManualInputForm(_config, SaveAndApply);
        RunSuspended(() => { if (owner != null) _manualForm.ShowDialog(owner); else _manualForm.ShowDialog(); });
        _manualForm = null;
        _autoSwitch.CurrentInput = _switcher.ReadInput();
    }

    private void OpenAutoSwitch()
    {
        if (_autoSwitchForm is { IsDisposed: false })
        {
            BringToFront(_autoSwitchForm);
            return;
        }

        var owner = _settingsForm is { IsDisposed: false, Visible: true } ? _settingsForm : null;
        _autoSwitchForm = new AutoSwitchForm(_config, SaveAndApply, _autoSwitch.CaptureFrames);
        RunSuspended(() => { if (owner != null) _autoSwitchForm.ShowDialog(owner); else _autoSwitchForm.ShowDialog(); });
        _autoSwitchForm = null;
        _autoSwitch.CurrentInput = _switcher.ReadInput();
    }

    private static void BringToFront(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;
        form.Activate();
        form.BringToFront();
    }

    private void ApplyConfig()
    {
        _switcher = new InputSwitcher(_config);
        RebuildMenu();
        RegisterHotkeys();
        AutoStartManager.Apply(_config.AutoStart);
    }

    private static void OpenDataDirectory()
    {
        var dir = ConfigStore.DirectoryPath;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        try
        {
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private void ExitApp()
    {
        _autoSwitch.Dispose();
        _notify.Visible = false;
        _notify.Dispose();
        _hotkeys.Dispose();
        _osd.Dispose();
        ExitThread();
    }
}
