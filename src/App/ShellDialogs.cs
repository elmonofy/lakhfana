using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lakhfana
{
    /// <summary>
    /// Small themed Arabic (RTL) message windows shared by Program and TrayApp. Replaces MessageBox, whose
    /// buttons come out in the Windows display language. Never show exception text here — log it
    /// (ErrorLog.Write → %APPDATA%\Lakhfana\error.log) and show a friendly line instead.
    /// </summary>
    public static class ShellDialogs
    {
        public const int KindInfo = 0, KindQuestion = 1, KindWarning = 2;

        public static bool ConfirmUninstall()
        {
            return Confirm("متأكد إنك عايز تشيل لخفنه من الجهاز؟\nهنمسح البرنامج والإعدادات بتاعته.",
                "أيوه، شيله", "لأ", false);
        }

        public static void ShowUninstalled()
        {
            Tell("خلاص، لخفنه اتشال من الجهاز. نورتنا!", KindInfo);
        }

        public static void ShowError(string message)
        {
            Tell(message, KindWarning);
        }

        public static void ShowInfo(string message)
        {
            Tell(message, KindInfo);
        }

        /// <summary>Yes/no question. defaultYes = false makes Enter pick "no" (safer for destructive actions).</summary>
        public static bool Confirm(string message, string yesText, string noText, bool defaultYes)
        {
            return Run(message, KindQuestion, yesText, noText, defaultYes) == DialogResult.Yes;
        }

        /// <summary>One-button message ("تمام").</summary>
        public static void Tell(string message, int kind)
        {
            Run(message, kind, "تمام", null, true);
        }

        /// <summary>MessageBox-shaped entry point kept for callers that still think in MessageBox terms.</summary>
        public static DialogResult Show(string text, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton def)
        {
            int kind = KindInfo;
            if (icon == MessageBoxIcon.Question) kind = KindQuestion;
            else if (icon == MessageBoxIcon.Warning || icon == MessageBoxIcon.Error) kind = KindWarning;

            if (buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.OKCancel)
            {
                bool yesNo = buttons == MessageBoxButtons.YesNo;
                DialogResult r = Run(text, kind, yesNo ? "أيوه" : "تمام", yesNo ? "لأ" : "إلغاء",
                    def != MessageBoxDefaultButton.Button2);
                if (yesNo) return r == DialogResult.Yes ? DialogResult.Yes : DialogResult.No;
                return r == DialogResult.Yes ? DialogResult.OK : DialogResult.Cancel;
            }
            Run(text, kind, "تمام", null, true);
            return DialogResult.OK;
        }

        // Returns DialogResult.Yes for the primary button, DialogResult.No for the secondary one / Esc / X.
        private static DialogResult Run(string message, int kind, string yesText, string noText, bool defaultYes)
        {
            try
            {
                using (DialogForm f = new DialogForm(message ?? "", kind, yesText, noText, defaultYes))
                {
                    f.ShowDialog();
                    return f.Result;
                }
            }
            catch (Exception ex)
            {
                // the themed window itself failed (should not happen): fall back to a plain RTL box
                ErrorLog.Write(ex);
                try
                {
                    DialogResult r = MessageBox.Show(message ?? "", TrayApp.AppName,
                        noText == null ? MessageBoxButtons.OK : MessageBoxButtons.YesNo,
                        kind == KindWarning ? MessageBoxIcon.Warning : (kind == KindQuestion ? MessageBoxIcon.Question : MessageBoxIcon.Information),
                        defaultYes ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2,
                        MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                    return (r == DialogResult.Yes || r == DialogResult.OK) ? DialogResult.Yes : DialogResult.No;
                }
                catch (Exception)
                {
                    return DialogResult.No;
                }
            }
        }

        // ---------------------------------------------------------------- the window

        private sealed class DialogForm : Form
        {
            public DialogResult Result = DialogResult.No;

            public DialogForm(string message, int kind, string yesText, string noText, bool defaultYes)
            {
                Theme.PrepareForm(this);
                Text = TrayApp.AppName;
                ShowInTaskbar = true;   // launched from Explorer there may be no other window of ours
                TopMost = true;
                SuspendLayout();

                int W = Theme.S(420), pad = Theme.S(24), y = Theme.S(24);
                Panel root = Theme.MakeRoot(this);

                // glyph on the start (right) side, message next to it
                int gs = Theme.S(36);
                Glyph glyph = new Glyph(kind);
                glyph.Size = new Size(gs, gs);
                glyph.Location = new Point(Theme.RtlX(W, pad, gs), y);
                root.Controls.Add(glyph);

                int tx = pad + gs + Theme.S(14);
                int tw = W - tx - pad;
                Label text = Theme.MakeLabel(message, Theme.Body, Theme.Text, Theme.Bg);
                text.MaximumSize = new Size(tw, 0);
                Size ts = text.GetPreferredSize(new Size(tw, 0));
                int ty = y + Math.Max(0, (gs - ts.Height) / 2); // one-liners centred on the glyph
                text.Location = new Point(Theme.RtlX(W, tx, ts.Width), ty);
                root.Controls.Add(text);

                y = Math.Max(ty + ts.Height, y + gs) + Theme.S(24);

                // footer (physical): [no] [yes] on the end (left) side, like SettingsForm
                UiButton yes = new UiButton(yesText, true);
                yes.Click += delegate { Finish(DialogResult.Yes); };
                UiButton no = null;
                int bx = pad;
                if (noText != null)
                {
                    no = new UiButton(noText, false);
                    no.Location = new Point(bx, y);
                    no.Click += delegate { Finish(DialogResult.No); };
                    root.Controls.Add(no);
                    bx = no.Right + Theme.S(10);
                }
                yes.Location = new Point(bx, y);
                root.Controls.Add(yes);

                y += yes.Height + Theme.S(20);
                ClientSize = new Size(W, y);

                UiButton def = (defaultYes || no == null) ? yes : no;
                AcceptButton = def;
                CancelButton = no ?? yes;
                ActiveControl = def;
                ResumeLayout(false);
            }

            private void Finish(DialogResult r)
            {
                Result = r;
                DialogResult = DialogResult.OK; // closes the modal loop; Result carries the answer
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try { Activate(); }
                catch (Exception) { }
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                // a one-button message closes on Esc too, answering "تمام"
                if (keyData == Keys.Escape && CancelButton == AcceptButton && AcceptButton != null)
                {
                    AcceptButton.PerformClick();
                    return true;
                }
                return base.ProcessDialogKey(keyData);
            }
        }

        // Filled circle with "!", "؟" or "i".
        private sealed class Glyph : Control
        {
            private readonly int kind;

            public Glyph(int kind)
            {
                this.kind = kind;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = kind == KindWarning ? Color.FromArgb(217, 119, 6) : Theme.Accent;
                string mark = kind == KindWarning ? "!" : (kind == KindQuestion ? "؟" : "i");
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillEllipse(b, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (Font f = new Font(Theme.BodyStrong.FontFamily, Math.Max(8f, Height * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    TextFormatFlags tf = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                         TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                    TextRenderer.DrawText(g, mark, f, new Rectangle(0, 0, Width, Height), Color.White, tf);
                }
            }
        }
    }
}
