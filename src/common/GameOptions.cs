using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CoCHD
{
    /// <summary>One input: keyboard (DirectInput key code) or mouse (0..9: axes, wheel, buttons).</summary>
    struct GameKey : IEquatable<GameKey>
    {
        public const byte Mouse = 0, Keyboard = 1;
        public byte Device;
        public int Code;

        public GameKey(byte device, int code) { Device = device; Code = code; }
        public static GameKey Key(int dik) { return new GameKey(Keyboard, dik); }
        public static GameKey Button(int code) { return new GameKey(Mouse, code); }
        /// <summary>No key: keyboard code 0, which DirectInput never reports as pressed.</summary>
        public static readonly GameKey None = new GameKey(Keyboard, 0);
        public bool IsNone { get { return Device == Keyboard && Code == 0; } }

        public bool Equals(GameKey o) { return Device == o.Device && Code == o.Code; }
        public override bool Equals(object o) { return o is GameKey && Equals((GameKey)o); }
        public override int GetHashCode() { return Device << 16 | Code; }

        // mouse codes as the game's DirectInput mouse reports them
        public const int WheelDown = 2, WheelUp = 5, MouseLeft = 6, MouseRight = 7, MouseMiddle = 8, Mouse4 = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetKeyNameText(int lParam, StringBuilder name, int size);

        /// <summary>Name in the current Windows keyboard layout (the game shows DIK_Z as "y" on a German one, too).</summary>
        public override string ToString()
        {
            if (Device == Mouse)
            {
                switch (Code)
                {
                    case MouseLeft: return T.Get("key_mouse_l");
                    case MouseRight: return T.Get("key_mouse_r");
                    case MouseMiddle: return T.Get("key_mouse_m");
                    case Mouse4: return T.Get("key_mouse_4");
                    case WheelUp: return T.Get("key_wheel_up");
                    case WheelDown: return T.Get("key_wheel_down");
                    default: return T.Get("key_mouse_axis", Code);
                }
            }
            if (IsNone) return T.Get("key_none");
            // Windows abbreviates these in German ("Rück", "Leer")
            if (Code == 0x0E) return T.Get("key_backspace");
            if (Code == 0x39) return T.Get("key_space");
            var sb = new StringBuilder(64);
            int lp = (Code & 0x7F) << 16 | ((Code & 0x80) != 0 ? 1 << 24 : 0);
            return GetKeyNameText(lp, sb, sb.Capacity) > 0 ? sb.ToString() : "#" + Code.ToString("X2");
        }
    }

    /// <summary>A line of the game's key configuration menu; some actions have two entries (press / hold).</summary>
    class GameAction
    {
        public readonly string Id, De, En;
        public readonly int[] TextIds;

        public GameAction(string id, string de, string en, params int[] textIds) { Id = id; De = de; En = en; TextIds = textIds; }
        public string Name { get { return T.German ? De : En; } }
    }

    /// <summary>
    /// Documents\Bethesda\Call of Cthulhu\Options\Configuration.Data - the game's options. Plain binary:
    /// header, the settings block (mouse slider at 0x31, invert mouse at 0x35, u32 each), then from 0x16A
    /// one 17-byte record per binding: u32 index, u8 device (0 mouse, 1 keyboard), u32 code, u32 trigger
    /// flags, u32 text id of the action. Only device and code are ever changed here.
    /// </summary>
    class GameOptions
    {
        const int Magic = unchecked((int)0x87654321);
        const int InvertAt = 0x35, RecordsAt = 0x16A, RecordSize = 17;

        // the 18 lines of the in-game key configuration menu; names exactly as the game's own texts
        public static readonly GameAction[] Actions =
        {
            new GameAction("fwd", "Vorwärts", "Forwards", 400),
            new GameAction("back", "Rückwärts", "Backwards", 401),
            new GameAction("left", "Seitschritt links", "Strafe Left", 402),
            new GameAction("right", "Seitschritt rechts", "Strafe Right", 403),
            new GameAction("crouch", "Ducken", "Crouch", 410),
            new GameAction("jump", "Springen", "Jump", 408),
            new GameAction("sneak", "Schleichen", "Sneak", 526, 596),
            new GameAction("leanl", "Nach links lehnen", "Lean Left", 416),
            new GameAction("leanr", "Nach rechts lehnen", "Lean Right", 417),
            new GameAction("look", "Blick/Seitschritt", "Look Strafe", 595),
            new GameAction("action", "Aktion", "Action", 418),
            new GameAction("inv", "Inventar", "Inventory", 586),
            new GameAction("heal", "Schnellheilung", "Quick Heal", 585),
            new GameAction("skip", "Szene überspringen", "Skip Scene", 566),
            new GameAction("aim", "Zielen", "Aim", 411, 412),
            new GameAction("fire", "Feuer", "Fire", 413, 510),
            new GameAction("reload", "Nachladen", "Reload", 424),
            new GameAction("weapon", "Waffen zeigen/verstecken", "Hide/Show Weapon", 605),
        };

        public static GameAction Action(string id) { return Actions.First(a => a.Id == id); }

        public static Dictionary<string, GameKey> OriginalLayout()
        {
            return new Dictionary<string, GameKey>
            {
                { "fwd", GameKey.Key(0x11) }, { "back", GameKey.Key(0x2D) }, { "left", GameKey.Key(0x1E) }, { "right", GameKey.Key(0x20) },
                { "crouch", GameKey.Key(0x2E) }, { "jump", GameKey.Key(0x39) }, { "sneak", GameKey.Key(0x2A) },
                { "leanl", GameKey.Key(0x10) }, { "leanr", GameKey.Key(0x12) }, { "look", GameKey.Key(0x21) },
                { "action", GameKey.Key(0x1F) }, { "inv", GameKey.Key(0x17) }, { "heal", GameKey.Key(0x23) }, { "skip", GameKey.Key(0x0E) },
                { "aim", GameKey.Button(GameKey.MouseRight) }, { "fire", GameKey.Button(GameKey.MouseLeft) },
                { "reload", GameKey.Key(0x13) }, { "weapon", GameKey.Key(0x2C) },
            };
        }

        /// <summary>Today's shooter layout: WASD, F acts, Tab opens the inventory, X holsters, V looks around.</summary>
        public static Dictionary<string, GameKey> ModernLayout()
        {
            var m = OriginalLayout();
            m["back"] = GameKey.Key(0x1F);   // S
            m["action"] = GameKey.Key(0x21); // F
            m["look"] = GameKey.Key(0x2F);   // V
            m["inv"] = GameKey.Key(0x0F);    // Tab
            m["weapon"] = GameKey.Key(0x2D); // X
            return m;
        }

        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Bethesda\Call of Cthulhu\Options\Configuration.Data"); }
        }

        byte[] data;
        public bool Loaded { get { return data != null; } }

        public static GameOptions Load()
        {
            var o = new GameOptions();
            try
            {
                var b = File.ReadAllBytes(FilePath);
                if (b.Length >= RecordsAt + RecordSize && BitConverter.ToInt32(b, 0) == b.Length && BitConverter.ToInt32(b, 4) == Magic &&
                    (b.Length - RecordsAt) % RecordSize == 0)
                    o.data = b;
            }
            catch (Exception)
            {
            }
            return o;
        }

        public bool InvertMouse
        {
            get { return data != null && BitConverter.ToInt32(data, InvertAt) == 1; }
            set { if (data != null) BitConverter.GetBytes(value ? 1 : 0).CopyTo(data, InvertAt); }
        }

        IEnumerable<int> Records()
        {
            for (int at = RecordsAt; at + RecordSize <= data.Length; at += RecordSize)
                yield return at;
        }

        /// <summary>The key of an action, or null if the file has no entry for it.</summary>
        public GameKey? Get(GameAction a)
        {
            if (data == null)
                return null;
            foreach (var at in Records())
                if (BitConverter.ToInt32(data, at + 13) == a.TextIds[0])
                    return new GameKey(data[at + 4], BitConverter.ToInt32(data, at + 5));
            return null;
        }

        public void Set(GameAction a, GameKey k)
        {
            if (data == null)
                return;
            foreach (var at in Records())
            {
                if (!a.TextIds.Contains(BitConverter.ToInt32(data, at + 13)))
                    continue;
                data[at + 4] = k.Device;
                BitConverter.GetBytes(k.Code).CopyTo(data, at + 5);
            }
        }

        /// <summary>Assign a key; an action that had it gets this action's old key (swap).</summary>
        public void Assign(GameAction a, GameKey k)
        {
            var old = Get(a);
            if (k.IsNone)
            {
                Set(a, k);
                return;
            }
            foreach (var other in Actions)
            {
                if (other == a)
                    continue;
                var ok = Get(other);
                if (ok.HasValue && ok.Value.Equals(k) && old.HasValue)
                    Set(other, old.Value);
            }
            Set(a, k);
        }

        public void Apply(Dictionary<string, GameKey> layout)
        {
            foreach (var a in Actions)
            {
                GameKey k;
                if (layout.TryGetValue(a.Id, out k))
                    Set(a, k);
            }
        }

        public void Save()
        {
            if (data != null)
                FileUtil.WriteAtomic(FilePath, data);
        }
    }
}
