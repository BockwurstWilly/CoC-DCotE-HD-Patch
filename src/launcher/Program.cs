using System;
using System.Windows.Forms;

namespace CoCHD
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var dir = Game.FindInstallDir();
            if (dir == null)
            {
                MessageBox.Show(T.Get("err_nogame"), "Call of Cthulhu - HD Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            T.LoadLanguage(dir);
            foreach (var a in args)
                if (a.StartsWith("/lang=", StringComparison.OrdinalIgnoreCase))     // tests / screenshots
                    T.SetLanguage(a.Substring(6));
            // /keys=modern|original: apply a key layout to the game's options without opening the window (tests)
            foreach (var a in args)
            {
                if (a.StartsWith("/keys=", StringComparison.OrdinalIgnoreCase))
                {
                    var o = GameOptions.Load();
                    if (!o.Loaded)
                        return 2;
                    o.Apply(a.Substring(6).Equals("modern", StringComparison.OrdinalIgnoreCase) ? GameOptions.ModernLayout() : GameOptions.OriginalLayout());
                    o.Save();
                    return 0;
                }
            }
            var form = new LauncherForm(dir);
            // /snapshot=<folder>: render every page invisibly to PNG files (documentation, tests)
            foreach (var a in args)
            {
                if (a.StartsWith("/snapshot=", StringComparison.OrdinalIgnoreCase))
                {
                    form.Opacity = 0;
                    form.ShowInTaskbar = false;
                    form.Shown += (o, e) => form.Snapshot(a.Substring(10).Trim('"'));
                }
            }
            Application.Run(form);
            return 0;
        }
    }
}
