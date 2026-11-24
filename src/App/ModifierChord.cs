using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Lakhfana
{
    /// <summary>
    /// Detects a shortcut made of modifier keys only (e.g. Ctrl + Shift): it fires when the last of them is released,
    /// provided exactly that set was held, no other key or mouse button was pressed meanwhile, and the chord was short.
    /// RegisterHotKey can't express this, so it uses low-level keyboard/mouse hooks. The hooks only look at whether
    /// keys are modifiers; nothing is recorded. Injected input (including our own Ctrl+C / Ctrl+V) is ignored.
    /// Must be started on a thread with a message loop (the UI thread); the callback runs on that thread.
    /// </summary>
    internal sealed class ModifierChord : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
        private const int WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B, WM_MOUSEHWHEEL = 0x020E;
        private const int LLKHF_INJECTED = 0x10;
        private const int LLMHF_INJECTED = 0x01;
        private const int MaxChordMs = 1200;

        /// <summary>Tests only: also react to injected (simulated) input.</summary>
        internal static bool AcceptInjectedForTests;

        private readonly Action fired;
        private readonly HookProc keyProc;
        private readonly HookProc mouseProc;
        private IntPtr keyHook = IntPtr.Zero;
        private IntPtr mouseHook = IntPtr.Zero;
        private int target;
        private int down;        // per-side modifier bits currently held (see SideBit)
        private int peak;        // MOD_* union held during the current chord
        private bool tainted;    // another key / mouse button was used during the chord
        private long startTicks;

        public ModifierChord(Action fired)
        {
            this.fired = fired;
            keyProc = KeyCallback;
            mouseProc = MouseCallback;
        }

        public bool IsActive
        {
            get { return keyHook != IntPtr.Zero; }
        }

        public bool Start(int modifiers)
        {
            Stop();
            target = modifiers;
            IntPtr module = ChordNative.GetModuleHandle(null);
            keyHook = ChordNative.SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, module, 0);
            if (keyHook == IntPtr.Zero) return false;
            mouseHook = ChordNative.SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
            Reset();
            return true;
        }

        public void Stop()
        {
            if (keyHook != IntPtr.Zero) { ChordNative.UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { ChordNative.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            Reset();
        }

        public void Dispose()
        {
            Stop();
        }

        private void Reset()
        {
            down = 0;
            peak = 0;
            tainted = false;
        }

        /// <summary>One bit per physical modifier key, so releasing Left Ctrl while Right Ctrl is held keeps Ctrl down.</summary>
        private static int SideBit(int vk)
        {
            switch (vk)
            {
                case 0xA2: return 1;          // LCONTROL
                case 0xA3: return 2;          // RCONTROL
                case 0x11: return 1;          // CONTROL (rare in LL hooks)
                case 0xA4: return 4;          // LMENU
                case 0xA5: return 8;          // RMENU
                case 0x12: return 4;          // MENU
                case 0xA0: return 16;         // LSHIFT
                case 0xA1: return 32;         // RSHIFT
                case 0x10: return 16;         // SHIFT
                case 0x5B: return 64;         // LWIN
                case 0x5C: return 128;        // RWIN
            }
            return 0;
        }

        private static int ModsOf(int sides)
        {
            int m = 0;
            if ((sides & 3) != 0) m |= Hotkey.MOD_CONTROL;
            if ((sides & 12) != 0) m |= Hotkey.MOD_ALT;
            if ((sides & 48) != 0) m |= Hotkey.MOD_SHIFT;
            if ((sides & 192) != 0) m |= Hotkey.MOD_WIN;
            return m;
        }

        private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    int vk = Marshal.ReadInt32(lParam);            // KBDLLHOOKSTRUCT.vkCode
                    int flags = Marshal.ReadInt32(lParam, 8);      // KBDLLHOOKSTRUCT.flags
                    if ((flags & LLKHF_INJECTED) == 0 || AcceptInjectedForTests) OnKey(msg, vk);
                }
                catch (Exception) { }
            }
            return ChordNative.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && down != 0)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    int flags = Marshal.ReadInt32(lParam, 12);     // MSLLHOOKSTRUCT.flags (after POINT + mouseData)
                    bool injected = (flags & LLMHF_INJECTED) != 0 && !AcceptInjectedForTests;
                    if (!injected && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN ||
                                      msg == WM_XBUTTONDOWN || msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL))
                    {
                        tainted = true;
                    }
                }
                catch (Exception) { }
            }
            return ChordNative.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private void OnKey(int msg, int vk)
        {
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            int side = SideBit(vk);
            if (side == 0)
            {
                if (isDown && down != 0) tainted = true;   // a real key inside the chord: it's a normal shortcut
                return;
            }
            if (isDown)
            {
                if (down == 0)
                {
                    peak = 0;
                    tainted = false;
                    startTicks = Stopwatch.GetTimestamp();
                }
                down |= side;
                peak |= ModsOf(down);
            }
            else if (isUp)
            {
                down &= ~side;
                if (down != 0) return;
                bool quick = (Stopwatch.GetTimestamp() - startTicks) * 1000 / Stopwatch.Frequency <= MaxChordMs;
                bool hit = !tainted && quick && peak == target;
                peak = 0;
                tainted = false;
                if (hit && fired != null) fired();
            }
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static class ChordNative
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);
        }
    }

    /// <summary>The keyboard shortcuts Windows itself uses to switch input language / layout (Alt+Shift, Ctrl+Shift).</summary>
    internal static class InputSwitchHotkeys
    {
        // HKCU\Keyboard Layout\Toggle values: "1" Left Alt+Shift, "2" Ctrl+Shift, "3" none, "4" grave accent.
        private static int ModsForCode(string code)
        {
            if (code == "1") return Hotkey.MOD_ALT | Hotkey.MOD_SHIFT;
            if (code == "2") return Hotkey.MOD_CONTROL | Hotkey.MOD_SHIFT;
            return 0;
        }

        private static string Read(string name, string fallback)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Toggle"))
                {
                    if (k == null) return fallback;
                    object v = k.GetValue(name);
                    return v == null ? fallback : v.ToString().Trim();
                }
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>Modifier set Windows uses to switch input language (default Alt+Shift).</summary>
        public static int LanguageMods
        {
            get { return ModsForCode(Read("Language Hotkey", Read("Hotkey", "1"))); }
        }

        /// <summary>Modifier set Windows uses to switch layouts within one language (default Ctrl+Shift).</summary>
        public static int LayoutMods
        {
            get { return ModsForCode(Read("Layout Hotkey", "2")); }
        }

        /// <summary>True when some installed language has more than one keyboard layout (so the layout hotkey does something).</summary>
        private static bool AnyLanguageHasSeveralLayouts()
        {
            try
            {
                Dictionary<int, int> perLang = new Dictionary<int, int>();
                foreach (LayoutInfo li in KeyboardLayouts.GetInstalled())
                {
                    int n;
                    perLang.TryGetValue(li.LangId, out n);
                    if (n >= 1) return true;
                    perLang[li.LangId] = n + 1;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>True when a modifier-only shortcut is the one Windows uses to switch language/layout.</summary>
        public static bool Conflicts(Hotkey hk)
        {
            if (!hk.IsModifierOnly) return false;
            if (hk.Modifiers == LanguageMods) return true;
            if (hk.Modifiers == LayoutMods && AnyLanguageHasSeveralLayouts()) return true;
            return false;
        }
    }
}
