using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MagicRemote.Win32
{
    struct ScreenRect
    {
        public int Left, Top, Width, Height;
    }

    /// <summary>Finds which monitor is the LG TV, using the name in its EDID (e.g. "LG TV SSCR2").</summary>
    static class Displays
    {
        static readonly Regex LgTvName = new Regex(@"^LG\b.*\b(TV|OLED|SSCR)", RegexOptions.IgnoreCase);
        static readonly object Gate = new object();
        static ScreenRect _cached;
        static string _cachedFor;
        static DateTime _cachedAt;

        sealed class Monitor
        {
            public string Device;   // \\.\DISPLAY1
            public string Name;     // LG TV SSCR2
            public ScreenRect Bounds;
            public bool Primary;
        }

        /// <param name="preference">Optional part of a monitor name or device ("\\.\DISPLAY2") from config.json.</param>
        public static ScreenRect TvMonitor(string preference)
        {
            lock (Gate)
            {
                // Pointer moves arrive ~60 times a second; monitors rarely change.
                if (_cachedFor == preference && DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(3)) return _cached;
                var monitors = EnumerateMonitors();
                var chosen =
                    (string.IsNullOrEmpty(preference) ? null : monitors.FirstOrDefault(m =>
                        string.Equals(m.Device, preference, StringComparison.OrdinalIgnoreCase) ||
                        (m.Name ?? "").IndexOf(preference, StringComparison.OrdinalIgnoreCase) >= 0))
                    ?? (monitors.Count == 1 ? monitors[0] : null)
                    ?? monitors.FirstOrDefault(m => m.Name != null && LgTvName.IsMatch(m.Name))
                    ?? monitors.FirstOrDefault(m => m.Primary)
                    ?? new Monitor { Bounds = VirtualScreen() };
                _cached = chosen.Bounds;
                _cachedFor = preference;
                _cachedAt = DateTime.UtcNow;
                return _cached;
            }
        }

        public static ScreenRect VirtualScreen() => new ScreenRect
        {
            Left = GetSystemMetrics(76), Top = GetSystemMetrics(77), Width = GetSystemMetrics(78), Height = GetSystemMetrics(79),
        };

        /// <summary>Monitor names for display in the UI, e.g. "LG TV SSCR2 (\\.\DISPLAY2)".</summary>
        public static IEnumerable<string> Describe() => EnumerateMonitors().Select(m => (m.Name ?? "Unknown") + " (" + m.Device + ")");

        static List<Monitor> EnumerateMonitors()
        {
            var names = FriendlyNames();
            var list = new List<Monitor>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr data) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (GetMonitorInfo(h, ref info))
                {
                    names.TryGetValue(info.szDevice, out var name);
                    list.Add(new Monitor
                    {
                        Device = info.szDevice,
                        Name = name,
                        Primary = (info.dwFlags & 1) != 0,
                        Bounds = new ScreenRect
                        {
                            Left = info.rcMonitor.Left, Top = info.rcMonitor.Top,
                            Width = info.rcMonitor.Right - info.rcMonitor.Left, Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                        },
                    });
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>GDI device name → EDID monitor name, through the Display Configuration API.</summary>
        static Dictionary<string, string> FriendlyNames()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return result;
                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

                for (int i = 0; i < pathCount; i++)
                {
                    var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    source.header.type = 1; // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
                    source.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    source.header.adapterId = paths[i].sourceInfo.adapterId;
                    source.header.id = paths[i].sourceInfo.id;

                    var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
                    target.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    target.header.adapterId = paths[i].targetInfo.adapterId;
                    target.header.id = paths[i].targetInfo.id;

                    if (DisplayConfigGetDeviceInfo(ref source) == 0 && DisplayConfigGetDeviceInfo(ref target) == 0 &&
                        !string.IsNullOrEmpty(target.monitorFriendlyDeviceName))
                        result[source.viewGdiDeviceName] = target.monitorFriendlyDeviceName;
                }
            }
            catch (EntryPointNotFoundException) { }
            return result;
        }

        const uint QDC_ONLY_ACTIVE_PATHS = 2;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
            public uint refreshNumerator, refreshDenominator;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        struct DISPLAYCONFIG_MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags, outputTechnology;
            public ushort edidManufactureId, edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint modeCount, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
    }
}
