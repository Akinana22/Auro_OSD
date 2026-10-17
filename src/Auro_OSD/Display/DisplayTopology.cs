using System.Runtime.InteropServices;
using Auro_OSD.Config;

namespace Auro_OSD.Display;

public static class DisplayTopology
{
    private const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
    private const uint SDC_TOPOLOGY_CLONE = 0x00000002;
    private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    private const uint SDC_TOPOLOGY_EXTERNAL = 0x00000008;
    private const uint SDC_APPLY = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray, uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);

    public static bool Apply(ProjectionMode mode)
    {
        var topology = mode switch
        {
            ProjectionMode.Clone => SDC_TOPOLOGY_CLONE,
            ProjectionMode.Internal => SDC_TOPOLOGY_INTERNAL,
            ProjectionMode.External => SDC_TOPOLOGY_EXTERNAL,
            _ => SDC_TOPOLOGY_EXTEND
        };

        return SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, topology | SDC_APPLY) == 0;
    }
}
