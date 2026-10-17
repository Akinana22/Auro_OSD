using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auro_OSD.Config;

public enum ProjectionMode
{
    Extend,
    Clone,
    Internal,
    External
}

public sealed class TargetMonitorConfig
{
    public string? DeviceString { get; set; }
    public string? Serial { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class InputMap
{
    public uint? Dp1 { get; set; }
    public uint? Hdmi1 { get; set; }
    public uint? Hdmi2 { get; set; }
    public bool LearnedByWizard { get; set; }
}

public sealed class LabelsConfig
{
    public string? Dp { get; set; }
    public string? Hdmi1 { get; set; }
    public string? Hdmi2 { get; set; }

    [JsonIgnore]
    public string DpDisplay => string.IsNullOrWhiteSpace(Dp) ? "NULL" : Dp!;

    [JsonIgnore]
    public string Hdmi1Display => string.IsNullOrWhiteSpace(Hdmi1) ? "NULL" : Hdmi1!;

    [JsonIgnore]
    public string Hdmi2Display => string.IsNullOrWhiteSpace(Hdmi2) ? "NULL" : Hdmi2!;
}

public sealed class HotkeyConfig
{
    public string? Dp { get; set; }
    public string? Hdmi1 { get; set; }
    public string? Hdmi2 { get; set; }
}

public sealed class ProjectionConfig
{
    public ProjectionMode OnDp { get; set; } = ProjectionMode.Extend;
    public ProjectionMode OnHdmi1 { get; set; } = ProjectionMode.Clone;
    public ProjectionMode OnHdmi2 { get; set; } = ProjectionMode.Clone;
}

public sealed class OsdConfig
{
    public int DurationMs { get; set; } = 3000;
}

public sealed class AutoSwitchEntry
{
    public string Input { get; set; } = "";
    public bool Enabled { get; set; }
    public string? CaptureDevice { get; set; }
    public string? NoSignalSnapshot { get; set; }
    public double SimilarityThreshold { get; set; } = 0.95;
}

public sealed class AutoSwitchConfig
{
    public int PollMs { get; set; } = 1000;
    public int ActiveHoldMs { get; set; } = 1500;
    public List<AutoSwitchEntry> Order { get; set; } = new();
}

public sealed class LogConfig
{
    public bool Enabled { get; set; }
}

public sealed class AudioConfig
{
    public string? OnDpPlaybackId { get; set; }
    public string? OnDpRecordingId { get; set; }
    public string? OffDpPlaybackId { get; set; }
    public string? OffDpRecordingId { get; set; }
}

public sealed class AppConfig
{
    public TargetMonitorConfig TargetMonitor { get; set; } = new();
    public InputMap InputMap { get; set; } = new();
    public LabelsConfig Labels { get; set; } = new();
    public HotkeyConfig Hotkeys { get; set; } = new();
    public ProjectionConfig Projection { get; set; } = new();
    public OsdConfig Osd { get; set; } = new();
    public AutoSwitchConfig AutoSwitch { get; set; } = new();
    public AudioConfig Audio { get; set; } = new();
    public LogConfig Log { get; set; } = new();
    public bool AutoStart { get; set; }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auro_OSD");

    public static string FilePath => Path.Combine(DirectoryPath, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // fall through to defaults
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));
    }
}
