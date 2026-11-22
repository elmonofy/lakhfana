using System;
using System.Collections.Generic;
using System.Text;

namespace Lakhfana
{
    /// <summary>
    /// Re-types text as if the same keys had been pressed in the other keyboard layout.
    /// Direction is decided per word (whitespace-separated), so mixed text converts both ways.
    /// </summary>
    public sealed class Converter
    {
        const int MaxCandidates = 256;

        readonly LayoutMap mapA;
        readonly LayoutMap mapB;

        public Converter(IntPtr hklA, IntPtr hklB)
        {
            mapA = LayoutMap.Build(hklA);
            mapB = LayoutMap.Build(hklB);
        }

        public IntPtr HklA { get { return mapA.Hkl; } }
        public IntPtr HklB { get { return mapB.Hkl; } }

        public string Convert(string text, IntPtr fallbackSourceHkl, out IntPtr targetHkl)
        {
            targetHkl = IntPtr.Zero;
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            // Split into runs: words and whitespace (whitespace kept exactly, including \r\n).
            List<string> runs = new List<string>();
            List<bool> isWord = new List<bool>();
            int start = 0;
            for (int i = 1; i <= text.Length; i++)
            {
                bool boundary = i == text.Length || char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[i - 1]);
                if (!boundary) continue;
                string run = text.Substring(start, i - start);
                runs.Add(run);
                isWord.Add(!char.IsWhiteSpace(run[0]));
                start = i;
            }

            // Whole-text direction: used for words that have no decisive characters.
            int totalA = 0, totalB = 0;
            int[] wordDir = new int[runs.Count];   // +1: A→B, -1: B→A, 0: undecided
            for (int r = 0; r < runs.Count; r++)
            {
                if (!isWord[r]) continue;
                int a, b;
                Count(runs[r], out a, out b);
                totalA += a;
                totalB += b;
                wordDir[r] = a > b ? 1 : (b > a ? -1 : 0);
            }
            int globalDir = totalA > totalB ? 1 : (totalB > totalA ? -1 : 0);
            if (globalDir == 0)
            {
                if (SameLayout(fallbackSourceHkl, mapB.Hkl) && !SameLayout(fallbackSourceHkl, mapA.Hkl)) globalDir = -1;
                else globalDir = 1;
            }

            StringBuilder sb = new StringBuilder(text.Length + 8);
            long toB = 0, toA = 0;
            for (int r = 0; r < runs.Count; r++)
            {
                if (!isWord[r]) { sb.Append(runs[r]); continue; }
                int dir = wordDir[r] != 0 ? wordDir[r] : globalDir;
                LayoutMap src = dir > 0 ? mapA : mapB;
                LayoutMap dst = dir > 0 ? mapB : mapA;
                string converted = ConvertWord(runs[r], src, dst);
                sb.Append(converted);
                if (dir > 0) toB += runs[r].Length; else toA += runs[r].Length;
            }
            targetHkl = toB >= toA ? mapB.Hkl : mapA.Hkl;
            return sb.ToString();
        }

        static bool SameLayout(IntPtr x, IntPtr y)
        {
            if (x == IntPtr.Zero || y == IntPtr.Zero) return false;
            if (x == y) return true;
            return KeyboardLayouts.LayoutIdOf(x) == KeyboardLayouts.LayoutIdOf(y)
                && (x.ToInt64() & 0xFFFF) == (y.ToInt64() & 0xFFFF);
        }

        /// <summary>Counts characters typed only by A, and only by B.</summary>
        void Count(string word, out int onlyA, out int onlyB)
        {
            onlyA = 0;
            onlyB = 0;
            foreach (char c in word)
            {
                bool inA = mapA.Chars.Contains(c);
                bool inB = mapB.Chars.Contains(c);
                if (inA && !inB) onlyA++;
                else if (inB && !inA) onlyB++;
            }
        }

        /// <summary>
        /// One step of a segmentation: either a keystroke found in the source layout (matched `length` chars),
        /// or a pass-through char (keystroke = -1).
        /// </summary>
        struct Step
        {
            public int Length;
            public int Keystroke;
        }

        static List<Step> OptionsAt(string word, int pos, LayoutMap src)
        {
            List<Step> options = new List<Step>();
            int max = Math.Min(src.MaxLength, word.Length - pos);
            for (int len = max; len >= 1; len--)
            {
                int ks;
                if (src.Reverse.TryGetValue(word.Substring(pos, len), out ks))
                {
                    Step s;
                    s.Length = len;
                    s.Keystroke = ks;
                    options.Add(s);
                }
            }
            if (options.Count == 0)
            {
                Step p;
                // Never split a surrogate pair.
                p.Length = (char.IsHighSurrogate(word[pos]) && pos + 1 < word.Length && char.IsLowSurrogate(word[pos + 1])) ? 2 : 1;
                p.Keystroke = -1;
                options.Add(p);
            }
            return options;
        }

        static string ConvertWord(string word, LayoutMap src, LayoutMap dst)
        {
            // Greedy longest-match result (also the answer when there is no ambiguity).
            string greedy = RenderGreedy(word, src, dst);
            if (!dst.IsLatin) return greedy;

            // Is there any ambiguity at all?
            bool ambiguous = false;
            for (int i = 0; i < word.Length && !ambiguous; i++)
            {
                if (OptionsAt(word, i, src).Count > 1) ambiguous = true;
            }
            if (!ambiguous) return greedy;

            List<string> candidates = new List<string>();
            Enumerate(word, 0, src, dst, new StringBuilder(), candidates);
            if (candidates.Count == 0 || candidates.Count >= MaxCandidates) return greedy;

            string best = greedy;
            double bestScore = EnglishScorer.Score(greedy);
            foreach (string c in candidates)
            {
                double s = EnglishScorer.Score(c);
                if (s > bestScore + 1e-9)
                {
                    best = c;
                    bestScore = s;
                }
            }
            return best;
        }

        static string RenderGreedy(string word, LayoutMap src, LayoutMap dst)
        {
            StringBuilder sb = new StringBuilder(word.Length + 4);
            int i = 0;
            while (i < word.Length)
            {
                Step s = OptionsAt(word, i, src)[0];
                AppendStep(sb, word, i, s, dst);
                i += s.Length;
            }
            return sb.ToString();
        }

        static void AppendStep(StringBuilder sb, string word, int pos, Step s, LayoutMap dst)
        {
            string outText = s.Keystroke >= 0 ? dst.Render(s.Keystroke) : null;
            if (outText == null) sb.Append(word, pos, s.Length);   // key types nothing in target: keep original
            else sb.Append(outText);
        }

        static void Enumerate(string word, int pos, LayoutMap src, LayoutMap dst, StringBuilder acc, List<string> results)
        {
            if (results.Count >= MaxCandidates) return;
            if (pos >= word.Length)
            {
                results.Add(acc.ToString());
                return;
            }
            foreach (Step s in OptionsAt(word, pos, src))
            {
                int mark = acc.Length;
                AppendStep(acc, word, pos, s, dst);
                Enumerate(word, pos + s.Length, src, dst, acc, results);
                acc.Length = mark;
                if (results.Count >= MaxCandidates) return;
            }
        }
    }
}
