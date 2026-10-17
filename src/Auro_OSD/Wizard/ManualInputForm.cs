using Auro_OSD.Config;
using Auro_OSD.Ddc;

namespace Auro_OSD.Wizard;

public sealed class ManualInputForm : Form
{
    private readonly AppConfig _config;
    private readonly Action _saveAndApply;
    private readonly DdcMonitor _ddc;
    private readonly Label _current = new();
    private readonly TextBox _dp = new();
    private readonly TextBox _hdmi1 = new();
    private readonly TextBox _hdmi2 = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new();

    public ManualInputForm(AppConfig config, Action saveAndApply)
    {
        _config = config;
        _saveAndApply = saveAndApply;
        _ddc = new DdcMonitor(
            config.TargetMonitor.DeviceString,
            config.TargetMonitor.Serial,
            config.TargetMonitor.DeviceId);

        Text = "手动填写输入源";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(470, 340);

        var header = new Label
        {
            Text = "识别前请确保对应输入源有视频信号输出。",
            ForeColor = Color.Firebrick
        };
        header.SetBounds(16, 16, 438, 40);
        Controls.Add(header);

        var currentLabel = new Label { Text = "当前 VCP 0x60：" };
        currentLabel.SetBounds(16, 66, 110, 24);
        Controls.Add(currentLabel);

        _current.SetBounds(130, 66, 190, 24);
        Controls.Add(_current);

        var refresh = new Button { Text = "刷新" };
        refresh.SetBounds(330, 62, 70, 26);
        refresh.Click += (_, _) => RefreshCurrent();
        Controls.Add(refresh);

        AddRow("DP", _dp, 106);
        AddRow("HDMI1", _hdmi1, 146);
        AddRow("HDMI2", _hdmi2, 186);

        var reset = new Button { Text = "重置" };
        reset.SetBounds(16, 250, 90, 32);
        reset.Click += Reset_Click;
        Controls.Add(reset);

        _status.SetBounds(210, 256, 80, 24);
        _status.ForeColor = Color.DarkGreen;
        Controls.Add(_status);

        var save = new Button { Text = "保存" };
        save.SetBounds(364, 250, 90, 32);
        save.Click += Save_Click;
        Controls.Add(save);
        AcceptButton = save;

        _statusTimer.Interval = 1500;
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status.Text = "";
        };

        LoadFromConfig();
        RefreshCurrent();
    }

    private void AddRow(string label, TextBox box, int y)
    {
        var caption = new Label { Text = label };
        caption.SetBounds(16, y + 3, 90, 24);
        Controls.Add(caption);

        box.SetBounds(110, y, 120, 26);
        Controls.Add(box);
    }

    private void LoadFromConfig()
    {
        _dp.Text = _config.InputMap.Dp1?.ToString() ?? "";
        _hdmi1.Text = _config.InputMap.Hdmi1?.ToString() ?? "";
        _hdmi2.Text = _config.InputMap.Hdmi2?.ToString() ?? "";
    }

    private void RefreshCurrent()
    {
        var value = _ddc.GetInputSource();
        _current.Text = value.HasValue ? $"{value.Value} (0x{value.Value:X2})" : "读取失败";
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (!TryParse(_dp.Text, out var dp) ||
            !TryParse(_hdmi1.Text, out var hdmi1) ||
            !TryParse(_hdmi2.Text, out var hdmi2))
        {
            MessageBox.Show(this, "请输入有效的数字，或留空表示未绑定。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _config.InputMap.Dp1 = dp;
        _config.InputMap.Hdmi1 = hdmi1;
        _config.InputMap.Hdmi2 = hdmi2;
        _config.InputMap.LearnedByWizard = true;
        _saveAndApply();
        ShowSaved();
    }

    private void Reset_Click(object? sender, EventArgs e)
    {
        _config.InputMap.Dp1 = null;
        _config.InputMap.Hdmi1 = null;
        _config.InputMap.Hdmi2 = null;

        _dp.Text = "";
        _hdmi1.Text = "";
        _hdmi2.Text = "";

        _saveAndApply();
        ShowSaved();
    }

    private static bool TryParse(string text, out uint? value)
    {
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return true;
        if (uint.TryParse(trimmed, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private void ShowSaved()
    {
        _status.Text = "已保存";
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
