using Auro_OSD.Config;
using Auro_OSD.Ddc;
using Auro_OSD.Display;
using Auro_OSD.Osd;

namespace Auro_OSD.Wizard;

public sealed class InputWizard : Form
{
    private const string AutoScanReminder = "识别前请确保对应输入源有视频信号输出。";

    private enum Step
    {
        Dp,
        Hdmi1,
        Hdmi2
    }

    private enum Phase
    {
        Ready,
        Detecting,
        Detected
    }

    private readonly AppConfig _config;
    private readonly DdcMonitor _ddc;
    private readonly OsdForm _osd = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _startTimer = new();

    private readonly Label _stepLabel = new();
    private readonly Label _warning = new();
    private readonly Label _instruction = new();
    private readonly Label _status = new();
    private readonly Button _back = new();
    private readonly Button _primary = new();
    private readonly Button _secondary = new();

    private Step _step = Step.Dp;
    private Phase _phase = Phase.Ready;
    private bool _attempted;
    private DateTime _deadline;
    private DateTime _nextReadAt;
    private bool _hasLast;
    private uint _lastValue;
    private bool _projectionApplied;

    private uint? _workDp;
    private uint? _workHdmi1;
    private uint? _workHdmi2;
    private uint? _originalInput;

    public InputWizard(AppConfig config)
    {
        _config = config;
        _ddc = new DdcMonitor(
            config.TargetMonitor.DeviceString,
            config.TargetMonitor.Serial,
            config.TargetMonitor.DeviceId);

        _workDp = config.InputMap.Dp1;
        _workHdmi1 = config.InputMap.Hdmi1;
        _workHdmi2 = config.InputMap.Hdmi2;

        _originalInput = _ddc.GetInputSource();

        Text = "自动识别输入源";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(560, 320);

        _stepLabel.SetBounds(16, 16, 520, 26);
        _stepLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        Controls.Add(_stepLabel);

        _warning.Text = AutoScanReminder;
        _warning.ForeColor = Color.Firebrick;
        _warning.SetBounds(16, 48, 528, 40);
        Controls.Add(_warning);

        _instruction.SetBounds(16, 92, 520, 50);
        Controls.Add(_instruction);

        _status.SetBounds(16, 150, 520, 26);
        _status.ForeColor = Color.DarkGreen;
        Controls.Add(_status);

        _back.SetBounds(16, 268, 90, 34);
        _back.Text = "上一步";
        _back.Click += (_, _) => Back();
        Controls.Add(_back);

        _primary.SetBounds(230, 268, 120, 34);
        _primary.Click += (_, _) => Primary_Click();
        Controls.Add(_primary);

        _secondary.SetBounds(360, 268, 120, 34);
        _secondary.Click += (_, _) => Secondary_Click();
        Controls.Add(_secondary);

        _timer.Interval = 750;
        _timer.Tick += Timer_Tick;

        _startTimer.Interval = Math.Max(300, _config.Osd.DurationMs);
        _startTimer.Tick += StartTimer_Tick;

        SetStep(Step.Dp);
        SetButtonsEnabled(false);

        var info = FindTargetInfo();
        var screen = ScreenUtil.FindTargetScreen(_config);
        _osd.ShowMessage($"目标显示器\n{info?.DisplayName ?? _config.TargetMonitor.DeviceString ?? "未配置"}", screen, _config.Osd.DurationMs);

        _startTimer.Start();
    }

    private MonitorInfo? FindTargetInfo()
    {
        var list = DdcMonitor.ListMonitors();
        var matches = list.Where(m => DdcMonitor.IdentityMatches(
            _config.TargetMonitor.DeviceString, _config.TargetMonitor.Serial, _config.TargetMonitor.DeviceId,
            m.DeviceString, m.Serial, m.DeviceId)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private void StartTimer_Tick(object? sender, EventArgs e)
    {
        _startTimer.Stop();

        try
        {
            DisplayTopology.Apply(ProjectionMode.Clone);
            _projectionApplied = true;
        }
        catch
        {
            // ignore
        }

        SetButtonsEnabled(true);
    }

    private uint? CurrentWorkValue() => _step switch
    {
        Step.Dp => _workDp,
        Step.Hdmi1 => _workHdmi1,
        _ => _workHdmi2
    };

    private void SetWorkValue(uint? value)
    {
        switch (_step)
        {
            case Step.Dp: _workDp = value; break;
            case Step.Hdmi1: _workHdmi1 = value; break;
            case Step.Hdmi2: _workHdmi2 = value; break;
        }
    }

    private void SetStep(Step step)
    {
        _step = step;
        _phase = Phase.Ready;
        _attempted = false;
        _hasLast = false;
        _timer.Stop();
        _status.ForeColor = Color.DarkGreen;
        _status.Text = "";

        switch (step)
        {
            case Step.Dp:
                _stepLabel.Text = "第 1 / 3 步：DP";
                _instruction.Text = "请用显示器 OSD 把信号源切到 DP。";
                break;
            case Step.Hdmi1:
                _stepLabel.Text = "第 2 / 3 步：HDMI1";
                _instruction.Text = "请用显示器 OSD 把信号源切到 HDMI1。";
                break;
            case Step.Hdmi2:
                _stepLabel.Text = "第 3 / 3 步：HDMI2";
                _instruction.Text = "请用显示器 OSD 把信号源切到 HDMI2。";
                break;
        }

        UpdateButtons();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _back.Enabled = enabled;
        _primary.Enabled = enabled;
        _secondary.Enabled = enabled;
    }

    private void UpdateButtons()
    {
        var identified = CurrentWorkValue().HasValue || _attempted;
        var detecting = _phase == Phase.Detecting;

        _primary.Text = identified ? "重试" : "识别";
        _primary.Enabled = !detecting;

        _secondary.Text = _step == Step.Hdmi2
            ? (identified ? "完成" : "跳过")
            : (identified ? "下一步" : "跳过");
        _secondary.Enabled = !detecting;

        _back.Visible = _step != Step.Dp;
        _back.Enabled = !detecting;
    }

    private void Primary_Click()
    {
        _phase = Phase.Detecting;
        _attempted = true;
        _hasLast = false;
        _lastValue = 0;
        _nextReadAt = DateTime.UtcNow.AddMilliseconds(400);
        _deadline = DateTime.UtcNow.AddSeconds(12);
        _status.ForeColor = Color.DarkOrange;
        _status.Text = "识别中…";
        UpdateButtons();
        _timer.Start();
    }

    private void Secondary_Click()
    {
        switch (_step)
        {
            case Step.Dp:
                SetStep(Step.Hdmi1);
                break;
            case Step.Hdmi1:
                SetStep(Step.Hdmi2);
                break;
            case Step.Hdmi2:
                Complete();
                break;
        }
    }

    private void Back()
    {
        switch (_step)
        {
            case Step.Hdmi1:
                SetStep(Step.Dp);
                break;
            case Step.Hdmi2:
                SetStep(Step.Hdmi1);
                break;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_phase != Phase.Detecting) return;
        if (DateTime.UtcNow < _nextReadAt) return;

        var value = _ddc.GetInputSource();

        if (DateTime.UtcNow > _deadline)
        {
            _timer.Stop();
            _phase = Phase.Ready;
            _status.ForeColor = Color.Firebrick;
            _status.Text = "读取失败（可能无信号），可重试或跳过。";
            UpdateButtons();
            return;
        }

        if (value is null or 0)
        {
            _hasLast = false;
            return;
        }

        if (_hasLast && value.Value == _lastValue)
        {
            _timer.Stop();
            _phase = Phase.Detected;
            SetWorkValue(value.Value);
            _status.ForeColor = Color.DarkGreen;
            _status.Text = $"识别到 {value.Value}（0x{value.Value:X2}）";
            UpdateButtons();
            return;
        }

        _hasLast = true;
        _lastValue = value.Value;
        _status.Text = $"读取中… {value.Value}";
    }

    private void Complete()
    {
        _config.InputMap.Dp1 = _workDp;
        _config.InputMap.Hdmi1 = _workHdmi1;
        _config.InputMap.Hdmi2 = _workHdmi2;

        if (_workDp.HasValue || _workHdmi1.HasValue || _workHdmi2.HasValue)
            _config.InputMap.LearnedByWizard = true;

        if (HasDuplicateValues(out var message))
            MessageBox.Show(this, message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        DialogResult = DialogResult.OK;
        Close();
    }

    private bool HasDuplicateValues(out string message)
    {
        var values = new[] { _workDp, _workHdmi1, _workHdmi2 }
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (values.Count != values.Distinct().Count())
        {
            message = "警告：识别出的输入源数值存在重复，切换可能不准确，请检查后重新识别。";
            return true;
        }

        message = "";
        return false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _startTimer.Stop();
        _osd.HideOsd();

        RestoreOriginalInput();

        if (_projectionApplied)
        {
            try
            {
                DisplayTopology.Apply(_config.Projection.OnDp);
            }
            catch
            {
                // ignore
            }
        }

        base.OnFormClosing(e);
    }

    private void RestoreOriginalInput()
    {
        if (_originalInput is not uint value) return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (_ddc.SetInputSource(value)) return;
            Thread.Sleep(200);
        }
    }
}
