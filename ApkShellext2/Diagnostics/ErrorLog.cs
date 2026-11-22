using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApkShellext2.Diagnostics {
    /// <summary>
    /// Default-on error/warning log for the shell extension.
    ///
    /// SharpShell reports handler failures through LogError -> Diagnostics.Logging (a Trace
    /// listener), which is invisible in release builds and inside the host processes
    /// (explorer.exe / dllhost.exe) where this assembly actually runs. That is why the
    /// v3.4.x explorer-crash and every "the menu did nothing" report left no evidence.
    /// This channel records only failures - errors and warnings, never progress chatter -
    /// to %PUBLIC%\apkshellext-error.log so one file can be attached to a bug report.
    ///
    /// Layout: one line per entry, fields separated by " | ":
    ///   local ISO-8601 timestamp | process(pid) | thread id | assembly version |
    ///   level | module | message | exception type + message + first 4 stack frames
    ///
    /// - Always on, no registry switch (DbgLog stays the opt-in verbose channel).
    /// - Rotates once: over 1 MiB the current file becomes apkshellext-error.1.log and the
    ///   previous .1 is overwritten, so at most ~2 MiB total.
    /// - A single line is capped at 4 KiB (UTF-8) so a huge stack/message cannot fill the file.
    /// - Never throws. A log that breaks the shell is worse than no log at all, so every
    ///   failure path here degrades to silently dropping the entry.
    /// - Process-wide lock + AppendAllText: concurrent handler threads never interleave a line.
    /// </summary>
    internal static class ErrorLog {
        private const int MaxLogBytes = 1024 * 1024;
        private const int MaxLineBytes = 4096;

        private static readonly object _writeLock = new object();
        private static readonly UTF8Encoding _utf8 = new UTF8Encoding(false);
        private static readonly string _logPath = BuildPath("apkshellext-error.log");
        private static readonly string _rotatedPath = BuildPath("apkshellext-error.1.log");
        private static readonly string _process;
        private static readonly string _pid;
        private static readonly string _version;
        private static long _size = -1;
        private static int _hooksInstalled;

        static ErrorLog() {
            try { _process = Process.GetCurrentProcess().ProcessName; } catch { _process = "?"; }
            try { _pid = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture); }
            catch { _pid = "?"; }
            try {
                Assembly a = Assembly.GetExecutingAssembly();
                _version = a.GetName().Version.ToString();
            } catch { _version = "?"; }
            EnsureHooks();
        }

        /// <summary>%PUBLIC% per spec, ProgramData when PUBLIC is unavailable (non-interactive hosts).</summary>
        private static string BuildPath(string fileName) {
            try {
                string dir = Environment.GetEnvironmentVariable("PUBLIC");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (string.IsNullOrEmpty(dir)) return "";
                return Path.Combine(dir, fileName);
            } catch { return ""; }
        }

        /// <summary>Full path of the live log file ("" when no writable directory was found).</summary>
        public static string LogPath { get { return _logPath; } }

        /// <summary>Full path of the rotated file.</summary>
        public static string RotatedLogPath { get { return _rotatedPath; } }

        public static void Error(string module, string message, Exception ex = null) {
            Write("ERROR", module, message, ex);
        }

        public static void Warn(string module, string message, Exception ex = null) {
            Write("WARN", module, message, ex);
        }

        /// <summary>
        /// Arms the global exception hooks once per AppDomain.
        ///
        /// This assembly is loaded into explorer.exe's shared AppDomain: the hooks therefore
        /// OBSERVE ONLY. They never swallow, rethrow or shorten the host's control flow -
        /// they append one line and return, so a crash report exists the next time the user
        /// reports "Explorer restarted". Idempotent (Interlocked), safe to call from hot paths.
        /// </summary>
        public static void EnsureHooks() {
            if (Interlocked.Exchange(ref _hooksInstalled, 1) != 0) return;
            try { AppDomain.CurrentDomain.UnhandledException += OnUnhandledException; } catch { }
            try { TaskScheduler.UnobservedTaskException += OnUnobservedTaskException; } catch { }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) {
            // Observe only: the CLR terminates the process immediately after this callback
            // returns, and explorer's own fatal-error handling must stay untouched.
            try {
                Exception ex = e.ExceptionObject as Exception;
                string why = "IsTerminating=" + e.IsTerminating;
                if (ex == null)
                    why += " exceptionObject=" + (e.ExceptionObject ?? "null") + " (" +
                           (e.ExceptionObject == null ? "null" : e.ExceptionObject.GetType().FullName) + ")";
                Write("ERROR", "UnhandledException", why, ex);
            } catch { }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e) {
            try {
                Write("ERROR", "UnobservedTaskException", "task exception was never observed", e.Exception);
                // Only this assembly's exception is marked observed; the host's own tasks keep
                // whatever policy they configured. Without this, a fire-and-forget task in a
                // handler would poison explorer's unobserved-exception bookkeeping.
                e.SetObserved();
            } catch { }
        }

        private static void Write(string level, string module, string message, Exception ex) {
            try {
                if (_logPath.Length == 0) return;
                string line = Format(level, module, message, ex) + "\r\n";
                int bytes = _utf8.GetByteCount(line);
                lock (_writeLock) {
                    if (_size < 0) _size = CurrentSize();
                    if (_size > 0 && _size + bytes > MaxLogBytes) Rotate();
                    File.AppendAllText(_logPath, line, _utf8);
                    _size += bytes;
                }
            } catch { }
        }

        private static long CurrentSize() {
            try {
                FileInfo fi = new FileInfo(_logPath);
                return fi.Exists ? fi.Length : 0;
            } catch { return 0; }
        }

        private static void Rotate() {
            try { if (File.Exists(_rotatedPath)) File.Delete(_rotatedPath); } catch { }
            try { if (File.Exists(_logPath)) File.Move(_logPath, _rotatedPath); } catch { }
            _size = 0;
        }

        private static string Format(string level, string module, string message, Exception ex) {
            StringBuilder sb = new StringBuilder(256);
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
            sb.Append(" | ").Append(_process).Append('(').Append(_pid).Append(')');
            sb.Append(" | tid ").Append(Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture));
            sb.Append(" | ").Append(_version);
            sb.Append(" | ").Append(level ?? "");
            sb.Append(" | ").Append(OneLine(module));
            sb.Append(" | ").Append(OneLine(message));
            string detail = DescribeException(ex);
            if (detail.Length > 0) sb.Append(" | ").Append(detail);
            string line = sb.ToString();
            if (_utf8.GetByteCount(line) > MaxLineBytes)
                line = TruncateUtf8(line, MaxLineBytes - 3) + "...";
            return line;
        }

        private static string OneLine(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            try {
                return s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            } catch { return ""; }
        }

        private static string DescribeException(Exception ex) {
            try {
                if (ex == null) return "";
                StringBuilder sb = new StringBuilder();
                Type t = ex.GetType();
                sb.Append(t.FullName).Append(": ").Append(OneLine(ex.Message));
                string stack = StackOf(ex);
                if (stack.Length > 0) sb.Append(" | ").Append(stack);
                return sb.ToString();
            } catch { return ""; }
        }

        /// <summary>First 4 frames, on one line. AggregateException often carries no frames of
        /// its own, so fall back to the first flattened inner exception.</summary>
        private static string StackOf(Exception ex) {
            try {
                string st = ex.StackTrace;
                AggregateException agg = ex as AggregateException;
                if (string.IsNullOrEmpty(st) && agg != null) {
                    var inner = agg.Flatten().InnerExceptions;
                    if (inner.Count > 0) st = inner[0].StackTrace;
                }
                if (string.IsNullOrEmpty(st)) return "";
                string[] frames = st.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < frames.Length && i < 4; i++) {
                    if (i > 0) sb.Append(" <- ");
                    sb.Append(frames[i].Trim());
                }
                return sb.ToString();
            } catch { return ""; }
        }

        /// <summary>UTF-8 byte-bounded truncation that never splits a surrogate pair.</summary>
        private static string TruncateUtf8(string s, int maxBytes) {
            try {
                if (maxBytes <= 0) return "";
                int bytes = 0;
                for (int i = 0; i < s.Length; i++) {
                    int w = _utf8.GetByteCount(s.Substring(i, 1));
                    if (bytes + w > maxBytes) return s.Substring(0, i);
                    bytes += w;
                }
            } catch { }
            return s;
        }
    }
}
