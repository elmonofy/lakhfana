using System;
using System.IO;
using System.Text;

namespace Lakhfana
{
    /// <summary>
    /// Appends exception details to %APPDATA%\Lakhfana\error.log (capped at ~256 KB). Never throws.
    /// Users only ever see a friendly Arabic line; the English details live here.
    /// </summary>
    public static class ErrorLog
    {
        private static readonly object logLock = new object();

        public static string FilePath
        {
            get { return Path.Combine(Settings.Folder, "error.log"); }
        }

        public static void Write(Exception ex)
        {
            if (ex == null) return;
            try
            {
                lock (logLock)
                {
                    Directory.CreateDirectory(Settings.Folder);
                    string path = FilePath;
                    try
                    {
                        FileInfo fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                    }
                    catch (Exception) { }
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + Installer.Version + "\r\n" + ex + "\r\n\r\n",
                        new UTF8Encoding(false));
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
