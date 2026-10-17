using Auro_OSD.Capture;
using Auro_OSD.Config;

namespace Auro_OSD.Wizard;

public sealed class AutoSwitchForm : Form
{
    private sealed class RowModel
    {
        public string Input = "";
        public string DisplayName = "";
        public uint? Value;
        public bool Enabled;
        public string? CaptureDevice;
        public string? NoSignalSnapshot;
        public double SimilarityThreshold = 0.95;
        public bool SnapshotBusy;

        public CheckBox Check = new();
        public Label Title = new();
        public Label ValueLabel = new();
        public ComboBox Device = new();
        public Button SnapshotButton = new();
        public Button ViewButton = new();
        public Label SnapshotStatus = new();
        public Button ClearButton = new();
        public Button Up = new();
        public Button Down = new();
    }

    private readonly AppConfig _config;
    private readonly Action _saveAndApply;
    private readonly Func<string, string, List<CapturedFrame>?> _captureFrames;
    private readonly List<CaptureDeviceInfo> _devices;
    private readonly List<RowModel> _rows = new();
    private readonly Panel _panel = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new();

    public AutoSwitchForm(AppConfig config, Action saveAndApply, Func<string, string, List<CapturedFrame>?> captureFrames)
    {
        _config = config;
        _saveAndApply = saveAndApply;
        _captureFrames = captureFrames;
        _devices = CaptureSignalMonitor.Enumerate();

        Text = "自动切换列表";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(900, 300);

        var header = new Label
        {
            Text = "勾选并排序自动切换：目标屏在 DP 时，检测到某个已勾选采集设备有信号，就自动切到对应 HDMI；信号丢失则切到下一个活跃项，无则切回 DP。\r\n" +
                   "请在对应 HDMI 输入源“无视频信号”时点击【创建快照】，在弹出的窗口中勾选采集卡显示“无信号”画面的帧。",
            ForeColor = Color.DimGray
        };
        header.SetBounds(16, 10, 868, 48);
        Controls.Add(header);

        _panel.SetBounds(8, 62, 884, 170);
        Controls.Add(_panel);

        _status.SetBounds(700, 246, 90, 24);
        _status.ForeColor = Color.DarkGreen;
        Controls.Add(_status);

        var save = new Button { Text = "保存" };
        save.SetBounds(796, 240, 90, 32);
        save.Click += (_, _) => Save();
        Controls.Add(save);
        AcceptButton = save;

        _statusTimer.Interval = 1500;
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status.Text = "";
        };

        LoadRows();
        Rebuild();
    }

    private void LoadRows()
    {
        var entries = _config.AutoSwitch.Order;

        foreach (var input in new[] { "Hdmi1", "Hdmi2" })
        {
            var entry = entries.FirstOrDefault(e => e.Input == input);
            _rows.Add(new RowModel
            {
                Input = input,
                DisplayName = input == "Hdmi1"
                    ? $"HDMI1（{_config.Labels.Hdmi1Display}）"
                    : $"HDMI2（{_config.Labels.Hdmi2Display}）",
                Value = input == "Hdmi1" ? _config.InputMap.Hdmi1 : _config.InputMap.Hdmi2,
                Enabled = entry?.Enabled ?? false,
                CaptureDevice = entry?.CaptureDevice,
                NoSignalSnapshot = entry?.NoSignalSnapshot,
                SimilarityThreshold = entry?.SimilarityThreshold ?? 0.95
            });
        }
    }

    private void Rebuild()
    {
        _panel.Controls.Clear();
        var y = 4;

        foreach (var row in _rows)
        {
            row.Check.Text = "";
            row.Check.SetBounds(8, y + 2, 20, 22);
            row.Check.Checked = row.Enabled;

            row.Title.Text = row.DisplayName;
            row.Title.SetBounds(34, y + 2, 140, 22);

            row.ValueLabel.Text = row.Value.HasValue ? $"码值={row.Value}" : "未绑定";
            row.ValueLabel.ForeColor = row.Value.HasValue ? Color.Black : Color.Firebrick;
            row.ValueLabel.SetBounds(176, y + 2, 80, 22);

            row.Device.SetBounds(258, y, 180, 24);
            row.Device.DropDownStyle = ComboBoxStyle.DropDownList;
            row.Device.Items.Clear();
            row.Device.Items.Add("（未选择）");
            foreach (var d in _devices) row.Device.Items.Add(d.Name);
            row.Device.SelectedIndex = ResolveDeviceIndex(row.CaptureDevice);

            row.SnapshotButton.Text = "创建快照";
            row.SnapshotButton.SetBounds(444, y, 78, 24);

            row.ViewButton.Text = "查看";
            row.ViewButton.SetBounds(526, y, 54, 24);

            row.ClearButton.Text = "清除";
            row.ClearButton.SetBounds(584, y, 50, 24);

            row.SnapshotStatus.SetBounds(640, y + 2, 170, 22);
            row.SnapshotStatus.ForeColor = Color.DimGray;

            row.Up.Text = "▲";
            row.Up.SetBounds(818, y, 32, 24);
            row.Down.Text = "▼";
            row.Down.SetBounds(854, y, 32, 24);

            var captured = row;
            row.SnapshotButton.Click += (_, _) => CreateSnapshot(captured);
            row.ViewButton.Click += (_, _) => ViewSnapshot(captured);
            row.ClearButton.Click += (_, _) => ClearSnapshot(captured);
            row.Device.SelectedIndexChanged += (_, _) => OnDeviceChanged(captured);
            row.Up.Click += (_, _) => MoveRow(captured, -1);
            row.Down.Click += (_, _) => MoveRow(captured, +1);

            _panel.Controls.Add(row.Check);
            _panel.Controls.Add(row.Title);
            _panel.Controls.Add(row.ValueLabel);
            _panel.Controls.Add(row.Device);
            _panel.Controls.Add(row.SnapshotButton);
            _panel.Controls.Add(row.ViewButton);
            _panel.Controls.Add(row.ClearButton);
            _panel.Controls.Add(row.SnapshotStatus);
            _panel.Controls.Add(row.Up);
            _panel.Controls.Add(row.Down);

            UpdateRowState(row);

            y += 34;
        }
    }

    private void UpdateRowState(RowModel row)
    {
        var bound = row.Value.HasValue;
        var hasSnapshot = !string.IsNullOrWhiteSpace(row.NoSignalSnapshot);
        row.Check.Enabled = bound && hasSnapshot;
        if (!hasSnapshot) row.Check.Checked = false;
        row.SnapshotStatus.Text = hasSnapshot ? "已创建" : "未创建";
        row.ClearButton.Enabled = hasSnapshot;
        row.ViewButton.Enabled = hasSnapshot;
    }

    private void OnDeviceChanged(RowModel row)
    {
        var deviceId = SelectedDeviceId(row);
        if (!string.IsNullOrWhiteSpace(row.NoSignalSnapshot) &&
            deviceId != null &&
            !string.Equals(row.NoSignalSnapshot, SnapshotPathFor(row.Input, deviceId), StringComparison.OrdinalIgnoreCase))
        {
            row.NoSignalSnapshot = null;
            row.Check.Checked = false;
        }
        UpdateRowState(row);
    }

    private string? SelectedDeviceId(RowModel row)
    {
        var index = row.Device.SelectedIndex;
        return index <= 0 ? null : _devices[index - 1].Id;
    }

    private async void CreateSnapshot(RowModel row)
    {
        if (row.SnapshotBusy) return;

        var deviceId = SelectedDeviceId(row);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            row.SnapshotStatus.Text = "请先选设备";
            return;
        }

        row.SnapshotBusy = true;
        row.SnapshotButton.Enabled = false;
        row.SnapshotStatus.Text = "采集中…";

        var previewDir = Path.Combine(SnapshotDir, "preview", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        List<CapturedFrame>? frames = null;
        try
        {
            frames = await Task.Run(() => _captureFrames(deviceId, previewDir));
        }
        catch
        {
            frames = null;
        }

        row.SnapshotBusy = false;
        row.SnapshotButton.Enabled = true;

        if (frames == null || frames.Count == 0)
        {
            CleanupDir(previewDir);
            row.SnapshotStatus.Text = "采集失败";
            return;
        }

        int[] selected;
        using (var picker = new SnapshotPickerForm(frames))
        {
            if (picker.ShowDialog(this) != DialogResult.OK)
            {
                CleanupDir(previewDir);
                row.SnapshotStatus.Text = "已取消";
                return;
            }
            selected = picker.SelectedIndices.ToArray();
        }

        var chosen = selected
            .Where(i => i >= 0 && i < frames.Count)
            .Select(i => frames[i])
            .ToList();

        if (chosen.Count == 0)
        {
            CleanupDir(previewDir);
            row.SnapshotStatus.Text = "已取消";
            return;
        }

        var signatures = chosen.Select(f => f.Signature).ToList();
        var baseline = CaptureSignalMonitor.Baseline(signatures);
        var threshold = CaptureSignalMonitor.ThresholdFromBaseline(baseline);
        var medoidIndex = CaptureSignalMonitor.MedoidIndex(signatures);
        var medoid = chosen[medoidIndex];

        var path = SnapshotPathFor(row.Input, deviceId);
        CaptureSignalMonitor.SaveReferenceSet(chosen, medoidIndex, path);

        CleanupDir(previewDir);

        row.NoSignalSnapshot = path;
        row.SimilarityThreshold = threshold;
        row.SnapshotStatus.Text = $"已创建 {medoid.Width}x{medoid.Height} 基线{baseline:F2}（保留{chosen.Count}帧）";
        UpdateRowState(row);
    }

    private void ViewSnapshot(RowModel row)
    {
        if (string.IsNullOrWhiteSpace(row.NoSignalSnapshot)) return;
        using var viewer = new SnapshotViewerForm(row.NoSignalSnapshot);
        viewer.ShowDialog(this);
    }

    private static string SnapshotDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Auro_OSD", "snapshots");

    private static void CleanupDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    private void ClearSnapshot(RowModel row)
    {
        if (!string.IsNullOrWhiteSpace(row.NoSignalSnapshot) && !IsSnapshotUsedByOtherRow(row))
            CaptureSignalMonitor.DeleteReference(row.NoSignalSnapshot);

        row.NoSignalSnapshot = null;
        row.Check.Checked = false;
        UpdateRowState(row);
    }

    private bool IsSnapshotUsedByOtherRow(RowModel row) =>
        _rows.Any(r => !ReferenceEquals(r, row) &&
                       string.Equals(r.NoSignalSnapshot, row.NoSignalSnapshot, StringComparison.OrdinalIgnoreCase));

    private int ResolveDeviceIndex(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return 0;
        for (var i = 0; i < _devices.Count; i++)
            if (_devices[i].Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        return 0;
    }

    private void MoveRow(RowModel row, int delta)
    {
        CollectValues();
        var index = _rows.IndexOf(row);
        var target = index + delta;
        if (target < 0 || target >= _rows.Count) return;

        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        Rebuild();
    }

    private void CollectValues()
    {
        foreach (var row in _rows)
        {
            row.Enabled = row.Check.Checked;
            row.CaptureDevice = SelectedDeviceId(row);
        }
    }

    private void Save()
    {
        CollectValues();

        _config.AutoSwitch.Order = _rows
            .Select(r => new AutoSwitchEntry
            {
                Input = r.Input,
                Enabled = r.Enabled && !string.IsNullOrWhiteSpace(r.NoSignalSnapshot),
                CaptureDevice = r.CaptureDevice,
                NoSignalSnapshot = r.NoSignalSnapshot,
                SimilarityThreshold = r.SimilarityThreshold
            })
            .ToList();

        _saveAndApply();

        _status.Text = "已保存";
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private static string SnapshotPathFor(string input, string deviceId)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{input}|{deviceId}");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes))[..12];
        return Path.Combine(SnapshotDir, $"snap_{hash}.png");
    }
}
