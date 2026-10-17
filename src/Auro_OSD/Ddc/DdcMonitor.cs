using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace Auro_OSD.Ddc;

internal static class NativeDdc
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprc, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte vcpCode, ref uint type, ref uint current, ref uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool SetVCPFeature(IntPtr hMonitor, byte vcpCode, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, ref uint length);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] StringBuilder buffer, uint length);

    internal static List<MonitorEnumEntry> EnumMonitorsForAdapter(string adapterDeviceName)
    {
        var list = new List<MonitorEnumEntry>();
        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(adapterDeviceName, i, ref device, 0)) break;
            list.Add(new MonitorEnumEntry(device.DeviceName, device.DeviceString, device.DeviceID));
        }
        return list;
    }
}

internal sealed class MonitorEnumEntry
{
    public string DeviceName { get; }
    public string DeviceString { get; }
    public string DeviceID { get; }

    public MonitorEnumEntry(string deviceName, string deviceString, string deviceId)
    {
        DeviceName = deviceName ?? "";
        DeviceString = deviceString ?? "";
        DeviceID = deviceId ?? "";
    }
}

public sealed class MonitorInfo
{
    public string DeviceName { get; init; } = "";
    public string Description { get; init; } = "";
    public string MonitorDeviceName { get; init; } = "";
    public string DeviceString { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string Serial { get; init; } = "";
    public bool IsPrimary { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(DeviceString) ? Description : DeviceString;
            var where = string.IsNullOrEmpty(MonitorDeviceName) ? DeviceName : MonitorDeviceName;
            return $"{name} ({where})";
        }
    }

    public override string ToString() => DisplayName;
}

internal sealed class MonitorEntry
{
    public string DeviceName = "";
    public string Description = "";
    public bool IsPrimary;
    public IntPtr Handle;
    public string MonitorDeviceName = "";
    public string DeviceString = "";
    public string DeviceId = "";
    public string Serial = "";
}

internal sealed class WmiMonitorInfo
{
    public string Token = "";
    public string Name = "";
    public string Serial = "";
}

internal static class WmiMonitorCache
{
    private static readonly object Sync = new();
    private static DateTime _loadedAt = DateTime.MinValue;
    private static List<WmiMonitorInfo> _items = new();

    public static List<WmiMonitorInfo> Get()
    {
        lock (Sync)
        {
            if (DateTime.UtcNow - _loadedAt < TimeSpan.FromSeconds(15))
                return _items;

            _items = Load();
            _loadedAt = DateTime.UtcNow;
            return _items;
        }
    }

    private static List<WmiMonitorInfo> Load()
    {
        var list = new List<WmiMonitorInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\wmi",
                "SELECT InstanceName, UserFriendlyName, SerialNumberID FROM WmiMonitorID");

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var instance = obj["InstanceName"] as string ?? "";
                    list.Add(new WmiMonitorInfo
                    {
                        Token = TokenOf(instance),
                        Name = DecodeString(obj["UserFriendlyName"] as ushort[]),
                        Serial = DecodeString(obj["SerialNumberID"] as ushort[])
                    });
                }
            }
        }
        catch
        {
            // WMI unavailable
        }

        return list;
    }

    private static string DecodeString(ushort[]? data)
    {
        if (data == null) return "";
        var builder = new StringBuilder();
        foreach (var value in data)
        {
            if (value == 0) break;
            builder.Append((char)value);
        }
        return builder.ToString().Trim();
    }

    public static string TokenOf(string instancePath)
    {
        var parts = instancePath.Split('\\');
        return parts.Length >= 2 ? parts[1] : "";
    }
}

public sealed class DdcSession : IDisposable
{
    private readonly List<(NativeDdc.PHYSICAL_MONITOR[] Monitors, uint Count)> _owned = new();

    internal List<MonitorEntry> Monitors { get; } = new();

    public DdcSession(bool resolveSerial = false)
    {
        var handles = new List<IntPtr>();
        NativeDdc.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, hdc, r, d) =>
        {
            handles.Add(h);
            return true;
        }, IntPtr.Zero);

        foreach (var hMonitor in handles)
        {
            var info = new NativeDdc.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeDdc.MONITORINFOEX>() };
            NativeDdc.GetMonitorInfo(hMonitor, ref info);

            uint count = 0;
            if (!NativeDdc.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count) || count == 0)
                continue;

            var array = new NativeDdc.PHYSICAL_MONITOR[count];
            if (!NativeDdc.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
                continue;

            _owned.Add((array, count));

            var enumMonitors = NativeDdc.EnumMonitorsForAdapter(info.szDevice);

            for (var i = 0; i < count; i++)
            {
                var entry = new MonitorEntry
                {
                    DeviceName = info.szDevice,
                    Description = array[i].szPhysicalMonitorDescription ?? "",
                    IsPrimary = (info.dwFlags & 1) != 0,
                    Handle = array[i].hPhysicalMonitor
                };

                if (i < enumMonitors.Count)
                {
                    entry.MonitorDeviceName = enumMonitors[i].DeviceName;
                    entry.DeviceString = enumMonitors[i].DeviceString;
                    entry.DeviceId = enumMonitors[i].DeviceID;
                }

                Monitors.Add(entry);
            }
        }

        if (resolveSerial)
            ResolveSerials();
    }

    private void ResolveSerials()
    {
        var wmi = WmiMonitorCache.Get();
        if (wmi.Count == 0) return;

        var groups = Monitors
            .Where(m => !string.IsNullOrEmpty(m.DeviceId))
            .GroupBy(m => WmiMonitorCache.TokenOf(m.DeviceId));

        foreach (var group in groups)
        {
            var matches = wmi
                .Where(w => w.Token.Equals(group.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var index = 0;
            foreach (var entry in group)
            {
                if (index < matches.Count)
                    entry.Serial = matches[index].Serial;
                index++;
            }
        }
    }

    public void Dispose()
    {
        foreach (var (array, count) in _owned)
            NativeDdc.DestroyPhysicalMonitors(count, array);
        _owned.Clear();
    }
}

public sealed class DdcMonitor
{
    public const byte VcpInputSource = 0x60;
    public const byte VcpPowerMode = 0xD6;

    private readonly string? _deviceString;
    private readonly string? _serial;
    private readonly string? _deviceId;

    public DdcMonitor(string? deviceString = null, string? serial = null, string? deviceId = null)
    {
        _deviceString = string.IsNullOrWhiteSpace(deviceString) ? null : deviceString;
        _serial = string.IsNullOrWhiteSpace(serial) ? null : serial;
        _deviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
    }

    private bool NeedsSerial => _serial != null;

    public static string DeviceIdPrefix(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return "";
        var parts = deviceId.Split('\\');
        return parts.Length >= 2 ? parts[0] + "\\" + parts[1] : deviceId;
    }

    public static bool IdentityMatches(string? cfgDeviceString, string? cfgSerial, string? cfgDeviceId,
        string? monDeviceString, string? monSerial, string? monDeviceId)
    {
        var any = false;
        if (!string.IsNullOrWhiteSpace(cfgDeviceString))
        {
            any = true;
            if (!string.Equals(monDeviceString, cfgDeviceString, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(cfgSerial))
        {
            any = true;
            if (!string.Equals(monSerial, cfgSerial, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(cfgDeviceId))
        {
            any = true;
            if (!string.Equals(DeviceIdPrefix(monDeviceId ?? ""), cfgDeviceId, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return any;
    }

    public static List<MonitorInfo> ListMonitors()
    {
        using var session = new DdcSession(resolveSerial: true);
        return session.Monitors.Select(ToInfo).ToList();
    }

    private static MonitorInfo ToInfo(MonitorEntry m) => new()
    {
        DeviceName = m.DeviceName,
        Description = m.Description,
        MonitorDeviceName = m.MonitorDeviceName,
        DeviceString = m.DeviceString,
        DeviceId = m.DeviceId,
        Serial = m.Serial,
        IsPrimary = m.IsPrimary
    };

    private bool MatchesIdentity(MonitorEntry entry) =>
        IdentityMatches(_deviceString, _serial, _deviceId, entry.DeviceString, entry.Serial, entry.DeviceId);

    private MonitorEntry? Find(DdcSession session)
    {
        var matches = session.Monitors.Where(MatchesIdentity).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public bool TryRead(uint vcpCode, out uint value, out uint max)
    {
        value = 0;
        max = 0;
        using var session = new DdcSession(NeedsSerial);
        var monitor = Find(session);
        if (monitor == null) return false;

        uint type = 0;
        return NativeDdc.GetVCPFeatureAndVCPFeatureReply(monitor.Handle, (byte)vcpCode, ref type, ref value, ref max);
    }

    public bool TryWrite(uint vcpCode, uint value)
    {
        using var session = new DdcSession(NeedsSerial);
        var monitor = Find(session);
        if (monitor == null) return false;
        return NativeDdc.SetVCPFeature(monitor.Handle, (byte)vcpCode, value);
    }

    public uint? GetInputSource() => TryRead(VcpInputSource, out var value, out _) ? value : null;

    public bool SetInputSource(uint value) => TryWrite(VcpInputSource, value);

    public bool TryGetCapabilities(out string capabilities)
    {
        capabilities = "";
        using var session = new DdcSession(NeedsSerial);
        var monitor = Find(session);
        if (monitor == null) return false;

        uint length = 0;
        if (!NativeDdc.GetCapabilitiesStringLength(monitor.Handle, ref length) || length == 0)
            return false;

        var buffer = new StringBuilder((int)length + 1);
        if (!NativeDdc.CapabilitiesRequestAndCapabilitiesReply(monitor.Handle, buffer, length))
            return false;

        capabilities = buffer.ToString();
        return true;
    }

    public bool Exists()
    {
        using var session = new DdcSession(NeedsSerial);
        return Find(session) != null;
    }
}
