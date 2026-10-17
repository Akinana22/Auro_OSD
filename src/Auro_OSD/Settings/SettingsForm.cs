using Auro_OSD.Audio;
using Auro_OSD.Config;
using Auro_OSD.Ddc;
using Auro_OSD.Logging;

namespace Auro_OSD.Settings;

public sealed class SettingsHost
{
    public Action? OpenWizard { get; init; }
    public Action? OpenManual { get; init; }
    public Action? OpenAutoSwitch { get; init; }
    public Action? SaveAndApply { get; init; }
    public Action<MonitorInfo>? IdentifyMonitor { get; init; }
}

public sealed class SettingsForm : Form
{
    private const string AutoScanReminder = "为增强使用体验，请在显示器OSD中关闭【自动选择输入源/Auto Scan】";

    private sealed class InputRow
    {
        public Func<uint?> Value = () => null;
        public Func<string?> Name = () => null;
        public Func<string?> Hotkey = () => null;
        public Func<ProjectionMode> Projection = () => ProjectionMode.Clone;
        public TextBox NameBox = new();
        public TextBox HotkeyBox = new();
        public Button ClearButton = new();
        public ComboBox ProjectionBox = new();
    }

    private readonly AppConfig _config;
    private readonly SettingsHost _host;
    private readonly List<InputRow> _rows = new();
    private readonly ComboBox _monitorCombo = new();
    private readonly CheckBox _autoStart = new();
    private readonly List<AudioDeviceInfo> _playbackDevices = new();
    private readonly List<AudioDeviceInfo> _recordingDevices = new();
    private readonly ComboBox _onDpPlayback = new();
    private readonly ComboBox _onDpRecording = new();
    private readonly ComboBox _offDpPlayback = new();
    private readonly ComboBox _offDpRecording = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new();

    public SettingsForm(AppConfig config, SettingsHost host)
    {
        _config = config;
        _host = host;

        _playbackDevices.AddRange(AudioDeviceService.ListPlayback());
        _recordingDevices.AddRange(AudioDeviceService.ListRecording());

        Text = "Auro_OSD 设置";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(620, 664);

        var y = 18;

        AddLabel("目标显示器", y);
        _monitorCombo.SetBounds(160, y - 3, 440, 26);
        _monitorCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _monitorCombo.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar)) e.Handled = true; };
        _monitorCombo.KeyDown += (_, e) => { if (e.Control) e.Handled = e.SuppressKeyPress = true; };
        foreach (var m in DdcMonitor.ListMonitors())
            _monitorCombo.Items.Add(m);
        Controls.Add(_monitorCombo);
        y += 42;

        AddInputRow("DP 名称", "切到 DP 快捷键", () => _config.InputMap.Dp1,
            () => _config.Labels.Dp, () => _config.Hotkeys.Dp, () => _config.Projection.OnDp, y);
        y += 42 * 3;

        AddAudioRow("切到 DP 音频", "切到 DP 麦克风", _onDpPlayback, _onDpRecording,
            _config.Audio.OnDpPlaybackId, _config.Audio.OnDpRecordingId, y);
        y += 42;
        AddAudioRow("切出 DP 音频", "切出 DP 麦克风", _offDpPlayback, _offDpRecording,
            _config.Audio.OffDpPlaybackId, _config.Audio.OffDpRecordingId, y);
        y += 42;

        AddInputRow("HDMI1 名称", "切到 HDMI1 快捷键", () => _config.InputMap.Hdmi1,
            () => _config.Labels.Hdmi1, () => _config.Hotkeys.Hdmi1, () => _config.Projection.OnHdmi1, y);
        y += 42 * 3;

        AddInputRow("HDMI2 名称", "切到 HDMI2 快捷键", () => _config.InputMap.Hdmi2,
            () => _config.Labels.Hdmi2, () => _config.Hotkeys.Hdmi2, () => _config.Projection.OnHdmi2, y);
        y += 42 * 3;

        _autoStart.SetBounds(160, y, 240, 24);
        _autoStart.Text = "开机自启";
        _autoStart.Checked = _config.AutoStart;
        Controls.Add(_autoStart);
        y += 38;

        var reminder = new Label
        {
            Text = AutoScanReminder,
            ForeColor = Color.Firebrick
        };
        reminder.SetBounds(16, y, 588, 24);
        Controls.Add(reminder);
        y += 40;

        var manualButton = new Button { Text = "手动填写输入源" };
        manualButton.SetBounds(16, y, 140, 32);
        manualButton.Click += (_, _) => _host.OpenManual?.Invoke();
        Controls.Add(manualButton);

        var autoButton = new Button { Text = "自动识别输入源" };
        autoButton.SetBounds(166, y, 140, 32);
        autoButton.Click += (_, _) => _host.OpenWizard?.Invoke();
        Controls.Add(autoButton);

        var autoSwitchButton = new Button { Text = "自动切换列表" };
        autoSwitchButton.SetBounds(316, y, 140, 32);
        autoSwitchButton.Click += (_, _) => _host.OpenAutoSwitch?.Invoke();
        Controls.Add(autoSwitchButton);

        _status.SetBounds(462, y + 7, 60, 24);
        _status.ForeColor = Color.DarkGreen;
        Controls.Add(_status);

        var save = new Button { Text = "保存" };
        save.SetBounds(526, y, 90, 32);
        save.Click += Save_Click;
        Controls.Add(save);

        AcceptButton = save;

        _statusTimer.Interval = 1500;
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status.Text = "";
        };

        PreselectMonitor();
        RefreshFromConfig();

        _monitorCombo.SelectedIndexChanged += (_, _) => IdentifySelected();
        IdentifySelected();
    }

    private void AddInputRow(string nameLabel, string hotkeyLabel, Func<uint?> value,
        Func<string?> name, Func<string?> hotkey, Func<ProjectionMode> projection, int y)
    {
        AddLabel(nameLabel, y);
        var nameBox = new TextBox { Text = name() ?? "" };
        nameBox.SetBounds(160, y - 3, 200, 26);
        Controls.Add(nameBox);
        y += 42;

        AddLabel(hotkeyLabel, y);
        var hotkeyBox = new TextBox { ReadOnly = true, Text = hotkey() ?? "" };
        hotkeyBox.SetBounds(160, y - 3, 200, 26);
        hotkeyBox.KeyDown += HotkeyKeyDown;
        Controls.Add(hotkeyBox);

        var clear = new Button { Text = "清除" };
        clear.SetBounds(372, y - 3, 70, 26);
        clear.Click += (_, _) => hotkeyBox.Text = "";
        Controls.Add(clear);
        y += 42;

        AddLabel("切到 " + nameLabel.Replace(" 名称", "") + " 投影", y);
        var projectionBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        projectionBox.SetBounds(160, y - 3, 160, 26);
        foreach (var mode in Enum.GetValues<ProjectionMode>())
            projectionBox.Items.Add(mode);
        projectionBox.SelectedItem = projection();
        Controls.Add(projectionBox);

        _rows.Add(new InputRow
        {
            Value = value,
            Name = name,
            Hotkey = hotkey,
            Projection = projection,
            NameBox = nameBox,
            HotkeyBox = hotkeyBox,
            ClearButton = clear,
            ProjectionBox = projectionBox
        });
    }

    private void AddLabel(string text, int y)
    {
        var label = new Label { Text = text };
        label.SetBounds(16, y, 145, 24);
        Controls.Add(label);
    }

    private void AddAudioRow(string playbackLabel, string recordingLabel, ComboBox playback, ComboBox recording,
        string? playbackId, string? recordingId, int y)
    {
        var l1 = new Label { Text = playbackLabel };
        l1.SetBounds(16, y + 3, 100, 24);
        Controls.Add(l1);
        InitAudioCombo(playback, _playbackDevices, playbackId, 118, y);

        var l2 = new Label { Text = recordingLabel };
        l2.SetBounds(305, y + 3, 115, 24);
        Controls.Add(l2);
        InitAudioCombo(recording, _recordingDevices, recordingId, 422, y);
    }

    private void InitAudioCombo(ComboBox combo, List<AudioDeviceInfo> devices, string? selectedId, int x, int y)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.SetBounds(x, y, 175, 24);
        combo.Items.Clear();
        combo.Items.Add("（不切换）");
        foreach (var device in devices) combo.Items.Add(device.Name);
        combo.SelectedIndex = 0;

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            for (var i = 0; i < devices.Count; i++)
            {
                if (devices[i].Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i + 1;
                    break;
                }
            }
        }

        Controls.Add(combo);
    }

    private static string? SelectedAudioId(ComboBox combo, List<AudioDeviceInfo> devices) =>
        combo.SelectedIndex <= 0 || combo.SelectedIndex - 1 >= devices.Count
            ? null
            : devices[combo.SelectedIndex - 1].Id;

    public void RefreshFromConfig()
    {
        foreach (var row in _rows)
        {
            var bound = row.Value().HasValue;
            row.NameBox.Text = row.Name() ?? "";
            row.HotkeyBox.Text = row.Hotkey() ?? "";
            row.ProjectionBox.SelectedItem = row.Projection();

            row.NameBox.Enabled = bound;
            row.HotkeyBox.Enabled = bound;
            row.ClearButton.Enabled = bound;
            row.ProjectionBox.Enabled = bound;
        }

        _autoStart.Checked = _config.AutoStart;
    }

    private void PreselectMonitor()
    {
        var matches = new List<int>();
        for (var i = 0; i < _monitorCombo.Items.Count; i++)
        {
            if (_monitorCombo.Items[i] is not MonitorInfo info) continue;
            if (DdcMonitor.IdentityMatches(
                    _config.TargetMonitor.DeviceString, _config.TargetMonitor.Serial, _config.TargetMonitor.DeviceId,
                    info.DeviceString, info.Serial, info.DeviceId))
                matches.Add(i);
        }

        if (matches.Count == 1)
        {
            _monitorCombo.SelectedIndex = matches[0];
            var name = (_monitorCombo.Items[matches[0]] as MonitorInfo)?.DisplayName ?? "?";
            AppLogger.Line($"[settings] open target {{{Describe(_config.TargetMonitor)}}} preselect index={matches[0]} name={name}");
            return;
        }

        _monitorCombo.SelectedIndex = -1;
        _monitorCombo.Text = matches.Count == 0
            ? $"（未找到：{Describe(_config.TargetMonitor)}）"
            : "（配置匹配到多台显示器）";
        AppLogger.Line($"[settings] open target {{{Describe(_config.TargetMonitor)}}} preselect none matches={matches.Count}");
    }

    private void IdentifySelected()
    {
        if (_monitorCombo.SelectedItem is MonitorInfo info)
            _host.IdentifyMonitor?.Invoke(info);
    }

    private void HotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (sender is not TextBox box) return;

        var key = e.KeyCode;
        if (key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");

        string name = key >= Keys.D0 && key <= Keys.D9 ? ((char)key).ToString() : key.ToString();
        parts.Add(name);

        box.Text = string.Join("+", parts);
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        ApplyToConfig();
        _host.SaveAndApply?.Invoke();
        ShowSaved();
    }

    private void ApplyToConfig()
    {
        var before = Describe(_config.TargetMonitor);
        if (_monitorCombo.SelectedItem is MonitorInfo info)
        {
            _config.TargetMonitor.DeviceString = NullIfEmpty(info.DeviceString);
            _config.TargetMonitor.Serial = NullIfEmpty(info.Serial);
            _config.TargetMonitor.DeviceId = NullIfEmpty(DdcMonitor.DeviceIdPrefix(info.DeviceId));
        }

        AppLogger.Line($"[settings] save target before {{{before}}} after {{{Describe(_config.TargetMonitor)}}} selected={(_monitorCombo.SelectedItem as MonitorInfo)?.DisplayName ?? "none"}");

        ApplyRow(_rows[0], v => _config.Labels.Dp = v, v => _config.Hotkeys.Dp = v, m => _config.Projection.OnDp = m);
        ApplyRow(_rows[1], v => _config.Labels.Hdmi1 = v, v => _config.Hotkeys.Hdmi1 = v, m => _config.Projection.OnHdmi1 = m);
        ApplyRow(_rows[2], v => _config.Labels.Hdmi2 = v, v => _config.Hotkeys.Hdmi2 = v, m => _config.Projection.OnHdmi2 = m);

        _config.Audio.OnDpPlaybackId = SelectedAudioId(_onDpPlayback, _playbackDevices);
        _config.Audio.OnDpRecordingId = SelectedAudioId(_onDpRecording, _recordingDevices);
        _config.Audio.OffDpPlaybackId = SelectedAudioId(_offDpPlayback, _playbackDevices);
        _config.Audio.OffDpRecordingId = SelectedAudioId(_offDpRecording, _recordingDevices);

        _config.AutoStart = _autoStart.Checked;
    }

    private static void ApplyRow(InputRow row, Action<string?> setName, Action<string?> setHotkey, Action<ProjectionMode> setProjection)
    {
        // Unbound inputs keep their stored name/hotkey (only the binding is cleared elsewhere).
        if (!row.Value().HasValue)
            return;

        setName(NullIfEmpty(row.NameBox.Text));
        setHotkey(NullIfEmpty(row.HotkeyBox.Text));
        if (row.ProjectionBox.SelectedItem is ProjectionMode mode)
            setProjection(mode);
    }

    private void ShowSaved()
    {
        _status.Text = "已保存";
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Describe(TargetMonitorConfig t) =>
        $"DeviceString={t.DeviceString ?? "-"}, Serial={t.Serial ?? "-"}, DeviceId={t.DeviceId ?? "-"}";
}
