using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace Lakhfana
{
    public sealed class LayoutInfo
    {
        public IntPtr Hkl;
        public string Key;          // hex HKL string, e.g. "04010401"
        public string DisplayName;  // CultureInfo(langId).NativeName
        public int LangId;          // low word of HKL
        internal string Tag;        // layout name appended to DisplayName when two entries look alike

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public static class KeyboardLayouts
    {
        public const int LANG_ARABIC = 0x01;
        public const int LANG_ENGLISH = 0x09;

        /// <summary>HKL → settings key. Masked to 32 bits so the key is identical in 32- and 64-bit processes.</summary>
        public static string KeyOf(IntPtr hkl)
        {
            return (hkl.ToInt64() & 0xFFFFFFFFL).ToString("X8");
        }

        public static int PrimaryLanguage(int langId)
        {
            return langId & 0x3FF;
        }

        public static List<LayoutInfo> GetInstalled()
        {
            List<LayoutInfo> result = new List<LayoutInfo>();
            try
            {
                int count = Native.GetKeyboardLayoutList(0, null);
                if (count <= 0) return result;
                IntPtr[] list = new IntPtr[count];
                count = Native.GetKeyboardLayoutList(list.Length, list);
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < count; i++)
                {
                    IntPtr hkl = list[i];
                    if (hkl == IntPtr.Zero) continue;
                    string key = KeyOf(hkl);
                    if (!seen.Add(key)) continue;
                    LayoutInfo info = new LayoutInfo();
                    info.Hkl = hkl;
                    info.Key = key;
                    info.LangId = (int)(hkl.ToInt64() & 0xFFFF);
                    info.DisplayName = NameOf(info.LangId, key);
                    result.Add(info);
                }
                DisambiguateNames(result);
            }
            catch (Exception)
            {
                // never throw from here; an empty list is handled by callers
            }
            return result;
        }

        static string NameOf(int langId, string key)
        {
            try
            {
                CultureInfo ci = new CultureInfo(langId);
                if (!string.IsNullOrEmpty(ci.NativeName)) return ci.NativeName;
            }
            catch (Exception)
            {
            }
            return "Keyboard " + key;
        }

        static long Normalize(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            string s = key.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            long v;
            if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return -1;
            return v & 0xFFFFFFFFL;
        }

        public static LayoutInfo Find(List<LayoutInfo> all, string key)
        {
            if (all == null) return null;
            long want = Normalize(key);
            if (want < 0) return null;
            foreach (LayoutInfo li in all)
            {
                if ((li.Hkl.ToInt64() & 0xFFFFFFFFL) == want) return li;
            }
            return null;
        }

        /// <summary>High word of an HKL: the physical layout / device id (e.g. 0x0401 for Arabic 101).</summary>
        public static int LayoutIdOf(IntPtr hkl)
        {
            return (int)((hkl.ToInt64() >> 16) & 0xFFFF);
        }

        /// <summary>True when both HKLs type with the same physical layout (converting between them changes nothing).</summary>
        public static bool SameLayout(LayoutInfo x, LayoutInfo y)
        {
            if (x == null || y == null) return false;
            if (x == y) return true;
            return LayoutIdOf(x.Hkl) == LayoutIdOf(y.Hkl);
        }

        static LayoutInfo FirstWithLanguage(List<LayoutInfo> all, int primaryLang, LayoutInfo except)
        {
            foreach (LayoutInfo li in all)
            {
                if (except != null && SameLayout(li, except)) continue;
                if (PrimaryLanguage(li.LangId) == primaryLang) return li;
            }
            return null;
        }

        static LayoutInfo FirstOther(List<LayoutInfo> all, LayoutInfo except)
        {
            foreach (LayoutInfo li in all)
            {
                if (!SameLayout(li, except)) return li;
            }
            return null;
        }

        /// <summary>Picks the layout pair: saved keys if installed, else Arabic + English, else the first two.</summary>
        public static void PickPair(List<LayoutInfo> all, string keyA, string keyB, out LayoutInfo a, out LayoutInfo b)
        {
            a = null;
            b = null;
            if (all == null || all.Count == 0) return;

            LayoutInfo savedA = Find(all, keyA);
            LayoutInfo savedB = Find(all, keyB);
            if (savedA != null && savedB != null && !SameLayout(savedA, savedB))
            {
                a = savedA;
                b = savedB;
                return;
            }

            LayoutInfo keep = savedA != null ? savedA : savedB;
            if (keep != null)
            {
                LayoutInfo partner = null;
                int lang = PrimaryLanguage(keep.LangId);
                if (lang == LANG_ARABIC) partner = FirstWithLanguage(all, LANG_ENGLISH, keep);
                else if (lang == LANG_ENGLISH) partner = FirstWithLanguage(all, LANG_ARABIC, keep);
                if (partner == null) partner = FirstOther(all, keep);
                if (savedA != null) { a = keep; b = partner; }
                else { a = partner; b = keep; }
                if (a == null) { a = b; b = null; }
                return;
            }

            LayoutInfo ar = FirstWithLanguage(all, LANG_ARABIC, null);
            LayoutInfo en = FirstWithLanguage(all, LANG_ENGLISH, null);
            if (ar != null && en != null)
            {
                a = ar;
                b = en;
                return;
            }
            if (ar != null || en != null)
            {
                a = ar != null ? ar : en;
                b = FirstOther(all, a);
                return;
            }
            a = all[0];
            b = FirstOther(all, a);
        }

        /// <summary>The KLID registry key name of an HKL's layout, e.g. "00000401"; null when unknown.</summary>
        static string KlidOf(IntPtr hkl)
        {
            int id = LayoutIdOf(hkl);
            if ((id & 0xF000) != 0xF000) return id.ToString("X8");
            // 0xFnnn: "Layout Id" nnn of a variant layout; look it up
            try
            {
                string want = (id & 0x0FFF).ToString("X4");
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts", false))
                {
                    if (root == null) return null;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        using (RegistryKey k = root.OpenSubKey(name, false))
                        {
                            if (k == null) continue;
                            object v = k.GetValue("Layout Id");
                            if (v != null && string.Equals(v.ToString(), want, StringComparison.OrdinalIgnoreCase)) return name;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>Localized keyboard layout name (e.g. "Arabic (101)"); null when unknown.</summary>
        static string LayoutNameOf(IntPtr hkl)
        {
            try
            {
                string klid = KlidOf(hkl);
                if (klid == null) return null;
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\" + klid, false))
                {
                    if (k == null) return null;
                    object disp = k.GetValue("Layout Display Name");
                    if (disp != null && disp.ToString().StartsWith("@", StringComparison.Ordinal))
                    {
                        StringBuilder sb = new StringBuilder(260);
                        if (Native.SHLoadIndirectString(disp.ToString(), sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                            return sb.ToString();
                    }
                    object text = k.GetValue("Layout Text");
                    if (text != null && text.ToString().Length > 0) return text.ToString();
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>When two entries share a display name, append the keyboard layout name so they can be told apart.</summary>
        static void DisambiguateNames(List<LayoutInfo> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                bool dup = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (i != j && string.Equals(list[i].DisplayName, list[j].DisplayName, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (!dup) continue;
                string ln = LayoutNameOf(list[i].Hkl);
                list[i].Tag = string.IsNullOrEmpty(ln) ? list[i].Key : ln;
            }
            foreach (LayoutInfo li in list)
            {
                if (li.Tag != null) li.DisplayName = li.DisplayName + " - " + li.Tag;
            }
        }
    }
}
