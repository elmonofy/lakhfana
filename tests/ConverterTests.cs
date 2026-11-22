using System;
using System.Collections.Generic;
using System.Text;

namespace Lakhfana.Tests
{
    /// <summary>Console tests for the converter against the real Windows layouts (en-US 0409, Arabic 101 0401).</summary>
    public static class ConverterTests
    {
        static int failures;
        static int passes;

        static void Check(string name, string actual, string expected)
        {
            if (actual == expected) { passes++; return; }
            failures++;
            Console.WriteLine("FAIL " + name + "\n   expected: [" + expected + "]\n   actual:   [" + actual + "]");
        }

        static void CheckAny(string name, string actual, params string[] allowed)
        {
            foreach (string a in allowed) if (actual == a) { passes++; return; }
            failures++;
            Console.WriteLine("FAIL " + name + "\n   expected one of: [" + string.Join("] [", allowed) + "]\n   actual: [" + actual + "]");
        }

        /// <summary>Finds an installed HKL whose high word is the layout id; loads it only if missing.</summary>
        static IntPtr Acquire(int layoutId, string klid, List<IntPtr> loaded)
        {
            foreach (LayoutInfo li in KeyboardLayouts.GetInstalled())
            {
                if (KeyboardLayouts.LayoutIdOf(li.Hkl) == layoutId) return li.Hkl;
            }
            IntPtr h = Native.LoadKeyboardLayout(klid, Native.KLF_NOTELLSHELL);
            if (h != IntPtr.Zero) loaded.Add(h);
            return h;
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            List<IntPtr> loaded = new List<IntPtr>();
            try
            {
                IntPtr en = Acquire(0x0409, "00000409", loaded);
                IntPtr ar = Acquire(0x0401, "00000401", loaded);
                if (en == IntPtr.Zero || ar == IntPtr.Zero)
                {
                    Console.WriteLine("SKIP: en-US and Arabic 101 layouts are required");
                    return 2;
                }
                Console.WriteLine("en=" + KeyboardLayouts.KeyOf(en) + " ar=" + KeyboardLayouts.KeyOf(ar));
                Run(new Converter(ar, en), ar, en);
                // Pair order must not matter.
                IntPtr t;
                Check("order-independent", new Converter(en, ar).Convert("hgsghl ugd;l", IntPtr.Zero, out t), "السلام عليكم");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("CRASH " + ex);
            }
            finally
            {
                foreach (IntPtr h in loaded) Native.UnloadKeyboardLayout(h);
            }
            Console.WriteLine(passes + " passed, " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static string C(Converter c, string s)
        {
            IntPtr t;
            return c.Convert(s, IntPtr.Zero, out t);
        }

        static void Run(Converter c, IntPtr ar, IntPtr en)
        {
            IntPtr target;

            // English keys → Arabic
            Check("salam", c.Convert("hgsghl ugd;l", IntPtr.Zero, out target), "السلام عليكم");
            Check("salam target", KeyboardLayouts.KeyOf(target), KeyboardLayouts.KeyOf(ar));
            Check("b is lam-alef", C(c, "b"), "لا");

            // Arabic keys → English (lam-alef disambiguation)
            Check("about", c.Convert("شلاخعف", IntPtr.Zero, out target), "about");
            Check("about target", KeyboardLayouts.KeyOf(target), KeyboardLayouts.KeyOf(en));
            Check("light", C(c, "مهلاف"), "light");
            Check("black", C(c, "لامشؤن"), "black");
            Check("night", C(c, "ىهلاف"), "night");
            Check("table", C(c, "فشلامث"), "table");
            Check("right", C(c, "قهلاف"), "right");
            Check("hello world", C(c, "اثممخ صخقمي"), "hello world");
            Check("Good (shift)", C(c, "لأخخي"), "Good");

            // Arabic → English → Arabic round trip of the salam rendering accepts either lam-alef spelling
            CheckAny("salam back", C(c, "السلام عليكم"), "hgsghl ugd;l", "hgsbl ugd;l");

            // Whitespace preserved exactly
            Check("whitespace", C(c, "  hgsghl\r\n\tugd;l  "), "  السلام\r\n\tعليكم  ");
            Check("empty", C(c, ""), "");
            Check("digits", C(c, "12345"), "12345");
            Check("spaces only", C(c, "   "), "   ");

            // Mixed directions: each word converts on its own
            Check("mixed", C(c, "hgsghl عليكم"), "السلام ugd;l");

            // Punctuation attached to words follows the word's direction
            Check("comma key", C(c, "hgsghl,"), "السلامو");

            // Unknown characters pass through (emoji, surrogate pairs)
            Check("emoji", C(c, "hgsghl😀"), "السلام😀");

            // Long text
            StringBuilder longIn = new StringBuilder(), longOut = new StringBuilder();
            for (int i = 0; i < 500; i++) { longIn.Append("hgsghl ugd;l\n"); longOut.Append("السلام عليكم\n"); }
            Check("long", C(c, longIn.ToString()), longOut.ToString());

            // Property: random keystrokes typed in English convert to exactly the same keystrokes typed in Arabic.
            LayoutMap mEn = LayoutMap.Build(en);
            LayoutMap mAr = LayoutMap.Build(ar);
            List<int> keys = new List<int>();
            foreach (int ks in mEn.Outputs.Keys)
            {
                string e = mEn.Render(ks), a = mAr.Render(ks);
                if (e == null || a == null || e == " ") continue;
                // Only keys whose English output is unique and contains a Latin letter, so direction is decisive.
                if (mEn.Reverse[e] != ks) continue;
                keys.Add(ks);
            }
            Random rnd = new Random(12345);
            int bad = 0;
            for (int trial = 0; trial < 2000; trial++)
            {
                StringBuilder ein = new StringBuilder(), aout = new StringBuilder();
                int letters = 0;
                int n = 1 + rnd.Next(8);
                for (int k = 0; k < n; k++)
                {
                    int ks = keys[rnd.Next(keys.Count)];
                    string e = mEn.Render(ks);
                    ein.Append(e);
                    aout.Append(mAr.Render(ks));
                    if (char.IsLetter(e[0])) letters++;
                }
                if (letters * 2 <= n) continue;   // direction must be clear from the letters
                string got = C(c, ein.ToString());
                if (got != aout.ToString())
                {
                    if (bad < 5) Console.WriteLine("FAIL roundtrip [" + ein + "] → [" + got + "] expected [" + aout + "]");
                    bad++;
                }
            }
            if (bad == 0) passes++; else { failures++; Console.WriteLine("roundtrip failures: " + bad); }
        }
    }
}
