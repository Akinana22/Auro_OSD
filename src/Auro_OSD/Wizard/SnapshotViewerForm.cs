using Auro_OSD.Capture;

namespace Auro_OSD.Wizard;

public sealed class SnapshotViewerForm : Form
{
    private readonly List<string> _frames;
    private readonly PictureBox _picture = new();
    private readonly Label _counter = new();
    private int _index;

    public SnapshotViewerForm(string primaryPng)
    {
        _frames = CaptureSignalMonitor.KeptFramePaths(primaryPng);
        if (_frames.Count == 0 && File.Exists(primaryPng))
            _frames.Add(primaryPng);

        Text = "查看快照";
        Icon = AppIcon.Get();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(920, 640);
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        _picture.SetBounds(12, 12, 880, 540);
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        _picture.BackColor = Color.Black;
        _picture.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_picture);

        var prev = new Button { Text = "上一张" };
        prev.SetBounds(12, 562, 90, 32);
        prev.Click += (_, _) => Navigate(-1);
        Controls.Add(prev);

        var next = new Button { Text = "下一张" };
        next.SetBounds(110, 562, 90, 32);
        next.Click += (_, _) => Navigate(+1);
        Controls.Add(next);

        _counter.SetBounds(212, 568, 340, 24);
        Controls.Add(_counter);

        var close = new Button { Text = "关闭", DialogResult = DialogResult.OK };
        close.SetBounds(802, 562, 90, 32);
        Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;

        ShowCurrent();
    }

    private void Navigate(int delta)
    {
        if (_frames.Count == 0) return;
        _index = ((_index + delta) % _frames.Count + _frames.Count) % _frames.Count;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        _picture.Image?.Dispose();
        _picture.Image = _frames.Count == 0 ? null : LoadImage(_frames[_index]);
        _counter.Text = _frames.Count == 0
            ? "无参考帧"
            : $"{_index + 1} / {_frames.Count}　{Path.GetFileName(_frames[_index])}";
    }

    private static Image LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
