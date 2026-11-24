using System;
using System.Globalization;
using System.Text;

namespace Lakhfana
{
    /// <summary>A global hotkey: MOD_* modifier flags plus a virtual-key code.</summary>
    public struct Hotkey : IEquatable<Hotkey>
    {
        public const int MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8;
        private const int ModMask = MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN;

        public int Modifiers;   // MOD_* flags
        public int Vk;          // virtual-key code

        public Hotkey(int modifiers, int vk)
        {
            Modifiers = modifiers & ModMask;
            Vk = vk & 0xFF;
        }

        /// <summary>No shortcut at all.</summary>
        public bool IsEmpty
        {
            get { return Vk == 0 && !IsModifierOnly; }
        }

        /// <summary>A shortcut made of two or more modifier keys only, e.g. Ctrl + Shift (fires on release).</summary>
        public bool IsModifierOnly
        {
            get { return Vk == 0 && BitCount(Modifiers) >= 2; }
        }

        public static int BitCount(int m)
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if ((m & (1 << i)) != 0) n++;
            return n;
        }

        public static Hotkey Default
        {
            get { return new Hotkey(MOD_CONTROL | MOD_SHIFT, 0x20); }
        }

        /// <summary>"Ctrl + Shift + Space" (order Ctrl, Alt, Shift, Win, key).</summary>
        public string ToDisplayString()
        {
            StringBuilder sb = new StringBuilder();
            if ((Modifiers & MOD_CONTROL) != 0) Append(sb, "Ctrl");
            if ((Modifiers & MOD_ALT) != 0) Append(sb, "Alt");
            if ((Modifiers & MOD_SHIFT) != 0) Append(sb, "Shift");
            if ((Modifiers & MOD_WIN) != 0) Append(sb, "Win");
            if (Vk != 0) Append(sb, KeyName(Vk));
            return sb.ToString();
        }

        public override string ToString()
        {
            return ToDisplayString();
        }

        /// <summary>"6:32"</summary>
        public string Serialize()
        {
            return Modifiers.ToString(CultureInfo.InvariantCulture) + ":" + Vk.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Inverse of Serialize; default(Hotkey) on bad input.</summary>
        public static Hotkey Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return default(Hotkey);
            string[] parts = s.Trim().Split(':');
            if (parts.Length != 2) return default(Hotkey);
            int mods, vk;
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out mods)) return default(Hotkey);
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out vk)) return default(Hotkey);
            if (mods < 0 || (mods & ~ModMask) != 0) return default(Hotkey);
            if (vk == 0) return BitCount(mods) >= 2 ? new Hotkey(mods, 0) : default(Hotkey);
            if (vk < 0 || vk > 0xFE) return default(Hotkey);
            if (IsModifierVk(vk)) return default(Hotkey);
            return new Hotkey(mods, vk);
        }

        /// <summary>True for Shift/Ctrl/Alt/Win virtual keys (never valid as the main key).</summary>
        public static bool IsModifierVk(int vk)
        {
            switch (vk)
            {
                case 0x10: case 0x11: case 0x12:          // SHIFT CONTROL MENU
                case 0xA0: case 0xA1: case 0xA2: case 0xA3: case 0xA4: case 0xA5:
                case 0x5B: case 0x5C:                      // LWIN RWIN
                    return true;
            }
            return false;
        }

        /// <summary>Latin display name of a virtual-key code.</summary>
        public static string KeyName(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
            if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60).ToString(CultureInfo.InvariantCulture);
            switch (vk)
            {
                case 0x03: return "Break";
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0C: return "Clear";
                case 0x0D: return "Enter";
                case 0x13: return "Pause";
                case 0x14: return "Caps Lock";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "Page Up";
                case 0x22: return "Page Down";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2C: return "Print Screen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5D: return "Menu";
                case 0x6A: return "Num *";
                case 0x6B: return "Num +";
                case 0x6C: return "Num Enter";
                case 0x6D: return "Num -";
                case 0x6E: return "Num .";
                case 0x6F: return "Num /";
                case 0x90: return "Num Lock";
                case 0x91: return "Scroll Lock";
                case 0xAD: return "Mute";
                case 0xAE: return "Volume Down";
                case 0xAF: return "Volume Up";
                case 0xB0: return "Next Track";
                case 0xB1: return "Previous Track";
                case 0xB2: return "Stop";
                case 0xB3: return "Play/Pause";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
                case 0xE2: return "<";
            }
            return "Key 0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static void Append(StringBuilder sb, string part)
        {
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append(part);
        }

        public bool Equals(Hotkey other)
        {
            return Modifiers == other.Modifiers && Vk == other.Vk;
        }

        public override bool Equals(object obj)
        {
            return obj is Hotkey && Equals((Hotkey)obj);
        }

        public override int GetHashCode()
        {
            return (Modifiers << 8) | Vk;
        }

        public static bool operator ==(Hotkey a, Hotkey b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Hotkey a, Hotkey b)
        {
            return !a.Equals(b);
        }
    }
}
