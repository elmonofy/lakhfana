using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lakhfana
{
    /// <summary>
    /// Owns one global hotkey registered on a hidden message-only window.
    /// Create it on the UI thread; Pressed is raised on that thread.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_NOREPEAT = 0x4000;
        private const int HotkeyId = 0x4C4B;
        private const int ProbeId = 0x4C4C;

        private static readonly List<HotkeyManager> live = new List<HotkeyManager>();
        private static readonly object liveLock = new object();

        private const int WM_CHORD = 0x8000 + 0x4C;   // WM_APP-based: posted by the modifier-only chord hook

        private readonly MessageWindow window;
        private readonly ModifierChord chord;
        private Hotkey current;
        private bool registered;
        private bool suspended;
        private bool paused;
        private bool disposed;

        public event EventHandler Pressed;

        public HotkeyManager()
        {
            window = new MessageWindow(this);
            // The hook callback runs on this (UI) thread; post so the hook returns immediately.
            chord = new ModifierChord(delegate { HotkeyNative.PostMessage(window.Handle, WM_CHORD, IntPtr.Zero, IntPtr.Zero); });
            lock (liveLock) live.Add(this);
        }

        public Hotkey Current
        {
            get { return current; }
        }

        /// <summary>True while the hotkey is actually registered with Windows.</summary>
        public bool IsRegistered
        {
            get { return registered; }
        }

        /// <summary>
        /// While paused nothing is registered; Register/Resume only remember the hotkey.
        /// Setting it back to false re-registers Current.
        /// </summary>
        public bool Paused
        {
            get { return paused; }
            set
            {
                if (paused == value) return;
                paused = value;
                if (paused) UnregisterNative();
                else if (!suspended) TryRegisterCurrent();
            }
        }

        /// <summary>Unregisters the previous hotkey, then registers hk (MOD_NOREPEAT). False if taken.</summary>
        public bool Register(Hotkey hk)
        {
            if (disposed) return false;
            UnregisterNative();
            current = hk;
            suspended = false;
            if (hk.IsEmpty) return false;
            if (paused) return IsAvailable(hk);
            return TryRegisterCurrent();
        }

        /// <summary>Unregisters and forgets the current hotkey.</summary>
        public void Unregister()
        {
            UnregisterNative();
            current = default(Hotkey);
        }

        /// <summary>Temporarily unregister (while the user records a new hotkey).</summary>
        public void Suspend()
        {
            suspended = true;
            UnregisterNative();
        }

        /// <summary>Re-register Current if not paused.</summary>
        public void Resume()
        {
            suspended = false;
            if (disposed || paused || registered || current.IsEmpty) return;
            TryRegisterCurrent();
        }

        /// <summary>
        /// Try-register on a temporary window, then release. A hotkey currently held by a
        /// HotkeyManager of this process counts as available (it is ours).
        /// </summary>
        public static bool IsAvailable(Hotkey hk)
        {
            if (hk.IsEmpty || Hotkey.IsModifierVk(hk.Vk)) return false;
            // Modifier-only chords use a hook, not RegisterHotKey: nothing else can "own" them.
            if (hk.IsModifierOnly) return true;
            lock (liveLock)
            {
                foreach (HotkeyManager m in live)
                {
                    if (m.registered && m.current == hk) return true;
                }
            }
            NativeWindow probe = new NativeWindow();
            try
            {
                probe.CreateHandle(MessageWindow.MessageOnlyParams());
                bool ok = HotkeyNative.RegisterHotKey(probe.Handle, ProbeId, hk.Modifiers | MOD_NOREPEAT, hk.Vk);
                if (ok) HotkeyNative.UnregisterHotKey(probe.Handle, ProbeId);
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try { probe.DestroyHandle(); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            UnregisterNative();
            disposed = true;
            lock (liveLock) live.Remove(this);
            try { window.DestroyHandle(); }
            catch (Exception) { }
        }

        private bool TryRegisterCurrent()
        {
            if (disposed || current.IsEmpty) return false;
            if (registered) return true;
            if (current.IsModifierOnly)
            {
                registered = chord.Start(current.Modifiers);
                return registered;
            }
            registered = HotkeyNative.RegisterHotKey(window.Handle, HotkeyId, current.Modifiers | MOD_NOREPEAT, current.Vk);
            return registered;
        }

        private void UnregisterNative()
        {
            if (!registered) return;
            try
            {
                if (chord.IsActive) chord.Stop();
                else HotkeyNative.UnregisterHotKey(window.Handle, HotkeyId);
            }
            catch (Exception) { }
            registered = false;
        }

        private void OnHotkey()
        {
            if (!registered || paused || suspended) return;
            EventHandler h = Pressed;
            if (h != null) h(this, EventArgs.Empty);
        }

        private sealed class MessageWindow : NativeWindow
        {
            private readonly HotkeyManager owner;

            public MessageWindow(HotkeyManager owner)
            {
                this.owner = owner;
                CreateHandle(MessageOnlyParams());
            }

            public static CreateParams MessageOnlyParams()
            {
                CreateParams cp = new CreateParams();
                cp.Caption = "Lakhfana.Hotkey";
                cp.Parent = new IntPtr(-3); // HWND_MESSAGE
                return cp;
            }

            protected override void WndProc(ref Message m)
            {
                if ((m.Msg == WM_HOTKEY && m.WParam.ToInt64() == HotkeyId) || m.Msg == WM_CHORD)
                {
                    owner.OnHotkey();
                    return;
                }
                base.WndProc(ref m);
            }
        }

        private static class HotkeyNative
        {
            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        }
    }
}
