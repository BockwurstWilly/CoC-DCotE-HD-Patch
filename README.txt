Call of Cthulhu: Dark Corners of the Earth (GOG) - HD Patch 1.2.2
================================================================

DEUTSCH
-------
Für die GOG-Version unter Windows 10/11.

Installation
  CoCHD_Setup.exe starten. Das Setup findet das Spiel automatisch (GOG-Registry), prüft die
  Spieldateien und installiert. Danach das Spiel über "Call of Cthulhu - HD Launcher"
  (Desktop / Startmenü) oder wie gewohnt über GOG Galaxy starten - der Patch ist immer aktiv.
  Der neue Launcher ersetzt den Original-Launcher (CoCDCoTELauncher.exe, wird gesichert); auch
  der GOG-Galaxy-Eintrag "Settings" öffnet ihn.
  Deinstallation: Startmenü "Call of Cthulhu - HD Patch entfernen", Launcher -> Info ->
  "HD-Patch deinstallieren", Windows "Apps & Features" oder das Setup erneut starten. Alle
  Originaldateien werden wiederhergestellt; Spielstände und Einstellungen bleiben erhalten.

Grafik
  * Auflösung automatisch passend zum Monitor - bei jedem Start, auch über GOG Galaxy.
    Ausnahme: Breiter als 3276 Pixel zerbricht das Inventar des Spiels (Ganzzahl-Überlauf im
    HUD). Auf 4K-/5K-Monitoren rechnet das Spiel daher in der größten Auflösung gleichen
    Formats (4K: 3200 x 1800) und wird auf den Monitor skaliert.
  * Widescreen Fix von ThirteenAG: korrektes Bildformat, HUD, Texte, Videos.
  * Anzeigemodus: Vollbild, randloses Fenster oder Fenster - alle Alt+Tab-sicher. Das Original
    stürzt bei Alt+Tab im Vollbild ab (es setzt das Grafikgerät zurück, ohne seine Ressourcen
    freizugeben). "Vollbild" ist deshalb ein randloses Fenster über den ganzen Monitor, und der
    Monitor läuft für die Dauer des Spiels mit 60 Hz (beim Beenden zurück auf die Desktop-Rate).
  * Bildrate fest auf 60 fps: darüber ist das Spiel instabil (Abstürze, hängende Skripte,
    die bekannten Treppen-/Bücherregal-Fehler). Im Vollbild über VSync bei 60 Hz, sonst über
    einen präzisen Frame-Limiter.
  * Kantenglättung (MSAA, Original-Feature) und 16x anisotrope Texturfilterung.
  * Helligkeit im Spielmenü und Blitz-Effekte wirken wieder: das Spiel stellt beides über eine
    Gamma-Kurve ein, die Windows im randlosen Vollbild ignoriert - der Patch rechnet sie selbst ins Bild.
    Tipp gegen den Grauschleier im Nebel: Helligkeit im Spielmenü etwas senken.
  * Filmkorn und Kratzer abschaltbar (Launcher -> Grafik). Das Spiel legt in jedem Bild neues Rauschen
    über das Bild, in Zwischensequenzen deutlich stärker und mit Kratzern; ab Werk an wie im Original.
  * Large Address Aware: das 32-Bit-Spiel darf 4 statt 2 GB nutzen (HD-Textur-Mods).

Maus & Tastatur
  Die Maus wird roh über DirectInput (exklusiv) gelesen - ohne Windows-Beschleunigung. Das
  Original dreht die Sicht aber mit 0,35 Grad pro Maus-Count (Regler-Mitte), gedacht für
  400-DPI-Mäuse: Mit heutigen Mäusen ist selbst die langsamste Reglerstufe viel zu schnell, und
  die Stufen sind grob. Außerdem multipliziert das Spiel die Mausbewegung mit der Bildzeit -
  dieselbe Handbewegung dreht bei schwankender Bildrate unterschiedlich weit.
  "Moderne Maussteuerung" (Patch im Speicher, die exe bleibt unverändert):
  * Bildraten-unabhängig: jeder Count dreht immer um denselben Winkel.
  * Empfindlichkeit auf derselben Skala wie Counter-Strike & Co. (0,022 Grad x Wert pro
    Count) - wer seinen Wert aus Shootern kennt, stellt einfach denselben ein. Keine DPI-Angabe
    nötig; der Regler im Spiel wird dann ignoriert.
  * Keine Glättung, keine Beschleunigung, keine Rundung.
  Im Spiel nachgemessen (4K, RX 9070 XT): Original 0,3497 Grad pro Count; mit Patch bei
  Empfindlichkeit 2,0 drehen 1000 Counts genau 44,00 Grad - bei 60 und bei 30 fps gleich
  (ohne den Fix dreht dieselbe Bewegung bei 30 fps doppelt so weit).
  Tastenbelegung und "Maus umkehren" lassen sich im Launcher vor dem Spiel einstellen (inkl.
  Vorlage "Modern (WASD)"). Gespeichert wird in den Optionen des Spiels
  (Dokumente\Bethesda\Call of Cthulhu\Options\Configuration.Data) - das Spielmenü zeigt dieselbe
  Belegung. Die Datei entsteht beim ersten Spielstart.

Controller
  Die PC-Fassung kennt nur Tastatur und Maus. Der Patch übersetzt einen Xbox-Controller oder einen
  PlayStation-Controller (DualShock 4, DualSense - direkt per USB oder Bluetooth, ohne Steam oder
  DS4Windows) in die Tasten deiner Belegung - eine geänderte Tastenbelegung gilt also auch fürs Pad.
  Ideal für den TV im Wohnzimmer. PlayStation-Tasten: Kreuz = A, Kreis = B, Quadrat = X,
  Dreieck = Y, L1/R1 = LB/RB, L2/R2 = LT/RT, Share/Create = View, Options = Start.
  * Belegung wie in der Xbox-Fassung (Handbuch, "Controller Configuration 1"): linker Stick laufen,
    rechter Stick umsehen (Tempo und Umkehren im Launcher), RT Feuer, LT Zielen, A Aktion,
    X Springen, B Ducken, Y Nachladen, rechten Stick drücken Schleichen, View Szene überspringen,
    Start Menü, LB Schnellheilung (Xbox: weiße Taste), RB Inventar (Xbox: schwarze Taste),
    Steuerkreuz: hoch nächste Waffe, runter Waffe zeigen/verstecken, links/rechts lehnen (die
    PC-Fassung lehnt mit eigenen Tasten, auf der Xbox ging das über den Stick im Schleichmodus).
  * Stufenloses Lauftempo wie auf der Xbox: leicht gedrückt schleichen, voll gedrückt normales
    Tempo (im Spiel gemessen: 100 / 56 / 37 % bei 100 / 58 / 40 % Ausschlag).
  * Laufen in jede Richtung, nicht nur in 8. Bis 15 Grad neben gerade bleibt die Figur exakt
    gerade - der Stick muss nicht haargenau gedrückt werden.
  * Pad jederzeit ein- und ausstecken; Maus und Tastatur funktionieren parallel weiter.
  * In Menüs wählt das Steuerkreuz die Einträge aus (wie die Pfeiltasten), der Stick bewegt den
    Zeiger, A bestätigt, Start geht zurück. B ist immer nur Ducken.

Ton
  Das Spiel mischt seinen Ton ausschließlich mit FMOD Ex in Software (3D-Positionierung,
  Entfernung, Dämpfungsfilter durch Wände) und folgt der Lautsprecherkonfiguration von Windows.
  Es gibt in der PC-Fassung kein EAX und kein Hardware-3D - deshalb fügt der Patch bewusst
  nichts hinzu. Bestes Setup: in Windows die echte Lautsprecheranordnung einstellen (Stereo,
  5.1 oder 7.1; Kopfhörer: Stereo), Untertitel und Sprache im Launcher, Lautstärken im Spiel.

Hinweise
  * GOG Galaxy "Dateien prüfen/reparieren" stellt Launcher und exe wieder her -> Setup erneut
    ausführen ("Aktualisieren", Einstellungen bleiben).
  * Bekannte Original-Fehler bleiben: Kapitel 3 Hoggs Bücherregal, Zeitungsbüro (Raum der
    Erhängten meiden). Spielstände, die mit über 60 fps entstanden sind, können Fehler mitnehmen.
  * Diagnose: in Engine\scripts\CoCHD.ini unter [Debug] Log=1 setzen -> CoCHD.log.

ENGLISH
-------
For the GOG release on Windows 10/11. Run CoCHD_Setup.exe: it finds the game, checks the files
and installs. Start the game with "Call of Cthulhu - HD Launcher" or from GOG Galaxy as usual;
the patch is active either way. The new launcher replaces CoCDCoTELauncher.exe (backed up), so
GOG Galaxy's "Settings" entry opens it too. Uninstall via Windows "Apps & Features" (restores
every original file, keeps savegames and settings).

Graphics: automatic resolution from the monitor on every start (capped at 3276 px width, the
inventory breaks beyond - 4K renders at 3200x1800 and is scaled), ThirteenAG's widescreen fix,
fullscreen (borderless window, monitor switched to 60 Hz while playing - Alt+Tab safe, the
original crashes) / borderless / windowed, 60 fps lock (the game is unstable above), MSAA, 16x
anisotropic filtering, Large Address Aware, film grain can be switched off, the in-game brightness
option and screen flashes work again (the game's gamma ramp is applied by the patch).
Mouse & keyboard: raw DirectInput already; the patch removes the frame-time factor from mouse
look and replaces the coarse slider (made for 400 DPI) with a sensitivity on the Counter-Strike
scale (no DPI needed). Key bindings and invert mouse can be set in the launcher before playing.
Sound: FMOD Ex software mixing only (no EAX / hardware 3D in the PC version) - nothing is added;
set your real speaker layout in Windows, the game follows it.
Controller: an Xbox pad or a PlayStation pad (DualShock 4, DualSense - read directly over USB or
Bluetooth, no Steam or DS4Windows needed) is translated into your key bindings: sticks walk and look, buttons as in the Xbox version's default configuration (A action,
X jump, B crouch, Y reload, right stick click sneak, Back skip, LB quick heal, RB inventory, d-pad
weapons and leaning). Walking speed follows the tilt and
you walk in any direction (straight within 15 degrees), like on the Xbox; in menus the stick moves
the pointer (the d-pad selects entries like the arrow keys), A confirms, Start goes back.

Versionen / versions
--------------------
  1.2.2  Launcher und Setup ohne Fremdlogo / launcher and setup without a third-party logo
  1.2.1  Lizenz jetzt PolyForm Noncommercial 1.0.0 (nicht kommerziell, Namensnennung) /
         license now PolyForm Noncommercial 1.0.0 (non-commercial, credit required)
  1.2  Controller: stufenloses Lauftempo und Laufen in jede Richtung (bis 15 Grad gerade);
       PlayStation-Controller direkt; Filmkorn abschaltbar; Helligkeit im Spielmenü und Blitze wirken
       wieder; Tastenbelegung löschen (Entf halten); Launcher auf Deutsch oder Englisch (Info);
       Menüs mit dem Steuerkreuz; Pad-Belegung wie in der Xbox-Fassung /
       controller: analog walking speed and direction (straight within 15 degrees); PlayStation pads
       directly; film grain switch; in-game brightness and screen flashes work again; clear a key
       binding (hold Del); launcher in English or German (About); d-pad menu navigation; pad layout as
       in the Xbox version
  1.1  Tastenbelegung im Launcher, Controller-Unterstützung / key bindings in the launcher, controller
  1.0  erste Version / first release

Technical details
-----------------
  * Engine\dinput8.dll: Ultimate ASI Loader 7.7.0 (ThirteenAG), loads Engine\scripts\*.asi
  * Engine\scripts\CallOfCthulhu.WidescreenFix.asi/.ini: ThirteenAG, release 2024-09-11
  * Engine\scripts\CoCHD.asi/.ini (this patch, source in the project):
    - SHGetValueA hook: ScreenWidth/ScreenHeight/RefreshRate of HKCU\Software\Bethesda
      Softworks\Call Of Cthulhu DCoTE\Settings answered with the monitor's mode
    - IDirect3D9::CreateDevice/Reset: always a windowed device (exclusive ones crash on Alt+Tab),
      window pinned to the monitor, 60 Hz desktop mode for "fullscreen", needless resets skipped,
      MSAA check
    - PlayStation pads: a watcher thread finds a DualShock 4 / DualSense among the HID devices (Sony
      054C, product 05C4 / 09CC / 0BA0 / 0CE6 / 0DF2), a reader thread turns its input reports (USB and
      Bluetooth, basic and full) into an XInput state; it is used when no XInput pad is there
      (src\asi\sony_pad.h, offline test tests\sony_parse_test.ps1)
    - film grain: the post-effect that draws a per-frame rand() noise texture over the picture is skipped
      with [Display] FilmGrain=0 (its draw call goes through a wrapper)
    - gamma ramp: the game sets brightness and flashes with SetGammaRamp, which only exclusive fullscreen
      devices honour; CoCHD keeps the ramp and applies it before Present (copy of the back buffer, 256-entry
      lookup texture, ps_2_0 pass compiled with d3dcompiler_47.dll); [Display] GameGamma=0 turns it off
    - IDirect3DDevice9::Present: frame limiter (high resolution waitable timer); SetSamplerState:
      linear minification -> anisotropic
    - player look (CoCRotationController, RotateSpeed 400): "fmul [60.0]" on the frame delta
      -> fld1; the four look scales (1/65535) point to own values: rad per count =
      Sens x 0.022 deg (or 2 pi / (cm360 / 2.54 * DPI)), divided by the in-game slider x RotateSpeed
    - gamepad: XInput (xinput1_4 / 1_3 / 9_1_0) read in the game's own DirectInput polls; buttons and
      the left stick become the keys/mouse buttons bound in Configuration.Data, the right stick mouse
      counts (LookSpeed deg/s through the look scale); analog walking (AnalogWalk=1) scales the config
      movement speeds (MoveSpeedCrawl/Walk/Run, +0x5B8..+0x5C4) by the tilt while the pad walks, and the movement code's four direction reads (0x54D4EF) get the stick's
      share per axis instead of the keys' 1.0 (axis snap 15 degrees); in menus (game timer 8 paused or the look
      controller idle) both sticks move the menu cursor, A = left click, B/Start = Esc
  * Configuration.Data (game options, launcher "Mouse & Keyboard"): from 0x16A 17-byte bindings
    (u32 index, u8 device 0 mouse / 1 keyboard, u32 DirectInput code, u32 trigger flags, u32 text
    id); invert mouse = u32 at 0x35. Only device/code and the invert flag are changed.
    - FMOD Ex is only observed (log): output DSOUND, software sounds (FMOD_SOFTWARE | 2D/3D)
  * Engine\CoCMainWin32.exe: Large Address Aware bit (optional, original in _HDPatch_Backup)
  * Engine\CoCDCoTELauncher.exe: the HD launcher (original in _HDPatch_Backup)
  * Development\pcvideo: logo videos moved to _HDPatch_Backup\pcvideo when skipped
  * Registry (HKCU\...\Call Of Cthulhu DCoTE\Settings): MultisampleMode, ActivateSubtitles,
    Language, ScreenWidth/Height - the same values the original launcher writes

Lizenz / license
----------------
  HD-Patch (c) 2026 BockwurstWilly - https://github.com/BockwurstWilly
  PolyForm Noncommercial License 1.0.0 (Lizenzen\HD-Patch-License.txt): nutzen, weitergeben und
  verändern ist frei, kommerzielle Nutzung nicht; der Hinweis auf BockwurstWilly muss bleiben.
  Kommerzielle Nutzung nur nach Absprache über GitHub.
  Free to use, share and change; no commercial use; keep the credit to BockwurstWilly.
  Commercial use only by arrangement - contact via GitHub.
  Mitgelieferte Fremd-Dateien behalten ihre Lizenzen (THIRD-PARTY.txt) / bundled third-party
  files keep their own licenses.

Call of Cthulhu: Dark Corners of the Earth (c) 2005 Bethesda Softworks LLC. This patch contains
no game files.
