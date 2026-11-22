using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Lakhfana
{
    // Builds each window and saves a PNG of it to dist\snapshots (or the folder given as the first argument).
    //
    // Control.DrawToBitmap cannot render forms that use RightToLeftLayout (child controls come out missing),
    // so the window is shown invisibly (Opacity 0, no activation) and captured with PrintWindow, which yields
    // the mirrored image; it is flipped back to what the user sees. DrawToBitmap is used only as a fallback.
    static class UiSnapshot
    {
        [STAThread]
        static int Main(string[] args)
        {
            try { SnapNative.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string outDir = args.Length > 0 ? args[0] : DefaultOutDir();
            Directory.CreateDirectory(outDir);
            int failures = 0;

            Settings s = Settings.Load();
            failures += Snap(delegate { return new OnboardingForm(s, null, true); }, Path.Combine(outDir, "onboarding.png"), Mode.Plain);
            failures += Snap(delegate { return new SettingsForm(s, null); }, Path.Combine(outDir, "settings.png"), Mode.Plain);
            failures += Snap(delegate { return new SettingsForm(s, null); }, Path.Combine(outDir, "settings-recording.png"), Mode.Recording);
            failures += Snap(delegate { return new SettingsForm(s, null); }, Path.Combine(outDir, "settings-keyfocus.png"), Mode.KeyboardFocus);
            failures += SnapHotkeys(Path.Combine(outDir, "hotkeys.png"));

            Console.WriteLine(failures == 0 ? "UI snapshots OK -> " + outDir : "UI snapshots: " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        enum Mode { Plain, Recording, KeyboardFocus }

        // A column of HotkeyBoxes with long / unusual shortcuts, to check the caps fit.
        static int SnapHotkeys(string path)
        {
            const int C = Hotkey.MOD_CONTROL, A = Hotkey.MOD_ALT, S = Hotkey.MOD_SHIFT, W = Hotkey.MOD_WIN;
            Hotkey[] values = {
                Hotkey.Default,
                new Hotkey(C | A | S | W, 0x2C),   // Print Screen
                new Hotkey(C | A | S, 0x22),       // Page Down
                new Hotkey(C | A | S | W, 0xAF),   // Volume Up
                new Hotkey(A | S, 0xB1),           // Previous Track
                new Hotkey(C | A | S | W, 0xB3),   // Play/Pause
                new Hotkey(C | W, 0xE9),           // unnamed key
                new Hotkey(0, 0x7C),               // F13
                default(Hotkey)
            };
            try
            {
                HotkeyBox probe = new HotkeyBox();
                int bw = probe.Width, bh = probe.Height, gap = 8;
                probe.Dispose();
                using (Bitmap all = new Bitmap(bw + 2 * gap, values.Length * (bh + gap) + gap, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(all)) g.Clear(Color.White);
                    for (int i = 0; i < values.Length; i++)
                    {
                        using (HotkeyBox box = new HotkeyBox())
                        using (Bitmap one = new Bitmap(bw, bh))
                        {
                            box.Value = values[i];
                            box.DrawToBitmap(one, new Rectangle(0, 0, bw, bh));
                            using (Graphics g = Graphics.FromImage(all)) g.DrawImage(one, gap, gap + i * (bh + gap));
                        }
                    }
                    all.Save(path, ImageFormat.Png);
                }
                Console.WriteLine("wrote " + path);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAILED " + path + ": " + ex);
                return 1;
            }
        }

        static string DefaultOutDir()
        {
            DirectoryInfo d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (DirectoryInfo p = d; p != null; p = p.Parent)
                if (string.Equals(p.Name, "dist", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(p.FullName, "snapshots");
            return Path.Combine(d.FullName, "snapshots");
        }

        static int Snap(Func<Form> make, string path, Mode mode)
        {
            Form f = null;
            try
            {
                f = make();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(40, 40);
                f.ShowInTaskbar = false;
                f.Opacity = 0;
                IntPtr h = f.Handle;
                SnapNative.ShowWindow(h, 4 /* SW_SHOWNOACTIVATE */);
                Pump();
                if (mode != Mode.Plain)
                {
                    HotkeyBox box = Find<HotkeyBox>(f);
                    if (box != null)
                    {
                        // focus arrives from another control of the window, as with Tab
                        if (f.ActiveControl != null && f.ActiveControl != box) { f.ActiveControl.Focus(); Pump(); }
                        box.Focus();
                        Pump();
                        if (mode == Mode.Recording) { box.BeginRecording(); Pump(); }
                    }
                }
                using (Bitmap bmp = Capture(f))
                    bmp.Save(path, ImageFormat.Png);
                Console.WriteLine("wrote " + path);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAILED " + path + ": " + ex);
                return 1;
            }
            finally
            {
                if (f != null) { try { f.Close(); f.Dispose(); } catch { } }
            }
        }

        static void Pump()
        {
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(40); }
        }

        static T Find<T>(Control root) where T : Control
        {
            foreach (Control c in root.Controls)
            {
                T t = c as T;
                if (t != null) return t;
                t = Find<T>(c);
                if (t != null) return t;
            }
            return null;
        }

        static Bitmap Capture(Form f)
        {
            SnapNative.RECT wr;
            SnapNative.GetWindowRect(f.Handle, out wr);
            int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
            Bitmap full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bool ok;
            using (Graphics g = Graphics.FromImage(full))
            {
                IntPtr hdc = g.GetHdc();
                ok = SnapNative.PrintWindow(f.Handle, hdc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(hdc);
            }
            if (!ok)
            {
                full.Dispose();
                Bitmap b = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                return b;
            }
            if (f.RightToLeftLayout) full.RotateFlip(RotateFlipType.RotateNoneFlipX);

            // crop the invisible resize borders using the DWM visible frame
            SnapNative.RECT fr;
            if (SnapNative.DwmGetWindowAttribute(f.Handle, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out fr, Marshal.SizeOf(typeof(SnapNative.RECT))) == 0)
            {
                Rectangle crop = Rectangle.Intersect(new Rectangle(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - fr.Left, fr.Bottom - fr.Top),
                                                     new Rectangle(0, 0, w, h));
                if (crop.Width > 0 && crop.Height > 0 && crop.Size != full.Size)
                {
                    Bitmap c = full.Clone(crop, PixelFormat.Format32bppArgb);
                    full.Dispose();
                    return c;
                }
            }
            return full;
        }

        static class SnapNative
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct RECT { public int Left, Top, Right, Bottom; }
            [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
            [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
            [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
            [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        }
    }
}
