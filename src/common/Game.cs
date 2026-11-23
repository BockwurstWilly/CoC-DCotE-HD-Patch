using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace CoCHD
{
    /// <summary>Locating and validating the GOG installation of Call of Cthulhu: Dark Corners of the Earth.</summary>
    static class Game
    {
        public const string GogId = "1189711155";
        public const string EngineDir = "Engine";
        public const string Exe = @"Engine\CoCMainWin32.exe";
        public const string LauncherExe = @"Engine\CoCDCoTELauncher.exe";
        public const string BackupDir = "_HDPatch_Backup";
        public const string RegSettings = @"Software\Bethesda Softworks\Call Of Cthulhu DCoTE\Settings";
        public const string VideoDir = @"Development\pcvideo";

        // SHA-1 of the executables this patch was tested against (with the Large Address Aware bit cleared,
        // so the hash is the same before and after the setup sets it)
        static readonly Dictionary<string, string> TestedBuilds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "84AD74871D8F1BCB3B647271E08963C7B24B48A6", "CoCMainWin32.exe GOG 1.0(A)" },
        };

        public static string FindInstallDir()
        {
            foreach (var candidate in Candidates())
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;
                var dir = candidate.Trim().TrimEnd('\\', '"');
                try
                {
                    if (File.Exists(Path.Combine(dir, Exe)))
                        return Path.GetFullPath(dir);
                }
                catch (ArgumentException)
                {
                }
            }
            return null;
        }

        static IEnumerable<string> Candidates()
        {
            // the folder this program runs from: Engine (replaced launcher) or _HDPatch (uninstaller copy)
            var self = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            yield return self;
            yield return Path.GetDirectoryName(self);

            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    {
                        yield return ReadValue(root, @"SOFTWARE\GOG.com\Games\" + GogId, "path");
                        yield return ReadValue(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + GogId + "_is1", "InstallLocation");
                        yield return ReadValue(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 22340", "InstallLocation");
                    }
                }
            }
        }

        static string ReadValue(RegistryKey root, string path, string name)
        {
            try
            {
                using (var key = root.OpenSubKey(path))
                    return key == null ? null : key.GetValue(name) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsRunning()
        {
            return Process.GetProcessesByName("CoCMainWin32").Length > 0;
        }

        // ---- executable ------------------------------------------------------------------------

        static int CharacteristicsOffset(byte[] exe)
        {
            if (exe.Length < 0x40 || exe[0] != 'M' || exe[1] != 'Z')
                return -1;
            int pe = BitConverter.ToInt32(exe, 0x3C);
            if (pe <= 0 || pe + 0x18 > exe.Length || exe[pe] != 'P' || exe[pe + 1] != 'E')
                return -1;
            return pe + 0x16;
        }

        public static bool IsLargeAddressAware(byte[] exe)
        {
            int at = CharacteristicsOffset(exe);
            return at > 0 && (exe[at] & 0x20) != 0;
        }

        /// <summary>Sets the Large Address Aware flag: 4 GB instead of 2 GB for the 32-bit game (HD texture mods).</summary>
        public static bool SetLargeAddressAware(byte[] exe, bool on)
        {
            int at = CharacteristicsOffset(exe);
            if (at < 0)
                return false;
            exe[at] = on ? (byte)(exe[at] | 0x20) : (byte)(exe[at] & ~0x20);
            return true;
        }

        public static string NormalizedSha1(byte[] exe)
        {
            var copy = (byte[])exe.Clone();
            SetLargeAddressAware(copy, false);
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(copy)).Replace("-", "");
        }

        /// <summary>
        /// Checks that the folder holds a game build the patch understands. Signatures (the code the
        /// fixes rely on) decide; the hash list only tells "tested" from "compatible".
        /// </summary>
        public static GameCheck Check(string dir)
        {
            var c = new GameCheck();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                c.Problems.Add(T.Get("chk_nodir"));
                return c;
            }
            var exe = Path.Combine(dir, Exe);
            if (!File.Exists(exe))
            {
                c.Problems.Add(T.Get("chk_missing", Exe));
                return c;
            }
            var b = File.ReadAllBytes(exe);
            if (Signatures.Find(b, Signatures.Resolution) < 0)
                c.Problems.Add(T.Get("chk_sig", "Widescreen"));
            if (Signatures.Find(b, Signatures.LookTime) < 0 || Signatures.Find(b, Signatures.LookScale) < 0)
                c.Problems.Add(T.Get("chk_sig", "Maus/Mouse"));
            if (!File.Exists(Path.Combine(dir, @"Engine\fmodex.dll")))
                c.Problems.Add(T.Get("chk_missing", @"Engine\fmodex.dll"));
            string tested;
            c.Tested = TestedBuilds.TryGetValue(NormalizedSha1(b), out tested);
            c.Version = tested ?? "CoCMainWin32.exe (" + T.Get("chk_unknown_build") + ")";
            c.LargeAddressAware = IsLargeAddressAware(b);
            c.Ok = c.Problems.Count == 0;
            return c;
        }
    }

    class GameCheck
    {
        public bool Ok;
        public bool Tested;
        public bool LargeAddressAware;
        public string Version = "";
        public readonly List<string> Problems = new List<string>();
    }

    /// <summary>Byte patterns inside CoCMainWin32.exe that the fixes depend on (null = any byte).</summary>
    static class Signatures
    {
        // widescreen fix: resolution setup (ThirteenAG's ResHook)
        public static readonly int?[] Resolution = { 0x8B, 0x4C, 0x24, 0x18, 0xBF, 0x01, 0x00, 0x00, 0x00, 0xC7 };
        // player look: push 8; call timers; mov ecx,eax; call delta; fmul [60.0]; fstp [esp+8]
        public static readonly int?[] LookTime = { 0x6A, 0x08, 0xE8, null, null, null, null, 0x8B, 0xC8, 0xE8, null, null, null, null, 0xD8, 0x0D, null, null, null, null, 0xD9, 0x5C, 0x24, 0x08 };
        // look input -> option value: fild [esp+50]; jge; fadd [2^32]; fmul [scale]
        public static readonly int?[] LookScale = { 0xDB, 0x44, 0x24, 0x50, 0x7D, 0x06, 0xD8, 0x05, null, null, null, null, 0xD8, 0x0D };

        public static int Find(byte[] b, int?[] pat, int start = 0)
        {
            for (int i = start; i + pat.Length <= b.Length; i++)
            {
                int j = 0;
                while (j < pat.Length && (pat[j] == null || b[i + j] == pat[j]))
                    j++;
                if (j == pat.Length)
                    return i;
            }
            return -1;
        }

        public static int Find(byte[] b, byte[] pat, int start = 0)
        {
            return Find(b, pat.Select(x => (int?)x).ToArray(), start);
        }
    }
}
