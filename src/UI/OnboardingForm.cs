using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lakhfana
{
    // First-run window: welcome, live example, shortcut, languages, options, "يلا نبدأ".
    public sealed class OnboardingForm : Form
    {
        readonly Settings settings;
        readonly PreferencesPanel prefs;
        readonly Label footNote;
        readonly UiButton go;
        readonly string noteText;
        int prefsHeight;

        public OnboardingForm(Settings settings, HotkeyManager hotkeys, bool installMode)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
            Theme.PrepareForm(this);
            Text = "لخفنه";
            ShowInTaskbar = true;
            SuspendLayout();

            int W = Theme.S(560), pad = Theme.S(24), y = Theme.S(24);
            Panel root = Theme.MakeRoot(this);

            // ---------- header: logo on the start (right) side; title, one-liner and live example next to it ----------
            int logo = Theme.S(56);
            UiLogo lg = new UiLogo();
            lg.Size = new Size(logo, logo);
            root.Controls.Add(lg);

            int tx = pad + logo + Theme.S(16);
            int tw = W - tx - pad;
            Label title = Theme.MakeLabel("أهلاً بيك في لخفنه", Theme.Title, Theme.Text, Theme.Bg);
            Label sub = Theme.MakeLabel("كتبت واللغة غلط؟ حدد الكلام ودوس الاختصار، وهو يقلبه للغة الصح.", Theme.Body, Theme.TextMuted, Theme.Bg);
            sub.MaximumSize = new Size(tw, 0);
            Size ts = title.PreferredSize;
            Size ss = sub.GetPreferredSize(new Size(tw, 0));

            UiChips chips = new UiChips();
            chips.BackColor = Theme.Bg;
            chips.AddText("لو كتبت");
            chips.AddChip("hgsghl ugd;l", UiChips.ChipMuted);
            chips.AddArrow();
            chips.AddChip("السلام عليكم", UiChips.ChipAccent);
            int chH = Theme.S(32);
            int cw = Math.Min(chips.PreferredWidth, tw);

            int ty = y;
            title.Location = new Point(Theme.RtlX(W, tx, ts.Width), ty);
            ty += ts.Height + Theme.S(1);
            sub.Location = new Point(Theme.RtlX(W, tx + Theme.S(1), ss.Width), ty);
            ty += ss.Height + Theme.S(10);
            chips.SetBounds(Theme.RtlX(W, tx - Theme.S(2), cw), ty, cw, chH);
            ty += chH;
            lg.Location = new Point(Theme.RtlX(W, pad, logo), y + Theme.S(2));
            root.Controls.Add(title);
            root.Controls.Add(sub);
            root.Controls.Add(chips);
            y = Math.Max(ty, y + logo) + Theme.S(20);

            // ---------- settings body ----------
            prefs = new PreferencesPanel(settings, hotkeys, W - 2 * pad);
            prefs.Location = new Point(pad, y);
            root.Controls.Add(prefs);
            y += prefs.Height + Theme.S(20);

            // ---------- footer: note on the start side, primary button on the end (left) side ----------
            go = new UiButton("يلا نبدأ", true);
            go.Width = Math.Max(go.Width, Theme.S(140));
            go.Location = new Point(pad, y);
            go.Click += OnStart;
            root.Controls.Add(go);

            noteText = installMode ? "هيتثبت على جهازك ويستناك جنب الساعة" : "هتلاقيه مستنيك جنب الساعة";
            footNote = Theme.MakeLabel(noteText, Theme.Small, Theme.TextMuted, Theme.Bg);
            footNote.AutoSize = false;
            footNote.AutoEllipsis = true;
            footNote.TextAlign = ContentAlignment.MiddleLeft;   // RightToLeft flips this to the right
            int nw = W - 2 * pad - go.Width - Theme.S(16);
            footNote.SetBounds(Theme.RtlX(W, pad, nw), y, nw, go.Height);
            root.Controls.Add(footNote);

            prefsHeight = prefs.Height;
            prefs.SizeChanged += OnPrefsResized;

            y += go.Height + Theme.S(20);
            ClientSize = new Size(W, y);
            AcceptButton = go;
            ActiveControl = go;
            ResumeLayout(false);
        }

        void OnStart(object sender, EventArgs e)
        {
            footNote.ForeColor = Theme.TextMuted;
            footNote.Text = noteText;
            if (!prefs.ValidateInput())
            {
                footNote.ForeColor = Theme.Danger;
                footNote.Text = "في حاجة محتاجة تتظبط فوق";
                return;
            }
            prefs.WriteTo(settings);
            settings.FirstRunDone = true;
            bool saved;
            try { saved = settings.TrySave(); }
            catch (Exception) { saved = false; }
            if (!saved)
            {
                settings.FirstRunDone = false;
                footNote.ForeColor = Theme.Danger;
                footNote.Text = "معرفناش نحفظ الإعدادات، جرّب تاني";
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // The languages card can change height after the user adds a keyboard language: move the footer with it.
        void OnPrefsResized(object sender, EventArgs e)
        {
            int delta = prefs.Height - prefsHeight;
            if (delta == 0) return;
            prefsHeight = prefs.Height;
            go.Top += delta;
            footNote.Top += delta;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            prefs.RefreshLayouts();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) DialogResult = DialogResult.Cancel;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            prefs.ReleaseHotkeys();
            base.OnFormClosed(e);
        }
    }
}
