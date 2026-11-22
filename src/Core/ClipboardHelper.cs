using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Lakhfana
{
    /// <summary>
    /// Clipboard access through Win32 directly: retries while another app holds the clipboard, full best-effort
    /// backup/restore of the user's clipboard, and text writes that are kept out of Win+V history.
    /// </summary>
    internal static class ClipboardHelper
    {
        /// <summary>A saved copy of the clipboard. Formats are HGLOBAL byte copies, plus an optional metafile handle.</summary>
        internal sealed class Snapshot
        {
            public readonly List<KeyValuePair<uint, byte[]>> Formats = new List<KeyValuePair<uint, byte[]>>();
            public IntPtr EnhMetaFile = IntPtr.Zero;
            public bool WasEmpty;
        }

        const long MaxBackupBytes = 200L * 1024 * 1024;

        static uint fmtExcludeMonitor, fmtHistory, fmtCloud, fmtVsCode, fmtChromiumCustom, fmtMsDevLine, fmtVsLineTag;

        static ClipboardHelper()
        {
            fmtExcludeMonitor = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            fmtHistory = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
            fmtCloud = Native.RegisterClipboardFormat("CanUploadToCloudClipboard");
            fmtVsCode = Native.RegisterClipboardFormat("vscode-editor-data");
            fmtChromiumCustom = Native.RegisterClipboardFormat("Chromium Web Custom MIME Data Format");
            fmtMsDevLine = Native.RegisterClipboardFormat("MSDEVLineSelect");
            fmtVsLineTag = Native.RegisterClipboardFormat("VisualStudioEditorOperationsLineCutCopyClipboardTag");
        }

        public static uint Sequence
        {
            get { return Native.GetClipboardSequenceNumber(); }
        }

        static bool Open()
        {
            for (int i = 0; i < 40; i++)
            {
                if (Native.OpenClipboard(IntPtr.Zero)) return true;
                Thread.Sleep(15);
            }
            return false;
        }

        static bool IsSkippedFormat(uint f)
        {
            if (f == Native.CF_BITMAP || f == Native.CF_PALETTE || f == Native.CF_METAFILEPICT) return true;
            if (f == Native.CF_OWNERDISPLAY || f == Native.CF_DSPTEXT || f == Native.CF_DSPBITMAP) return true;
            if (f == Native.CF_DSPMETAFILEPICT || f == Native.CF_DSPENHMETAFILE) return true;
            if (f >= Native.CF_PRIVATEFIRST && f <= Native.CF_PRIVATELAST) return true;
            if (f >= Native.CF_GDIOBJFIRST && f <= Native.CF_GDIOBJLAST) return true;
            return false;
        }

        static byte[] ReadGlobal(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            long size = (long)Native.GlobalSize(h).ToUInt64();
            if (size <= 0 || size > int.MaxValue) return null;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try
            {
                byte[] data = new byte[size];
                Marshal.Copy(p, data, 0, (int)size);
                return data;
            }
            finally
            {
                Native.GlobalUnlock(h);
            }
        }

        static IntPtr ToGlobal(byte[] data)
        {
            IntPtr h = Native.GlobalAlloc(Native.GMEM_MOVEABLE, new UIntPtr((ulong)Math.Max(data.Length, 1)));
            if (h == IntPtr.Zero) return IntPtr.Zero;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) { Native.GlobalFree(h); return IntPtr.Zero; }
            if (data.Length > 0) Marshal.Copy(data, 0, p, data.Length);
            Native.GlobalUnlock(h);
            return h;
        }

        static void Put(uint format, byte[] data)
        {
            IntPtr h = ToGlobal(data);
            if (h == IntPtr.Zero) return;
            if (Native.SetClipboardData(format, h) == IntPtr.Zero) Native.GlobalFree(h);
        }

        static void PutPrivacyMarkers(HashSet<uint> already)
        {
            byte[] zero = BitConverter.GetBytes(0);
            if (fmtExcludeMonitor != 0 && (already == null || !already.Contains(fmtExcludeMonitor))) Put(fmtExcludeMonitor, zero);
            if (fmtHistory != 0 && (already == null || !already.Contains(fmtHistory))) Put(fmtHistory, zero);
            if (fmtCloud != 0 && (already == null || !already.Contains(fmtCloud))) Put(fmtCloud, zero);
        }

        /// <summary>Copies every readable format. Never throws.</summary>
        public static Snapshot Backup()
        {
            Snapshot snap = new Snapshot();
            if (!Open()) return null;
            try
            {
                long total = 0;
                uint f = 0;
                int count = 0;
                while ((f = Native.EnumClipboardFormats(f)) != 0)
                {
                    count++;
                    if (f == fmtExcludeMonitor || f == fmtHistory || f == fmtCloud) continue;
                    if (f == Native.CF_ENHMETAFILE)
                    {
                        IntPtr emf = Native.GetClipboardData(f);
                        if (emf != IntPtr.Zero) snap.EnhMetaFile = Native.CopyEnhMetaFile(emf, null);
                        continue;
                    }
                    if (IsSkippedFormat(f)) continue;
                    try
                    {
                        byte[] data = ReadGlobal(Native.GetClipboardData(f));
                        if (data == null) continue;
                        total += data.Length;
                        if (total > MaxBackupBytes) break;
                        snap.Formats.Add(new KeyValuePair<uint, byte[]>(f, data));
                    }
                    catch (Exception) { }
                }
                snap.WasEmpty = count == 0;
            }
            catch (Exception) { }
            finally
            {
                Native.CloseClipboard();
            }
            return snap;
        }

        /// <summary>Puts the saved clipboard back. Never throws.</summary>
        public static void Restore(Snapshot snap)
        {
            if (snap == null) return;
            if (!Open()) return;
            try
            {
                Native.EmptyClipboard();
                if (snap.WasEmpty) return;
                HashSet<uint> written = new HashSet<uint>();
                foreach (KeyValuePair<uint, byte[]> kv in snap.Formats)
                {
                    Put(kv.Key, kv.Value);
                    written.Add(kv.Key);
                }
                if (snap.EnhMetaFile != IntPtr.Zero)
                {
                    if (Native.SetClipboardData(Native.CF_ENHMETAFILE, snap.EnhMetaFile) != IntPtr.Zero)
                        snap.EnhMetaFile = IntPtr.Zero;   // owned by the clipboard now
                }
                // The original entry is already in Win+V history; don't add a duplicate.
                PutPrivacyMarkers(written);
            }
            catch (Exception) { }
            finally
            {
                Native.CloseClipboard();
                Dispose(snap);
            }
        }

        public static void Dispose(Snapshot snap)
        {
            if (snap != null && snap.EnhMetaFile != IntPtr.Zero)
            {
                Native.DeleteEnhMetaFile(snap.EnhMetaFile);
                snap.EnhMetaFile = IntPtr.Zero;
            }
        }

        /// <summary>Reads CF_UNICODETEXT; null when there is none.</summary>
        public static string ReadText()
        {
            if (!Open()) return null;
            try
            {
                byte[] data = ReadGlobal(Native.GetClipboardData(Native.CF_UNICODETEXT));
                if (data == null) return null;
                string s = Encoding.Unicode.GetString(data);
                int nul = s.IndexOf('\0');
                return nul >= 0 ? s.Substring(0, nul) : s;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        /// <summary>
        /// True when the clipboard holds an editor's "copy the whole line" result for an empty selection
        /// (VS Code / Electron editors, Visual Studio).
        /// </summary>
        public static bool IsEmptySelectionLineCopy()
        {
            if (!Open()) return false;
            try
            {
                if (fmtMsDevLine != 0 && Native.IsClipboardFormatAvailable(fmtMsDevLine)) return true;
                if (fmtVsLineTag != 0 && Native.IsClipboardFormatAvailable(fmtVsLineTag)) return true;
                foreach (uint f in new uint[] { fmtVsCode, fmtChromiumCustom })
                {
                    if (f == 0 || !Native.IsClipboardFormatAvailable(f)) continue;
                    byte[] data = ReadGlobal(Native.GetClipboardData(f));
                    if (data == null) continue;
                    if (Contains(data, Encoding.UTF8.GetBytes("\"isFromEmptySelection\":true"))) return true;
                    if (Contains(data, Encoding.Unicode.GetBytes("\"isFromEmptySelection\":true"))) return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        static bool Contains(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
        }

        /// <summary>Replaces the clipboard with plain text (kept out of Win+V history). Returns false on failure.</summary>
        public static bool SetText(string text)
        {
            if (!Open()) return false;
            try
            {
                if (!Native.EmptyClipboard()) return false;
                byte[] data = Encoding.Unicode.GetBytes(text + "\0");
                IntPtr h = ToGlobal(data);
                if (h == IntPtr.Zero) return false;
                if (Native.SetClipboardData(Native.CF_UNICODETEXT, h) == IntPtr.Zero)
                {
                    Native.GlobalFree(h);
                    return false;
                }
                PutPrivacyMarkers(null);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        /// <summary>Waits until the clipboard sequence number moves past `before`.</summary>
        public static bool WaitForChange(uint before, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (Native.GetClipboardSequenceNumber() != before) return true;
                Thread.Sleep(10);
                waited += 10;
            }
            return Native.GetClipboardSequenceNumber() != before;
        }
    }
}
