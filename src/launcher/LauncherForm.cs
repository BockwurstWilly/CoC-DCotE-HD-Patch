using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoCHD
{
    class LauncherForm : Form
    {
        readonly string dir;
        readonly PatchSettings s;
        readonly GameCheck check;
        Bitmap banner;

        readonly Panel content = new Panel();
        readonly List<RetroButton> navButtons = new List<RetroButton>();
        readonly List<Control> pages = new List<Control>();
        readonly Label status = Theme.Label("", Theme.Small, Theme.TextDim);
        readonly Timer statusTimer = new Timer { Interval = 4000 };

        // graphics
        ComboBox cbResolution, cbMode, cbMsaa, cbAniso;
        CheckBox chkVSync, chkStretch, chkGrain;
        // sound
        CheckBox chkSubtitles;
        // mouse & keyboard
        readonly GameOptions options = GameOptions.Load();
        CheckBox chkModernMouse, chkInvert;
        RetroSlider slSens;
        Label lblSens;
        DataGridView grid;
        // controller
        CheckBox chkPad, chkPadInvert;
        RetroSlider slPadLook;
        Label lblPadLook, lblPadStatus;
        // game
        ComboBox cbLanguage;
        CheckBox chkSkipIntro;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public LauncherForm(string dir)
        {
            this.dir = dir;
            check = Game.Check(dir);
            s = PatchSettings.Load(dir);
            banner = Banner.Load(dir);

            Text = "Call of Cthulhu: Dark Corners of the Earth - HD Launcher";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(880, 720);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Path.Combine(dir, Game.Exe)); } catch (Exception) { }

            BuildLayout();
            LoadValues();
            ShowPage(0);
            UpdateStatus();
            Theme.ScaleToDpi(this);
        }

        // ---- layout ------------------------------------------------------------------------------

        const int BannerH = 150, NavW = 180, BottomH = 58;

        void BuildLayout()
        {
            var head = new Panel { Bounds = new Rectangle(0, 0, ClientSize.Width, BannerH) };
            head.Paint += PaintBanner;
            Controls.Add(head);

            var nav = new Panel { Bounds = new Rectangle(0, BannerH, NavW, ClientSize.Height - BannerH - BottomH), BackColor = Theme.Back };
            nav.Paint += (o, e) =>
            {
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, nav.Width - 1, 0, nav.Width - 1, nav.Height);
            };
            Controls.Add(nav);
            string[] names = { T.Get("nav_gfx"), T.Get("nav_audio"), T.Get("nav_mouse"), T.Get("nav_pad"), T.Get("nav_game"), T.Get("nav_info") };
            for (int i = 0; i < names.Length; i++)
            {
                int page = i;
                var b = Theme.Button(names[i]);
                b.Font = Theme.Display(10.5f);
                b.Bounds = new Rectangle(14, 18 + i * 46, NavW - 28, 38);
                b.Click += (o, e) => ShowPage(page);
                nav.Controls.Add(b);
                navButtons.Add(b);
            }
            var motto = Theme.Label(T.Get("motto"), Theme.Display(8.5f, FontStyle.Italic), Theme.TextDim);
            motto.MaximumSize = new Size(NavW - 28, 0);
            motto.Location = new Point(14, nav.Height - 70);
            nav.Controls.Add(motto);

            content.Bounds = new Rectangle(NavW, BannerH, ClientSize.Width - NavW, ClientSize.Height - BannerH - BottomH);
            content.BackColor = Theme.Panel;
            Controls.Add(content);

            pages.Add(BuildGraphicsPage());
            pages.Add(BuildAudioPage());
            pages.Add(BuildMousePage());
            pages.Add(BuildPadPage());
            pages.Add(BuildGamePage());
            pages.Add(BuildInfoPage());
            foreach (var p in pages)
            {
                p.Bounds = content.ClientRectangle;
                p.Visible = false;
                content.Controls.Add(p);
            }

            var bottom = new Panel { Bounds = new Rectangle(0, ClientSize.Height - BottomH, ClientSize.Width, BottomH), BackColor = Theme.Panel };
            bottom.Paint += (o, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0); };
            Controls.Add(bottom);
            status.AutoSize = false;
            status.AutoEllipsis = true;
            status.Bounds = new Rectangle(16, 9, 428, 18);
            bottom.Controls.Add(status);
            statusTimer.Tick += (o, e) => ShowWarning();
            bottom.Controls.Add(Theme.CreditsLabel(new Point(16, 31)));

            var play = Theme.Button(T.Get("btn_play"), true);
            play.Bounds = new Rectangle(ClientSize.Width - 168, 11, 154, 36);
            play.Click += (o, e) => Play();
            var apply = Theme.Button(T.Get("btn_apply"));
            apply.Bounds = new Rectangle(ClientSize.Width - 296, 12, 118, 34);
            apply.Click += (o, e) => { if (SaveAll()) SetStatus(T.Get("st_saved"), Theme.Good); };
            var close = Theme.Button(T.Get("btn_close"));
            close.Bounds = new Rectangle(ClientSize.Width - 424, 12, 118, 34);
            close.Click += (o, e) => Close();
            bottom.Controls.AddRange(new Control[] { play, apply, close });
        }

        void PaintBanner(object sender, PaintEventArgs e)
        {
            Banner.Paint(e.Graphics, ((Panel)sender).ClientRectangle, banner, "HD LAUNCHER  ·  v" + Version.Text);
        }

        void ShowPage(int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navButtons[i].Primary = i == index;
                navButtons[i].Invalidate();
            }
            if (index == 3)
                UpdatePadStatus();
        }

        Panel NewPage(string title, out int y)
        {
            var page = new Panel { BackColor = Theme.Panel };
            var head = new Label
            {
                Text = title,
                Font = Theme.Display(16f),
                ForeColor = Theme.Brass,
                BackColor = Color.Transparent,
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(24, 14),
            };
            page.Controls.Add(head);
            page.Paint += (o, e) => Theme.Rule(e.Graphics, Theme.S(24), page.Width - Theme.S(24), Theme.S(50));
            y = 64;
            return page;
        }

        // label left, control right
        void Row(Panel page, ref int y, string label, Control control, string hint = null)
        {
            var l = Theme.Label(label);
            l.Location = new Point(28, y + 4);
            page.Controls.Add(l);
            control.Location = new Point(230, y);
            page.Controls.Add(control);
            y += control.Height + 4;
            if (hint != null)
            {
                var h = Theme.Label(hint, Theme.Small, Theme.TextDim);
                h.Location = new Point(230, y);
                h.MaximumSize = new Size(440, 0);
                page.Controls.Add(h);
                y += Theme.Unscaled(h.PreferredHeight) + 2;
            }
            y += 8;
        }

        void CheckRow(Panel page, ref int y, CheckBox box, string hint = null)
        {
            box.Location = new Point(28, y);
            page.Controls.Add(box);
            y += 26;
            if (hint != null)
            {
                var h = Theme.Label(hint, Theme.Small, Theme.TextDim);
                h.Location = new Point(50, y - 4);
                h.MaximumSize = new Size(620, 0);
                page.Controls.Add(h);
                y += Theme.Unscaled(h.PreferredHeight) + 2;
            }
            y += 6;
        }

        Label Note(Panel page, ref int y, string text)
        {
            var info = Theme.Label(text, Theme.Small, Theme.TextDim);
            info.MaximumSize = new Size(640, 0);
            info.Location = new Point(28, y);
            page.Controls.Add(info);
            y += Theme.Unscaled(info.PreferredHeight) + 8;
            return info;
        }

        // value slider with its label right of it
        RetroSlider Slider(Panel page, ref int y, string label, int min, int max, out Label value)
        {
            var sl = new RetroSlider { Minimum = min, Maximum = max, Width = 300 };
            value = Theme.Label("", Theme.Bold, Theme.AccentLight);
            var l = Theme.Label(label);
            l.Location = new Point(28, y + 4);
            sl.Location = new Point(230, y);
            value.Location = new Point(540, y + 4);
            page.Controls.AddRange(new Control[] { l, sl, value });
            y += 28;
            return sl;
        }

        static DataGridView Table(int columns, int rows, bool selectable, int nameWeight = 30, int valueWeight = 20)
        {
            var g = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                ScrollBars = ScrollBars.None,
                RowHeadersVisible = false,
                ColumnHeadersVisible = false,
                BackgroundColor = Theme.Panel,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Theme.Border,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            };
            g.DefaultCellStyle.BackColor = Theme.Panel;
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.SelectionBackColor = selectable ? Theme.Accent : Theme.Panel;
            g.DefaultCellStyle.SelectionForeColor = Theme.Text;
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.PanelLight;
            g.AlternatingRowsDefaultCellStyle.SelectionBackColor = selectable ? Theme.Accent : Theme.PanelLight;
            g.RowTemplate.Height = TextRenderer.MeasureText("Ag", Theme.Body).Height + 6;
            // two entries per line: name, value, name, value
            for (int c = 0; c < columns; c++)
            {
                g.Columns.Add("c" + c, "");
                g.Columns[c].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                g.Columns[c].FillWeight = c % 2 == 0 ? nameWeight : valueWeight;
                g.Columns[c].SortMode = DataGridViewColumnSortMode.NotSortable;
                if (c % 2 == 1)
                    g.Columns[c].DefaultCellStyle.ForeColor = Theme.AccentLight;
            }
            g.Height = Theme.Unscaled(g.RowTemplate.Height) * rows + 2;
            return g;
        }

        static ComboBox Options(int width, params string[] pairs)
        {
            var cb = Theme.Combo(width);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                cb.Items.Add(new Choice(pairs[i], pairs[i + 1]));
            return cb;
        }

        static void Select(ComboBox cb, string value)
        {
            foreach (Choice c in cb.Items)
            {
                if (string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedItem = c;
                    return;
                }
            }
            // value set by hand in the ini: show it rather than silently replacing it
            var custom = new Choice(value, T.Get("opt_custom", value));
            cb.Items.Add(custom);
            cb.SelectedItem = custom;
        }

        static string Value(ComboBox cb) { return ((Choice)cb.SelectedItem).Value; }

        // ---- pages -------------------------------------------------------------------------------

        Control BuildGraphicsPage()
        {
            int y;
            var p = NewPage(T.Get("nav_gfx"), out y);
            // only resolutions the game can show are offered (the HUD breaks above 3276 px width)
            var auto = Display.Auto();
            var cur = Display.Current();
            cbResolution = Options(320, "auto", T.Get("gfx_res_auto", auto.Width, auto.Height));
            foreach (var m in Display.Modes())
                cbResolution.Items.Add(new Choice(m.Width + "x" + m.Height, m.ToString()));
            Row(p, ref y, T.Get("gfx_res"), cbResolution, T.Get("gfx_res_hint", cur.Width, cur.Height));
            cbMode = Options(320, "fullscreen", T.Get("gfx_mode_fs"), "borderless", T.Get("gfx_mode_bl"), "windowed", T.Get("gfx_mode_win"));
            Row(p, ref y, T.Get("gfx_mode"), cbMode);
            cbMsaa = Options(320, "0", T.Get("gfx_off"), "2", "2x MSAA", "4", "4x MSAA", "8", "8x MSAA");
            Row(p, ref y, T.Get("gfx_aa"), cbMsaa);
            cbAniso = Options(320, "0", T.Get("gfx_af_off"), "4", T.Get("gfx_af_x", 4), "8", T.Get("gfx_af_x", 8), "16", T.Get("gfx_af_x", 16));
            Row(p, ref y, T.Get("gfx_af"), cbAniso);
            chkVSync = Theme.Check(T.Get("gfx_vsync"));
            CheckRow(p, ref y, chkVSync, T.Get("gfx_vsync_hint"));
            chkStretch = Theme.Check(T.Get("gfx_stretch"));
            CheckRow(p, ref y, chkStretch);
            chkGrain = Theme.Check(T.Get("gfx_grain"));
            CheckRow(p, ref y, chkGrain);
            return p;
        }

        Control BuildAudioPage()
        {
            int y;
            var p = NewPage(T.Get("nav_audio"), out y);
            var spk = Speakers.Config();
            var l = Theme.Label(T.Get("au_windows"));
            l.Location = new Point(28, y + 6);
            p.Controls.Add(l);
            var v = Theme.Label(spk == null ? T.Get("spk_unknown") : T.Get(spk), Theme.Bold, Theme.AccentLight);
            v.Location = new Point(230, y + 6);
            p.Controls.Add(v);
            var open = Theme.Button(T.Get("au_open"));
            open.Bounds = new Rectangle(400, y, 220, 30);
            open.Click += (o, e) => { try { Process.Start("control.exe", "mmsys.cpl"); } catch (Exception) { } };
            p.Controls.Add(open);
            y += 42;
            chkSubtitles = Theme.Check(T.Get("au_subtitles"));
            CheckRow(p, ref y, chkSubtitles);
            return p;
        }

        Control BuildMousePage()
        {
            int y;
            var p = NewPage(T.Get("nav_mouse"), out y);
            chkModernMouse = Theme.Check(T.Get("mo_modern"));
            chkModernMouse.CheckedChanged += (o, e) => UpdateMouseUi();
            CheckRow(p, ref y, chkModernMouse);

            slSens = Slider(p, ref y, T.Get("mo_sens"), 1, 100, out lblSens);
            slSens.ValueChanged += (o, e) => UpdateMouseUi();
            y += 6;
            chkInvert = Theme.Check(T.Get("mo_invert"));
            chkInvert.Enabled = options.Loaded;
            CheckRow(p, ref y, chkInvert);

            var lk = Theme.Label(T.Get("keys_title"), Theme.Display(11f), Theme.Brass);
            lk.Location = new Point(28, y + 2);
            p.Controls.Add(lk);
            var hk = Theme.Label(options.Loaded ? T.Get("keys_hint") : T.Get("keys_nofile"), Theme.Small, options.Loaded ? Theme.TextDim : Theme.Warn);
            hk.Location = new Point(230, y + 5);
            p.Controls.Add(hk);
            y += 28;

            grid = Table(4, Half, true);
            grid.Location = new Point(24, y);
            grid.Width = 640;
            grid.Enabled = options.Loaded;
            grid.CellDoubleClick += (o, e) => EditKey(ActionAt(e.RowIndex, e.ColumnIndex));
            grid.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Enter && grid.CurrentCell != null) { EditKey(ActionAt(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex)); e.Handled = true; }
            };
            p.Controls.Add(grid);

            int by = grid.Bottom + 8;
            var edit = Theme.Button(T.Get("keys_edit"));
            edit.Bounds = new Rectangle(24, by, 140, 30);
            edit.Click += (o, e) => { if (grid.CurrentCell != null) EditKey(ActionAt(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex)); };
            var modern = Theme.Button(T.Get("keys_modern"));
            modern.Bounds = new Rectangle(344, by, 160, 30);
            modern.Click += (o, e) => { options.Apply(GameOptions.ModernLayout()); FillKeys(); };
            var original = Theme.Button(T.Get("keys_original"));
            original.Bounds = new Rectangle(512, by, 152, 30);
            original.Click += (o, e) => { options.Apply(GameOptions.OriginalLayout()); FillKeys(); };
            foreach (var b in new[] { edit, modern, original })
                b.Enabled = options.Loaded;
            p.Controls.AddRange(new Control[] { edit, modern, original });
            return p;
        }

        Control BuildPadPage()
        {
            int y;
            var p = NewPage(T.Get("nav_pad"), out y);
            chkPad = Theme.Check(T.Get("pad_enable"));
            chkPad.CheckedChanged += (o, e) => UpdatePadUi();
            chkPad.Location = new Point(28, y);
            p.Controls.Add(chkPad);
            lblPadStatus = Theme.Label("", Theme.Bold, Theme.TextDim);
            lblPadStatus.Location = new Point(230, y + 3);
            p.Controls.Add(lblPadStatus);
            y += 26;
            var h = Theme.Label(T.Get("pad_hint"), Theme.Small, Theme.TextDim);
            h.Location = new Point(50, y - 4);
            p.Controls.Add(h);
            y += 26;

            slPadLook = Slider(p, ref y, T.Get("pad_look"), 60, 400, out lblPadLook);
            slPadLook.ValueChanged += (o, e) => UpdatePadUi();
            y += 6;
            chkPadInvert = Theme.Check(T.Get("pad_invert"));
            CheckRow(p, ref y, chkPadInvert);

            var lt = Theme.Label(T.Get("pad_layout"), Theme.Display(11f), Theme.Brass);
            lt.Location = new Point(28, y + 2);
            p.Controls.Add(lt);
            var lh = Theme.Label(T.Get("pad_layout_hint"), Theme.Small, Theme.TextDim);
            lh.Location = new Point(230, y + 5);
            p.Controls.Add(lh);
            y += 28;

            Func<string, string> act = id => GameOptions.Action(id).Name;
            var rows = new[]
            {
                // the Xbox version's default layout (manual: Controller Configuration 1)
                new[] { T.Get("pad_lstick"), T.Get("pad_move"), "LB / L1", act("heal") },
                new[] { T.Get("pad_rstick"), T.Get("pad_lookaround"), "RB / R1", act("inv") },
                new[] { "RT / R2", act("fire"), T.Get("pad_r3"), act("sneak") },
                new[] { "LT / L2", act("aim"), T.Get("pad_view"), act("skip") },
                new[] { T.Get("pad_a"), act("action"), "Start / Options", T.Get("pad_menu") },
                new[] { T.Get("pad_b"), act("crouch"), T.Get("pad_dup"), T.Get("pad_nextweapon") },
                new[] { T.Get("pad_x"), act("jump"), T.Get("pad_ddown"), act("weapon") },
                new[] { T.Get("pad_y"), act("reload"), T.Get("pad_dleft"), act("leanl") },
                new[] { "", "", T.Get("pad_dright"), act("leanr") },
            };
            var layout = Table(4, rows.Length, false, 21, 29);
            layout.Location = new Point(24, y);
            layout.Width = 640;
            foreach (var r in rows)
                layout.Rows.Add(r[0], r[1], r[2], r[3]);
            layout.TabStop = false;
            p.Controls.Add(layout);
            y = layout.Bottom + 8;
            Note(p, ref y, T.Get("pad_menus"));
            return p;
        }

        Control BuildGamePage()
        {
            int y;
            var p = NewPage(T.Get("nav_game"), out y);
            cbLanguage = Options(320, "English", "English", "Deutsch", "Deutsch", "Francais", "Français");
            Row(p, ref y, T.Get("ga_lang"), cbLanguage);
            chkSkipIntro = Theme.Check(T.Get("ga_intro"));
            CheckRow(p, ref y, chkSkipIntro, T.Get("ga_intro_hint"));
            y += 8;
            var saves = Theme.Button(T.Get("ga_saves"));
            saves.Bounds = new Rectangle(28, y, 260, 30);
            saves.Click += (o, e) =>
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Bethesda\Call of Cthulhu");
                if (Directory.Exists(d))
                    Process.Start("explorer.exe", "\"" + d + "\"");
            };
            p.Controls.Add(saves);
            return p;
        }

        Control BuildInfoPage()
        {
            int y;
            var p = NewPage(T.Get("nav_info"), out y);
            var lines = new List<string>();
            lines.Add(T.Get("info_game", dir));
            lines.Add(check.Version + (check.LargeAddressAware ? "  ·  Large Address Aware" : ""));
            if (!check.Ok)
                lines.Add(T.Get("info_problems") + " " + string.Join(" | ", check.Problems));
            else
                lines.Add(check.Tested ? T.Get("info_tested") : T.Get("info_untested"));
            lines.Add("");
            lines.Add(T.Get("info_components"));
            var text = Theme.Label(string.Join("\n", lines));
            text.MaximumSize = new Size(640, 0);
            text.Location = new Point(28, y);
            p.Controls.Add(text);
            y += Theme.Unscaled(text.PreferredHeight) + 18;
            // language: the window is rebuilt in the new language (settings are saved first)
            var cbLang = Options(320, "auto", T.Get("lang_auto"), "en", "English", "de", "Deutsch");
            Row(p, ref y, T.Get("info_lang"), cbLang);
            Select(cbLang, T.Language);
            cbLang.SelectedIndexChanged += (o, e) =>
            {
                var lang = Value(cbLang);
                if (lang == T.Language)
                    return;
                if (!Game.IsRunning())
                    SaveAll();
                try { T.SaveLanguage(dir, lang); } catch (Exception) { }
                Application.Restart();
            };
            y += 8;
            var open = Theme.Button(T.Get("info_open"));
            open.Bounds = new Rectangle(28, Math.Max(y, 330), 200, 30);
            open.Click += (o, e) => Process.Start("explorer.exe", "\"" + dir + "\"");
            var readme = Theme.Button(T.Get("info_readme"));
            readme.Bounds = new Rectangle(236, open.Top, 200, 30);
            readme.Click += (o, e) =>
            {
                var r = Path.Combine(dir, @"_HDPatch\README.txt");
                if (File.Exists(r)) Process.Start("notepad.exe", "\"" + r + "\"");
            };
            var remove = Theme.Button(T.Get("info_uninstall"));
            remove.Bounds = new Rectangle(444, open.Top, 200, 30);
            remove.Click += (o, e) =>
            {
                // the uninstaller replaces this launcher with the original: start it, then get out of its way
                var u = Path.Combine(dir, @"_HDPatch\CoCHD_Setup.exe");
                if (!File.Exists(u))
                    return;
                Process.Start(new ProcessStartInfo(u, "/uninstall") { WorkingDirectory = dir });
                Close();
            };
            p.Controls.AddRange(new Control[] { open, readme, remove });
            return p;
        }

        // ---- values --------------------------------------------------------------------------------

        void LoadValues()
        {
            Select(cbResolution, s.Resolution);
            Select(cbMode, s.Mode);
            Select(cbMsaa, s.Msaa.ToString(Inv));
            Select(cbAniso, s.Anisotropy <= 1 ? "0" : s.Anisotropy.ToString(Inv));
            chkVSync.Checked = s.VSync;
            chkStretch.Checked = s.StretchFullscreenImages;
            chkGrain.Checked = !s.FilmGrain;

            chkSubtitles.Checked = s.Subtitles;

            chkModernMouse.Checked = s.ModernMouse;
            slSens.Value = (int)Math.Round(s.Sens * 10);
            chkInvert.Checked = options.InvertMouse;
            FillKeys();

            chkPad.Checked = s.Pad;
            slPadLook.Value = (int)Math.Round(s.PadLook);
            chkPadInvert.Checked = s.PadInvert;

            Select(cbLanguage, s.Language);
            chkSkipIntro.Checked = s.SkipIntro;
            UpdateMouseUi();
            UpdatePadUi();
        }

        void TakeValues()
        {
            s.Resolution = Value(cbResolution);
            s.Mode = Value(cbMode);
            s.Msaa = int.Parse(Value(cbMsaa), Inv);
            s.Anisotropy = int.Parse(Value(cbAniso), Inv);
            s.VSync = chkVSync.Checked;
            s.StretchFullscreenImages = chkStretch.Checked;
            s.FilmGrain = !chkGrain.Checked;

            s.Subtitles = chkSubtitles.Checked;

            s.ModernMouse = chkModernMouse.Checked;
            s.Sens = slSens.Value / 10.0;
            options.InvertMouse = chkInvert.Checked;

            s.Pad = chkPad.Checked;
            s.PadLook = slPadLook.Value;
            s.PadInvert = chkPadInvert.Checked;

            s.Language = Value(cbLanguage);
            s.SkipIntro = chkSkipIntro.Checked;
        }

        void UpdateMouseUi()
        {
            slSens.Enabled = chkModernMouse.Checked;
            lblSens.Text = chkModernMouse.Checked ? (slSens.Value / 10.0).ToString("0.0", Inv) : T.Get("mo_sens_game");
        }

        void UpdatePadUi()
        {
            slPadLook.Enabled = chkPadInvert.Enabled = chkPad.Checked;
            lblPadLook.Text = slPadLook.Value + " °/s";
        }

        void UpdatePadStatus()
        {
            var kind = Gamepad.Detect();
            bool on = kind != Gamepad.Kind.None;
            lblPadStatus.Text = kind == Gamepad.Kind.PlayStation ? T.Get("pad_on_ps") : on ? T.Get("pad_on") : T.Get("pad_off");
            lblPadStatus.ForeColor = on ? Theme.Good : Theme.TextDim;
        }

        int Half { get { return (GameOptions.Actions.Length + 1) / 2; } }

        int ActionAt(int row, int col)
        {
            if (row < 0 || col < 0)
                return -1;
            int i = row + (col >= 2 ? Half : 0);
            return i < GameOptions.Actions.Length ? i : -1;
        }

        static string KeyText(GameAction a, GameOptions o)
        {
            var k = o.Get(a);
            if (!k.HasValue)
                return "—";
            // Windows names some keys in capitals ("UMSCHALT"): write them like the others
            var s = k.Value.ToString();
            return s.Length > 1 && s == s.ToUpperInvariant() && s.Any(char.IsLetter) ? s.Substring(0, 1) + s.Substring(1).ToLowerInvariant() : s;
        }

        void FillKeys()
        {
            var keep = grid.CurrentCell != null ? new Point(grid.CurrentCell.ColumnIndex, grid.CurrentCell.RowIndex) : Point.Empty;
            grid.Rows.Clear();
            var all = GameOptions.Actions;
            for (int r = 0; r < Half; r++)
            {
                var b = r + Half < all.Length ? all[r + Half] : null;
                grid.Rows.Add(all[r].Name, KeyText(all[r], options), b != null ? b.Name : "", b != null ? KeyText(b, options) : "");
            }
            if (grid.Rows.Count > 0)
                grid.CurrentCell = grid.Rows[Math.Min(keep.Y, grid.Rows.Count - 1)].Cells[keep.X];
        }

        void EditKey(int index)
        {
            if (index < 0 || !options.Loaded)
                return;
            var a = GameOptions.Actions[index];
            using (var dlg = new KeyBindDialog(a))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result.HasValue)
                {
                    options.Assign(a, dlg.Result.Value);
                    FillKeys();
                }
            }
        }

        // ---- actions -------------------------------------------------------------------------------

        bool SaveAll()
        {
            if (Game.IsRunning())
            {
                MessageBox.Show(this, T.Get("err_running"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                TakeValues();
                s.Save();
                options.Save();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T.Get("err_save", ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void Play()
        {
            if (!SaveAll())
                return;
            try
            {
                // The game needs the foreground at startup. Let it take it, and only close once its
                // window is in front - closing earlier hands the focus to whatever window was before.
                NativeMethods.AllowSetForegroundWindow(-1);
                var game = Process.Start(new ProcessStartInfo(Path.Combine(dir, Game.Exe)) { WorkingDirectory = Path.Combine(dir, Game.EngineDir), UseShellExecute = true });
                Hide();
                var until = DateTime.Now.AddSeconds(20);
                while (game != null && !game.HasExited && DateTime.Now < until)
                {
                    game.Refresh();
                    if (game.MainWindowHandle != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(game.MainWindowHandle);
                        break;
                    }
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                }
                Close();
            }
            catch (Exception ex)
            {
                Show();
                MessageBox.Show(this, T.Get("err_start", ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // a warning about the installation stays; everything else is a short confirmation
        string warning = "";

        void UpdateStatus()
        {
            if (!check.Ok)
                warning = T.Get("st_problem");
            else if (!File.Exists(Path.Combine(dir, @"Engine\dinput8.dll")) || !File.Exists(Path.Combine(dir, @"Engine\scripts\CoCHD.asi")))
                warning = T.Get("st_notinstalled");
            else
                warning = "";
            ShowWarning();
        }

        void ShowWarning()
        {
            statusTimer.Stop();
            status.Text = warning;
            status.ForeColor = Theme.Warn;
        }

        void SetStatus(string text, Color color)
        {
            status.Text = text;
            status.ForeColor = color;
            statusTimer.Stop();
            statusTimer.Start();
        }

        public void Snapshot(string folder)
        {
            Directory.CreateDirectory(folder);
            for (int i = 0; i < pages.Count; i++)
            {
                ShowPage(i);
                Refresh();
                using (var bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(folder, "launcher_" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (banner != null)
                banner.Dispose();
            base.OnFormClosed(e);
        }
    }

    class Choice
    {
        public readonly string Value, Label;
        public Choice(string value, string label) { Value = value; Label = label; }
        public override string ToString() { return Label; }
    }
}
