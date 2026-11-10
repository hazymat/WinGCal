using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace WinGCal.Shell;

// One connected monitor, identified the way Windows does: EDID maker, product and serial (SPEC.md › Monitor setups)
public record Monitor(string Id, string Name, string GdiName, System.Drawing.Rectangle Bounds, System.Drawing.Rectangle WorkArea, bool Internal);

// The set of monitors connected right now. The same monitors give the same key whichever port or order they come up in
public static class MonitorSetup
{
    public static List<Monitor> Current()
    {
        var monitors = new List<Monitor>();
        try
        {
            if (GetDisplayConfigBufferSizes(OnlyActivePaths, out var pathCount, out var modeCount) != 0) return Fallback();
            var paths = new PathInfo[pathCount]; var modes = new ModeInfo[modeCount];
            if (QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return Fallback();
            var screens = System.Windows.Forms.Screen.AllScreens;
            foreach (var path in paths.Take((int)pathCount))
            {
                var target = new TargetDeviceName { Header = new DeviceInfoHeader { Type = GetTargetName, Size = (uint)Marshal.SizeOf<TargetDeviceName>(), AdapterId = path.TargetInfo.AdapterId, Id = path.TargetInfo.Id } };
                var source = new SourceDeviceName { Header = new DeviceInfoHeader { Type = GetSourceName, Size = (uint)Marshal.SizeOf<SourceDeviceName>(), AdapterId = path.SourceInfo.AdapterId, Id = path.SourceInfo.Id } };
                if (DisplayConfigGetDeviceInfo(ref target) != 0 || DisplayConfigGetDeviceInfo(ref source) != 0) continue;
                var screen = screens.FirstOrDefault(candidate => string.Equals(candidate.DeviceName, source.ViewGdiDeviceName, StringComparison.OrdinalIgnoreCase));
                if (screen == null) continue;
                var isInternal = target.OutputTechnology is 0x80000000 or 11 or 13; // internal, embedded DisplayPort, UDI embedded
                var name = target.MonitorFriendlyDeviceName is { Length: > 0 } friendly ? friendly : isInternal ? "Built-in display" : "Monitor";
                monitors.Add(new Monitor(IdFor(target), name, source.ViewGdiDeviceName, screen.Bounds, screen.WorkingArea, isInternal));
            }
        }
        catch { return Fallback(); }
        // Two identical monitors with no serial would share an id: number the repeats
        foreach (var group in monitors.GroupBy(monitor => monitor.Id).Where(group => group.Count() > 1).ToList())
        {
            var index = 0;
            foreach (var repeat in group.OrderBy(monitor => monitor.Bounds.X).ThenBy(monitor => monitor.Bounds.Y).ToList()) monitors[monitors.IndexOf(repeat)] = repeat with { Id = repeat.Id + "#" + index++ };
        }
        return monitors.Count > 0 ? monitors : Fallback();
    }

    public static string KeyOf(List<Monitor> monitors) => string.Join("+", monitors.Select(monitor => monitor.Id).OrderBy(id => id, StringComparer.Ordinal));
    public static string NameOf(List<Monitor> monitors) => string.Join(" + ", monitors.OrderByDescending(monitor => monitor.Internal).ThenBy(monitor => monitor.Bounds.X).Select(monitor => monitor.Name));

    // Hardware id (e.g. DEL41F3) plus the EDID serial from the registry, so the same monitor on another port or dock is still the same monitor
    static string IdFor(TargetDeviceName target)
    {
        var maker = DecodeMaker(target.EdidManufactureId);
        var hardwareId = $"{maker}{target.EdidProductCodeId:X4}";
        var serial = "";
        // \\?\DISPLAY#DEL41F3#5&1a2b3c&0&UID4352#{guid}: the middle parts name the registry key holding the EDID
        var parts = target.MonitorDevicePath.Split('#');
        if (parts.Length >= 3)
        {
            hardwareId = parts[1];
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
                if (key?.GetValue("EDID") is byte[] edid && edid.Length >= 128) serial = EdidSerial(edid);
            }
            catch { }
            if (serial == "") serial = "port" + parts[2].GetHashCode().ToString("X8"); // no serial: tell the same model apart by where it's plugged in
        }
        return serial == "" ? hardwareId : $"{hardwareId}-{serial}";
    }

    // The serial-number text descriptor if there is one, else the 32-bit serial at bytes 12-15 (often zero)
    static string EdidSerial(byte[] edid)
    {
        for (var offset = 54; offset + 18 <= 126; offset += 18)
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFF) return new string(edid.Skip(offset + 5).Take(13).Select(value => (char)value).ToArray()).Split('\n')[0].Trim();
        var number = BitConverter.ToUInt32(edid, 12);
        return number == 0 ? "" : number.ToString("X8");
    }
    static string DecodeMaker(ushort code) { var swapped = (code >> 8) | ((code & 0xFF) << 8); return new string(new[] { (char)('A' - 1 + ((swapped >> 10) & 31)), (char)('A' - 1 + ((swapped >> 5) & 31)), (char)('A' - 1 + (swapped & 31)) }); }

    // Without the display config API: screens by device name and size (less exact, still per setup)
    static List<Monitor> Fallback() => System.Windows.Forms.Screen.AllScreens.Select(screen => new Monitor($"{screen.DeviceName}-{screen.Bounds.Width}x{screen.Bounds.Height}", screen.Primary ? "Main display" : "Display", screen.DeviceName, screen.Bounds, screen.WorkingArea, false)).ToList();

    // ---- Windows display configuration API ----
    const uint OnlyActivePaths = 2, GetSourceName = 1, GetTargetName = 2;
    [StructLayout(LayoutKind.Sequential)] struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] struct PathSourceInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIndex; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct PathTargetInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIndex; public uint OutputTechnology; public uint Rotation; public uint Scaling; public uint RefreshNumerator; public uint RefreshDenominator; public uint ScanLineOrdering; public int TargetAvailable; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct PathInfo { public PathSourceInfo SourceInfo; public PathTargetInfo TargetInfo; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, Size = 64)] struct ModeInfo { public uint InfoType; public uint Id; public Luid AdapterId; }
    [StructLayout(LayoutKind.Sequential)] struct DeviceInfoHeader { public uint Type; public uint Size; public Luid AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TargetDeviceName
    {
        public DeviceInfoHeader Header; public uint Flags; public uint OutputTechnology; public ushort EdidManufactureId; public ushort EdidProductCodeId; public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SourceDeviceName { public DeviceInfoHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName; }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);
}
