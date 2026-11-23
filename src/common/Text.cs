using System.Collections.Generic;
using System.Globalization;

namespace CoCHD
{
    static class Version
    {
        public const string Text = "1.2.2";
    }

    /// <summary>UI texts, German or English: the Windows display language, or the choice under Info.</summary>
    static class T
    {
        public static readonly bool WindowsGerman = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        public static bool German = WindowsGerman;
        public static string Language = "auto";     // auto | en | de

        public static void SetLanguage(string lang)
        {
            lang = (lang ?? "auto").Trim().ToLowerInvariant();
            Language = lang == "en" || lang == "de" ? lang : "auto";
            German = Language == "auto" ? WindowsGerman : Language == "de";
        }

        /// <summary>Reads the player's choice from Engine\scripts\CoCHD.ini ([Launcher] Language).</summary>
        public static void LoadLanguage(string dir)
        {
            try { SetLanguage(IniDoc.Load(System.IO.Path.Combine(dir, PatchSettings.CocHdIni)).Get("Launcher", "Language", "auto")); }
            catch (System.Exception) { }
        }

        public static void SaveLanguage(string dir, string lang)
        {
            var ini = IniDoc.Load(System.IO.Path.Combine(dir, PatchSettings.CocHdIni));
            ini.Set("Launcher", "Language", lang);
            ini.Save();
        }

        public static string Get(string key, params object[] args)
        {
            string[] pair;
            if (!Texts.TryGetValue(key, out pair))
                return key;
            var s = German ? pair[0] : pair[1];
            return args.Length > 0 ? string.Format(s, args) : s;
        }

        static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
        {
            // game check
            { "chk_nodir", new[] { "Ordner nicht gefunden.", "Folder not found." } },
            { "chk_missing", new[] { "{0} fehlt.", "{0} is missing." } },
            { "chk_sig", new[] { "CoCMainWin32.exe ist eine unbekannte Spielversion ({0}-Code nicht gefunden) - benötigt wird die GOG-Version.", "CoCMainWin32.exe is an unknown game version ({0} code not found) - the GOG release is required." } },
            { "chk_unknown_build", new[] { "unbekannter Build, kompatibel", "unknown build, compatible" } },

            // launcher frame
            { "nav_gfx", new[] { "Grafik", "Graphics" } },
            { "nav_audio", new[] { "Ton", "Sound" } },
            { "nav_mouse", new[] { "Maus & Tastatur", "Mouse & Keyboard" } },
            { "nav_pad", new[] { "Controller", "Controller" } },
            { "nav_game", new[] { "Spiel", "Game" } },
            { "nav_info", new[] { "Info", "About" } },
            { "motto", new[] { "„Was ewig liegt,\nist nicht tot\u00A0…“", "\"That is not dead\nwhich can eternal lie\u00A0…\"" } },
            { "btn_play", new[] { "SPIELEN", "PLAY" } },
            { "btn_apply", new[] { "Übernehmen", "Apply" } },
            { "btn_close", new[] { "Schließen", "Close" } },
            { "opt_custom", new[] { "Eigene Einstellung ({0})", "Custom ({0})" } },

            // graphics
            { "gfx_res", new[] { "Auflösung", "Resolution" } },
            { "gfx_res_auto", new[] { "Automatisch ({0} × {1})", "Automatic ({0} × {1})" } },
            { "gfx_res_hint", new[] { "Erkannte Monitorauflösung {0} × {1} – Optimalwert ermittelt", "Detected monitor resolution {0} × {1} – optimal value set" } },
            { "gfx_mode", new[] { "Anzeigemodus", "Display mode" } },
            { "gfx_mode_fs", new[] { "Vollbild (60 Hz)", "Fullscreen (60 Hz)" } },
            { "gfx_mode_bl", new[] { "Randloses Fenster", "Borderless window" } },
            { "gfx_mode_win", new[] { "Fenster", "Window" } },
            { "gfx_aa", new[] { "Kantenglättung", "Anti-aliasing" } },
            { "gfx_off", new[] { "Aus", "Off" } },
            { "gfx_af", new[] { "Texturfilter", "Texture filtering" } },
            { "gfx_af_off", new[] { "Trilinear", "Trilinear" } },
            { "gfx_af_x", new[] { "{0}x anisotrop", "{0}x anisotropic" } },
            { "gfx_vsync", new[] { "VSync", "VSync" } },
            { "gfx_vsync_hint", new[] { "Maximal 60 fps – mehr verträgt das Spiel nicht.", "Capped at 60 fps - the game can't handle more." } },
            { "gfx_stretch", new[] { "Menübilder bildschirmfüllend", "Full-screen menu images" } },
            { "gfx_grain", new[] { "Filmkorn- und Filmkratzer-Effekte deaktivieren", "Disable film grain and scratch effects" } },

            // sound
            { "au_windows", new[] { "Lautsprecher", "Speakers" } },
            { "spk_unknown", new[] { "unbekannt", "unknown" } },
            { "spk_headphone", new[] { "Kopfhörer", "Headphones" } },
            { "spk_mono", new[] { "Mono", "Mono" } },
            { "spk_quad", new[] { "Quadro (4.0)", "Quad (4.0)" } },
            { "spk_stereo", new[] { "Stereo", "Stereo" } },
            { "spk_surround", new[] { "Surround", "Surround" } },
            { "spk_51", new[] { "5.1 Surround", "5.1 surround" } },
            { "spk_71", new[] { "7.1 Surround", "7.1 surround" } },
            { "au_open", new[] { "Windows-Sound …", "Windows sound …" } },
            { "au_subtitles", new[] { "Untertitel", "Subtitles" } },

            // mouse & keyboard
            { "mo_modern", new[] { "Moderne Maussteuerung", "Modern mouse control" } },
            { "mo_sens", new[] { "Empfindlichkeit", "Sensitivity" } },
            { "mo_sens_game", new[] { "im Spiel", "in-game" } },
            { "mo_invert", new[] { "Maus umkehren", "Invert mouse" } },
            { "keys_title", new[] { "Tastenbelegung", "Key bindings" } },
            { "keys_hint", new[] { "Doppelklick, dann Taste drücken.", "Double-click, then press a key." } },
            { "keys_nofile", new[] { "Erst einmal das Spiel starten.", "Start the game once first." } },
            { "keys_edit", new[] { "Ändern", "Change" } },
            { "keys_modern", new[] { "Modern (WASD)", "Modern (WASD)" } },
            { "keys_original", new[] { "Original", "Original" } },
            { "keys_press", new[] { "Taste, Maustaste oder Mausrad drücken.", "Press a key, mouse button or wheel." } },
            { "keys_esc", new[] { "Esc = Abbrechen  ·  Entf gedrückt halten = Zuweisung löschen", "Esc = cancel  ·  hold Del = clear the binding" } },
            { "key_none", new[] { "–", "–" } },
            { "key_mouse_l", new[] { "Linke Maustaste", "Left mouse button" } },
            { "key_mouse_r", new[] { "Rechte Maustaste", "Right mouse button" } },
            { "key_mouse_m", new[] { "Mittlere Maustaste", "Middle mouse button" } },
            { "key_mouse_4", new[] { "Maustaste 4", "Mouse button 4" } },
            { "key_wheel_up", new[] { "Mausrad hoch", "Mouse wheel up" } },
            { "key_wheel_down", new[] { "Mausrad runter", "Mouse wheel down" } },
            { "key_backspace", new[] { "Rücktaste", "Backspace" } },
            { "key_space", new[] { "Leertaste", "Space" } },
            { "key_mouse_axis", new[] { "Mausachse {0}", "Mouse axis {0}" } },

            // controller
            { "pad_enable", new[] { "Controller verwenden", "Use controller" } },
            { "pad_on", new[] { "Controller verbunden.", "Controller connected." } },
            { "pad_on_ps", new[] { "PlayStation-Controller verbunden.", "PlayStation controller connected." } },
            { "pad_off", new[] { "Kein Controller gefunden.", "No controller found." } },
            { "pad_hint", new[] { "Xbox, DualShock 4 und DualSense – per USB oder Bluetooth.", "Xbox, DualShock 4 and DualSense – USB or Bluetooth." } },
            { "pad_look", new[] { "Kamera-Tempo", "Camera speed" } },
            { "pad_invert", new[] { "Kamera vertikal umkehren", "Invert camera vertically" } },
            { "pad_layout", new[] { "Belegung", "Layout" } },
            { "pad_layout_hint", new[] { "Folgt deiner Tastenbelegung.", "Follows your key bindings." } },
            { "pad_move", new[] { "Bewegen", "Move" } },
            { "pad_lookaround", new[] { "Umsehen", "Look around" } },
            { "pad_menu", new[] { "Menü", "Menu" } },
            { "pad_nextweapon", new[] { "Nächste Waffe", "Next weapon" } },
            { "pad_menus", new[] { "Im Menü: Steuerkreuz wählt aus, Stick bewegt den Zeiger, A / Kreuz bestätigt, Start / Options = Zurück.", "In menus: d-pad selects, stick moves the pointer, A / Cross confirms, Start / Options = back." } },
            { "pad_lstick", new[] { "Linker Stick", "Left stick" } },
            { "pad_rstick", new[] { "Rechter Stick", "Right stick" } },
            { "pad_l3", new[] { "Linker Stick drücken", "Left stick click" } },
            { "pad_r3", new[] { "Rechter Stick drücken", "Right stick click" } },
            { "pad_view", new[] { "View / Share", "View / Share" } },
            { "pad_a", new[] { "A / Kreuz", "A / Cross" } },
            { "pad_b", new[] { "B / Kreis", "B / Circle" } },
            { "pad_x", new[] { "X / Quadrat", "X / Square" } },
            { "pad_y", new[] { "Y / Dreieck", "Y / Triangle" } },
            { "pad_dup", new[] { "Steuerkreuz ↑", "D-pad ↑" } },
            { "pad_ddown", new[] { "Steuerkreuz ↓", "D-pad ↓" } },
            { "pad_dleft", new[] { "Steuerkreuz ←", "D-pad ←" } },
            { "pad_dright", new[] { "Steuerkreuz →", "D-pad →" } },

            // game
            { "ga_lang", new[] { "Spielsprache", "Game language" } },
            { "ga_intro", new[] { "Intros überspringen", "Skip intros" } },
            { "ga_intro_hint", new[] { "Story-Videos bleiben.", "Story videos stay." } },
            { "ga_saves", new[] { "Spielstände öffnen", "Open savegames" } },

            // info
            { "info_game", new[] { "Spiel: {0}", "Game: {0}" } },
            { "info_problems", new[] { "Probleme:", "Problems:" } },
            { "info_tested", new[] { "Getestete Spielversion.", "Tested game version." } },
            { "info_untested", new[] { "Kompatible, nicht getestete Spielversion.", "Compatible, untested game version." } },
            { "info_components", new[] { "Enthalten:\n• CoCHD.asi – Auflösung, Vollbild, 60 fps, Texturfilter, Maus, Controller\n• Widescreen Fix und Ultimate ASI Loader (ThirteenAG)\n• HD Launcher\n\nLizenz: PolyForm Noncommercial 1.0.0 – frei nutzen und weitergeben, nicht kommerziell.\n© 2026 BockwurstWilly · github.com/BockwurstWilly", "Included:\n• CoCHD.asi - resolution, fullscreen, 60 fps, texture filtering, mouse, controller\n• Widescreen Fix and Ultimate ASI Loader (ThirteenAG)\n• HD Launcher\n\nLicense: PolyForm Noncommercial 1.0.0 – free to use and share, non-commercial.\n© 2026 BockwurstWilly · github.com/BockwurstWilly" } },
            { "info_lang", new[] { "Sprache / Language", "Language / Sprache" } },
            { "lang_auto", new[] { "Automatisch (Windows-Sprache)", "Automatic (Windows language)" } },
            { "info_open", new[] { "Spielordner", "Game folder" } },
            { "info_readme", new[] { "Liesmich", "Readme" } },
            { "info_uninstall", new[] { "Patch entfernen", "Remove patch" } },

            // status / errors
            { "st_saved", new[] { "Einstellungen gespeichert.", "Settings saved." } },
            { "st_problem", new[] { "Spielversion wird nicht vollständig unterstützt – siehe Info.", "Game version not fully supported - see About." } },
            { "st_notinstalled", new[] { "HD-Patch-Dateien fehlen – Setup erneut ausführen.", "HD patch files missing - run the setup again." } },
            { "err_running", new[] { "Das Spiel läuft gerade. Bitte zuerst beenden.", "The game is running. Please quit it first." } },
            { "err_save", new[] { "Speichern fehlgeschlagen: {0}", "Saving failed: {0}" } },
            { "err_start", new[] { "Spielstart fehlgeschlagen: {0}", "Could not start the game: {0}" } },
            { "err_nogame", new[] { "Call of Cthulhu: Dark Corners of the Earth (GOG) wurde nicht gefunden.", "Call of Cthulhu: Dark Corners of the Earth (GOG) was not found." } },

            // setup
            { "su_title", new[] { "Call of Cthulhu: Dark Corners of the Earth – HD Patch Setup", "Call of Cthulhu: Dark Corners of the Earth - HD Patch Setup" } },
            { "su_folder", new[] { "Spielordner (GOG-Version)", "Game folder (GOG release)" } },
            { "su_browse", new[] { "Durchsuchen …", "Browse …" } },
            { "su_browse_desc", new[] { "Ordner von Call of Cthulhu: Dark Corners of the Earth wählen (enthält den Ordner Engine)", "Choose the Call of Cthulhu: Dark Corners of the Earth folder (contains the Engine folder)" } },
            { "su_bad", new[] { "Hier kann der Patch nicht installiert werden:", "The patch cannot be installed here:" } },
            { "su_ok_tested", new[] { "Spiel gefunden – getestete GOG-Version.", "Game found - tested GOG release." } },
            { "su_ok_untested", new[] { "Spiel gefunden – kompatible, aber nicht getestete Version.", "Game found - compatible but untested version." } },
            { "su_install", new[] { "Installieren", "Install" } },
            { "su_update", new[] { "Aktualisieren", "Update" } },
            { "su_uninstall", new[] { "Deinstallieren", "Uninstall" } },
            { "su_launch", new[] { "Launcher starten", "Start launcher" } },
            { "su_desktop", new[] { "Verknüpfung auf dem Desktop", "Desktop shortcut" } },
            { "su_startmenu", new[] { "Einträge im Startmenü", "Start menu entries" } },
            { "su_laa", new[] { "Large Address Aware (4 GB Speicher für das 32-Bit-Spiel)", "Large Address Aware (4 GB memory for the 32-bit game)" } },
            { "su_intro", new[] { "Intros überspringen (Story-Videos bleiben)", "Skip intros (story videos stay)" } },
            { "su_success", new[] { "HD-Patch installiert.\n\nStart über „Call of Cthulhu - HD Launcher“ oder wie gewohnt über GOG Galaxy.", "HD patch installed.\n\nStart with \"Call of Cthulhu - HD Launcher\" or from GOG Galaxy as usual." } },
            { "su_removed", new[] { "HD-Patch entfernt. Originaldateien wiederhergestellt, Spielstände bleiben.", "HD patch removed. Original files restored, savegames kept." } },
            { "su_uninstall_confirm", new[] { "HD-Patch wirklich deinstallieren?\nAlle Originaldateien werden wiederhergestellt.", "Really uninstall the HD patch?\nAll original files will be restored." } },
            { "su_note", new[] { "Nach „Dateien prüfen“ in GOG Galaxy das Setup erneut starten.", "After \"verify files\" in GOG Galaxy, run the setup again." } },
            { "lnk_uninstall", new[] { "Call of Cthulhu – HD Patch entfernen", "Call of Cthulhu - Remove HD Patch" } },
            { "err_check", new[] { "Die Spieldateien passen nicht:", "The game files don't match:" } },
            { "err_nobackup", new[] { "Keine Sicherung gefunden – der HD-Patch scheint nicht installiert zu sein.", "No backup found - the HD patch does not seem to be installed." } },
            { "err_nowrite", new[] { "Keine Schreibrechte für {0}.", "No write access to {0}." } },
            { "log_backup", new[] { "Originaldateien gesichert nach {0}", "Original files backed up to {0}" } },
            { "log_update", new[] { "Aktualisierung: vorhandene Sicherung wird beibehalten", "Update: keeping the existing backup" } },
            { "log_copy", new[] { "kopiert: {0}", "copied: {0}" } },
            { "log_keep", new[] { "behalten (eigene Einstellungen): {0}", "kept (your settings): {0}" } },
            { "log_laa", new[] { "CoCMainWin32.exe: Large Address Aware gesetzt", "CoCMainWin32.exe: Large Address Aware set" } },
            { "log_registry", new[] { "Spieleinstellungen (Registry) gesetzt", "Game settings (registry) written" } },
            { "log_intro", new[] { "Logo-Videos beiseitegelegt", "Logo videos moved aside" } },
            { "log_shortcuts", new[] { "Verknüpfungen angelegt", "Shortcuts created" } },
            { "log_done", new[] { "Fertig.", "Done." } },
            { "log_delete", new[] { "entfernt: {0}", "removed: {0}" } },
            { "log_restore", new[] { "wiederhergestellt: {0}", "restored: {0}" } },
            { "log_leftover", new[] { "Ordner {0} konnte nicht entfernt werden ({1}) – später von Hand löschen.", "Folder {0} could not be removed ({1}) - delete it later by hand." } },
            { "log_uninstalled", new[] { "HD-Patch entfernt.", "HD patch removed." } },
        };
    }
}
