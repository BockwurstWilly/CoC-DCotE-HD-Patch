using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace CoCHD
{
    class InstallOptions
    {
        public bool DesktopShortcut = true;
        public bool StartMenuShortcut = true;
        public bool LargeAddressAware = true;
        public bool SkipIntro = true;
    }

    /// <summary>Install / uninstall. Every step logs; any exception aborts before the next step.</summary>
    class Installer
    {
        public const string PatchDir = "_HDPatch";
        public const string SetupCopy = "CoCHD_Setup.exe";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CoCDCotEHDPatch";
        public const string ShortcutName = "Call of Cthulhu - HD Launcher.lnk";
        // start menu entry that runs the uninstaller (both languages, so either one is removed again)
        static readonly string[] UninstallShortcuts = { "Call of Cthulhu – HD Patch entfernen.lnk", "Call of Cthulhu - Remove HD Patch.lnk" };
        const string Resource = "payload/";

        // files of the GOG release the patch replaces or changes; backed up before the first install
        static readonly string[] Replaced = { Game.LauncherExe, Game.Exe, @"Engine\dinput8.dll" };
        // files the patch adds (removed again on uninstall, unless restored from the backup)
        static readonly string[] Added =
        {
            @"Engine\dinput8.dll", @"Engine\scripts\CoCHD.asi", @"Engine\scripts\CoCHD.ini", @"Engine\scripts\CoCHD.log",
            @"Engine\scripts\CallOfCthulhu.WidescreenFix.asi", @"Engine\scripts\CallOfCthulhu.WidescreenFix.ini",
            @"Engine\scripts\CallOfCthulhu.WidescreenFix.log", ShortcutName,
        };
        // settings files a reinstall/update must not reset
        static readonly string[] UserSettings = { PatchSettings.CocHdIni, PatchSettings.WfpIni };

        readonly Action<string> log;
        public Installer(Action<string> log) { this.log = log; }

        public static bool IsInstalled(string dir)
        {
            return dir != null && Directory.Exists(Path.Combine(dir, Game.BackupDir)) &&
                   File.Exists(Path.Combine(dir, Game.BackupDir, Game.LauncherExe));
        }

        // ---- install ---------------------------------------------------------------------------------

        public void Install(string dir, InstallOptions o)
        {
            if (Game.IsRunning())
                throw new InvalidOperationException(T.Get("err_running"));
            var check = Game.Check(dir);
            if (!check.Ok)
                throw new InvalidOperationException(T.Get("err_check") + "\n" + string.Join("\n", check.Problems));
            bool update = IsInstalled(dir);

            // 1. back up the GOG originals (only once: on an update the backup already holds them)
            var backup = Path.Combine(dir, Game.BackupDir);
            if (!update)
            {
                foreach (var name in Replaced)
                    BackUp(dir, backup, name);
                log(T.Get("log_backup", backup));
            }
            else
            {
                log(T.Get("log_update"));
            }

            // 2. payload: Engine\... into the game, doc\... into _HDPatch
            var asm = Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames().Where(r => r.StartsWith(Resource)).OrderBy(r => r))
            {
                var rel = res.Substring(Resource.Length).Replace('/', '\\');
                string target = rel.StartsWith(@"doc\") ? Path.Combine(dir, PatchDir, rel.Substring(4)) : Path.Combine(dir, rel);
                if (UserSettings.Contains(rel, StringComparer.OrdinalIgnoreCase) && File.Exists(target))
                {
                    log(T.Get("log_keep", rel));
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var s = asm.GetManifestResourceStream(res))
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    FileUtil.WriteAtomic(target, ms.ToArray());
                }
                Unblock(target);
                log(T.Get("log_copy", rel));
            }

            // 3. Large Address Aware: one header bit of the exe (the original is in the backup)
            var exe = Path.Combine(dir, Game.Exe);
            var bytes = File.ReadAllBytes(exe);
            if (o.LargeAddressAware != Game.IsLargeAddressAware(bytes) && Game.SetLargeAddressAware(bytes, o.LargeAddressAware))
            {
                FileUtil.WriteAtomic(exe, bytes);
                if (o.LargeAddressAware)
                    log(T.Get("log_laa"));
            }

            // 4. the game's own settings: a fresh install starts with the patch's defaults
            if (!update)
            {
                bool subtitles = true;
                string language = null;
                using (var k = Registry.CurrentUser.OpenSubKey(Game.RegSettings))
                {
                    if (k != null)
                    {
                        subtitles = !(k.GetValue("ActivateSubtitles") is int) || (int)k.GetValue("ActivateSubtitles") != 0;
                        language = k.GetValue("Language") as string;
                    }
                }
                if (string.IsNullOrEmpty(language))
                {
                    var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    language = ui == "de" ? "Deutsch" : ui == "fr" ? "Francais" : "English";
                }
                PatchSettings.WriteRegistry("auto", 4, subtitles, language);
                log(T.Get("log_registry"));
            }

            // 5. intro videos
            if (o.SkipIntro)
            {
                PatchSettings.SetIntrosHidden(dir, true);
                log(T.Get("log_intro"));
            }

            // 6. uninstaller copy, shortcuts, Apps & Features entry
            var self = Assembly.GetExecutingAssembly().Location;
            var copy = Path.Combine(dir, PatchDir, SetupCopy);
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, copy, true);
            var launcher = Path.Combine(dir, Game.LauncherExe);
            var icon = Path.Combine(dir, Game.Exe);
            var engine = Path.Combine(dir, Game.EngineDir);
            Shortcut.Create(Path.Combine(dir, ShortcutName), launcher, engine, icon);
            if (o.StartMenuShortcut)
            {
                Shortcut.Create(Path.Combine(StartMenuDir(), ShortcutName), launcher, engine, icon);
                Shortcut.Create(Path.Combine(StartMenuDir(), T.Get("lnk_uninstall") + ".lnk"), copy, dir, icon, "/uninstall");
            }
            if (o.DesktopShortcut)
                Shortcut.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName), launcher, engine, icon);
            log(T.Get("log_shortcuts"));
            Register(dir);
            log(T.Get("log_done"));
        }

        static void BackUp(string dir, string backup, string name)
        {
            var src = Path.Combine(dir, name);
            if (!File.Exists(src))
                return;
            var dst = Path.Combine(backup, name);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
        }

        static string StartMenuDir()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        }

        static void Register(string dir)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Call of Cthulhu: Dark Corners of the Earth - HD Patch");
                k.SetValue("DisplayVersion", Version.Text);
                k.SetValue("Publisher", "BockwurstWilly");
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", Path.Combine(dir, Game.Exe));
                k.SetValue("UninstallString", "\"" + Path.Combine(dir, PatchDir, SetupCopy) + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 4000, RegistryValueKind.DWord);
            }
        }

        // ---- uninstall -------------------------------------------------------------------------------

        public void Uninstall(string dir)
        {
            if (Game.IsRunning())
                throw new InvalidOperationException(T.Get("err_running"));
            var backup = Path.Combine(dir, Game.BackupDir);
            if (!IsInstalled(dir))
                throw new InvalidOperationException(T.Get("err_nobackup"));

            // intro videos come back first: they live in the backup folder
            PatchSettings.SetIntrosHidden(dir, false);
            foreach (var name in Added)
                Delete(Path.Combine(dir, name));
            foreach (var name in Replaced)
            {
                var b = Path.Combine(backup, name);
                if (File.Exists(b))
                {
                    CopyRetrying(b, Path.Combine(dir, name));
                    log(T.Get("log_restore", name));
                }
            }
            var scripts = Path.Combine(dir, @"Engine\scripts");
            if (Directory.Exists(scripts) && !Directory.EnumerateFileSystemEntries(scripts).Any())
                Directory.Delete(scripts);
            // shortcuts and the Apps & Features entry are shared by name: only remove them when they belong to this folder
            DeleteShortcut(Path.Combine(StartMenuDir(), ShortcutName), dir);
            DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName), dir);
            foreach (var name in UninstallShortcuts)
                DeleteShortcut(Path.Combine(StartMenuDir(), name), dir);
            Directory.Delete(backup, true);
            var patch = Path.Combine(dir, PatchDir);
            try
            {
                if (Directory.Exists(patch))
                    Directory.Delete(patch, true);
            }
            catch (Exception ex)
            {
                // only the folder of a still-running uninstaller copy can be locked; the game is clean
                log(T.Get("log_leftover", patch, ex.Message));
            }
            if (RegisteredFor(dir))
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            log(T.Get("log_uninstalled"));
        }

        /// <summary>The launcher that started the uninstall may still be closing: give it a few seconds.</summary>
        static void CopyRetrying(string from, string to)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Copy(from, to, true);
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 25)
                        throw;
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        void DeleteShortcut(string lnk, string dir)
        {
            if (File.Exists(lnk) && IsInside(Shortcut.Target(lnk), dir))
                Delete(lnk);
        }

        static bool RegisteredFor(string dir)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return k != null && IsInside(k.GetValue("InstallLocation") as string, dir);
        }

        static bool IsInside(string path, string dir)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            var root = Path.GetFullPath(dir).TrimEnd('\\') + '\\';
            return (Path.GetFullPath(path).TrimEnd('\\') + '\\').StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                log(T.Get("log_delete", Path.GetFileName(path)));
            }
        }

        // ---- helpers ---------------------------------------------------------------------------------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool DeleteFile(string path);

        /// <summary>Drop the "downloaded from the internet" mark so Windows loads the DLLs without prompts.</summary>
        static void Unblock(string path)
        {
            DeleteFile(path + ":Zone.Identifier");
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, Game.EngineDir, "_hdpatch_write_test.tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsElevated()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    static class Shortcut
    {
        public static void Create(string lnk, string target, string workDir, string icon, string arguments = "")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = target;
                sc.Arguments = arguments;
                sc.WorkingDirectory = workDir;
                sc.IconLocation = icon + ",0";
                sc.Description = "Call of Cthulhu: Dark Corners of the Earth - HD Launcher";
                sc.Save();
                Marshal.FinalReleaseComObject(sc);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        public static string Target(string lnk)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                dynamic sc = shell.CreateShortcut(lnk);
                string target = sc.TargetPath;
                Marshal.FinalReleaseComObject(sc);
                return target;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
