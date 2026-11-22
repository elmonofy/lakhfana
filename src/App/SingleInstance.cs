using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lakhfana
{
    /// <summary>
    /// Single-instance guard (mutex Local\Lakhfana_SingleInstance) plus the two named events other
    /// copies use to talk to the running one: Local\Lakhfana_ShowSettings and Local\Lakhfana_Exit.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        public const string MutexName = @"Local\Lakhfana_SingleInstance";
        public const string ShowSettingsEventName = @"Local\Lakhfana_ShowSettings";
        public const string ExitEventName = @"Local\Lakhfana_Exit";

        private Mutex mutex;
        private EventWaitHandle showEvent;
        private EventWaitHandle exitEvent;
        private ManualResetEvent stopEvent;
        private Thread listener;
        private bool disposed;

        // current receivers; while a slot is null a signal is remembered and delivered by the next SetHandlers
        private readonly object handlerLock = new object();
        private Action showHandler;
        private Action exitHandler;
        private bool pendingShow;
        private bool pendingExit;

        private SingleInstance()
        {
        }

        /// <summary>Returns the owner object, or null if another instance already runs.</summary>
        public static SingleInstance TryAcquire()
        {
            Mutex m = null;
            try
            {
                bool createdNew;
                m = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    bool got = false;
                    try { got = m.WaitOne(0, false); }
                    catch (AbandonedMutexException) { got = true; }
                    if (!got)
                    {
                        m.Dispose();
                        return null;
                    }
                }
                SingleInstance si = new SingleInstance();
                si.mutex = m;
                si.showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
                si.exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
                si.stopEvent = new ManualResetEvent(false);
                return si;
            }
            catch (Exception)
            {
                if (m != null)
                {
                    try { m.Dispose(); }
                    catch (Exception) { }
                }
                return null;
            }
        }

        /// <summary>True when some process currently owns the single-instance mutex.</summary>
        public static bool IsAnotherRunning()
        {
            Mutex m = null;
            try
            {
                if (!Mutex.TryOpenExisting(MutexName, out m)) return false;
                bool got = false;
                try { got = m.WaitOne(0, false); }
                catch (AbandonedMutexException) { got = true; }
                if (got)
                {
                    m.ReleaseMutex();
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (m != null)
                {
                    try { m.Dispose(); }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>Asks the running instance to open its settings window (and lets it take the foreground).</summary>
        public static bool SignalShowSettings()
        {
            // we are the foreground process (the user just launched us): hand that right on, otherwise
            // the running copy's SetForegroundWindow only flashes its taskbar button
            try { SingleInstanceNative.AllowSetForegroundWindow(SingleInstanceNative.ASFW_ANY); }
            catch (Exception) { }
            return Signal(ShowSettingsEventName);
        }

        /// <summary>Asks the running instance to quit.</summary>
        public static bool SignalExit()
        {
            return Signal(ExitEventName);
        }

        private static bool Signal(string name)
        {
            // the owner may still be starting up: retry for a short while
            for (int i = 0; i < 20; i++)
            {
                EventWaitHandle h = null;
                try
                {
                    if (EventWaitHandle.TryOpenExisting(name, out h))
                    {
                        h.Set();
                        return true;
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    if (h != null) h.Dispose();
                }
                Thread.Sleep(100);
            }
            return false;
        }

        /// <summary>
        /// Closes the running copy: signals Exit, waits up to timeoutMs for it to go away, then (if it
        /// is still alive) kills any other process started from exePath. Returns true when nothing is left.
        /// </summary>
        public static bool CloseRunning(int timeoutMs, string exePath)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (IsAnotherRunning())
            {
                SignalExit();
                while (IsAnotherRunning() && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(100);
            }

            if (string.IsNullOrEmpty(exePath)) return !IsAnotherRunning();

            // wait for the process itself (mutex is released a little before the exe unlocks)
            bool clean = true;
            int self = Process.GetCurrentProcess().Id;
            string name = Path.GetFileNameWithoutExtension(exePath);
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch (Exception) { procs = new Process[0]; }
            foreach (Process p in procs)
            {
                try
                {
                    if (p.Id == self) continue;
                    if (!SamePath(SafeProcessPath(p), exePath)) continue;
                    int left = (int)Math.Max(200, timeoutMs - sw.ElapsedMilliseconds);
                    if (!p.WaitForExit(left))
                    {
                        p.Kill();
                        if (!p.WaitForExit(2000)) clean = false;
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
            return clean && !IsAnotherRunning();
        }

        private static string SafeProcessPath(Process p)
        {
            try { return p.MainModule.FileName; }
            catch (Exception) { return null; }
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sets the receivers and starts (once) a background thread that waits on the named events.
        /// Callbacks run on that worker thread (or, for a signal remembered while no receiver was set,
        /// on the thread calling Listen/SetHandlers); the caller must marshal to the UI thread.
        /// Calling it again only swaps the receivers.
        /// </summary>
        public void Listen(Action onShowSettings, Action onExit)
        {
            if (disposed) return;
            SetHandlers(onShowSettings, onExit);
            if (listener != null) return;
            WaitHandle[] handles = new WaitHandle[] { stopEvent, showEvent, exitEvent };
            listener = new Thread(delegate()
            {
                while (true)
                {
                    int which;
                    try { which = WaitHandle.WaitAny(handles); }
                    catch (Exception) { return; }
                    if (which == 0) return;
                    Deliver(which == 1, which == 2);
                    if (which == 2) return;
                }
            });
            listener.IsBackground = true;
            listener.Name = "Lakhfana.SingleInstance";
            listener.Start();
        }

        /// <summary>
        /// Swaps the receivers (null = nobody right now: a signal arriving meanwhile is kept and handed
        /// to the next non-null receiver instead of being lost).
        /// </summary>
        public void SetHandlers(Action onShowSettings, Action onExit)
        {
            bool show, exit;
            lock (handlerLock)
            {
                showHandler = onShowSettings;
                exitHandler = onExit;
                show = pendingShow && onShowSettings != null;
                exit = pendingExit && onExit != null;
                if (show) pendingShow = false;
                if (exit) pendingExit = false;
            }
            if (exit) Invoke(onExit);
            else if (show) Invoke(onShowSettings);
        }

        private void Deliver(bool show, bool exit)
        {
            Action h = null;
            lock (handlerLock)
            {
                if (exit)
                {
                    h = exitHandler;
                    if (h == null) pendingExit = true;
                }
                else if (show)
                {
                    h = showHandler;
                    if (h == null) pendingShow = true;
                }
            }
            Invoke(h);
        }

        private static void Invoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (stopEvent != null) stopEvent.Set(); }
            catch (Exception) { }
            if (listener != null && listener != Thread.CurrentThread)
            {
                try { listener.Join(500); }
                catch (Exception) { }
            }
            if (mutex != null)
            {
                try { mutex.ReleaseMutex(); }
                catch (Exception) { }
                try { mutex.Dispose(); }
                catch (Exception) { }
                mutex = null;
            }
            if (showEvent != null) { showEvent.Dispose(); showEvent = null; }
            if (exitEvent != null) { exitEvent.Dispose(); exitEvent = null; }
            // stopEvent intentionally not disposed while the listener might still touch it
            lock (handlerLock)
            {
                showHandler = null;
                exitHandler = null;
            }
        }

        private static class SingleInstanceNative
        {
            public const int ASFW_ANY = -1;

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AllowSetForegroundWindow(int dwProcessId);
        }
    }
}
