using Auro_OSD.Capture;

namespace Auro_OSD.Wizard;

public sealed class SnapshotPickerForm : Form
{
    private readonly IReadOnlyList<CapturedFrame> _frames;
    private readonly HashSet<int> _selected = new();
    private readonly Label _count = new();

    public List<int> SelectedIndices => _selected.OrderBy(i => i).ToList();

    public SnapshotPickerForm(IReadOnlyList<CapturedFrame> frames)
    {
        _frames = frames;

        Text = "选择无信号参考帧";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(824, 486);

        var hint = new Label
        {
            Text = "勾选采集卡显示“无信号”画面的帧（可多选）；单击图片可放大查看。确定后仅保留所勾选的帧用于相似度分析。",
            ForeColor = Color.DimGray
        };
        hint.SetBounds(12, 8, 800, 24);
        Controls.Add(hint);

        var flow = new FlowLayoutPanel
        {
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        flow.SetBounds(12, 36, 800, 396);
        Controls.Add(flow);

        for (var i = 0; i < frames.Count; i++)
        {
            var index = i;

            var cell = new Panel { Size = new Size(158, 150), Margin = new Padding(3) };

            var picture = new PictureBox
            {
                Size = new Size(150, 112),
                Location = new Point(0, 0),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Image = LoadImage(frames[i].Path)
            };
            picture.Click += (_, _) => ShowLarge(index);
            cell.Controls.Add(picture);

            var check = new CheckBox
            {
                Location = new Point(4, 4),
                Size = new Size(22, 22),
                BackColor = Color.White,
                Text = ""
            };
            check.CheckedChanged += (_, _) =>
            {
                if (check.Checked)
                {
                    _selected.Add(index);
                    picture.BorderStyle = BorderStyle.Fixed3D;
                }
                else
                {
                    _selected.Remove(index);
                    picture.BorderStyle = BorderStyle.FixedSingle;
                }
                UpdateCount();
            };
            cell.Controls.Add(check);
            check.BringToFront();

            var caption = new Label
            {
                Text = $"{index + 1}",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 118),
                Size = new Size(150, 22)
            };
            cell.Controls.Add(caption);

            flow.Controls.Add(cell);
        }

        _count.SetBounds(12, 444, 240, 24);
        _count.ForeColor = Color.DarkGreen;
        Controls.Add(_count);
        UpdateCount();

        var ok = new Button { Text = "确定" };
        ok.SetBounds(614, 440, 90, 32);
        ok.Click += (_, _) =>
        {
            if (_selected.Count == 0)
            {
                MessageBox.Show(this, "请至少勾选一帧。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(ok);

        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        cancel.SetBounds(714, 440, 90, 32);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void UpdateCount() => _count.Text = $"已选 {_selected.Count} 帧";

    private void ShowLarge(int index)
    {
        using var form = new Form
        {
            Text = $"参考帧 {index + 1}",
            Icon = AppIcon.Get(),
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(900, 620),
            ShowInTaskbar = false
        };

        using var image = LoadImage(_frames[index].Path);
        var picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = image
        };
        form.Controls.Add(picture);
        form.ShowDialog(this);
    }

    private static Image LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
