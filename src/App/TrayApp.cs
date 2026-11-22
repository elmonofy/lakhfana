using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lakhfana
{
    /// <summary>The tray icon and everything it drives (hotkey → engine, settings, pause, exit, uninstall).</summary>
    public sealed class TrayApp : ApplicationContext
    {
        public const string AppName = "لخفنه";

        private readonly Settings settings;
        private readonly HotkeyManager hotkeys;
        private readonly SingleInstance instance;
        private readonly bool portable;
        private readonly SwitchEngine engine;
        private readonly Control invoker;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem settingsItem;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem exitItem;
        private readonly Icon normalIcon;
        private IntPtr normalIconHandle;
        private Icon pausedIcon;
        private IntPtr pausedIconHandle;
        private SettingsForm settingsForm;
        private bool exiting;

        /// <param name="startupMessage">optional balloon shown right after start (e.g. after install); null for none</param>
        public TrayApp(Settings settings, HotkeyManager hotkeys, SingleInstance instance, bool portable, string startupMessage)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
            this.hotkeys = hotkeys ?? new HotkeyManager();
            this.instance = instance;
            this.portable = portable;

            invoker = new Control();
            invoker.CreateControl();
            GC.KeepAlive(invoker.Handle); // force the handle so BeginInvoke works before Application.Run pumps

            normalIcon = LoadAppIcon(out normalIconHandle);

            menu = new ContextMenuStrip();
            menu.RightToLeft = RightToLeft.Yes;
            menu.ShowImageMargin = false;
            settingsItem = new ToolStripMenuItem("الإعدادات");
            settingsItem.Font = new Font(menu.Font, FontStyle.Bold);
            settingsItem.Click += delegate { ShowSettings(); };
            pauseItem = new ToolStripMenuItem("إيقاف مؤقت");
            pauseItem.Click += delegate { TogglePause(); };
            exitItem = new ToolStripMenuItem("خروج");
            exitItem.Click += delegate { ExitApp(); };
            menu.Items.Add(settingsItem);
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            tray = new NotifyIcon();
            tray.Icon = normalIcon;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.BalloonTipClicked += delegate { };

            engine = new SwitchEngine(settings);
            engine.Notify += OnEngineNotify;

            this.hotkeys.Pressed += OnHotkeyPressed;

            if (instance != null)
            {
                instance.Listen(
                    delegate { Post(ShowSettings); },
                    delegate { Post(ExitApp); });
            }

            UpdateTrayState();
            tray.Visible = true;

            bool ok = ApplySettings(false);
            if (!ok)
            {
                Balloon("الاختصار " + settings.Hotkey.ToDisplayString() + " مش متاح دلوقتي. اختار اختصار تاني.", ToolTipIcon.Warning);
                Post(ShowSettings);
            }
            else if (!string.IsNullOrEmpty(startupMessage))
            {
                Balloon(startupMessage, ToolTipIcon.Info);
            }
        }

        // ---------------------------------------------------------------- behaviour

        private void OnHotkeyPressed(object sender, EventArgs e)
        {
            if (exiting || settings.Paused) return;
            try { engine.Trigger(); }
            catch (Exception) { }
        }

        private void OnEngineNotify(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Post(delegate { Balloon(message, ToolTipIcon.None); });
        }

        /// <summary>Re-registers the hotkey, updates the Run key (installed only) and reloads layouts. False if the hotkey is taken.</summary>
        private bool ApplySettings(bool warn)
        {
            hotkeys.Paused = settings.Paused;
            bool ok = !settings.Hotkey.IsEmpty && hotkeys.Register(settings.Hotkey);

            if (!portable && Installer.IsRunningFromInstallDir() && Installer.IsInstalled())
            {
                try { Installer.SetStartWithWindows(settings.StartWithWindows); }
                catch (Exception) { }
            }

            try { engine.ReloadLayouts(); }
            catch (Exception) { }

            UpdateTrayState();
            if (!ok && warn)
            {
                Balloon("الاختصار " + settings.Hotkey.ToDisplayString() + " مستخدم في برنامج تاني. اختار غيره من الإعدادات.", ToolTipIcon.Warning);
            }
            return ok;
        }

        private void TogglePause()
        {
            settings.Paused = !settings.Paused;
            settings.Save();
            hotkeys.Paused = settings.Paused;
            bool ok = true;
            if (!settings.Paused && !hotkeys.IsRegistered)
            {
                ok = !settings.Hotkey.IsEmpty && hotkeys.Register(settings.Hotkey);
            }
            UpdateTrayState();

            if (settings.Paused)
            {
                Balloon("وقفنا مؤقتاً. دوس \"تشغيل\" لما تحتاجنا.", ToolTipIcon.None);
            }
            else if (ok)
            {
                Balloon("رجعنا تاني! " + settings.Hotkey.ToDisplayString(), ToolTipIcon.None);
            }
            else
            {
                // same as at startup: only the warning (a second balloon would replace it), then let them pick another
                Balloon("رجعنا، بس الاختصار " + settings.Hotkey.ToDisplayString() + " مستخدم في برنامج تاني. اختار غيره من الإعدادات.", ToolTipIcon.Warning);
                Post(ShowSettings);
            }
        }

        private void ShowSettings()
        {
            if (exiting) return;
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Show();
                settingsForm.Activate();
                return;
            }
            SettingsForm f = new SettingsForm(settings, hotkeys);
            f.Applied += delegate { ApplySettings(true); };
            f.UninstallRequested += delegate { Post(RunUninstall); };
            f.FormClosed += delegate
            {
                settingsForm = null;
                // in case the form closed while recording a hotkey
                if (!exiting) hotkeys.Resume();
            };
            settingsForm = f;
            f.Show();
            f.Activate();
            BringToFront(f);
        }

        private void RunUninstall()
        {
            if (exiting) return;
            if (!ShellDialogs.ConfirmUninstall()) return;
            exiting = true;
            CloseSettingsForm();
            Shutdown();
            try { Installer.Uninstall(); }
            catch (Exception ex) { ErrorLog.Write(ex); }
            ShellDialogs.ShowUninstalled();
            ExitThread();
        }

        private void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            CloseSettingsForm();
            Shutdown();
            ExitThread();
        }

        private void CloseSettingsForm()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                try { settingsForm.Close(); }
                catch (Exception) { }
            }
            settingsForm = null;
        }

        private void Shutdown()
        {
            try { hotkeys.Pressed -= OnHotkeyPressed; } catch (Exception) { }
            try { hotkeys.Dispose(); } catch (Exception) { }
            try { engine.Notify -= OnEngineNotify; } catch (Exception) { }
            try { tray.Visible = false; } catch (Exception) { }
            if (instance != null)
            {
                try { instance.Dispose(); } catch (Exception) { }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!exiting)
                {
                    exiting = true;
                    Shutdown();
                }
                try { tray.Dispose(); } catch (Exception) { }
                try { menu.Dispose(); } catch (Exception) { }
                try { invoker.Dispose(); } catch (Exception) { }
                if (pausedIcon != null) { pausedIcon.Dispose(); pausedIcon = null; }
                if (pausedIconHandle != IntPtr.Zero) { TrayNative.DestroyIcon(pausedIconHandle); pausedIconHandle = IntPtr.Zero; }
                if (normalIconHandle != IntPtr.Zero) { TrayNative.DestroyIcon(normalIconHandle); normalIconHandle = IntPtr.Zero; }
            }
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- tray visuals

        private void UpdateTrayState()
        {
            bool paused = settings.Paused;
            pauseItem.Text = paused ? "تشغيل" : "إيقاف مؤقت";
            string text = paused
                ? AppName + " — متوقف مؤقتاً"
                : AppName + " — " + settings.Hotkey.ToDisplayString();
            if (text.Length > 63) text = text.Substring(0, 63);
            tray.Text = text;
            tray.Icon = paused ? GetPausedIcon() : normalIcon;
        }

        private void Balloon(string message, ToolTipIcon icon)
        {
            if (exiting || string.IsNullOrEmpty(message)) return;
            try
            {
                tray.BalloonTipTitle = AppName;
                tray.BalloonTipText = message;
                tray.BalloonTipIcon = icon;
                tray.ShowBalloonTip(3000);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// The tray wants the SMALL frame (SystemInformation.SmallIconSize). Icon.ExtractAssociatedIcon returns the
        /// 32 px frame, which Windows then squeezes into 16/20/24 px and blurs. So: take the first RT_GROUP_ICON of
        /// our own module (csc /win32icon stores it as group #32512) and load exactly that size from it.
        /// ownedHandle is non-zero when the returned Icon wraps an HICON we must DestroyIcon later.
        /// </summary>
        private static Icon LoadAppIcon(out IntPtr ownedHandle)
        {
            ownedHandle = IntPtr.Zero;
            Size sz = SystemInformation.SmallIconSize;
            if (sz.Width <= 0 || sz.Height <= 0) sz = new Size(16, 16);

            IntPtr h = LoadOwnIcon(sz.Width, sz.Height);
            if (h != IntPtr.Zero)
            {
                try
                {
                    Icon ic = Icon.FromHandle(h);
                    ownedHandle = h;
                    return ic;
                }
                catch (Exception)
                {
                    TrayNative.DestroyIcon(h);
                }
            }

            // no icon resource (e.g. a build without /win32icon): the drawn logo has every small size
            try { return new Icon(Theme.AppIcon, sz); }
            catch (Exception) { }
            return SystemIcons.Application;
        }

        private static IntPtr LoadOwnIcon(int cx, int cy)
        {
            IntPtr module;
            try { module = TrayNative.GetModuleHandle(null); }
            catch (Exception) { return IntPtr.Zero; }
            if (module == IntPtr.Zero) return IntPtr.Zero;

            int groupId = 0;
            string groupName = null;
            try
            {
                TrayNative.EnumResNameProc cb = delegate(IntPtr hModule, IntPtr type, IntPtr name, IntPtr param)
                {
                    long v = name.ToInt64();
                    if ((v >> 16) == 0) groupId = (int)v;            // MAKEINTRESOURCE id
                    else groupName = Marshal.PtrToStringUni(name);  // named resource
                    return false;                                    // first one is enough
                };
                TrayNative.EnumResourceNamesW(module, (IntPtr)TrayNative.RT_GROUP_ICON, cb, IntPtr.Zero);
                GC.KeepAlive(cb);
            }
            catch (Exception) { }

            IntPtr h = IntPtr.Zero;
            if (groupId != 0 || groupName != null)
            {
                // best quality: picks the exact frame, or scales DOWN from a larger one (comctl32 v6)
                try
                {
                    int hr = groupName != null
                        ? TrayNative.LoadIconWithScaleDown(module, groupName, cx, cy, out h)
                        : TrayNative.LoadIconWithScaleDown(module, (IntPtr)groupId, cx, cy, out h);
                    if (hr != 0) h = IntPtr.Zero;
                }
                catch (Exception) { h = IntPtr.Zero; }

                if (h == IntPtr.Zero)
                {
                    try
                    {
                        h = groupName != null
                            ? TrayNative.LoadImageW(module, groupName, TrayNative.IMAGE_ICON, cx, cy, 0)
                            : TrayNative.LoadImageW(module, (IntPtr)groupId, TrayNative.IMAGE_ICON, cx, cy, 0);
                    }
                    catch (Exception) { h = IntPtr.Zero; }
                }
            }

            if (h == IntPtr.Zero)
            {
                // last resort: the shell's small icon (SM_CXSMICON) of icon #0 of our exe
                try
                {
                    IntPtr[] small = new IntPtr[1];
                    if (TrayNative.ExtractIconExW(Application.ExecutablePath, 0, null, small, 1) > 0) h = small[0];
                }
                catch (Exception) { h = IntPtr.Zero; }
            }
            return h;
        }

        /// <summary>Greyed, half-transparent copy of the small tray icon (same pixel size, no rescaling blur).</summary>
        private Icon GetPausedIcon()
        {
            if (pausedIcon != null) return pausedIcon;
            try
            {
                Size sz = normalIcon.Size;
                if (sz.Width <= 0 || sz.Height <= 0) sz = SystemInformation.SmallIconSize;
                using (Bitmap src = normalIcon.ToBitmap())
                using (Bitmap dst = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(dst))
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        ColorMatrix cm = new ColorMatrix(new float[][]
                        {
                            new float[] { 0.30f, 0.30f, 0.30f, 0f, 0f },
                            new float[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
                            new float[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
                            new float[] { 0f, 0f, 0f, 0.6f, 0f },
                            new float[] { 0.06f, 0.06f, 0.06f, 0f, 1f }
                        });
                        ia.SetColorMatrix(cm);
                        g.DrawImage(src, new Rectangle(0, 0, sz.Width, sz.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                    }
                    pausedIconHandle = dst.GetHicon();
                    pausedIcon = Icon.FromHandle(pausedIconHandle);
                }
            }
            catch (Exception)
            {
                if (pausedIconHandle != IntPtr.Zero) { TrayNative.DestroyIcon(pausedIconHandle); pausedIconHandle = IntPtr.Zero; }
                pausedIcon = null;
                return normalIcon;
            }
            return pausedIcon;
        }

        // ---------------------------------------------------------------- threading helpers

        private void Post(MethodInvoker action)
        {
            if (action == null) return;
            try
            {
                if (invoker.IsDisposed || !invoker.IsHandleCreated) return;
                invoker.BeginInvoke(action);
            }
            catch (Exception) { }
        }

        private static void BringToFront(Form f)
        {
            try
            {
                f.TopMost = true;
                f.TopMost = false;
                TrayNative.SetForegroundWindow(f.Handle);
            }
            catch (Exception) { }
        }

        private static class TrayNative
        {
            public const int RT_GROUP_ICON = 14;
            public const int IMAGE_ICON = 1;

            public delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpType, IntPtr lpName, IntPtr lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DestroyIcon(IntPtr hIcon);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr lpType, EnumResNameProc lpEnumFunc, IntPtr lParam);

            [DllImport("comctl32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadIconWithScaleDown")]
            public static extern int LoadIconWithScaleDown(IntPtr hinst, IntPtr pszName, int cx, int cy, out IntPtr phico);

            [DllImport("comctl32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadIconWithScaleDown")]
            public static extern int LoadIconWithScaleDown(IntPtr hinst, string pszName, int cx, int cy, out IntPtr phico);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
            public static extern IntPtr LoadImageW(IntPtr hinst, IntPtr name, int type, int cx, int cy, int fuLoad);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
            public static extern IntPtr LoadImageW(IntPtr hinst, string name, int type, int cx, int cy, int fuLoad);

            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            public static extern int ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, int nIcons);
        }
    }
}
