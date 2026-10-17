using System.Text;
using Auro_OSD.Audio;
using Auro_OSD.Capture;
using Auro_OSD.Config;
using Auro_OSD.Ddc;
using Auro_OSD.Tray;

namespace Auro_OSD;

internal static class Program
{
    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
        {
            RunConsole(args);
            return;
        }

        _instanceMutex = new Mutex(true, @"Local\Auro_OSD_SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("Auro_OSD 已在运行。", "Auro_OSD", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* ignore */ }
        try { Directory.CreateDirectory(ConfigStore.DirectoryPath); } catch { /* ignore */ }
        Application.Run(new TrayContext());
    }

    private static void RunConsole(string[] args)
    {
        var config = ConfigStore.Load();
        var monitor = new DdcMonitor(
            config.TargetMonitor.DeviceString,
            config.TargetMonitor.Serial,
            config.TargetMonitor.DeviceId);
        var output = new StringBuilder();

        switch (args[0].ToLowerInvariant())
        {
            case "--list":
                foreach (var m in DdcMonitor.ListMonitors())
                    output.AppendLine($"{m.DisplayName} | primary={m.IsPrimary} | serial={m.Serial} | id={m.DeviceId}");
                break;
            case "--read":
                output.AppendLine(monitor.GetInputSource()?.ToString() ?? "read failed");
                break;
            case "--caps":
                output.AppendLine(monitor.TryGetCapabilities(out var caps) ? caps : "read failed");
                break;
            case "--set" when args.Length > 1 && uint.TryParse(args[1], out var value):
                output.AppendLine(monitor.SetInputSource(value) ? "ok" : "failed");
                break;
            case "--capture":
            {
                var waitMs = args.Length > 1 && int.TryParse(args[1], out var w) ? w : 6000;
                var devices = CaptureSignalMonitor.Enumerate();
                output.AppendLine($"capture devices: {devices.Count}");
                foreach (var d in devices)
                    output.AppendLine($"  {d.Name} | {d.Id}");

                using var signal = new CaptureSignalMonitor();
                signal.SetDevices(devices.Select(d => d.Id));
                Thread.Sleep(waitMs);
                foreach (var d in devices)
                {
                    output.AppendLine(
                        $"  {d.Name}: open={signal.IsOpen(d.Id)} ageMs={signal.GetAgeMs(d.Id)} " +
                        $"active={signal.IsActive(d.Id, 1500)} err={signal.GetError(d.Id)}");
                }
                break;
            }
            case "--capture-snapshot" when args.Length >= 2:
            {
                var png = Path.GetFullPath(args[1]);
                var dir = Path.GetDirectoryName(png);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var devices = CaptureSignalMonitor.Enumerate();
                if (devices.Count == 0) { output.AppendLine("no capture devices"); break; }

                using var signal = new CaptureSignalMonitor();
                signal.SetDevices(devices.Select(d => d.Id));
                Thread.Sleep(1500);
                foreach (var d in devices)
                {
                    var snapshot = signal.CreateSnapshot(d.Id, png);
                    output.AppendLine(snapshot == null
                        ? $"{d.Name}: snapshot failed"
                        : $"{d.Name}: snapshot {snapshot.Width}x{snapshot.Height} saved={png} baseline={snapshot.Baseline:F4} threshold={snapshot.Threshold:F4}");
                    if (snapshot != null)
                    {
                        var runtime = signal.GetSignature(d.Id);
                        output.AppendLine(runtime == null
                            ? "  runtime signature: null"
                            : $"  runtimeVsSaved={CaptureSignalMonitor.Similarity(runtime, snapshot.Signature):F4} len={runtime.Length}/{snapshot.Signature.Length}");
                    }
                }
                break;
            }
            case "--capture-check" when args.Length >= 2:
            {
                var png = Path.GetFullPath(args[1]);
                var references = CaptureSignalMonitor.LoadSignatures(png);
                if (references.Count == 0) { output.AppendLine($"no reference signature for {png}"); break; }

                var devices = CaptureSignalMonitor.Enumerate();
                using var signal = new CaptureSignalMonitor();
                signal.SetDevices(devices.Select(d => d.Id));
                for (var i = 0; i < 20; i++)
                {
                    Thread.Sleep(1000);
                    foreach (var d in devices)
                    {
                        var sig = signal.GetSignature(d.Id);
                        if (sig == null)
                        {
                            output.AppendLine($"{d.Name}: t={i}s no frame (ageMs={signal.GetAgeMs(d.Id)})");
                        }
                        else
                        {
                            var max = references.Max(r => CaptureSignalMonitor.Similarity(sig, r));
                            output.AppendLine($"{d.Name}: t={i}s maxSimilarity={max:F4} (ageMs={signal.GetAgeMs(d.Id)})");
                        }
                    }
                }
                break;
            }
            case "--capture-probe":
            {
                var refPng = args.Length > 1 && File.Exists(args[1]) ? args[1] : null;
                var seconds = args.Length > 2 && int.TryParse(args[2], out var sec) ? sec : 30;
                var interval = args.Length > 3 && int.TryParse(args[3], out var iv) ? iv : 250;
                if (args.Length > 4) CaptureSignalMonitor.SetApi(args[4]);
                if (args.Length > 5 && int.TryParse(args[5], out var fw) && fw > 0)
                {
                    CaptureSignalMonitor.ForceSmallFormat = true;
                }
                output.AppendLine($"api={CaptureSignalMonitor.PreferredApi} forceSmallFormat={CaptureSignalMonitor.ForceSmallFormat}");
                var references = refPng != null ? CaptureSignalMonitor.LoadSignatures(refPng) : new List<byte[]>();

                var probeDevices = CaptureSignalMonitor.Enumerate();
                using var probe = new CaptureSignalMonitor();
                probe.SetDevices(probeDevices.Select(d => d.Id));

                var end = DateTime.UtcNow.AddSeconds(seconds);
                while (DateTime.UtcNow < end)
                {
                    foreach (var d in probeDevices)
                    {
                        var res = probe.GetResolution(d.Id);
                        var sig = probe.GetSignature(d.Id);
                        var sim = sig != null && references.Count > 0
                            ? references.Max(r => CaptureSignalMonitor.Similarity(sig, r))
                            : -1;
                        output.AppendLine(
                            $"{DateTime.Now:HH:mm:ss.fff} {res?.Width}x{res?.Height} sim={sim:F3} ageMs={probe.GetAgeMs(d.Id)}");
                    }
                    Thread.Sleep(interval);
                }
                break;
            }
            case "--capture-reopen":
            {
                var refPng = args.Length > 1 && File.Exists(args[1]) ? args[1] : null;
                var seconds = args.Length > 2 && int.TryParse(args[2], out var sec) ? sec : 150;
                var interval = args.Length > 3 && int.TryParse(args[3], out var iv) ? iv : 250;
                var reopenMs = args.Length > 4 && int.TryParse(args[4], out var rv) ? rv : 2000;
                var references = refPng != null ? CaptureSignalMonitor.LoadSignatures(refPng) : new List<byte[]>();

                var devices = CaptureSignalMonitor.Enumerate();
                var cap = new CaptureSignalMonitor();
                cap.SetDevices(devices.Select(d => d.Id));

                var nextReopen = DateTime.UtcNow.AddMilliseconds(reopenMs);
                var end = DateTime.UtcNow.AddSeconds(seconds);
                while (DateTime.UtcNow < end)
                {
                    if (DateTime.UtcNow >= nextReopen)
                    {
                        cap.Dispose();
                        cap = new CaptureSignalMonitor();
                        cap.SetDevices(devices.Select(d => d.Id));
                        nextReopen = DateTime.UtcNow.AddMilliseconds(reopenMs);
                        output.AppendLine($"{DateTime.Now:HH:mm:ss.fff} REOPEN");
                    }

                    foreach (var d in devices)
                    {
                        var sig = cap.GetSignature(d.Id);
                        var sim = sig != null && references.Count > 0
                            ? references.Max(r => CaptureSignalMonitor.Similarity(sig, r))
                            : -1;
                        output.AppendLine($"{DateTime.Now:HH:mm:ss.fff} sim={sim:F3} ageMs={cap.GetAgeMs(d.Id)}");
                    }

                    Thread.Sleep(interval);
                }

                cap.Dispose();
                break;
            }
            case "--audio-list":
            {
                var defPlay = AudioDeviceService.GetDefaultPlaybackId();
                var defRec = AudioDeviceService.GetDefaultRecordingId();
                foreach (var d in AudioDeviceService.ListPlayback())
                    output.AppendLine($"{(d.Id == defPlay ? "*" : " ")} [Playback] {d.Name} | {d.Id}");
                foreach (var d in AudioDeviceService.ListRecording())
                    output.AppendLine($"{(d.Id == defRec ? "*" : " ")} [Recording] {d.Name} | {d.Id}");
                break;
            }
            case "--audio-set" when args.Length >= 3:
            {
                if (args[1].StartsWith("rec", StringComparison.OrdinalIgnoreCase))
                {
                    AudioDeviceService.SetRecording(args[2]);
                    output.AppendLine("recording set");
                }
                else
                {
                    AudioDeviceService.SetPlayback(args[2]);
                    output.AppendLine("playback set");
                }
                break;
            }
            default:
                output.AppendLine("Auro_OSD");
                output.AppendLine("  --list       list physical monitors");
                output.AppendLine("  --read       read input source (VCP 0x60)");
                output.AppendLine("  --caps       read DDC capabilities string");
                output.AppendLine("  --set <n>    set input source");
                output.AppendLine("  --capture [ms]  enumerate capture devices and probe signal");
                output.AppendLine("  --capture-snapshot <png>  save a no-signal snapshot + frame similarity");
                output.AppendLine("  --capture-check <png>     compare live frame with a saved snapshot");
                break;
        }

        var text = output.ToString();

        try
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            stdout.Write(text);
        }
        catch
        {
            // ignore
        }

        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "auro_osd_cli.txt"), text);
        }
        catch
        {
            // ignore
        }
    }
}
