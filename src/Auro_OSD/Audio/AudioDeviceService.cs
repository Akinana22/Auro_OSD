using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using Auro_OSD.Logging;

namespace Auro_OSD.Audio;

public sealed record AudioDeviceInfo(string Id, string Name);

public static class AudioDeviceService
{
    private static CoreAudioController? _controller;

    private static CoreAudioController Controller => _controller ??= new CoreAudioController();

    public static List<AudioDeviceInfo> ListPlayback() => List(DeviceType.Playback);

    public static List<AudioDeviceInfo> ListRecording() => List(DeviceType.Capture);

    private static List<AudioDeviceInfo> List(DeviceType type)
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            foreach (var device in Controller.GetDevices(type, DeviceState.Active))
                list.Add(new AudioDeviceInfo(device.Id.ToString() ?? "", device.FullName));
        }
        catch (Exception ex)
        {
            AppLogger.Line($"audio list {type} failed: {ex.Message}");
        }

        return list;
    }

    public static void SetPlayback(string? id) => Set(id, DeviceType.Playback);

    public static void SetRecording(string? id) => Set(id, DeviceType.Capture);

    public static string? GetDefaultPlaybackId() =>
        Controller.GetDefaultDevice(DeviceType.Playback, Role.Multimedia)?.Id.ToString();

    public static string? GetDefaultRecordingId() =>
        Controller.GetDefaultDevice(DeviceType.Capture, Role.Multimedia)?.Id.ToString();

    private static void Set(string? id, DeviceType type)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        try
        {
            var device = Controller.GetDevices(type, DeviceState.Active)
                .FirstOrDefault(d => string.Equals(d.Id.ToString(), id, StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                AppLogger.Line($"audio set {type}: device not found ({id})");
                return;
            }

            var current = Controller.GetDefaultDevice(type, Role.Multimedia);
            if (current != null && string.Equals(current.Id.ToString(), id, StringComparison.OrdinalIgnoreCase))
                return;

            Controller.SetDefaultDevice(device);
            AppLogger.Line($"audio set {type} -> {device.FullName}");
        }
        catch (Exception ex)
        {
            AppLogger.Line($"audio set {type} failed: {ex.Message}");
        }
    }
}
