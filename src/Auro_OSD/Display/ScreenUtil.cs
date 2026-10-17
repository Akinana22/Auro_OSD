using Auro_OSD.Config;
using Auro_OSD.Ddc;

namespace Auro_OSD.Display;

public static class ScreenUtil
{
    public static Screen FindTargetScreen(AppConfig config)
    {
        var matches = DdcMonitor.ListMonitors()
            .Where(m => DdcMonitor.IdentityMatches(
                config.TargetMonitor.DeviceString, config.TargetMonitor.Serial, config.TargetMonitor.DeviceId,
                m.DeviceString, m.Serial, m.DeviceId))
            .ToList();

        if (matches.Count == 1)
        {
            var byName = Screen.AllScreens.FirstOrDefault(s =>
                s.DeviceName.Equals(matches[0].DeviceName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }

        return Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }
}
