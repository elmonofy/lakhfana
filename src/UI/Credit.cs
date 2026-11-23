using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Lakhfana
{
    /// <summary>Author credit shown in the app ("Created by …" + portfolio link) and used in the install metadata.</summary>
    internal static class Credit
    {
        public const string Author = "Abdelrahman Elmonofy";
        public const string Site = "fullstack.elmonofy.dev";
        public const string Url = "https://fullstack.elmonofy.dev";

        /// <summary>A small, muted, left-to-right line: "Created by Abdelrahman Elmonofy · fullstack.elmonofy.dev" (the site is a link).</summary>
        public static LinkLabel MakeLabel(Color back)
        {
            LinkLabel l = new LinkLabel();
            l.AutoSize = true;
            l.RightToLeft = RightToLeft.No;   // Latin text: keep its natural order inside the RTL window
            l.UseMnemonic = false;
            l.Font = Theme.Small;
            l.ForeColor = Theme.TextMuted;
            l.BackColor = back;
            string prefix = "Created by " + Author + "  ·  ";
            l.Text = prefix + Site;
            l.LinkArea = new LinkArea(prefix.Length, Site.Length);
            l.LinkColor = Theme.Accent;
            l.ActiveLinkColor = Theme.Accent;
            l.VisitedLinkColor = Theme.Accent;
            l.LinkBehavior = LinkBehavior.HoverUnderline;
            l.TabStop = false;
            l.LinkClicked += delegate { Open(); };
            return l;
        }

        public static void Open()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }
    }
}
