namespace Auro_OSD.Wizard;

public sealed class AutoSwitchPauseForm : Form
{
    public TimeSpan? PauseDuration { get; private set; }

    public AutoSwitchPauseForm()
    {
        Text = "暂停自动切换";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(400, 180);

        var label = new Label
        {
            Text = "是否设置定时器？在定时器结束前暂停自动切换。"
        };
        label.SetBounds(16, 20, 368, 40);
        Controls.Add(label);

        var presets = new (string Text, int Minutes)[]
        {
            ("1 分钟", 1),
            ("5 分钟", 5),
            ("10 分钟", 10),
            ("30 分钟", 30)
        };

        var x = 16;
        foreach (var preset in presets)
        {
            var minutes = preset.Minutes;
            var button = new Button { Text = preset.Text };
            button.SetBounds(x, 76, 80, 32);
            button.Click += (_, _) => Choose(TimeSpan.FromMinutes(minutes));
            Controls.Add(button);
            x += 92;
        }

        var none = new Button { Text = "不设置" };
        none.SetBounds(16, 126, 88, 32);
        none.Click += (_, _) => Choose(null);
        Controls.Add(none);
    }

    private void Choose(TimeSpan? duration)
    {
        PauseDuration = duration;
        DialogResult = DialogResult.OK;
        Close();
    }
}
