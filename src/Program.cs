using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Lakhfana
{
    internal static class Program
    {
        private const string SetupMutexName = @"Local\Lakhfana_Setup";

        [STAThread]
        private static int Main(string[] args)
        {
            bool uninstall = HasArg(args, "uninstall");
            bool quiet = HasArg(args, "quiet");
            bool portable = HasArg(args, "portable");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log(e.ExceptionObject as Exception);
            };

            try
            {
                if (uninstall) return RunUninstall(quiet);
                if (portable) return RunTray(true, null);
                if (Installer.IsRunningFromInstallDir()) return RunTray(false, StartupMessageFor(args));
                return RunSetup();
            }
            catch (Exception ex)
            {
                Log(ex);
                if (!quiet)
                {
                    ShellDialogs.ShowError("حصلت مشكلة ولخفنه اضطر يقفل.\nجرّب تفتحه تاني، ولو اتكررت هتلاقي التفاصيل في ملف error.log.");
                }
                return 1;
            }
        }

        // ---------------------------------------------------------------- flows

        /// <summary>Normal run (installed copy or /portable): single instance, onboarding if needed, then the tray.</summary>
        private static int RunTray(bool portable, string startupMessageTemplate)
        {
            SingleInstance si = SingleInstance.TryAcquire();
            if (si == null)
            {
                SingleInstance.SignalShowSettings();
                return 0;
            }

            // listen from the very start: a ShowSettings/Exit sent while we are still setting up (or showing
            // onboarding) is consumed now and routed below instead of firing out of the blue later
            si.Listen(null, null);

            Settings settings = Settings.Load();
            HotkeyManager hotkeys = new HotkeyManager();

            if (!settings.FirstRunDone)
            {
                bool exitRequested;
                DialogResult r = RunOnboarding(settings, hotkeys, si, out exitRequested);
                if (r != DialogResult.OK || exitRequested)
                {
                    hotkeys.Dispose();
                    si.Dispose();
                    return 0;
                }
                // Onboarding just set things up: say hello from the tray
                if (startupMessageTemplate == null) startupMessageTemplate = InstalledMessage;
            }

            string startupMessage = null;
            if (startupMessageTemplate != null)
            {
                startupMessage = startupMessageTemplate.Replace("{hotkey}", settings.Hotkey.ToDisplayString());
            }

            using (TrayApp app = new TrayApp(settings, hotkeys, si, portable, startupMessage))
            {
                Application.Run(app);
            }
            return 0;
        }

        /// <summary>
        /// First-run onboarding of the installed/portable copy. While it is open a second launch (ShowSettings)
        /// brings it to the front and an Exit signal closes it (exitRequested = true).
        /// </summary>
        private static DialogResult RunOnboarding(Settings settings, HotkeyManager hotkeys, SingleInstance si, out bool exitRequested)
        {
            bool exitFlag = false;
            Control invoker = new Control();
            invoker.CreateControl();
            GC.KeepAlive(invoker.Handle);
            OnboardingForm f = new OnboardingForm(settings, hotkeys, false);
            try
            {
                MethodInvoker activate = delegate
                {
                    if (f.IsDisposed || !f.IsHandleCreated || !f.Visible) return;
                    if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
                    f.Activate(); // SetForegroundWindow; allowed because the signalling copy called AllowSetForegroundWindow
                };
                MethodInvoker close = delegate
                {
                    exitFlag = true;
                    if (!f.IsDisposed && f.IsHandleCreated && f.Visible) f.Close();
                };
                f.Shown += delegate { if (exitFlag) f.Close(); };

                si.SetHandlers(
                    delegate { PostTo(invoker, activate); },
                    delegate { PostTo(invoker, close); });

                DialogResult r = f.ShowDialog();

                // from here on signals are kept for the tray (TrayApp's Listen picks them up)
                si.SetHandlers(null, null);
                Application.DoEvents(); // run whatever the listener posted right before the form closed
                exitRequested = exitFlag;
                return r;
            }
            finally
            {
                si.SetHandlers(null, null);
                f.Dispose();
                invoker.Dispose();
            }
        }

        private static void PostTo(Control invoker, MethodInvoker action)
        {
            try
            {
                if (invoker.IsDisposed || !invoker.IsHandleCreated) return;
                invoker.BeginInvoke(action);
            }
            catch (Exception) { }
        }

        /// <summary>Launched from outside the install folder (e.g. the downloaded exe): install or update, then hand over.</summary>
        private static int RunSetup()
        {
            Mutex setupMutex = null;
            try
            {
                bool createdNew;
                setupMutex = new Mutex(true, SetupMutexName, out createdNew);
                if (!createdNew) return 0; // another setup window is already open

                Settings settings = Settings.Load();
                string arg;

                if (!Installer.IsInstalled())
                {
                    DialogResult r;
                    using (OnboardingForm f = new OnboardingForm(settings, null, true))
                    {
                        r = f.ShowDialog();
                    }
                    if (r != DialogResult.OK) return 0;
                    settings = Settings.Load(); // pick up exactly what onboarding saved
                    arg = "/installed";
                }
                else
                {
                    Version mine = FileVersionOf(Application.ExecutablePath);
                    Version installed = FileVersionOf(Installer.InstalledExe);
                    if (mine != null && installed != null)
                    {
                        int cmp = mine.CompareTo(installed);
                        if (cmp == 0)
                        {
                            // same version: nothing to install, just bring up the copy we already have
                            return OpenInstalled() ? 0 : 1;
                        }
                        if (cmp < 0)
                        {
                            bool downgrade = ShellDialogs.Confirm(
                                "النسخة اللي فتحتها (" + Short(mine) + ") أقدم من اللي متسطّبة عندك (" + Short(installed) + ").\n" +
                                "عايز ترجع للنسخة الأقدم؟",
                                "أيوه، رجّعها", "لأ، خليها", false);
                            if (!downgrade) return OpenInstalled() ? 0 : 1;
                        }
                    }
                    arg = "/updated";
                }

                SingleInstance.CloseRunning(3000, Installer.InstalledExe);

                try
                {
                    Installer.Install(settings.StartWithWindows);
                }
                catch (Exception ex)
                {
                    Log(ex);
                    ShellDialogs.ShowError("معرفناش نسطّب لخفنه.\nاقفل أي نسخة شغالة منه وجرب تاني.");
                    return 1;
                }

                if (!StartInstalled(arg))
                {
                    ShellDialogs.ShowError("لخفنه اتسطّب، بس معرفناش نشغّله.\nشغّله من قايمة ابدأ.");
                    return 1;
                }
                return 0;
            }
            finally
            {
                if (setupMutex != null)
                {
                    try { setupMutex.ReleaseMutex(); }
                    catch (Exception) { }
                    setupMutex.Dispose();
                }
            }
        }

        /// <summary>/uninstall [/quiet]: confirm, close the running copy, remove everything.</summary>
        private static int RunUninstall(bool quiet)
        {
            if (!quiet && !ShellDialogs.ConfirmUninstall()) return 0;
            SingleInstance.CloseRunning(3000, Installer.InstalledExe);
            Installer.Uninstall();
            if (!quiet) ShellDialogs.ShowUninstalled();
            return 0;
        }

        // ---------------------------------------------------------------- helpers

        private const string InstalledMessage = "لخفنه شغال هنا جنب الساعة. حدد الكلام ودوس {hotkey}";
        private const string UpdatedMessage = "لخفنه اتحدّث وشغال. الاختصار: {hotkey}";

        private static string StartupMessageFor(string[] args)
        {
            if (HasArg(args, "installed")) return InstalledMessage;
            if (HasArg(args, "updated")) return UpdatedMessage;
            return null;
        }

        /// <summary>Shows the installed copy: its settings if it is running, otherwise starts it.</summary>
        private static bool OpenInstalled()
        {
            if (SingleInstance.IsAnotherRunning() && SingleInstance.SignalShowSettings()) return true;
            if (StartInstalled(null)) return true;
            ShellDialogs.ShowError("معرفناش نشغّل لخفنه.\nشغّله من قايمة ابدأ.");
            return false;
        }

        /// <summary>The Win32 file version (FileVersion resource) of an exe, or null when it can't be read.</summary>
        private static Version FileVersionOf(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(path);
                if (fvi.FileMajorPart == 0 && fvi.FileMinorPart == 0 && fvi.FileBuildPart == 0 && fvi.FilePrivatePart == 0) return null;
                return new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);
            }
            catch (Exception ex)
            {
                Log(ex);
                return null;
            }
        }

        private static string Short(Version v)
        {
            string s = v.Major + "." + v.Minor + "." + v.Build;
            if (v.Revision > 0) s += "." + v.Revision;
            return s;
        }

        private static bool StartInstalled(string arg)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Installer.InstalledExe, arg ?? "");
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Installer.InstallDir;
                Process p = Process.Start(psi);
                if (p != null) p.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Log(ex);
                return false;
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string a in args)
            {
                if (string.IsNullOrEmpty(a)) continue;
                string t = a.Trim();
                if (t.Length < 2 || (t[0] != '/' && t[0] != '-')) continue;
                t = t.TrimStart('/', '-');
                if (string.Equals(t, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static void Log(Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }
}
