using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoCHD
{
    /// <summary>
    /// CoCHD_Setup.exe [/install | /uninstall] [/dir="..."] [/silent] [/nodesktop] [/nostartmenu] [/nolaa] [/intros]
    /// Without /install or /uninstall the setup window opens.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            finally
            {
                // the temporary uninstaller copy removes itself once it has exited
                if (Has(args, "/fromtemp"))
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del \"" + Application.ExecutablePath + "\"")
                    {
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                    });
            }
        }

        static int Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool silent = Has(args, "/silent");
            bool install = Has(args, "/install"), uninstall = Has(args, "/uninstall");
            var dir = Arg(args, "/dir=") ?? Game.FindInstallDir();

            // the uninstaller lives in the folder it deletes: continue from a temporary copy
            // (compare folder names, not paths: 8.3 short names would defeat a path comparison)
            var exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            if (uninstall && dir != null && !Has(args, "/fromtemp") &&
                string.Equals(Path.GetFileName(exeDir), Installer.PatchDir, StringComparison.OrdinalIgnoreCase))
            {
                var tmp = Path.Combine(Path.GetTempPath(), "CoCHD_Uninstall.exe");
                File.Copy(Application.ExecutablePath, tmp, true);
                var rest = string.Join(" ", args.Select(Quote)) + " /fromtemp /dir=\"" + dir + "\"";
                Process.Start(new ProcessStartInfo(tmp, rest) { UseShellExecute = false });
                return 0;
            }

            if (silent && (install || uninstall))
            {
                var logPath = Path.Combine(Path.GetTempPath(), "CoCHD_Setup.log");
                using (var w = new StreamWriter(logPath, false, new System.Text.UTF8Encoding(true)))
                {
                    try
                    {
                        if (dir == null)
                            throw new InvalidOperationException(T.Get("err_nogame"));
                        var inst = new Installer(line => { w.WriteLine(line); w.Flush(); });
                        if (install)
                            inst.Install(dir, Options(args));
                        else
                            inst.Uninstall(dir);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        w.WriteLine("!! " + ex);
                        return 1;
                    }
                }
            }

            if (uninstall && dir != null)
            {
                // Apps & Features / launcher: ask, then run like the button in the window
                if (MessageBox.Show(T.Get("su_uninstall_confirm"), T.Get("su_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return 0;
                try
                {
                    new Installer(_ => { }).Uninstall(dir);
                    MessageBox.Show(T.Get("su_removed"), T.Get("su_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, T.Get("su_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }

            var form = new SetupForm(dir);
            if (install && dir != null)
                form.Shown += (o, e) => form.BeginInvoke(new Action(() => AutoInstall(form, args)));
            Application.Run(form);
            return 0;
        }

        // restarted elevated with /install: run the installation right away with the chosen options
        static void AutoInstall(SetupForm form, string[] args)
        {
            var dir = Arg(args, "/dir=");
            try
            {
                new Installer(_ => { }).Install(dir, Options(args));
                MessageBox.Show(form, T.Get("su_success"), form.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, ex.Message, form.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            form.Close();
        }

        static InstallOptions Options(string[] args)
        {
            return new InstallOptions
            {
                DesktopShortcut = !Has(args, "/nodesktop"),
                StartMenuShortcut = !Has(args, "/nostartmenu"),
                LargeAddressAware = !Has(args, "/nolaa"),
                SkipIntro = !Has(args, "/intros"),
            };
        }

        public static string OptionArgs(InstallOptions o)
        {
            return (o.DesktopShortcut ? "" : " /nodesktop") + (o.StartMenuShortcut ? "" : " /nostartmenu") +
                   (o.LargeAddressAware ? "" : " /nolaa") + (o.SkipIntro ? "" : " /intros");
        }

        static bool Has(string[] args, string flag)
        {
            return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        }

        static string Arg(string[] args, string prefix)
        {
            var a = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return a == null ? null : a.Substring(prefix.Length).Trim('"');
        }

        static string Quote(string a)
        {
            return a.Contains(" ") ? "\"" + a + "\"" : a;
        }
    }
}
