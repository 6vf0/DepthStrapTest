using System.Runtime.InteropServices;

namespace Bloxstrap.Roblox
{
    internal static class MonitorRefreshRateService
    {
        internal static int? LastDetectedHz { get; private set; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public ushort SpecVersion, DriverVersion, Size, DriverExtra;
            public uint Fields;
            public int X, Y;
            public uint Orientation, FixedOutput;
            public short Color, Duplex, YResolution, TTOption, Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
            public ushort LogPixels;
            public uint BitsPerPixel, Width, Height, Flags, Frequency;
            public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice result, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string device, int mode, ref DisplayMode result);

        /// <summary>Highest supported Hz at the current resolution of an active display. Does not change Windows display mode.</summary>
        public static int? DetectMaximumHz()
        {
            try
            {
                int maximum = 0;
                for (uint index = 0; index < 32; index++)
                {
                    var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                    if (!EnumDisplayDevices(null, index, ref device, 0)) break;
                    if ((device.Flags & 1) == 0 || (device.Flags & 8) != 0) continue; // inactive/mirroring driver
                    var current = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
                    if (!EnumDisplaySettings(device.Name, -1, ref current)) continue;
                    maximum = Math.Max(maximum, (int)current.Frequency);
                    for (int modeIndex = 0; modeIndex < 4096; modeIndex++)
                    {
                        var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
                        if (!EnumDisplaySettings(device.Name, modeIndex, ref mode)) break;
                        if (mode.Width == current.Width && mode.Height == current.Height)
                            maximum = Math.Max(maximum, (int)mode.Frequency);
                    }
                }
                return maximum is >= 24 and <= 1000 ? maximum : null;
            }
            catch (Exception ex) { App.Logger.WriteException("MonitorRefreshRate", ex); return null; }
        }

        public static void ApplyDetectedCap()
        {
            if (!App.Settings.Prop.MatchFpsToMonitorRefreshRate) return;
            int? hz = DetectMaximumHz();
            LastDetectedHz = hz;
            if (hz is null) return; // retain the user's cap when display data is unavailable
            App.Settings.Prop.CompetitiveFpsCap = hz.Value;
        }
    }
}
