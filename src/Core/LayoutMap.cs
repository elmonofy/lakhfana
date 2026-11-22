using System;
using System.Collections.Generic;

namespace Lakhfana
{
    /// <summary>
    /// What every key (VK 0x20..0xFE, unshifted and shifted) types in one keyboard layout, read from the OS via
    /// ToUnicodeEx, plus the reverse map (typed text → keystroke).
    /// A keystroke is encoded as an int: vk | ShiftBit.
    /// </summary>
    public sealed class LayoutMap
    {
        public const int ShiftBit = 0x100;
        public const int MaxOutputChars = 3;

        public readonly IntPtr Hkl;
        /// <summary>keystroke → text it types (1..3 chars).</summary>
        public readonly Dictionary<int, string> Outputs = new Dictionary<int, string>();
        /// <summary>text (1..3 chars) → keystroke; unshifted keystrokes win when a text is typed by several keys.</summary>
        public readonly Dictionary<string, int> Reverse = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Every character that appears in any output of this layout.</summary>
        public readonly HashSet<char> Chars = new HashSet<char>();
        /// <summary>Longest output in chars.</summary>
        public int MaxLength;
        /// <summary>True when most letters this layout types are ASCII Latin letters.</summary>
        public bool IsLatin;

        LayoutMap(IntPtr hkl)
        {
            Hkl = hkl;
        }

        public static bool IsShifted(int keystroke)
        {
            return (keystroke & ShiftBit) != 0;
        }

        public static int VkOf(int keystroke)
        {
            return keystroke & 0xFF;
        }

        public string Render(int keystroke)
        {
            string s;
            return Outputs.TryGetValue(keystroke, out s) ? s : null;
        }

        public static LayoutMap Build(IntPtr hkl)
        {
            LayoutMap map = new LayoutMap(hkl);
            if (hkl == IntPtr.Zero) return map;

            byte[] state = new byte[256];
            char[] buf = new char[16];

            // Unshifted pass first so unshifted keystrokes win in the reverse map.
            for (int pass = 0; pass < 2; pass++)
            {
                bool shift = pass == 1;
                for (int vk = 0x20; vk <= 0xFE; vk++)
                {
                    if (IsExcludedVk(vk)) continue;
                    string text = Translate(hkl, vk, shift, state, buf);
                    if (text == null) continue;
                    int ks = vk | (shift ? ShiftBit : 0);
                    map.Outputs[ks] = text;
                    if (!map.Reverse.ContainsKey(text)) map.Reverse[text] = ks;
                    foreach (char c in text) map.Chars.Add(c);
                    if (text.Length > map.MaxLength) map.MaxLength = text.Length;
                }
            }

            int letters = 0, latin = 0;
            foreach (KeyValuePair<int, string> kv in map.Outputs)
            {
                if (IsShifted(kv.Key) || kv.Value.Length != 1) continue;
                char c = kv.Value[0];
                if (!char.IsLetter(c)) continue;
                letters++;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            }
            map.IsLatin = letters > 0 && latin * 2 >= letters;
            return map;
        }

        static bool IsExcludedVk(int vk)
        {
            if (vk >= 0x60 && vk <= 0x6F) return true;  // numeric keypad: duplicates of main keys
            if (vk >= 0x21 && vk <= 0x2F) return true;  // navigation / editing keys
            if (vk >= 0x5B && vk <= 0x5F) return true;  // Win, Apps, Sleep
            if (vk >= 0x70 && vk <= 0x87) return true;  // F1..F24
            if (vk >= 0x88 && vk <= 0x8F) return true;  // reserved / navigation
            if (vk >= 0x90 && vk <= 0x9F) return true;  // NumLock, ScrollLock, OEM specific
            if (vk >= 0xA0 && vk <= 0xB9) return true;  // modifiers, browser and media keys
            if (vk == 0xE5 || vk == 0xE7) return true;  // IME process key, VK_PACKET
            if (vk >= 0xF6) return true;                 // Attn, CrSel, ... OEM_CLEAR
            return false;
        }

        static string Translate(IntPtr hkl, int vk, bool shift, byte[] state, char[] buf)
        {
            Array.Clear(state, 0, state.Length);
            if (shift)
            {
                state[Native.VK_SHIFT] = 0x80;
                state[Native.VK_LSHIFT] = 0x80;
            }
            uint scan = Native.MapVirtualKeyEx((uint)vk, Native.MAPVK_VK_TO_VSC, hkl);
            if (scan == 0 && vk != 0x20) return null;

            // wFlags = 4: do not change the keyboard state (Windows 10 1607+).
            int n = Native.ToUnicodeEx((uint)vk, scan, state, buf, buf.Length, 4, hkl);
            if (n < 0)
            {
                // Dead key: flush whatever state it left behind and skip the key.
                for (int i = 0; i < 4; i++)
                {
                    int r = Native.ToUnicodeEx((uint)vk, scan, state, buf, buf.Length, 4, hkl);
                    if (r >= 0) break;
                }
                return null;
            }
            if (n == 0) return null;
            if (n > MaxOutputChars) return null;
            string text = new string(buf, 0, n);
            foreach (char c in text)
            {
                if (c < 0x20 || c == 0x7F || (c >= 0x80 && c < 0xA0)) return null;
            }
            if (vk != 0x20 && text.Trim().Length == 0) return null;
            return text;
        }
    }
}
