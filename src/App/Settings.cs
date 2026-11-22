using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lakhfana
{
    /// <summary>User settings, stored as key=value lines in %APPDATA%\Lakhfana\settings.ini.</summary>
    public sealed class Settings
    {
        public Hotkey Hotkey = Hotkey.Default;
        public string LayoutA;              // HKL hex string e.g. "04090409"; null/"" = auto-pick
        public string LayoutB;
        public bool SwitchLayoutAfter = true;
        public bool SelectLineIfEmpty = true;
        public bool StartWithWindows = true;
        public bool FirstRunDone;
        public bool Paused;

        private const string FileName = "settings.ini";

        public static string Folder
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData)) appData = Path.GetTempPath();
                return Path.Combine(appData, "Lakhfana");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Folder, FileName); }
        }

        /// <summary>Never throws; returns defaults for a missing or corrupt file.</summary>
        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return s;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in lines)
                {
                    if (raw == null) continue;
                    string line = raw.Trim().TrimStart('﻿');
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key.Length > 0) map[key] = val;
                }

                string v;
                if (map.TryGetValue("Hotkey", out v))
                {
                    Hotkey hk = Hotkey.Parse(v);
                    if (!hk.IsEmpty) s.Hotkey = hk;
                }
                if (map.TryGetValue("LayoutA", out v)) s.LayoutA = CleanLayout(v);
                if (map.TryGetValue("LayoutB", out v)) s.LayoutB = CleanLayout(v);
                s.SwitchLayoutAfter = ReadBool(map, "SwitchLayoutAfter", s.SwitchLayoutAfter);
                s.SelectLineIfEmpty = ReadBool(map, "SelectLineIfEmpty", s.SelectLineIfEmpty);
                s.StartWithWindows = ReadBool(map, "StartWithWindows", s.StartWithWindows);
                s.FirstRunDone = ReadBool(map, "FirstRunDone", s.FirstRunDone);
                s.Paused = ReadBool(map, "Paused", s.Paused);
            }
            catch (Exception)
            {
                // corrupt / unreadable: keep whatever was parsed so far (defaults otherwise)
            }
            return s;
        }

        /// <summary>Writes settings.ini atomically (temp file + replace). Never throws; ignores failures.</summary>
        public void Save()
        {
            TrySave();
        }

        /// <summary>Writes settings.ini atomically (temp file + replace). Never throws; false on any failure.</summary>
        public bool TrySave()
        {
            string tmp = null;
            bool done = false;
            try
            {
                string folder = Folder;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, FileName);
                tmp = path + ".tmp";

                StringBuilder sb = new StringBuilder();
                sb.Append("# Lakhfana settings\r\n");
                sb.Append("Hotkey=").Append(Hotkey.Serialize()).Append("\r\n");
                sb.Append("LayoutA=").Append(LayoutA ?? "").Append("\r\n");
                sb.Append("LayoutB=").Append(LayoutB ?? "").Append("\r\n");
                sb.Append("SwitchLayoutAfter=").Append(B(SwitchLayoutAfter)).Append("\r\n");
                sb.Append("SelectLineIfEmpty=").Append(B(SelectLineIfEmpty)).Append("\r\n");
                sb.Append("StartWithWindows=").Append(B(StartWithWindows)).Append("\r\n");
                sb.Append("FirstRunDone=").Append(B(FirstRunDone)).Append("\r\n");
                sb.Append("Paused=").Append(B(Paused)).Append("\r\n");

                byte[] data = new UTF8Encoding(false).GetBytes(sb.ToString());
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null, true);
                        done = true;
                    }
                    catch (Exception)
                    {
                        done = false;
                    }
                    if (!done)
                    {
                        File.Copy(tmp, path, true);
                        done = true;
                        try { File.Delete(tmp); }
                        catch (Exception) { }
                    }
                }
                else
                {
                    File.Move(tmp, path);
                    done = true;
                }
                tmp = null;
            }
            catch (Exception)
            {
                // never crash the tray app over settings; the caller decides what to tell the user
                done = false;
            }
            finally
            {
                if (tmp != null)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); }
                    catch (Exception) { }
                }
            }
            return done;
        }

        private static string B(bool b)
        {
            return b ? "1" : "0";
        }

        private static string CleanLayout(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            v = v.Trim();
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v.Substring(2);
            if (v.Length == 0 || v.Length > 16) return null;
            long dummy;
            if (!long.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dummy)) return null;
            return v.ToUpperInvariant();
        }

        private static bool ReadBool(Dictionary<string, string> map, string key, bool fallback)
        {
            string v;
            if (!map.TryGetValue(key, out v) || v == null) return fallback;
            v = v.Trim().ToLowerInvariant();
            switch (v)
            {
                case "1": case "true": case "yes": case "on": case "y":
                    return true;
                case "0": case "false": case "no": case "off": case "n":
                    return false;
            }
            return fallback;
        }
    }
}
