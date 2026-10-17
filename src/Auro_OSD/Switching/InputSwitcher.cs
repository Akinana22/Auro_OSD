using Auro_OSD.Audio;
using Auro_OSD.Config;
using Auro_OSD.Ddc;
using Auro_OSD.Display;
using Auro_OSD.Logging;

namespace Auro_OSD.Switching;

public sealed class InputSwitcher
{
    private const int WriteDurationMs = 2000;
    private const int WriteCount = 4;

    private readonly AppConfig _config;

    public InputSwitcher(AppConfig config) => _config = config;

    private void ApplyProjection(ProjectionMode mode)
    {
        if (DisplayTopology.Apply(mode))
            AppLogger.Line($"projection applied mode={mode}");
        else
            AppLogger.Line($"projection fail mode={mode}");
    }

    private void ApplyAudio(bool onDp)
    {
        if (onDp)
        {
            AudioDeviceService.SetPlayback(_config.Audio.OnDpPlaybackId);
            AudioDeviceService.SetRecording(_config.Audio.OnDpRecordingId);
        }
        else
        {
            AudioDeviceService.SetPlayback(_config.Audio.OffDpPlaybackId);
            AudioDeviceService.SetRecording(_config.Audio.OffDpRecordingId);
        }
    }

    private DdcMonitor CreateMonitor() => new(
        _config.TargetMonitor.DeviceString,
        _config.TargetMonitor.Serial,
        _config.TargetMonitor.DeviceId);

    public bool TargetExists() => CreateMonitor().Exists();

    public uint? ReadInput() => CreateMonitor().GetInputSource();

    public bool SwitchToDp()
    {
        if (_config.InputMap.Dp1 is not uint value) return false;
        return Switch(value, _config.Projection.OnDp, onDp: true);
    }

    public bool SwitchToHdmi(uint value, ProjectionMode projectionMode) =>
        Switch(value, projectionMode, onDp: false);

    private bool Switch(uint targetValue, ProjectionMode targetProjection, bool onDp)
    {
        AppLogger.Line($"switch begin target={targetValue}");

        var interval = WriteDurationMs / WriteCount;
        for (var i = 0; i < WriteCount; i++)
        {
            var ok = CreateMonitor().SetInputSource(targetValue);
            AppLogger.Line($"write {i + 1}/{WriteCount} value={targetValue} result={(ok ? "ok" : "fail")}");
            if (i < WriteCount - 1) Thread.Sleep(interval);
        }

        ApplyProjection(targetProjection);
        ApplyAudio(onDp);
        AppLogger.Line($"switch applied target={targetValue} projection={targetProjection} audio={(onDp ? "on" : "off")}");
        return true;
    }
}
