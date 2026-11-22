using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lakhfana
{
    /// <summary>Synthesized editing shortcuts (copy, paste, select line) sent to the focused window via SendInput.</summary>
    internal static class InputSim
    {
        /// <summary>Marks our own injected events (dwExtraInfo).</summary>
        static readonly IntPtr Marker = new IntPtr(0x4C4B4846);

        static readonly int[] Modifiers = {
            Native.VK_LSHIFT, Native.VK_RSHIFT, Native.VK_LCONTROL, Native.VK_RCONTROL,
            Native.VK_LMENU, Native.VK_RMENU, Native.VK_LWIN, Native.VK_RWIN };

        static bool IsDown(int vk)
        {
            return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public static bool IsAltDown()
        {
            return IsDown(Native.VK_LMENU) || IsDown(Native.VK_RMENU) || IsDown(Native.VK_MENU);
        }

        public static bool IsWinDown()
        {
            return IsDown(Native.VK_LWIN) || IsDown(Native.VK_RWIN);
        }

        static bool IsExtended(int vk)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24:   // PgUp PgDn End Home
                case 0x25: case 0x26: case 0x27: case 0x28:   // arrows
                case 0x2D: case 0x2E:                         // Insert Delete
                case 0xA3: case 0xA5:                         // RCtrl RAlt
                case 0x5B: case 0x5C:                         // Win keys
                    return true;
            }
            return false;
        }

        static Native.INPUT Key(int vk, bool up)
        {
            Native.INPUT inp = new Native.INPUT();
            inp.type = Native.INPUT_KEYBOARD;
            inp.u.ki.wVk = (ushort)vk;
            inp.u.ki.wScan = (ushort)(Native.MapVirtualKey((uint)vk, Native.MAPVK_VK_TO_VSC) & 0xFF);
            uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
            if (IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            inp.u.ki.dwFlags = flags;
            inp.u.ki.time = 0;
            inp.u.ki.dwExtraInfo = Marker;
            return inp;
        }

        static bool Send(List<Native.INPUT> inputs)
        {
            if (inputs.Count == 0) return true;
            Native.INPUT[] arr = inputs.ToArray();
            uint sent = Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(Native.INPUT)));
            return sent == arr.Length;
        }

        /// <summary>Presses the modifiers, taps the key, releases everything — in one SendInput batch.</summary>
        static bool Chord(int key, params int[] mods)
        {
            List<Native.INPUT> list = new List<Native.INPUT>();
            foreach (int m in mods) list.Add(Key(m, false));
            list.Add(Key(key, false));
            list.Add(Key(key, true));
            for (int i = mods.Length - 1; i >= 0; i--) list.Add(Key(mods[i], true));
            bool ok = Send(list);
            if (!ok)
            {
                // Whatever went in, make sure nothing stays pressed.
                List<Native.INPUT> ups = new List<Native.INPUT>();
                ups.Add(Key(key, true));
                foreach (int m in mods) ups.Add(Key(m, true));
                Send(ups);
            }
            return ok;
        }

        /// <summary>
        /// Taps the unassigned VK 0xE8 so releasing Alt (or Win) after our hotkey doesn't open the target's
        /// menu bar (or the Start menu).
        /// </summary>
        public static void MaskMenuKey()
        {
            List<Native.INPUT> list = new List<Native.INPUT>();
            Native.INPUT down = Key(Native.VK_MASK, false);
            down.u.ki.wScan = 0;
            Native.INPUT up = Key(Native.VK_MASK, true);
            up.u.ki.wScan = 0;
            list.Add(down);
            list.Add(up);
            Send(list);
        }

        /// <summary>
        /// Waits until the user lets go of Ctrl/Shift/Alt/Win (so our Ctrl+C isn't seen as Ctrl+Shift+C).
        /// After the timeout, releases Ctrl/Shift/Alt for them. Returns false if Win is still held (can't safely release it).
        /// </summary>
        public static bool WaitForModifiersReleased(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                bool any = false;
                foreach (int vk in Modifiers) if (IsDown(vk)) { any = true; break; }
                if (!any) return true;
                Thread.Sleep(10);
                waited += 10;
            }
            if (IsWinDown()) return false;
            List<Native.INPUT> ups = new List<Native.INPUT>();
            foreach (int vk in Modifiers)
            {
                if (vk == Native.VK_LWIN || vk == Native.VK_RWIN) continue;
                if (IsDown(vk)) ups.Add(Key(vk, true));
            }
            if (ups.Count > 0)
            {
                // Mask first so a released Alt doesn't activate the menu bar.
                MaskMenuKey();
                Send(ups);
            }
            return true;
        }

        public static bool Copy()
        {
            return Chord(Native.VK_C, Native.VK_CONTROL);
        }

        public static bool Paste()
        {
            return Chord(Native.VK_V, Native.VK_CONTROL);
        }

        /// <summary>Home, then Shift+End: selects the line the caret is on.</summary>
        public static bool SelectCurrentLine()
        {
            bool a = Chord(Native.VK_HOME);
            Thread.Sleep(15);
            bool b = Chord(Native.VK_END, Native.VK_SHIFT);
            return a && b;
        }
    }
}
