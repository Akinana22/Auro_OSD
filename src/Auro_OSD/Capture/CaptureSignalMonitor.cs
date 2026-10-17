using System.Runtime.InteropServices;
using OpenCvSharp;
using Windows.Devices.Enumeration;

namespace Auro_OSD.Capture;

public sealed class CaptureDeviceInfo
{
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
}

public sealed class NoSignalSnapshot
{
    public string Path { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public double Baseline { get; init; }
    public double Threshold { get; init; }
    public byte[] Signature { get; init; } = Array.Empty<byte>();
}

public sealed class CapturedFrame
{
    public string Path { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Signature { get; init; } = Array.Empty<byte>();
}

public sealed class CaptureSignalMonitor : IDisposable
{
    public const int SigW = 32;
    public const int SigH = 18;
    private const int ReadIntervalMs = 250;

    private sealed class DeviceState
    {
        public VideoCapture? Capture;
        public Thread? Thread;
        public volatile bool Stop;
        public volatile bool Calibrating;
        public readonly object Sync = new();
        public readonly object CaptureLock = new();
        public long LastFrameTicks;
        public long OpenedAtTicks;
        public string? Error;
        public byte[]? Signature;
        public bool SnapshotRequested;
        public int FrameWidth;
        public int FrameHeight;
        public Mat? SnapshotFrame;
        public readonly ManualResetEventSlim SnapshotReady = new(false);
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, DeviceState> _states = new(StringComparer.OrdinalIgnoreCase);

    public static VideoCaptureAPIs PreferredApi { get; private set; } = VideoCaptureAPIs.DSHOW;

    public static bool ForceSmallFormat { get; set; }

    public static void SetApi(string api) =>
        PreferredApi = api.Equals("msmf", StringComparison.OrdinalIgnoreCase)
            ? VideoCaptureAPIs.MSMF
            : VideoCaptureAPIs.DSHOW;

    public static List<CaptureDeviceInfo> Enumerate()
    {
        var list = new List<CaptureDeviceInfo>();
        try
        {
            var devices = DeviceInformation
                .FindAllAsync(DeviceClass.VideoCapture)
                .AsTask().GetAwaiter().GetResult();

            foreach (var device in devices)
            {
                list.Add(new CaptureDeviceInfo
                {
                    Name = device.Name ?? "",
                    Id = device.Id ?? ""
                });
            }
        }
        catch
        {
            // enumeration unavailable
        }

        return list;
    }

    public void Reopen(string deviceId)
    {
        Close(deviceId);
        Open(deviceId);
    }

    public void SetDevices(IEnumerable<string> deviceIds)
    {
        var wanted = new HashSet<string>(
            deviceIds.Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);

        List<string> toClose;
        List<string> toOpen;

        lock (_sync)
        {
            toClose = _states.Keys.Where(id => !wanted.Contains(id)).ToList();
            toOpen = wanted.Where(id => !_states.ContainsKey(id)).ToList();
        }

        foreach (var id in toClose) Close(id);
        foreach (var id in toOpen) Open(id);
    }

    private void Open(string id)
    {
        var state = new DeviceState();
        lock (_sync)
        {
            if (_states.ContainsKey(id)) return;
            _states[id] = state;
        }

        state.Thread = new Thread(() => Pump(state, id))
        {
            IsBackground = true,
            Name = "Auro_OSD_Capture"
        };
        state.Thread.Start();
    }

    private void Pump(DeviceState state, string id)
    {
        try
        {
            var devices = Enumerate();
            var index = devices.FindIndex(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = 0;

            var capture = new VideoCapture(index, PreferredApi);
            if (!capture.IsOpened())
            {
                state.Error = "capture open failed";
                return;
            }

            if (ForceSmallFormat)
            {
                try
                {
                    capture.Set(VideoCaptureProperties.FrameWidth, 640);
                    capture.Set(VideoCaptureProperties.FrameHeight, 360);
                }
                catch
                {
                    // keep default format
                }
            }

            state.Capture = capture;
            state.OpenedAtTicks = DateTime.UtcNow.Ticks;

            while (!state.Stop)
            {
                if (state.Calibrating)
                {
                    Thread.Sleep(50);
                    continue;
                }

                Mat? frame = null;
                var ok = false;
                try
                {
                    frame = new Mat();
                    lock (state.CaptureLock) ok = capture.Read(frame);
                }
                catch (Exception ex)
                {
                    state.Error = ex.Message;
                }

                if (ok && frame != null && !frame.Empty())
                {
                    var signature = ComputeSignature(frame);

                    lock (state.Sync)
                    {
                        state.FrameWidth = frame.Width;
                        state.FrameHeight = frame.Height;
                        state.Signature = signature;
                        state.LastFrameTicks = DateTime.UtcNow.Ticks;

                        if (state.SnapshotRequested)
                        {
                            try { state.SnapshotFrame?.Dispose(); } catch { }
                            state.SnapshotFrame = frame.Clone();
                            state.SnapshotRequested = false;
                            state.SnapshotReady.Set();
                        }
                    }
                }

                frame?.Dispose();

                if (state.Stop) break;
                Thread.Sleep(ReadIntervalMs);
            }
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
        }
        finally
        {
            try { state.Capture?.Release(); } catch { }
            try { state.Capture?.Dispose(); } catch { }
            state.Capture = null;
        }
    }

    private static byte[] ComputeSignature(Mat frame)
    {
        Mat gray = frame;
        var createdGray = false;

        if (frame.Channels() == 3 || frame.Channels() == 4)
        {
            gray = new Mat();
            createdGray = true;
            var code = frame.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY;
            Cv2.CvtColor(frame, gray, code);
        }

        using var small = new Mat();
        Cv2.Resize(gray, small, new OpenCvSharp.Size(SigW, SigH), 0, 0, InterpolationFlags.Area);

        var signature = new byte[SigW * SigH];
        Marshal.Copy(small.Data, signature, 0, signature.Length);

        if (createdGray) gray.Dispose();
        return signature;
    }

    public byte[]? GetSignature(string deviceId)
    {
        DeviceState? state;
        lock (_sync) _states.TryGetValue(deviceId, out state);
        if (state == null) return null;
        lock (state.Sync) return state.Signature;
    }

    public long GetAgeMs(string deviceId)
    {
        DeviceState? state;
        lock (_sync) _states.TryGetValue(deviceId, out state);
        if (state == null) return -1;
        lock (state.Sync)
        {
            if (state.LastFrameTicks == 0) return -1;
            return (long)(DateTime.UtcNow - new DateTime(state.LastFrameTicks, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }

    public (int Width, int Height)? GetResolution(string deviceId)
    {
        DeviceState? state;
        lock (_sync) _states.TryGetValue(deviceId, out state);
        if (state == null) return null;
        lock (state.Sync)
            return state.FrameWidth > 0 ? (state.FrameWidth, state.FrameHeight) : null;
    }

    public bool IsActive(string deviceId, int holdMs)
    {
        var age = GetAgeMs(deviceId);
        return age >= 0 && age <= Math.Max(200, holdMs);
    }

    public bool IsOpen(string deviceId)
    {
        DeviceState? state;
        lock (_sync) _states.TryGetValue(deviceId, out state);
        return state?.Capture?.IsOpened() == true;
    }

    public string? GetError(string deviceId)
    {
        DeviceState? state;
        lock (_sync) _states.TryGetValue(deviceId, out state);
        return state?.Error;
    }

    public byte[]? CaptureSignature(string deviceId)
    {
        var frame = RequestFrame(deviceId);
        if (frame == null) return null;
        using var mat = frame;
        return ComputeSignature(mat);
    }

    public List<CapturedFrame>? CaptureFrames(string deviceId, string previewDir, int count = 10, int intervalMs = 100)
    {
        DeviceState? state;
        lock (_sync) _states.TryGetValue(deviceId, out state);
        if (state?.Capture == null) return null;

        Directory.CreateDirectory(previewDir);
        var frames = new List<CapturedFrame>();
        state.Calibrating = true;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var warm = ReadDirect(state);
                warm?.Dispose();
                Thread.Sleep(60);
            }

            for (var i = 0; i < count; i++)
            {
                var mat = ReadDirect(state);
                if (mat != null)
                {
                    var signature = ComputeSignature(mat);
                    var path = Path.Combine(previewDir, $"frame_{i:D2}.png");
                    SavePng(mat, path);
                    frames.Add(new CapturedFrame
                    {
                        Path = path,
                        Width = mat.Width,
                        Height = mat.Height,
                        Signature = signature
                    });
                    mat.Dispose();
                }

                Thread.Sleep(intervalMs);
            }
        }
        finally
        {
            state.Calibrating = false;
        }

        return frames.Count == 0 ? null : frames;
    }

    public static double Baseline(IReadOnlyList<byte[]> signatures)
    {
        if (signatures.Count <= 1) return 1.0;

        var min = 1.0;
        for (var i = 0; i < signatures.Count; i++)
            for (var j = i + 1; j < signatures.Count; j++)
            {
                var s = Similarity(signatures[i], signatures[j]);
                if (s < min) min = s;
            }

        return Math.Max(min, 0.95);
    }

    public static int MedoidIndex(IReadOnlyList<byte[]> signatures)
    {
        if (signatures.Count <= 1) return 0;

        var best = 0;
        var bestScore = -1.0;
        for (var i = 0; i < signatures.Count; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < signatures.Count; j++)
            {
                if (i == j) continue;
                sum += Similarity(signatures[i], signatures[j]);
            }
            if (sum > bestScore) { bestScore = sum; best = i; }
        }
        return best;
    }

    public static double ThresholdFromBaseline(double baseline) =>
        Math.Clamp(baseline - 0.02, 0.9, 0.99);

    public static void SaveReference(string sourcePng, string targetPng, IEnumerable<byte[]> signatures)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPng)!);
        File.Copy(sourcePng, targetPng, true);
        var all = signatures.SelectMany(s => s).ToArray();
        File.WriteAllBytes(SignaturePath(targetPng), all);
    }

    public static void SaveReferenceSet(IReadOnlyList<CapturedFrame> chosen, int medoidIndex, string targetPng)
    {
        var dir = Path.GetDirectoryName(targetPng)!;
        Directory.CreateDirectory(dir);
        var baseName = Path.GetFileNameWithoutExtension(targetPng);

        try
        {
            foreach (var old in Directory.GetFiles(dir, baseName + "_*.png"))
                File.Delete(old);
        }
        catch
        {
            // ignore
        }

        File.Copy(chosen[medoidIndex].Path, targetPng, true);
        for (var i = 0; i < chosen.Count; i++)
            File.Copy(chosen[i].Path, Path.Combine(dir, $"{baseName}_{i}.png"), true);

        var all = chosen.SelectMany(c => c.Signature).ToArray();
        File.WriteAllBytes(SignaturePath(targetPng), all);
    }

    public static List<string> KeptFramePaths(string targetPng)
    {
        var result = new List<string>();
        try
        {
            var dir = Path.GetDirectoryName(targetPng)!;
            var baseName = Path.GetFileNameWithoutExtension(targetPng);
            var files = Directory.GetFiles(dir, baseName + "_*.png");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            result.AddRange(files);
        }
        catch
        {
            // ignore
        }
        return result;
    }

    public static void DeleteReference(string targetPng)
    {
        try { File.Delete(targetPng); } catch { }
        try { File.Delete(SignaturePath(targetPng)); } catch { }

        try
        {
            var dir = Path.GetDirectoryName(targetPng)!;
            var baseName = Path.GetFileNameWithoutExtension(targetPng);
            foreach (var file in Directory.GetFiles(dir, baseName + "_*.png"))
                File.Delete(file);
        }
        catch
        {
            // ignore
        }
    }

    public NoSignalSnapshot? CreateSnapshot(string deviceId, string pngPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "auro_osd_prev_" + Guid.NewGuid().ToString("N"));
        List<CapturedFrame>? frames;
        try
        {
            frames = CaptureFrames(deviceId, tempDir, 10, 100);
        }
        catch
        {
            frames = null;
        }

        try
        {
            if (frames == null || frames.Count == 0) return null;

            var signatures = frames.Select(f => f.Signature).ToList();
            var baseline = Baseline(signatures);
            var threshold = ThresholdFromBaseline(baseline);
            var medoidIndex = MedoidIndex(signatures);
            var medoid = frames[medoidIndex];

            SaveReferenceSet(frames, medoidIndex, pngPath);

            lock (_sync)
            {
                if (_states.TryGetValue(deviceId, out var state))
                    lock (state.Sync) state.Signature = medoid.Signature;
            }

            return new NoSignalSnapshot
            {
                Path = pngPath,
                Width = medoid.Width,
                Height = medoid.Height,
                Baseline = baseline,
                Threshold = threshold,
                Signature = medoid.Signature
            };
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static Mat? ReadDirect(DeviceState state)
    {
        var capture = state.Capture;
        if (capture == null) return null;

        try
        {
            var mat = new Mat();
            lock (state.CaptureLock)
            {
                if (!capture.Read(mat) || mat.Empty())
                {
                    mat.Dispose();
                    return null;
                }
            }
            return mat;
        }
        catch
        {
            return null;
        }
    }

    private Mat? RequestFrame(string deviceId)
    {
        DeviceState? state;
        lock (_sync) _states.TryGetValue(deviceId, out state);
        if (state == null) return null;

        state.SnapshotReady.Reset();
        lock (state.Sync) state.SnapshotRequested = true;

        if (!state.SnapshotReady.Wait(4000)) return null;

        lock (state.Sync)
        {
            var frame = state.SnapshotFrame;
            state.SnapshotFrame = null;
            return frame;
        }
    }

    public static double Similarity(byte[] a, byte[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;

        long diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff += Math.Abs(a[i] - b[i]);

        return 1.0 - diff / (255.0 * a.Length);
    }

    public static string SignaturePath(string pngPath) => Path.ChangeExtension(pngPath, ".sig");

    private static void SavePng(Mat frame, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Cv2.ImWrite(path, frame);
    }

    private static void SaveSignature(byte[] signature, string pngPath)
    {
        File.WriteAllBytes(SignaturePath(pngPath), signature);
    }

    public static List<byte[]> LoadSignatures(string pngPath)
    {
        var list = new List<byte[]>();
        try
        {
            var sigPath = SignaturePath(pngPath);
            if (!File.Exists(sigPath)) return list;

            var bytes = File.ReadAllBytes(sigPath);
            var block = SigW * SigH;
            for (var offset = 0; offset + block <= bytes.Length; offset += block)
            {
                var signature = new byte[block];
                Array.Copy(bytes, offset, signature, 0, block);
                list.Add(signature);
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    public static byte[]? LoadSignature(string pngPath)
    {
        var list = LoadSignatures(pngPath);
        return list.Count > 0 ? list[0] : null;
    }

    private void Close(string id)
    {
        DeviceState? state;
        lock (_sync)
        {
            if (!_states.Remove(id, out state)) return;
        }

        state.Stop = true;
        try { state.Thread?.Join(3000); } catch { }
        lock (state.Sync)
        {
            try { state.SnapshotFrame?.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        List<string> ids;
        lock (_sync) ids = _states.Keys.ToList();
        foreach (var id in ids) Close(id);
    }
}
