using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lakhfana
{
    /// <summary>
    /// One hotkey press: copy the selection (or the current line), convert it to the other layout, paste it back,
    /// restore the user's clipboard, and optionally switch the window's keyboard language.
    /// </summary>
    public sealed class SwitchEngine
    {
        const int CopyTimeoutMs = 400;
        const int PasteSettleMs = 350;

        readonly Settings settings;
        readonly object gate = new object();
        int busy;
        Converter converter;
        string converterKey;
        // The last conversion, so pressing the hotkey again on the same text undoes it exactly.
        string lastOriginal, lastConverted;
        IntPtr lastOriginalHkl;

        public event Action<string> Notify;

        public SwitchEngine(Settings settings)
        {
            this.settings = settings;
        }

        public void ReloadLayouts()
        {
            lock (gate)
            {
                converter = null;
                converterKey = null;
            }
        }

        public void Trigger()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            try
            {
                // Must happen while the user still holds the keys: stops Alt/Win release from opening menus.
                if (InputSim.IsAltDown() || InputSim.IsWinDown()) InputSim.MaskMenuKey();
                Thread t = new Thread(Run);
                t.IsBackground = true;
                t.Name = "Lakhfana switch";
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        void Say(string message)
        {
            Action<string> h = Notify;
            if (h == null) return;
            try { h(message); } catch (Exception) { }
        }

        Converter GetConverter(out string problem)
        {
            problem = null;
            List<LayoutInfo> all = KeyboardLayouts.GetInstalled();
            LayoutInfo a, b;
            KeyboardLayouts.PickPair(all, settings.LayoutA, settings.LayoutB, out a, out b);
            if (a == null || b == null || KeyboardLayouts.SameLayout(a, b))
            {
                problem = "لازم يبقى عندك لغتين كيبورد على الأقل في الويندوز عشان لخفنه يشتغل.";
                return null;
            }
            string key = a.Key + "|" + b.Key;
            lock (gate)
            {
                if (converter == null || converterKey != key)
                {
                    converter = new Converter(a.Hkl, b.Hkl);
                    converterKey = key;
                }
                return converter;
            }
        }

        void Run()
        {
            try
            {
                RunOnce();
            }
            catch (Exception)
            {
                Say("حصلت مشكلة وإحنا بنحوّل الكلام، جرّب تاني.");
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        void RunOnce()
        {
            if (!InputSim.WaitForModifiersReleased(1500))
            {
                Say("سيب زرار الويندوز وجرّب تاني.");
                return;
            }

            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == Native.GetCurrentProcessId()) return;   // our own windows

            if (Elevation.IsHigherThanUs(pid))
            {
                Say("البرنامج ده شغال كمسؤول (Administrator)، فالويندوز مش بيسمح للخفنه يكتب فيه. شغّل لخفنه كمسؤول أو جرّب في برنامج تاني.");
                return;
            }

            string problem;
            Converter conv = GetConverter(out problem);
            if (conv == null)
            {
                Say(problem);
                return;
            }

            IntPtr sourceHkl = Native.GetKeyboardLayout(thread);

            ClipboardHelper.Snapshot backup = ClipboardHelper.Backup();
            bool restored = false;
            try
            {
                string text = CopySelection();
                if (text == null && settings.SelectLineIfEmpty)
                {
                    InputSim.SelectCurrentLine();
                    Thread.Sleep(30);
                    text = CopySelection();
                }
                if (text == null)
                {
                    if (!settings.SelectLineIfEmpty) Say("حدد الكلام الأول وبعدين دوس الاختصار.");
                    return;
                }

                IntPtr targetHkl;
                string converted;
                if (lastConverted != null && text == lastConverted && lastOriginal != null)
                {
                    converted = lastOriginal;
                    targetHkl = lastOriginalHkl;
                }
                else
                {
                    converted = conv.Convert(text, sourceHkl, out targetHkl);
                }
                if (converted == text)
                {
                    Say("مفيش حاجة تتحوّل في الكلام ده.");
                    return;
                }

                if (!ClipboardHelper.SetText(converted))
                {
                    Say("الحافظة مشغولة دلوقتي، جرّب تاني.");
                    return;
                }
                uint ours = ClipboardHelper.Sequence;
                if (Native.GetForegroundWindow() != hwnd) return;   // user switched away; don't paste elsewhere
                InputSim.Paste();
                WaitForTarget(hwnd);
                lastOriginal = text;
                lastConverted = converted;
                lastOriginalHkl = targetHkl == conv.HklA ? conv.HklB : conv.HklA;

                // If something else replaced the clipboard in the meantime, keep it.
                if (ClipboardHelper.Sequence == ours)
                {
                    ClipboardHelper.Restore(backup);
                }
                else
                {
                    ClipboardHelper.Dispose(backup);
                }
                restored = true;

                if (settings.SwitchLayoutAfter && targetHkl != IntPtr.Zero) SwitchLayout(thread, hwnd, targetHkl);
            }
            finally
            {
                if (!restored) ClipboardHelper.Restore(backup);
            }
        }

        /// <summary>Ctrl+C and wait for the clipboard to change. Null when nothing is selected.</summary>
        static string CopySelection()
        {
            uint before = ClipboardHelper.Sequence;
            InputSim.Copy();
            if (!ClipboardHelper.WaitForChange(before, CopyTimeoutMs)) return null;
            // Give the source app a moment to finish writing all of its formats.
            Thread.Sleep(20);
            if (ClipboardHelper.IsEmptySelectionLineCopy()) return null;
            string text = ClipboardHelper.ReadText();
            if (string.IsNullOrEmpty(text)) return null;
            return text;
        }

        /// <summary>Lets the target process the paste before the clipboard is restored.</summary>
        static void WaitForTarget(IntPtr hwnd)
        {
            Thread.Sleep(120);
            IntPtr result;
            Native.SendMessageTimeout(hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 1000, out result);
            Thread.Sleep(PasteSettleMs - 120);
        }

        static void SwitchLayout(uint thread, IntPtr hwnd, IntPtr hkl)
        {
            Native.GUITHREADINFO info = new Native.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO));
            IntPtr target = hwnd;
            if (Native.GetGUIThreadInfo(thread, ref info) && info.hwndFocus != IntPtr.Zero) target = info.hwndFocus;
            Native.PostMessage(target, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        }

        /// <summary>UIPI: input from a lower-integrity process is dropped by higher-integrity windows.</summary>
        static class Elevation
        {
            public static bool IsHigherThanUs(uint pid)
            {
                int ours = IntegrityOf(Native.GetCurrentProcess());
                if (ours < 0) return false;
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                {
                    return Marshal.GetLastWin32Error() == Native.ERROR_ACCESS_DENIED && ours < 0x3000;
                }
                try
                {
                    int theirs = IntegrityOf(h);
                    if (theirs < 0) return false;
                    return theirs > ours;
                }
                finally
                {
                    Native.CloseHandle(h);
                }
            }

            static int IntegrityOf(IntPtr process)
            {
                IntPtr token;
                if (!Native.OpenProcessToken(process, Native.TOKEN_QUERY, out token)) return -1;
                try
                {
                    int len;
                    Native.GetTokenInformation(token, Native.TokenIntegrityLevel, IntPtr.Zero, 0, out len);
                    if (len <= 0) return -1;
                    IntPtr buf = Marshal.AllocHGlobal(len);
                    try
                    {
                        if (!Native.GetTokenInformation(token, Native.TokenIntegrityLevel, buf, len, out len)) return -1;
                        IntPtr sid = Marshal.ReadIntPtr(buf);   // TOKEN_MANDATORY_LABEL.Label.Sid
                        int count = Marshal.ReadByte(Native.GetSidSubAuthorityCount(sid));
                        if (count == 0) return -1;
                        return Marshal.ReadInt32(Native.GetSidSubAuthority(sid, (uint)(count - 1)));
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                }
                finally
                {
                    Native.CloseHandle(token);
                }
            }
        }
    }
}
