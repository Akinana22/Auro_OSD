using System.Drawing;

namespace Auro_OSD.Osd;

public sealed class OsdForm : Form
{
    private readonly System.Windows.Forms.Timer _hideTimer = new();
    private readonly Label _label;

    protected override bool ShowWithoutActivation => true;

    public OsdForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(20, 20, 20);
        Opacity = 0.82;
        Size = new Size(560, 130);
        Padding = new Padding(1);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };
        Controls.Add(_label);

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    private void PositionOn(Screen screen)
    {
        var bounds = screen.Bounds;
        Location = new Point(
            bounds.Left + (bounds.Width - Width) / 2,
            bounds.Top + bounds.Height - Height - 140);
    }

    public void ShowMessage(string text, Screen screen, int durationMs)
    {
        _label.Text = text;
        PositionOn(screen);
        Show();
        BringToFront();

        _hideTimer.Stop();
        _hideTimer.Interval = Math.Max(300, durationMs);
        _hideTimer.Start();
    }

    public void HideOsd()
    {
        _hideTimer.Stop();
        Hide();
    }
}
