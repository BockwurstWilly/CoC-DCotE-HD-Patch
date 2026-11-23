using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CoCHD
{
    /// <summary>
    /// Every setting the launcher offers, read from and written to the place that owns it:
    /// Engine\scripts\CoCHD.ini (display, frame rate, filtering, mouse, gamepad), the widescreen fix's
    /// ini, the game's registry key (anti-aliasing, subtitles, language, resolution) and the intro videos.
    /// </summary>
    class PatchSettings
    {
        public const string CocHdIni = @"Engine\scripts\CoCHD.ini";
        public const string WfpIni = @"Engine\scripts\CallOfCthulhu.WidescreenFix.ini";
        public static readonly string[] IntroVideos =
        {
            "beth_logo.wmv", "hf_logo.wmv", "cocdcote_logo.wmv", "cocdcote_logo_french.wmv", "cocdcote_logo_german.wmv",
            "legal.wmv", "legal_french.wmv", "legal_german.wmv", "warning.wmv", "warning_french.wmv", "warning_german.wmv",
        };

        public string Dir;

        // display
        public string Resolution = "auto";      // auto | WxH
        public string Mode = "fullscreen";      // fullscreen | borderless | windowed
        public bool VSync = true;
        public int Anisotropy = 16;
        public int Msaa = 4;                     // D3DMULTISAMPLE_TYPE: 0, 2, 4, 8
        public bool StretchFullscreenImages = true;
        public bool FilmGrain = true;

        // mouse
        public bool ModernMouse = true;         // [Mouse] Mode=sens + FrameIndependent=1, else original
        public double Sens = 2.0;               // Counter-Strike scale: 0.022 deg per count x Sens

        // gamepad
        public bool Pad = true;
        public double PadLook = 200;            // degrees per second at full tilt
        public bool PadInvert;

        // game
        public bool Subtitles = true;
        public string Language = "English";     // English | Deutsch | Francais
        public bool SkipIntro;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static PatchSettings Load(string dir)
        {
            var s = new PatchSettings { Dir = dir };

            var hd = IniDoc.Load(Path.Combine(dir, CocHdIni));
            s.Resolution = hd.Get("Display", "Resolution", s.Resolution);
            s.Mode = hd.Get("Display", "Mode", s.Mode).ToLowerInvariant();
            s.VSync = ParseInt(hd.Get("Display", "VSync"), 1) != 0;
            s.Anisotropy = ParseInt(hd.Get("Display", "Anisotropy"), s.Anisotropy);
            s.FilmGrain = ParseInt(hd.Get("Display", "FilmGrain"), 1) != 0;
            s.ModernMouse = !string.Equals(hd.Get("Mouse", "Mode", "sens"), "off", StringComparison.OrdinalIgnoreCase);
            s.Sens = ParseDouble(hd.Get("Mouse", "Sens"), s.Sens);
            s.Pad = ParseInt(hd.Get("Pad", "Enabled"), 1) != 0;
            s.PadLook = ParseDouble(hd.Get("Pad", "LookSpeed"), s.PadLook);
            s.PadInvert = ParseInt(hd.Get("Pad", "InvertY"), 0) != 0;

            var wfp = IniDoc.Load(Path.Combine(dir, WfpIni));
            s.StretchFullscreenImages = ParseInt(wfp.Get("MAIN", "StretchFullscreenImages"), 1) != 0;

            using (var k = Registry.CurrentUser.OpenSubKey(Game.RegSettings))
            {
                if (k != null)
                {
                    s.Msaa = k.GetValue("MultisampleMode") is int ? (int)k.GetValue("MultisampleMode") : s.Msaa;
                    s.Subtitles = !(k.GetValue("ActivateSubtitles") is int) || (int)k.GetValue("ActivateSubtitles") != 0;
                    s.Language = k.GetValue("Language") as string ?? s.Language;
                }
            }
            s.SkipIntro = IntrosHidden(dir);
            return s;
        }

        public void Save()
        {
            var hd = IniDoc.Load(Path.Combine(Dir, CocHdIni));
            hd.Set("Display", "Resolution", Resolution);
            hd.Set("Display", "Mode", Mode);
            hd.Set("Display", "VSync", VSync ? "1" : "0");
            hd.Set("Display", "FPSLimit", "60");
            hd.Set("Display", "Anisotropy", Anisotropy.ToString(Inv));
            hd.Set("Display", "FilmGrain", FilmGrain ? "1" : "0");
            hd.Set("Mouse", "Mode", ModernMouse ? "sens" : "off");
            hd.Set("Mouse", "Sens", Sens.ToString("0.00", Inv));
            hd.Set("Mouse", "FrameIndependent", ModernMouse ? "1" : "0");
            hd.Set("Pad", "Enabled", Pad ? "1" : "0");
            hd.Set("Pad", "LookSpeed", PadLook.ToString("0", Inv));
            hd.Set("Pad", "InvertY", PadInvert ? "1" : "0");
            hd.Save();

            var wfp = IniDoc.Load(Path.Combine(Dir, WfpIni));
            wfp.Set("MAIN", "StretchFullscreenImages", StretchFullscreenImages ? "1" : "0");
            wfp.Save();

            WriteRegistry(Resolution, Msaa, Subtitles, Language);
            SetIntrosHidden(Dir, SkipIntro);
        }

        /// <summary>
        /// The game's own settings. Resolution is written as well, so the values are right even
        /// without the patch (CoCHD.asi answers the game's reads itself when set to auto).
        /// </summary>
        public static void WriteRegistry(string resolution, int msaa, bool subtitles, string language)
        {
            var m = resolution == "auto" ? Display.Auto() : ParseMode(resolution);
            using (var k = Registry.CurrentUser.CreateSubKey(Game.RegSettings))
            {
                k.SetValue("VideoModeSet", 1, RegistryValueKind.DWord);
                k.SetValue("ScreenWidth", m.Width, RegistryValueKind.DWord);
                k.SetValue("ScreenHeight", m.Height, RegistryValueKind.DWord);
                k.SetValue("RefreshRate", 60, RegistryValueKind.DWord);
                k.SetValue("MultisampleMode", msaa, RegistryValueKind.DWord);
                k.SetValue("ActivateSubtitles", subtitles ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("Language", language, RegistryValueKind.String);
                if (k.GetValue("Adapter") == null)
                    k.SetValue("Adapter", 0, RegistryValueKind.DWord);
                k.SetValue("Executable", "CoCMainWin32.exe", RegistryValueKind.String);
            }
        }

        public static DisplayMode ParseMode(string s)
        {
            var p = (s ?? "").ToLowerInvariant().Split('x');
            int w, h;
            if (p.Length == 2 && int.TryParse(p[0].Trim(), out w) && int.TryParse(p[1].Trim(), out h) && w > 0 && h > 0)
                return new DisplayMode { Width = Math.Min(w, Display.HudMaxWidth), Height = h };
            return Display.Auto();
        }

        // ---- intro videos: moved aside into the backup folder, never deleted ----------------------

        static string HiddenVideoDir(string dir) { return Path.Combine(dir, Game.BackupDir, "pcvideo"); }

        public static bool IntrosHidden(string dir)
        {
            return IntroVideos.Any(v => File.Exists(Path.Combine(HiddenVideoDir(dir), v)));
        }

        public static void SetIntrosHidden(string dir, bool hide)
        {
            var videos = Path.Combine(dir, Game.VideoDir);
            var hidden = HiddenVideoDir(dir);
            foreach (var v in IntroVideos)
            {
                string from = Path.Combine(hide ? videos : hidden, v), to = Path.Combine(hide ? hidden : videos, v);
                if (!File.Exists(from) || File.Exists(to))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Move(from, to);
            }
            if (!hide && Directory.Exists(hidden) && !Directory.EnumerateFileSystemEntries(hidden).Any())
                Directory.Delete(hidden);
        }

        static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, Inv, out v) ? v : fallback;
        }

        static double ParseDouble(string s, double fallback)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, Inv, out v) && v > 0 ? v : fallback;
        }
    }
}
