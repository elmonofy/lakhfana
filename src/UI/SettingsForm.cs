using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Lakhfana
{
    public sealed class SettingsForm : Form
    {
        readonly Settings settings;
        readonly PreferencesPanel prefs;
        readonly Label errorLabel;
        readonly Control[] footer;
        int prefsHeight;

        public event EventHandler Applied;            // after Save() — TrayApp re-applies
        public event EventHandler UninstallRequested; // TrayApp runs the uninstall flow

        public SettingsForm(Settings settings, HotkeyManager hotkeys)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
            Theme.PrepareForm(this);
            Text = "إعدادات لخفنه";
            ShowInTaskbar = true;
            SuspendLayout();

            int W = Theme.S(560), pad = Theme.S(24), y = Theme.S(22);
            Panel root = Theme.MakeRoot(this);

            // header: logo on the start (right) side, title + version next to it
            int logo = Theme.S(44);
            UiLogo lg = new UiLogo();
            lg.Size = new Size(logo, logo);
            lg.Location = new Point(Theme.RtlX(W, pad, logo), y);
            root.Controls.Add(lg);
            int tx = pad + logo + Theme.S(14);
            Label title = Theme.MakeLabel("الإعدادات", Theme.Title, Theme.Text, Theme.Bg);
            Label ver = Theme.MakeLabel("لخفنه · الإصدار " + Installer.Version, Theme.Small, Theme.TextMuted, Theme.Bg);
            Size ts = title.PreferredSize, vs = ver.PreferredSize;
            int ty = y + (logo - ts.Height - vs.Height + Theme.S(2)) / 2;
            title.Location = new Point(Theme.RtlX(W, tx, ts.Width), ty);
            ver.Location = new Point(Theme.RtlX(W, tx + Theme.S(1), vs.Width), ty + ts.Height - Theme.S(2));
            root.Controls.Add(title);
            root.Controls.Add(ver);
            y += logo + Theme.S(20);

            prefs = new PreferencesPanel(settings, hotkeys, W - 2 * pad);
            prefs.Location = new Point(pad, y);
            root.Controls.Add(prefs);
            y += prefs.Height + Theme.S(20);

            // footer (physical): [إلغاء] [حفظ] ......... [إلغاء تثبيت البرنامج]
            UiButton save = new UiButton("حفظ", true);
            UiButton cancel = new UiButton("إلغاء", false);
            int bh = save.Height;
            cancel.Location = new Point(pad, y);
            save.Location = new Point(cancel.Right + Theme.S(10), y);
            save.Click += OnSave;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            root.Controls.Add(save);
            root.Controls.Add(cancel);

            LinkLabel un = new LinkLabel();
            un.AutoSize = true;
            un.Text = "إلغاء تثبيت البرنامج";
            un.Font = Theme.Small;
            un.LinkColor = Theme.TextMuted;
            un.ActiveLinkColor = Theme.Danger;
            un.VisitedLinkColor = Theme.TextMuted;
            un.LinkBehavior = LinkBehavior.HoverUnderline;
            un.BackColor = Theme.Bg;
            un.UseMnemonic = false;
            Size us = un.PreferredSize;
            un.Location = new Point(Theme.RtlX(W, pad, us.Width), y + (bh - us.Height) / 2);
            un.LinkClicked += delegate
            {
                DialogResult = DialogResult.Cancel;
                if (UninstallRequested != null) UninstallRequested(this, EventArgs.Empty);
                if (!IsDisposed) Close();
            };
            root.Controls.Add(un);

            errorLabel = Theme.MakeLabel("", Theme.Small, Theme.Danger, Theme.Bg);
            errorLabel.AutoSize = false;
            errorLabel.AutoEllipsis = true;
            errorLabel.TextAlign = ContentAlignment.MiddleLeft;   // RightToLeft flips this to the right
            int ex = save.Right + Theme.S(12);
            errorLabel.SetBounds(ex, y, Math.Max(Theme.S(40), un.Left - Theme.S(12) - ex), bh);
            root.Controls.Add(errorLabel);

            LinkLabel credit = Credit.MakeLabel(Theme.Bg);
            Size cs = credit.PreferredSize;
            credit.Location = new Point((W - cs.Width) / 2, y + bh + Theme.S(16));
            root.Controls.Add(credit);

            footer = new Control[] { save, cancel, un, errorLabel, credit };
            prefsHeight = prefs.Height;
            prefs.SizeChanged += OnPrefsResized;

            y = credit.Bottom + Theme.S(14);
            ClientSize = new Size(W, y);
            AcceptButton = save;
            CancelButton = cancel;
            ActiveControl = save;
            ResumeLayout(false);
        }

        // The languages card can grow/shrink after the user adds a keyboard language: move the footer with it.
        void OnPrefsResized(object sender, EventArgs e)
        {
            int delta = prefs.Height - prefsHeight;
            if (delta == 0) return;
            prefsHeight = prefs.Height;
            foreach (Control c in footer) c.Top += delta;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            prefs.RefreshLayouts();
        }

        void OnSave(object sender, EventArgs e)
        {
            errorLabel.Text = "";
            if (!prefs.ValidateInput()) return;
            prefs.WriteTo(settings);
            bool saved;
            try { saved = settings.TrySave(); }
            catch (Exception) { saved = false; }
            if (!saved)
            {
                errorLabel.Text = "معرفناش نحفظ الإعدادات، جرّب تاني";
                return;
            }
            if (Applied != null) Applied(this, EventArgs.Empty);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            prefs.ReleaseHotkeys();
            base.OnFormClosed(e);
        }
    }

    // The shared body of Onboarding and Settings: shortcut card, languages card, options card.
    // Lays itself out for a fixed width and sets its own Height (which changes when the languages card is rebuilt).
    internal sealed class PreferencesPanel : Panel
    {
        readonly HotkeyManager hotkeys;
        List<LayoutInfo> layouts;
        LayoutInfo fixedA, fixedB;
        ComboBox comboA, comboB;
        const string LayoutHint = "اختار اللغتين اللي بتكتب بيهم";
        const string KeyboardHintText = "دوس Enter عشان تغيّر الاختصار";
        readonly UiStatus hotkeyStatus;
        UiStatus layoutStatus;
        Label layoutDesc;
        readonly UiCard cardLang, cardOpts;
        readonly UiToggle tStart, tSwitch, tLine;
        readonly string savedA, savedB;
        readonly int pad, cardW, gap;
        int statusKind;
        string statusText;
        bool suspended;
        ToolTip tip;

        public readonly HotkeyBox HotkeyInput;

        public PreferencesPanel(Settings s, HotkeyManager hotkeys, int width)
        {
            this.hotkeys = hotkeys;
            BackColor = Theme.Bg;
            Width = width;
            cardW = width;
            pad = Theme.S(20);
            gap = Theme.S(12);
            savedA = s.LayoutA;
            savedB = s.LayoutB;
            int y = 0;

            // ---------- shortcut ----------
            UiCard c1 = NewCard(ref y);
            int cy = 0;
            HotkeyInput = new HotkeyBox();
            HotkeyInput.Value = s.Hotkey.IsEmpty ? Hotkey.Default : s.Hotkey;
            AddRow(c1, ref cy, "اختصار التحويل", "دوس الاختصار اللي يريحك", HotkeyInput, null);
            hotkeyStatus = new UiStatus();
            hotkeyStatus.BackColor = Theme.Card;
            hotkeyStatus.SetBounds(pad, cy - Theme.S(6), cardW - 2 * pad, Theme.S(22));
            c1.Controls.Add(hotkeyStatus);
            cy += Theme.S(22) + Theme.S(10);
            c1.Height = cy;
            y += c1.Height + gap;

            HotkeyInput.RecordingStarted += OnRecordingStarted;
            HotkeyInput.RecordingEnded += OnRecordingEnded;
            HotkeyInput.KeyboardHintChanged += OnKeyboardHintChanged;
            HotkeyInput.Rejected += delegate(string why) { hotkeyStatus.Set(UiStatus.Error, why); };
            HotkeyInput.ValueChanged += delegate { CheckHotkey(); };
            CheckHotkey();

            // ---------- languages ----------
            cardLang = NewCard(ref y);
            BuildLanguageCard(SafeLayouts(), savedA, savedB);
            y = cardLang.Bottom + gap;

            // ---------- options ----------
            cardOpts = NewCard(ref y);
            cy = 0;
            tStart = new UiToggle(); tStart.Checked = s.StartWithWindows;
            tSwitch = new UiToggle(); tSwitch.Checked = s.SwitchLayoutAfter;
            tLine = new UiToggle(); tLine.Checked = s.SelectLineIfEmpty;
            AddRow(cardOpts, ref cy, "يشتغل مع الويندوز", "يفتح لوحده أول ما الجهاز يشتغل", tStart, tStart);
            AddDivider(cardOpts, ref cy);
            AddRow(cardOpts, ref cy, "يغيّر لغة الكيبورد بعد التحويل", "عشان تكمّل كتابة باللغة الصح على طول", tSwitch, tSwitch);
            AddDivider(cardOpts, ref cy);
            AddRow(cardOpts, ref cy, "لو مفيش كلام متحدد، يحدد السطر لوحده", "ويحوّل السطر اللي انت واقف فيه", tLine, tLine);
            cardOpts.Height = cy;

            Height = cardOpts.Bottom;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tip != null) { tip.Dispose(); tip = null; }
            base.Dispose(disposing);
        }

        // ---------- languages card ----------

        void BuildLanguageCard(List<LayoutInfo> list, string keyA, string keyB)
        {
            layouts = list;
            Control[] old = new Control[cardLang.Controls.Count];
            cardLang.Controls.CopyTo(old, 0);
            cardLang.Controls.Clear();
            foreach (Control c in old) c.Dispose();
            if (tip != null) { tip.Dispose(); tip = null; }
            comboA = null; comboB = null; layoutDesc = null; layoutStatus = null; fixedA = null; fixedB = null;

            LayoutInfo a, b;
            try { KeyboardLayouts.PickPair(layouts, keyA, keyB, out a, out b); }
            catch { a = layouts.Count > 0 ? layouts[0] : null; b = layouts.Count > 1 ? layouts[1] : null; }

            int cy = 0;
            if (layouts.Count > 2)
            {
                layoutDesc = AddRow(cardLang, ref cy, "اللغتين", LayoutHint, null, null);
                int swapW = Theme.S(36);
                int cw = (cardW - 2 * pad - swapW) / 2;
                comboA = NewCombo(a);
                comboB = NewCombo(b);
                // first language on the start (right) side
                comboA.SetBounds(cardW - pad - cw, cy - Theme.S(4), cw, comboA.Height);
                comboB.SetBounds(pad, cy - Theme.S(4), cw, comboB.Height);
                UiChips swap = new UiChips();
                swap.BackColor = Theme.Card;
                swap.AlignCenter = true;
                swap.AddSwap();
                swap.SetBounds(pad + cw, comboA.Top + (comboA.Height - Theme.S(32)) / 2, swapW, Theme.S(32));
                cardLang.Controls.Add(comboA);
                cardLang.Controls.Add(swap);
                cardLang.Controls.Add(comboB);
                cy = comboA.Bottom + Theme.S(18);
                comboA.SelectedIndexChanged += delegate { SetLayoutError(null); };
                comboB.SelectedIndexChanged += delegate { SetLayoutError(null); };
            }
            else if (layouts.Count == 2 && a != null && b != null)
            {
                fixedA = a; fixedB = b;
                int maxW = cardW - 2 * pad - Theme.S(16) - Theme.S(190);
                UiChips pair = PairChips(a, b, maxW);
                tip = new ToolTip();
                tip.SetToolTip(pair, NameOf(a) + "  ⇄  " + NameOf(b));
                AddRow(cardLang, ref cy, "اللغتين", "هيقلب الكلام بين اللغتين دول", pair, null);
            }
            else
            {
                fixedA = a; fixedB = null;
                LinkLabel open = new LinkLabel();
                open.AutoSize = true;
                open.Text = "افتح إعدادات اللغة";
                open.Font = Theme.Body;
                open.LinkColor = Theme.Accent;
                open.ActiveLinkColor = Theme.AccentPressed;
                open.LinkBehavior = LinkBehavior.HoverUnderline;
                open.BackColor = Theme.Card;
                open.LinkClicked += delegate
                {
                    try { Process.Start("ms-settings:regionlanguage"); } catch { }
                };
                AddRow(cardLang, ref cy, "اللغتين", "لازم يبقى عندك لغتين في الكيبورد، زي العربي والإنجليزي", open, null);
                cy -= Theme.S(6);
                layoutStatus = AddStatus(cardLang, ref cy);
                layoutStatus.Set(UiStatus.Error, layouts.Count >= 2
                    ? "اللغتين اللي عندك ليهم نفس الكيبورد، ضيف لغة تانية من إعدادات الويندوز"
                    : "ضيف لغة تانية من إعدادات الويندوز وبعدين ارجع هنا");
            }
            cardLang.Height = cy;
        }

        // Re-reads the installed keyboard languages (e.g. the user just added one in Windows settings) and
        // rebuilds the languages card when the set changed. Keeps the current choice where possible.
        public bool RefreshLayouts()
        {
            if (IsDisposed || cardLang == null) return false;
            List<LayoutInfo> now = SafeLayouts();
            if (SameSet(now, layouts)) return false;
            LayoutInfo a, b;
            SelectedPair(out a, out b);
            string ka = a != null ? a.Key : savedA, kb = b != null ? b.Key : savedB;
            bool hadFocus = cardLang.ContainsFocus;
            SuspendLayout();
            BuildLanguageCard(now, ka, kb);
            cardOpts.Top = cardLang.Bottom + gap;
            Height = cardOpts.Bottom;
            ResumeLayout(false);
            if (hadFocus)
            {
                if (comboA != null) comboA.Focus();
                else tStart.Focus();
            }
            return true;
        }

        static bool SameSet(List<LayoutInfo> x, List<LayoutInfo> y)
        {
            if (x == null || y == null) return x == y;
            if (x.Count != y.Count) return false;
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LayoutInfo li in x) keys.Add(li.Key);
            foreach (LayoutInfo li in y) if (!keys.Contains(li.Key)) return false;
            return true;
        }

        // ---------- building blocks ----------

        UiCard NewCard(ref int y)
        {
            UiCard c = new UiCard();
            c.SetBounds(0, y, cardW, Theme.S(10));
            Controls.Add(c);
            return c;
        }

        // A settings row: title + description on the start (right) side, accessory on the end (left) side.
        // When toggle != null, clicking the text toggles it.
        Label AddRow(Control card, ref int y, string title, string desc, Control accessory, UiToggle toggle)
        {
            int vpad = Theme.S(12);
            int accW = accessory != null ? accessory.Width : 0;
            int textW = cardW - 2 * pad - (accessory != null ? accW + Theme.S(16) : 0);

            Label t = Theme.MakeLabel(title, Theme.BodyStrong, Theme.Text, Theme.Card);
            t.MaximumSize = new Size(textW, 0);
            Label d = Theme.MakeLabel(desc, Theme.Small, Theme.TextMuted, Theme.Card);
            d.MaximumSize = new Size(textW, 0);
            Size ts = t.GetPreferredSize(new Size(textW, 0));
            Size ds = d.GetPreferredSize(new Size(textW, 0));
            int textH = ts.Height + Theme.S(1) + ds.Height;
            int rowH = Math.Max(textH, accessory != null ? accessory.Height : 0) + 2 * vpad;

            // Fixed-width labels anchored to the start (right) edge; RightToLeft right-aligns their text,
            // so it stays put when the text changes (e.g. an inline error replacing the description).
            int ty = y + (rowH - textH) / 2;
            t.AutoSize = false; d.AutoSize = false;
            t.SetBounds(Theme.RtlX(cardW, pad, textW), ty, textW, ts.Height);
            d.SetBounds(Theme.RtlX(cardW, pad, textW), ty + ts.Height + Theme.S(1), textW, ds.Height);
            card.Controls.Add(t);
            card.Controls.Add(d);
            if (accessory != null)
            {
                // end (left) side
                accessory.Location = new Point(pad, y + (rowH - accessory.Height) / 2);
                card.Controls.Add(accessory);
                accessory.AccessibleName = title;
            }
            if (toggle != null)
            {
                EventHandler flip = delegate { toggle.Focus(); toggle.Checked = !toggle.Checked; };
                t.Click += flip; d.Click += flip;
                t.Cursor = Cursors.Hand; d.Cursor = Cursors.Hand;
            }
            y += rowH;
            return d;
        }

        void AddDivider(Control card, ref int y)
        {
            Panel line = new Panel();
            line.BackColor = Theme.Divider;
            line.SetBounds(pad, y, cardW - 2 * pad, 1);
            card.Controls.Add(line);
            y += 1;
        }

        UiStatus AddStatus(Control card, ref int y)
        {
            UiStatus st = new UiStatus();
            st.BackColor = Theme.Card;
            st.SetBounds(pad, y, cardW - 2 * pad, Theme.S(22));
            card.Controls.Add(st);
            y += Theme.S(22) + Theme.S(10);
            return st;
        }

        ComboBox NewCombo(LayoutInfo selected)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.System;
            c.Font = Theme.Body;
            c.IntegralHeight = false;
            c.MaxDropDownItems = 10;
            List<string> names = DistinctNames(layouts);
            for (int k = 0; k < layouts.Count; k++)
            {
                LayoutInfo li = layouts[k];
                int i = c.Items.Add(new LayoutItem(li, names[k]));
                if (selected != null && li.Key == selected.Key) c.SelectedIndex = i;
            }
            if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        sealed class LayoutItem
        {
            public readonly LayoutInfo Info;
            readonly string text;
            public LayoutItem(LayoutInfo i, string text) { Info = i; this.text = text; }
            public override string ToString() { return text; }
        }

        static List<LayoutInfo> SafeLayouts()
        {
            try
            {
                List<LayoutInfo> l = KeyboardLayouts.GetInstalled();
                if (l != null) return l;
            }
            catch { }
            return new List<LayoutInfo>();
        }

        // ---------- layout names ----------

        // Full name including the keyboard layout name (LayoutInfo.Tag) when the core added one to tell look-alikes apart.
        static string NameOf(LayoutInfo li)
        {
            string n = !string.IsNullOrEmpty(li.DisplayName) ? li.DisplayName : li.Key;
            if (!string.IsNullOrEmpty(li.Tag) && n.IndexOf(li.Tag, StringComparison.OrdinalIgnoreCase) < 0) n += " - " + li.Tag;
            return n;
        }

        // Language name without the " - Tag" suffix.
        static string BaseName(LayoutInfo li)
        {
            string n = !string.IsNullOrEmpty(li.DisplayName) ? li.DisplayName : li.Key;
            if (!string.IsNullOrEmpty(li.Tag))
            {
                string suffix = " - " + li.Tag;
                if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - suffix.Length);
            }
            return n;
        }

        // Dropdown texts: the full name; any that still collide get the HKL key appended.
        static List<string> DistinctNames(List<LayoutInfo> list)
        {
            List<string> names = new List<string>();
            foreach (LayoutInfo li in list) names.Add(NameOf(li));
            for (int i = 0; i < names.Count; i++)
            {
                bool dup = false;
                for (int j = 0; j < names.Count; j++)
                    if (i != j && string.Equals(NameOf(list[i]), NameOf(list[j]), StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (dup) names[i] = NameOf(list[i]) + " (" + list[i].Key + ")";
            }
            return names;
        }

        // The read-only pair: the shortest names that still tell the two apart and fit in maxW.
        UiChips PairChips(LayoutInfo a, LayoutInfo b, int maxW)
        {
            List<string[]> options = new List<string[]>();
            string ba = BaseName(a), bb = BaseName(b), sa = Strip(ba), sb = Strip(bb);
            string ta = string.IsNullOrEmpty(a.Tag) ? null : a.Tag, tb = string.IsNullOrEmpty(b.Tag) ? null : b.Tag;
            options.Add(new string[] { sa, sb });
            options.Add(new string[] { ta != null ? sa + " · " + ta : sa, tb != null ? sb + " · " + tb : sb });
            options.Add(new string[] { ta ?? sa, tb ?? sb });
            options.Add(new string[] { NameOf(a), NameOf(b) });
            options.Add(new string[] { NameOf(a) + " (" + a.Key + ")", NameOf(b) + " (" + b.Key + ")" });

            UiChips best = null;
            foreach (string[] o in options)
            {
                if (string.Equals(o[0], o[1], StringComparison.OrdinalIgnoreCase)) continue;
                UiChips c = new UiChips();
                c.BackColor = Theme.Card;
                c.AddChip(o[0], UiChips.ChipNeutral);
                c.AddSwap();
                c.AddChip(o[1], UiChips.ChipNeutral);
                if (best == null || c.PreferredWidth < best.PreferredWidth)
                {
                    if (best != null) best.Dispose();
                    best = c;
                }
                else c.Dispose();
                if (best.PreferredWidth <= maxW) break;
            }
            best.Size = new Size(Math.Min(best.PreferredWidth, maxW), Theme.S(32));
            return best;
        }

        static string Strip(string n)
        {
            int i = n.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? n.Substring(0, i) : n;
        }

        // ---------- hotkey state ----------

        static bool Same(Hotkey x, Hotkey y) { return x.Modifiers == y.Modifiers && x.Vk == y.Vk; }

        bool Available(Hotkey hk)
        {
            // Our own registration only counts when it actually succeeded; a hotkey that failed to register
            // (taken by another program) must be probed like any other.
            if (hotkeys != null)
            {
                try { if (hotkeys.IsRegistered && Same(hk, hotkeys.Current)) return true; } catch { }
            }
            try { return HotkeyManager.IsAvailable(hk); }
            catch { return true; }
        }

        void SetHotkeyStatus(int kind, string text)
        {
            statusKind = kind;
            statusText = text;
            ShowHotkeyStatus();
        }

        // What the status line shows when not recording: the keyboard hint while it applies, else the check result.
        void ShowHotkeyStatus()
        {
            if (HotkeyInput.IsRecording) return;
            if (HotkeyInput.KeyboardHint) hotkeyStatus.Set(UiStatus.Hint, KeyboardHintText);
            else hotkeyStatus.Set(statusKind, statusText);
        }

        void CheckHotkey()
        {
            Hotkey hk = HotkeyInput.Value;
            if (hk.IsEmpty) SetHotkeyStatus(UiStatus.Error, "اختار اختصار الأول");
            else if (HotkeyBox.IsReserved(hk)) SetHotkeyStatus(UiStatus.Error, HotkeyBox.ReservedReason(hk));
            else if (!Available(hk)) SetHotkeyStatus(UiStatus.Error, "الاختصار ده مستخدم في برنامج تاني، جرّب غيره");
            else SetHotkeyStatus(UiStatus.Ok, "تمام، الاختصار ده فاضي");
        }

        void OnRecordingStarted(object sender, EventArgs e)
        {
            if (hotkeys != null && !suspended)
            {
                try { hotkeys.Suspend(); suspended = true; } catch { }
            }
            hotkeyStatus.Set(UiStatus.Hint, "Esc للإلغاء، Backspace للمسح");
        }

        void OnRecordingEnded(object sender, EventArgs e)
        {
            ReleaseHotkeys();
            ShowHotkeyStatus();
        }

        void OnKeyboardHintChanged(object sender, EventArgs e)
        {
            ShowHotkeyStatus();
        }

        public void ReleaseHotkeys()
        {
            if (hotkeys != null && suspended)
            {
                suspended = false;
                try { hotkeys.Resume(); } catch { }
            }
        }

        // ---------- validation / output ----------

        void SelectedPair(out LayoutInfo a, out LayoutInfo b)
        {
            if (comboA != null)
            {
                LayoutItem ia = comboA.SelectedItem as LayoutItem, ib = comboB.SelectedItem as LayoutItem;
                a = ia != null ? ia.Info : null;
                b = ib != null ? ib.Info : null;
            }
            else { a = fixedA; b = fixedB; }
        }

        void SetLayoutError(string text)
        {
            if (layoutDesc != null)
            {
                layoutDesc.Text = text ?? LayoutHint;
                layoutDesc.ForeColor = text != null ? Theme.Danger : Theme.TextMuted;
            }
            else if (layoutStatus != null && text != null) layoutStatus.Set(UiStatus.Error, text);
        }

        public bool ValidateInput()
        {
            bool ok = true;
            CheckHotkey();
            if (statusKind == UiStatus.Error)
            {
                ok = false;
                if (!HotkeyInput.IsRecording) hotkeyStatus.Set(statusKind, statusText);   // over the keyboard hint
            }
            RefreshLayouts();   // the user may have added a language since the window opened
            LayoutInfo a, b;
            SelectedPair(out a, out b);
            if (a == null || b == null)
            {
                ok = false;
                SetLayoutError("لازم يبقى عندك لغتين على الأقل");
            }
            else if (string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase))
            {
                ok = false;
                SetLayoutError("اختار لغتين مختلفين");
            }
            else if (KeyboardLayouts.SameLayout(a, b))
            {
                // e.g. two Arabic entries that both use the Arabic (101) keyboard: converting between them changes nothing
                ok = false;
                SetLayoutError("دول نفس الكيبورد، اختار لغتين مختلفين");
            }
            return ok;
        }

        public void WriteTo(Settings s)
        {
            s.Hotkey = HotkeyInput.Value;
            LayoutInfo a, b;
            SelectedPair(out a, out b);
            if (a != null && b != null) { s.LayoutA = a.Key; s.LayoutB = b.Key; }
            s.StartWithWindows = tStart.Checked;
            s.SwitchLayoutAfter = tSwitch.Checked;
            s.SelectLineIfEmpty = tLine.Checked;
        }
    }
}
