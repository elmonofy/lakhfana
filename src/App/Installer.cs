using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Lakhfana
{
    /// <summary>Per-user install: %LOCALAPPDATA%\Programs\Lakhfana, Start-menu shortcut, HKCU uninstall entry, Run key.</summary>
    public static class Installer
    {
        private const string AppKeyName = "Lakhfana";
        private const string DisplayName = "لخفنه";
        private const string ShortcutName = "لخفنه.lnk";
        private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppKeyName;
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string InstallDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.Combine(local, "Programs"), AppKeyName);
            }
        }

        public static string InstalledExe
        {
            get { return Path.Combine(InstallDir, "Lakhfana.exe"); }
        }

        public static string Version
        {
            get
            {
                Version v = typeof(Installer).Assembly.GetName().Version;
                if (v == null) return "1.0.5";
                return v.Major.ToString(CultureInfo.InvariantCulture) + "." +
                       v.Minor.ToString(CultureInfo.InvariantCulture) + "." +
                       Math.Max(0, v.Build).ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string ShortcutPath
        {
            get
            {
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                return Path.Combine(programs, ShortcutName);
            }
        }

        private static string SelfPath
        {
            get { return Application.ExecutablePath; }
        }

        public static bool IsRunningFromInstallDir()
        {
            return SamePath(SelfPath, InstalledExe);
        }

        /// <summary>HKCU uninstall key exists AND the installed exe exists.</summary>
        public static bool IsInstalled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, false))
                {
                    if (k == null) return false;
                }
                return File.Exists(InstalledExe);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Copies this exe to InstalledExe (overwrite), creates the Start-menu shortcut, writes the HKCU
        /// uninstall entry and sets the Run key according to startWithWindows. Throws on a fatal failure
        /// (could not copy the exe); everything else is best effort.
        /// </summary>
        public static void Install(bool startWithWindows)
        {
            Directory.CreateDirectory(InstallDir);
            string target = InstalledExe;

            if (!SamePath(SelfPath, target))
            {
                CopyWithRetry(SelfPath, target);
            }
            // the installed copy is ours now: drop the downloaded-file mark so it starts without prompts
            try { InstallerNative.DeleteFileW(target + ":Zone.Identifier"); }
            catch (Exception) { }

            try { CreateShortcut(ShortcutPath, target, InstallDir, "حوّل الكلام اللي اتكتب باللغة الغلط"); }
            catch (Exception) { }

            WriteUninstallEntry(target);

            try { SetStartWithWindows(startWithWindows); }
            catch (Exception) { }
        }

        /// <summary>HKCU\...\Run "Lakhfana" = "\"InstalledExe\"" (or removed).</summary>
        public static void SetStartWithWindows(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (k == null) return;
                if (on) k.SetValue(AppKeyName, "\"" + InstalledExe + "\"", RegistryValueKind.String);
                else if (k.GetValue(AppKeyName) != null) k.DeleteValue(AppKeyName, false);
            }
        }

        /// <summary>Removes Run value, shortcut, uninstall entry and settings folder; schedules deletion of the install folder.</summary>
        public static void Uninstall()
        {
            try { SetStartWithWindows(false); }
            catch (Exception) { }

            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); }
            catch (Exception) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); }
            catch (Exception) { }

            try
            {
                string folder = Settings.Folder;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception) { }

            ScheduleInstallDirDelete();
        }

        // ---------------------------------------------------------------- helpers

        private static void CopyWithRetry(string source, string target)
        {
            string tmp = target + ".new";
            Exception last = null;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                try
                {
                    File.Copy(source, tmp, true);
                    if (File.Exists(target))
                    {
                        File.Copy(tmp, target, true);
                        File.Delete(tmp);
                    }
                    else
                    {
                        File.Move(tmp, target);
                    }
                    return;
                }
                catch (IOException ex) { last = ex; }
                catch (UnauthorizedAccessException ex) { last = ex; }
                Thread.Sleep(300);
            }
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception) { }
            throw new IOException("Could not copy the program to " + target, last);
        }

        private static void WriteUninstallEntry(string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                if (k == null) throw new IOException("Could not create the uninstall registry key.");
                k.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
                k.SetValue("DisplayIcon", exe + ",0", RegistryValueKind.String);
                k.SetValue("DisplayVersion", Version, RegistryValueKind.String);
                k.SetValue("Publisher", Credit.Author, RegistryValueKind.String);
                k.SetValue("URLInfoAbout", Credit.Url, RegistryValueKind.String);
                k.SetValue("HelpLink", Credit.Url, RegistryValueKind.String);
                k.SetValue("InstallLocation", InstallDir, RegistryValueKind.String);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), RegistryValueKind.String);
                k.SetValue("UninstallString", "\"" + exe + "\" /uninstall", RegistryValueKind.String);
                k.SetValue("QuietUninstallString", "\"" + exe + "\" /uninstall /quiet", RegistryValueKind.String);
                int sizeKb = 1;
                try { sizeKb = (int)Math.Max(1, new FileInfo(exe).Length / 1024); }
                catch (Exception) { }
                k.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>
        /// Detached hidden cmd: waits (no time cap) until this process has exited — it may still sit behind
        /// a modal dialog for a long time — then deletes the exe and the folder. The paths and our PID reach
        /// the script through environment variables only, so Unicode, spaces, and, %, ^ and ! in them
        /// never meet the cmd parser; the script itself is plain ASCII in a temp file with an ASCII name.
        /// </summary>
        private static void ScheduleInstallDirDelete()
        {
            string dir = InstallDir;
            if (!Directory.Exists(dir)) return;
            string exe = InstalledExe;
            int pid;
            try { pid = Process.GetCurrentProcess().Id; }
            catch (Exception) { pid = 0; }

            string script =
                "@echo off\r\n" +
                "setlocal DisableDelayedExpansion\r\n" +
                // 1) wait for our process to go away (tasklist prints the PID as its own column)
                ":wait\r\n" +
                "if \"%LKF_PID%\"==\"0\" goto del\r\n" +
                "tasklist /fi \"PID eq %LKF_PID%\" /nh 2>nul | find \" %LKF_PID% \" >nul\r\n" +
                "if errorlevel 1 goto del\r\n" +
                "ping 127.0.0.1 -n 2 >nul\r\n" +
                "goto wait\r\n" +
                // 2) the image can stay locked a moment after exit (or by antivirus): retry ~30 s
                ":del\r\n" +
                "set /a LKF_N=0\r\n" +
                ":again\r\n" +
                "if exist \"%LKF_EXE%\" del /f /q \"%LKF_EXE%\" >nul 2>&1\r\n" +
                "if not exist \"%LKF_EXE%\" goto rd\r\n" +
                "set /a LKF_N+=1\r\n" +
                "if %LKF_N% geq 30 goto rd\r\n" +
                "ping 127.0.0.1 -n 2 >nul\r\n" +
                "goto again\r\n" +
                ":rd\r\n" +
                "rmdir /s /q \"%LKF_DIR%\" >nul 2>&1\r\n" +
                // 3) delete this script itself
                "(goto) 2>nul & del /f /q \"%~f0\"\r\n";

            string tempDir = null;
            string scriptName = "lakhfana-cleanup-" + Guid.NewGuid().ToString("N") + ".cmd";
            try
            {
                tempDir = Path.GetTempPath();
                File.WriteAllText(Path.Combine(tempDir, scriptName), script, Encoding.ASCII);
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                // relative, ASCII-only name; WorkingDirectory is the temp folder (never inside what we delete)
                psi.Arguments = "/d /c .\\" + scriptName;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = tempDir;
                psi.EnvironmentVariables["LKF_PID"] = pid.ToString(CultureInfo.InvariantCulture);
                psi.EnvironmentVariables["LKF_EXE"] = exe;
                psi.EnvironmentVariables["LKF_DIR"] = dir.TrimEnd('\\');
                Process p = Process.Start(psi);
                if (p != null) p.Dispose();
            }
            catch (Exception)
            {
                try { File.Delete(Path.Combine(tempDir, scriptName)); }
                catch (Exception) { }
            }
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void CreateShortcut(string lnkPath, string target, string workingDir, string description)
        {
            string folder = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            object obj = null;
            try
            {
                obj = new CShellLink();
                IShellLinkW link = (IShellLinkW)obj;
                link.SetPath(target);
                link.SetWorkingDirectory(workingDir);
                link.SetDescription(description);
                link.SetIconLocation(target, 0);
                link.SetArguments("");
                IPersistFileLink file = (IPersistFileLink)obj;
                file.Save(lnkPath, true);
            }
            finally
            {
                if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj);
            }
        }

        // ---------------------------------------------------------------- COM interop

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        [ClassInterface(ClassInterfaceType.None)]
        private class CShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFileLink
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig]
            int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        private static class InstallerNative
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteFileW(string lpFileName);
        }
    }
}
