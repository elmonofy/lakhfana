using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lakhfana
{
    // Click (or Enter / Space while focused) to record a global shortcut. Shows the shortcut as key caps.
    // While recording: Esc cancels, Backspace clears, plain Tab / Shift+Tab stop recording and move focus.
    // A modifier (Ctrl/Alt/Win) is required unless the key is F13–F24, Pause or Scroll Lock.
    // Shortcuts Windows and every app rely on (Ctrl+C, Alt+Tab, plain F1–F12, ...) are refused.
    public sealed class HotkeyBox : Control
    {
        const int WM_SETFOCUS = 0x7, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_SYSCHAR = 0x106;

        internal const string ReservedMessage = "ده اختصار مهم في الويندوز، اختار غيره";

        Hotkey value;
        bool recording, hover, swallowSysKeys, keyboardHint;
        int liveMods;
        IntPtr focusFrom;
        string flash;
        DateTime flashUntil;
        readonly Timer timer;

        public event EventHandler ValueChanged;
        public event EventHandler RecordingStarted;   // owner calls HotkeyManager.Suspend()
        public event EventHandler RecordingEnded;     // owner calls HotkeyManager.Resume()
        internal event EventHandler KeyboardHintChanged;   // KeyboardHint flipped
        internal event Action<string> Rejected;            // a pressed shortcut was refused (Arabic reason)

        public HotkeyBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            ImeMode = ImeMode.Disable;
            Size = new Size(Theme.S(236), Theme.S(40));
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = "اختصار التحويل";
            timer = new Timer();
            timer.Interval = 50;
            timer.Tick += OnTick;
            UpdateAccessibleValue();
        }

        public Hotkey Value
        {
            get { return value; }
            set
            {
                if (value.Modifiers == this.value.Modifiers && value.Vk == this.value.Vk) return;
                this.value = value;
                UpdateAccessibleValue();
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        internal bool IsRecording { get { return recording; } }

        // True while focus arrived here from the keyboard and recording has not started yet;
        // the owner shows "press Enter to change" meanwhile.
        internal bool KeyboardHint { get { return keyboardHint; } }

        internal void BeginRecording() { StartRecording(); }

        void SetKeyboardHint(bool on)
        {
            if (keyboardHint == on) return;
            keyboardHint = on;
            if (KeyboardHintChanged != null) KeyboardHintChanged(this, EventArgs.Empty);
        }

        void UpdateAccessibleValue()
        {
            AccessibleDescription = value.IsEmpty ? "مفيش اختصار" : value.ToDisplayString();
        }

        // ---------- recording state ----------

        void StartRecording()
        {
            if (recording || !Enabled) return;
            SetKeyboardHint(false);
            recording = true;
            liveMods = 0;
            flash = null;
            timer.Start();
            Invalidate();
            if (RecordingStarted != null) RecordingStarted(this, EventArgs.Empty);
        }

        void EndRecording()
        {
            if (!recording) return;
            recording = false;
            flash = null;
            swallowSysKeys = true;   // until the keys are released, so Alt-up won't open the window menu
            Invalidate();
            if (RecordingEnded != null) RecordingEnded(this, EventArgs.Empty);
        }

        void Accept(Hotkey hk)
        {
            // Stop recording first so the owner resumes the manager after seeing the new value.
            recording = false;
            Value = hk;
            recording = true;
            EndRecording();
        }

        void Flash(string text)
        {
            flash = text;
            flashUntil = DateTime.UtcNow.AddMilliseconds(1800);
            Invalidate();
        }

        void OnTick(object sender, EventArgs e)
        {
            int mods = PhysicalMods();
            if (recording)
            {
                if (mods != liveMods) { liveMods = mods; Invalidate(); }
                if (flash != null && DateTime.UtcNow > flashUntil) { flash = null; Invalidate(); }
            }
            else
            {
                if (mods == 0) { swallowSysKeys = false; timer.Stop(); }
            }
        }

        static bool Down(int vk) { return (HotkeyBoxNative.GetAsyncKeyState(vk) & 0x8000) != 0; }

        static int PhysicalMods()
        {
            int m = 0;
            if (Down(0x11)) m |= Hotkey.MOD_CONTROL;
            if (Down(0x12)) m |= Hotkey.MOD_ALT;
            if (Down(0x10)) m |= Hotkey.MOD_SHIFT;
            if (Down(0x5B) || Down(0x5C)) m |= Hotkey.MOD_WIN;
            return m;
        }

        static int ModsFrom(Keys keyData)
        {
            int m = 0;
            if ((keyData & Keys.Control) != 0) m |= Hotkey.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) m |= Hotkey.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) m |= Hotkey.MOD_SHIFT;
            if ((HotkeyBoxNative.GetKeyState(0x5B) & 0x8000) != 0 || (HotkeyBoxNative.GetKeyState(0x5C) & 0x8000) != 0) m |= Hotkey.MOD_WIN;
            return m;
        }

        static bool IsModifierKey(Keys k)
        {
            return k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin ||
                   k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.LControlKey || k == Keys.RControlKey ||
                   k == Keys.LMenu || k == Keys.RMenu;
        }

        static bool AllowedAlone(Keys k)
        {
            return (k >= Keys.F1 && k <= Keys.F24) || k == Keys.Pause || k == Keys.Scroll;
        }

        // Shortcuts that Windows or practically every app depends on; taking them globally would break them everywhere.
        internal static bool IsReserved(Hotkey hk)
        {
            if (hk.IsEmpty) return false;
            int m = hk.Modifiers;
            Keys k = (Keys)hk.Vk;
            const int C = Hotkey.MOD_CONTROL, A = Hotkey.MOD_ALT, S = Hotkey.MOD_SHIFT;
            if (m == 0 && k >= Keys.F1 && k <= Keys.F12) return true;
            if (m == C)
            {
                switch (k)
                {
                    case Keys.C: case Keys.V: case Keys.X: case Keys.A: case Keys.Z: case Keys.Y: case Keys.S:
                    case Keys.F: case Keys.P: case Keys.N: case Keys.O: case Keys.W: case Keys.T:
                    case Keys.Insert: case Keys.Home: case Keys.End: case Keys.Escape:
                        return true;
                }
            }
            if (m == (C | S) && (k == Keys.Home || k == Keys.End || k == Keys.Escape)) return true;
            if (m == S && (k == Keys.Insert || k == Keys.Delete)) return true;
            if ((m == A || m == (A | S)) && (k == Keys.Tab || k == Keys.Escape)) return true;
            if (m == A && k == Keys.F4) return true;
            return false;
        }

        void Reject(string text)
        {
            Flash(text);
            if (Rejected != null) Rejected(text);
        }

        void HandleKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            int mods = ModsFrom(keyData);
            if (key == Keys.None || IsModifierKey(key) || key == Keys.ProcessKey || key == Keys.Packet || (int)key == 0xFF)
            {
                liveMods = mods;
                Invalidate();
                return;
            }
            if (key == Keys.Escape && mods == 0) { EndRecording(); return; }
            if (key == Keys.Back && mods == 0) { Accept(default(Hotkey)); return; }
            Hotkey hk = new Hotkey(mods, (int)key);
            if (IsReserved(hk)) { Reject(ReservedMessage); return; }
            if (!AllowedAlone(key) && (mods & (Hotkey.MOD_CONTROL | Hotkey.MOD_ALT | Hotkey.MOD_WIN)) == 0)
            {
                Reject(mods == Hotkey.MOD_SHIFT ? "Shift لوحده مش كفاية، زوّد Ctrl أو Alt" : "لازم معاه Ctrl أو Alt");
                return;
            }
            Accept(hk);
        }

        static bool WinDown()
        {
            return (HotkeyBoxNative.GetKeyState(0x5B) & 0x8000) != 0 || (HotkeyBoxNative.GetKeyState(0x5C) & 0x8000) != 0;
        }

        // ---------- input plumbing ----------

        protected override bool IsInputKey(Keys keyData)
        {
            if (recording) return true;
            return base.IsInputKey(keyData);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (recording && (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN))
            {
                // Plain Tab / Shift+Tab leave the box like in any other control (no keyboard trap).
                if ((keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift)) && !WinDown())
                {
                    EndRecording();
                    return base.ProcessCmdKey(ref msg, keyData);   // IsInputKey is false now → normal tab navigation
                }
                HandleKey(keyData);
                return true;
            }
            if (!recording && Focused && msg.Msg == WM_KEYDOWN && (keyData == Keys.Space || keyData == Keys.Enter))
            {
                StartRecording();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SETFOCUS) focusFrom = m.WParam;
            if ((recording || swallowSysKeys) && (m.Msg == WM_SYSKEYUP || m.Msg == WM_SYSCHAR))
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            // Focus moved here inside our window without the mouse (Tab, code): hint that Enter starts
            // recording. Not when the window is merely re-activated from another app.
            Form f = FindForm();
            bool fromInside = focusFrom != IntPtr.Zero && f != null && f.IsHandleCreated &&
                              (focusFrom == f.Handle || HotkeyBoxNative.IsChild(f.Handle, focusFrom));
            if (fromInside && !recording && Control.MouseButtons == MouseButtons.None) SetKeyboardHint(true);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            SetKeyboardHint(false);
            EndRecording();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (!Focused) Focus();
            StartRecording();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) EndRecording(); Invalidate(); base.OnEnabledChanged(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Stop(); timer.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------- painting ----------

        static string[] ModNames(int mods)
        {
            List<string> l = new List<string>();
            if ((mods & Hotkey.MOD_CONTROL) != 0) l.Add("Ctrl");
            if ((mods & Hotkey.MOD_ALT) != 0) l.Add("Alt");
            if ((mods & Hotkey.MOD_SHIFT) != 0) l.Add("Shift");
            if ((mods & Hotkey.MOD_WIN) != 0) l.Add("Win");
            return l.ToArray();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = Theme.Sf(6f);
            Color fill = recording ? Theme.AccentSoft : (hover ? Color.FromArgb(250, 250, 252) : Color.White);
            Color border = recording ? Theme.Accent : (Focused ? Theme.Accent : (hover ? Theme.TextFaint : Theme.BorderStrong));
            using (GraphicsPath p = Theme.RoundRect(r, rad))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
            }
            float bw = recording ? Theme.Sf(1.6f) : 1f;
            RectangleF br = RectangleF.Inflate(r, -(bw - 1f) / 2f, -(bw - 1f) / 2f);
            using (GraphicsPath p = Theme.RoundRect(br, rad))
            using (Pen pen = new Pen(border, bw)) g.DrawPath(pen, p);

            Rectangle inner = new Rectangle(Theme.S(8), 0, Width - Theme.S(16), Height);
            TextFormatFlags centerRtl = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                        TextFormatFlags.NoPrefix | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis;
            if (recording)
            {
                if (flash != null)
                    TextRenderer.DrawText(g, flash, Theme.Small, inner, Theme.Danger, centerRtl);
                else if (liveMods != 0)
                {
                    List<string> parts = new List<string>(ModNames(liveMods));
                    parts.Add("…");
                    DrawKeys(g, parts.ToArray(), true);
                }
                else
                    TextRenderer.DrawText(g, "دوس الاختصار...", Theme.BodyStrong, inner, Theme.Accent, centerRtl);
            }
            else if (value.IsEmpty)
            {
                TextRenderer.DrawText(g, "مفيش اختصار، دوس هنا واختار", Theme.Body, inner, Theme.TextFaint, centerRtl);
            }
            else
            {
                List<string> parts = new List<string>(ModNames(value.Modifiers));
                parts.Add(ShortKeyName(value.Vk));
                DrawKeys(g, parts.ToArray(), false);
            }
        }

        // Compact cap labels for keys whose full names are long.
        internal static string ShortKeyName(int vk)
        {
            switch (vk)
            {
                case 0x08: return "Bksp";
                case 0x14: return "Caps";
                case 0x21: return "PgUp";
                case 0x22: return "PgDn";
                case 0x2C: return "PrtSc";
                case 0x2D: return "Ins";
                case 0x2E: return "Del";
                case 0x90: return "NumLk";
                case 0x91: return "ScrLk";
                case 0xAD: return "Mute";
                case 0xAE: return "Vol-";
                case 0xAF: return "Vol+";
                case 0xB0: return "Next";
                case 0xB1: return "Prev";
                case 0xB3: return "Play";
            }
            string n = Hotkey.KeyName(vk);
            if (n.StartsWith("Key 0x", StringComparison.Ordinal)) return n.Substring(4);   // "0xE9"
            return n;
        }

        // Key caps laid out left-to-right (shortcut names are Latin), centred. Padding shrinks to fit;
        // as a last resort the shortcut is drawn as plain text with an ellipsis.
        void DrawKeys(Graphics g, string[] keys, bool live)
        {
            TextFormatFlags tf = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                 TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            int capH = Theme.S(24);
            int avail = Width - 2 * Theme.S(5);

            // (small font?, cap padding, gap holding the plus sign, minimum cap width) from roomy to tight
            int[][] steps = new int[][] {
                new int[] { 0, 8, 16, 28 }, new int[] { 0, 6, 14, 24 }, new int[] { 0, 5, 12, 22 }, new int[] { 0, 4, 11, 20 },
                new int[] { 1, 4, 11, 18 }, new int[] { 1, 3, 10, 16 } };
            int[] widths = new int[keys.Length];
            int[] textW = new int[keys.Length];
            int total = 0, plusW = 0, measuredWith = -1;
            Font f = Theme.KeyFont;
            bool fits = false;
            foreach (int[] st in steps)
            {
                if (st[0] != measuredWith)
                {
                    measuredWith = st[0];
                    f = st[0] == 1 ? Theme.Small : Theme.KeyFont;
                    for (int i = 0; i < keys.Length; i++)
                        textW[i] = keys[i] == "…" ? 0 : TextRenderer.MeasureText(keys[i], f, new Size(int.MaxValue, int.MaxValue), tf).Width;
                }
                int padX = Theme.S(st[1]), minW = Theme.S(st[3]);
                plusW = Theme.S(st[2]);
                total = 0;
                for (int i = 0; i < keys.Length; i++)
                {
                    widths[i] = keys[i] == "…" ? Theme.S(18) : Math.Max(minW, textW[i] + 2 * padX);
                    total += widths[i] + (i > 0 ? plusW : 0);
                }
                if (total <= avail) { fits = true; break; }
            }
            if (!fits)
            {
                Rectangle tr = new Rectangle(Theme.S(8), 0, Width - Theme.S(16), Height);
                TextRenderer.DrawText(g, string.Join(" + ", keys), Theme.Small, tr, live ? Theme.Accent : Theme.Text,
                    tf | TextFormatFlags.EndEllipsis);
                return;
            }
            float x = (Width - total) / 2f;
            float y = (Height - capH) / 2f - Theme.Sf(0.5f);
            Color plusC = live ? Theme.Accent : Theme.TextFaint;
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0)
                {
                    float pcx = x + plusW / 2f, pcy = Height / 2f, arm = Math.Min(Theme.Sf(3.5f), (plusW - Theme.Sf(5f)) / 2f);
                    using (Pen pp = new Pen(plusC, Math.Max(1f, Theme.Sf(1.3f))))
                    {
                        g.DrawLine(pp, pcx - arm, pcy, pcx + arm, pcy);
                        g.DrawLine(pp, pcx, pcy - arm, pcx, pcy + arm);
                    }
                    x += plusW;
                }
                if (keys[i] == "…")
                {
                    TextRenderer.DrawText(g, "…", Theme.BodyStrong, new Rectangle((int)x, 0, widths[i], Height), Theme.Accent, tf);
                    x += widths[i];
                    continue;
                }
                RectangleF cap = new RectangleF(x + 0.5f, y + 0.5f, widths[i] - 1f, capH - 1f);
                float rad = Theme.Sf(5f);
                Color edge = live ? Theme.AccentSoftBorder : Theme.KeyBorder;
                using (GraphicsPath sh = Theme.RoundRect(new RectangleF(cap.X, cap.Y + Theme.Sf(1.5f), cap.Width, cap.Height), rad))
                using (SolidBrush sb = new SolidBrush(edge)) g.FillPath(sb, sh);
                using (GraphicsPath p = Theme.RoundRect(cap, rad))
                {
                    using (SolidBrush b = new SolidBrush(live ? Color.White : Theme.KeyFill)) g.FillPath(b, p);
                    using (Pen pen = new Pen(edge)) g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, keys[i], f, new Rectangle((int)x, (int)y, widths[i], capH), live ? Theme.Accent : Theme.Text, tf);
                x += widths[i];
            }
        }

        static class HotkeyBoxNative
        {
            [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
            [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
            [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
        }
    }
}
