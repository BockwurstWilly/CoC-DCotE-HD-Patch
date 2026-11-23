using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CoCHD
{
    struct DisplayMode
    {
        public int Width, Height, Hz;
        public override string ToString() { return Width + " × " + Height; }
    }

    /// <summary>Display modes of the primary monitor - the same choice CoCHD.asi makes for "auto".</summary>
    static class Display
    {
        /// <summary>The HUD stores x*10 in a 16-bit value: wider than this breaks the inventory.</summary>
        public const int HudMaxWidth = 3276;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

        const int ENUM_CURRENT_SETTINGS = -1;

        static IEnumerable<DEVMODE> Raw()
        {
            for (int i = 0; ; i++)
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (!EnumDisplaySettings(null, i, ref dm))
                    yield break;
                yield return dm;
            }
        }

        public static DisplayMode Current()
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm);
            return new DisplayMode { Width = dm.dmPelsWidth, Height = dm.dmPelsHeight, Hz = dm.dmDisplayFrequency };
        }

        /// <summary>All 32-bit resolutions the HUD can handle, largest first.</summary>
        public static List<DisplayMode> Modes()
        {
            return Raw().Where(d => d.dmBitsPerPel == 32 && d.dmPelsWidth <= HudMaxWidth && d.dmPelsWidth >= 800)
                .GroupBy(d => new { d.dmPelsWidth, d.dmPelsHeight })
                .Select(g => new DisplayMode { Width = g.Key.dmPelsWidth, Height = g.Key.dmPelsHeight, Hz = g.Max(x => x.dmDisplayFrequency) })
                .OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Width)
                .ToList();
        }

        /// <summary>What "auto" renders at: the monitor's resolution, or below the HUD limit with the same aspect ratio.</summary>
        public static DisplayMode Auto()
        {
            var cur = Current();
            if (cur.Width <= HudMaxWidth)
                return cur;
            double aspect = (double)cur.Width / cur.Height;
            var best = Raw().Where(d => d.dmBitsPerPel == 32 && d.dmPelsWidth <= HudMaxWidth &&
                                        Math.Abs((double)d.dmPelsWidth / d.dmPelsHeight - aspect) <= 0.02)
                .OrderByDescending(d => d.dmPelsWidth * d.dmPelsHeight).FirstOrDefault();
            if (best.dmPelsWidth == 0)
                return new DisplayMode { Width = cur.Width / 2, Height = cur.Height / 2 };
            return new DisplayMode { Width = best.dmPelsWidth, Height = best.dmPelsHeight };
        }
    }

    /// <summary>The speaker setup Windows reports to DirectSound - FMOD Ex mixes the game's 3D sound for it.</summary>
    static class Speakers
    {
        [ComImport, Guid("279AFA83-4981-11CE-A521-0020AF0BE560"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDirectSound
        {
            void CreateSoundBuffer();
            void GetCaps();
            void DuplicateSoundBuffer();
            void SetCooperativeLevel();
            void Compact();
            [PreserveSig] int GetSpeakerConfig(out uint config);
        }

        [DllImport("dsound.dll")]
        static extern int DirectSoundCreate(IntPtr guid, out IDirectSound ds, IntPtr outer);

        /// <summary>Text key of the configuration (spk_stereo, spk_51, ...), or null if unknown.</summary>
        public static string Config()
        {
            try
            {
                IDirectSound ds;
                uint cfg;
                if (DirectSoundCreate(IntPtr.Zero, out ds, IntPtr.Zero) != 0 || ds == null)
                    return null;
                try
                {
                    if (ds.GetSpeakerConfig(out cfg) != 0)
                        return null;
                }
                finally
                {
                    Marshal.ReleaseComObject(ds);
                }
                switch (cfg & 0xFF)
                {
                    case 1: return "spk_headphone";
                    case 2: return "spk_mono";
                    case 3: return "spk_quad";
                    case 4: return "spk_stereo";
                    case 5: return "spk_surround";
                    case 6: case 9: return "spk_51";
                    case 7: case 8: return "spk_71";
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Is a gamepad connected that CoCHD.asi will use: XInput, or a DualShock 4 / DualSense (HID)?</summary>
    static class Gamepad
    {
        public enum Kind { None, Xbox, PlayStation }

        public static Kind Detect()
        {
            if (Connected())
                return Kind.Xbox;
            return PlayStationConnected() ? Kind.PlayStation : Kind.None;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(Microsoft.Win32.SafeHandles.SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        // DualShock 4 (v1, v2, wireless adapter), DualSense, DualSense Edge - the same list as CoCHD.asi
        static readonly ushort[] SonyPads = { 0x05C4, 0x09CC, 0x0BA0, 0x0CE6, 0x0DF2 };

        static bool PlayStationConnected()
        {
            Guid guid;
            try { HidD_GetHidGuid(out guid); } catch (Exception) { return false; }
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12 /* PRESENT | DEVICEINTERFACE */);
            if (set == new IntPtr(-1))
                return false;
            try
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                IntPtr detail = Marshal.AllocHGlobal(2048);
                try
                {
                    for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize is 8 on 64-bit, 6 on 32-bit processes
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        int required;
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, 2048, out required, IntPtr.Zero))
                            continue;
                        string path = Marshal.PtrToStringUni(detail + 4);
                        using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (h.IsInvalid)
                                continue;
                            var at = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                            if (HidD_GetAttributes(h, ref at) && at.VendorID == 0x054C && Array.IndexOf(SonyPads, at.ProductID) >= 0)
                                return true;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public ushort wButtons;
            public byte bLeftTrigger, bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern int GetState14(int index, out XINPUT_STATE state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern int GetState910(int index, out XINPUT_STATE state);

        public static bool Connected()
        {
            for (int i = 0; i < 4; i++)
            {
                XINPUT_STATE s;
                try
                {
                    if (GetState14(i, out s) == 0)
                        return true;
                }
                catch (DllNotFoundException)
                {
                    try
                    {
                        if (GetState910(i, out s) == 0)
                            return true;
                    }
                    catch (DllNotFoundException)
                    {
                        return false;
                    }
                }
            }
            return false;
        }
    }

    static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int pid);
    }
}
