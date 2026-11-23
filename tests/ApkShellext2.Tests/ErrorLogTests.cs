using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// ErrorLog: the default-on failure log. The type is internal and its paths
    /// are static readonly fields pointing at %PUBLIC%, so each test redirects them
    /// (and resets the cached size) to a scratch directory: the assertions then read
    /// the real file the extension would have written.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class ErrorLogTests
    {
        private static readonly Type ErrorLogType =
            typeof(AppPackageReader).Assembly.GetType("ApkShellext2.Diagnostics.ErrorLog", true);

        private static readonly FieldInfo LogPathField =
            ErrorLogType.GetField("_logPath", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo RotatedPathField =
            ErrorLogType.GetField("_rotatedPath", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo SizeField =
            ErrorLogType.GetField("_size", BindingFlags.Static | BindingFlags.NonPublic);

        private string directory;
        private string originalLogPath;
        private string originalRotatedPath;

        private string LogPath { get { return Path.Combine(directory, "apkshellext-error.log"); } }
        private string RotatedPath { get { return Path.Combine(directory, "apkshellext-error.1.log"); } }

        [OneTimeSetUp]
        public void RememberTheRealPaths()
        {
            // Touching the type runs its static constructor, which captures the real
            // %PUBLIC% paths; they are restored after every test.
            originalLogPath = (string)LogPathField.GetValue(null);
            originalRotatedPath = (string)RotatedPathField.GetValue(null);
        }

        [SetUp]
        public void RedirectToAScratchDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "apkshellext-errorlog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            LogPathField.SetValue(null, LogPath);
            RotatedPathField.SetValue(null, RotatedPath);
            SizeField.SetValue(null, -1L);
        }

        [TearDown]
        public void RestoreTheRealPaths()
        {
            LogPathField.SetValue(null, originalLogPath);
            RotatedPathField.SetValue(null, originalRotatedPath);
            SizeField.SetValue(null, -1L);
            try { Directory.Delete(directory, true); } catch { }
        }

        private static void Warn(string module, string message, Exception ex = null)
        {
            ErrorLogType.GetMethod("Warn", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new object[] { module, message, ex });
        }

        private static void Error(string module, string message, Exception ex = null)
        {
            ErrorLogType.GetMethod("Error", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new object[] { module, message, ex });
        }

        private string ReadLog()
        {
            return File.ReadAllText(LogPath, Encoding.UTF8);
        }

        private static string[] Lines(string text)
        {
            return text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        [Test]
        public void WritesOneFormattedLinePerEntry()
        {
            Warn("TestModule", "something happened");

            string[] lines = Lines(ReadLog());
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(lines[0], Does.Contain(" | WARN | TestModule | something happened"));
            Assert.That(lines[0], Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2} \| "));
            Assert.That(lines[0], Does.Contain(" | tid "));
        }

        [Test]
        public void ErrorEntryCarriesTheExceptionTypeMessageAndFrames()
        {
            Error("TestModule", "handler failed", BuildFailure());

            string line = Lines(ReadLog())[0];
            Assert.That(line, Does.Contain(" | ERROR | TestModule | handler failed | "));
            Assert.That(line, Does.Contain("System.InvalidOperationException: boom"));
            Assert.That(line, Does.Contain(" <- "), "the stack should be inlined with ' <- ' separators");
        }

        /// <summary>
        /// Builds an exception whose stack has at least two frames, so the log's
        /// frame joining (first four frames, " &lt;- " separator) is observable.
        /// </summary>
        private static InvalidOperationException BuildFailure()
        {
            try
            {
                ThrowInnerFailure();
                return null;
            }
            catch (InvalidOperationException ex)
            {
                return ex;
            }
        }

        private static void ThrowInnerFailure()
        {
            throw new InvalidOperationException("boom");
        }

        [Test]
        public void CarriageReturnsAndNewlinesAreFoldedIntoOneLine()
        {
            Warn("TestModule", "first\r\nsecond\nthird");

            string[] lines = Lines(ReadLog());
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(lines[0], Does.Contain("first second third"));
        }

        [Test]
        public void ALongMessageIsCappedAtFourKiB()
        {
            Warn("TestModule", new string('a', 6000));

            string[] lines = Lines(ReadLog());
            Assert.That(lines, Has.Length.EqualTo(1));
            // The cap is per line; the file layer adds CRLF after the capped line.
            Assert.That(Encoding.UTF8.GetByteCount(lines[0]), Is.LessThanOrEqualTo(4096));
            Assert.That(lines[0], Does.EndWith("..."));
        }

        [Test]
        public void LiveLogOverOneMiBIsRotatedBeforeAppending()
        {
            const int preseeded = 1024 * 1024 - 10;
            File.WriteAllBytes(LogPath, new byte[preseeded]);

            Warn("TestModule", "after rotation");

            Assert.That(File.Exists(RotatedPath), Is.True, "the old file becomes apkshellext-error.1.log");
            Assert.That(new FileInfo(RotatedPath).Length, Is.EqualTo(preseeded));
            Assert.That(Lines(ReadLog()).Single(), Does.Contain("after rotation"));
        }

        [Test]
        public void ConcurrentWritersNeverInterleaveALine()
        {
            const int threads = 8;
            const int perThread = 25;
            List<Thread> workers = new List<Thread>();
            for (int t = 0; t < threads; t++)
            {
                int id = t;
                Thread worker = new Thread(() =>
                {
                    for (int i = 0; i < perThread; i++) Warn("Concurrent", "msg-" + id + "-" + i);
                });
                workers.Add(worker);
                worker.Start();
            }
            foreach (Thread worker in workers) worker.Join();

            string[] lines = Lines(ReadLog());
            Assert.That(lines, Has.Length.EqualTo(threads * perThread));
            foreach (string line in lines)
            {
                Assert.That(line, Does.Match(@" \| WARN \| Concurrent \| msg-\d+-\d+$"), "interleaved line: " + line);
            }
        }

        [Test]
        public void AnUnwritablePathIsSwallowed()
        {
            // A file where the directory should be makes both the append and the
            // size probe fail; the logging contract is that the shell never sees it.
            string blocker = Path.Combine(directory, "blocker");
            File.WriteAllText(blocker, "x");
            LogPathField.SetValue(null, Path.Combine(blocker, "apkshellext-error.log"));

            Assert.That(() => Warn("TestModule", "lost entry"), Throws.Nothing);
        }

        [Test]
        [Timeout(15000)]
        public void AHeldCrossProcessMutexDelaysTheWriteButNeverHangsIt()
        {
            string name = @"Local\ApkShellext2.ErrorLog.Write";
            using (Mutex held = new Mutex(false, name))
            {
                Assert.That(held.WaitOne(TimeSpan.FromSeconds(5)), Is.True);
                try
                {
                    // The named mutex is recursive per thread: Warn has to run on a
                    // different thread or its own WaitOne would succeed immediately.
                    long elapsed = 0;
                    Exception failure = null;
                    Thread worker = new Thread(() =>
                    {
                        try
                        {
                            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                            Warn("TestModule", "while the mutex was held");
                            watch.Stop();
                            elapsed = watch.ElapsedMilliseconds;
                        }
                        catch (Exception ex)
                        {
                            failure = ex;
                        }
                    });
                    worker.Start();
                    Assert.That(worker.Join(TimeSpan.FromSeconds(10)), Is.True, "logging must not hang on the mutex");

                    Assert.That(failure, Is.Null);
                    // WaitCrossProcessLock gives up after 2 s and the entry is still
                    // written unprotected: a lost line is acceptable, a hang is not.
                    Assert.That(elapsed, Is.GreaterThanOrEqualTo(1500));
                    Assert.That(ReadLog(), Does.Contain("while the mutex was held"));
                }
                finally
                {
                    held.ReleaseMutex();
                }
            }
        }
        [Test]
        public void TheHooksAreInstalledOnceAndStayIdempotent()
        {
            FieldInfo installed = ErrorLogType.GetField("_hooksInstalled", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo ensure = ErrorLogType.GetMethod("EnsureHooks", BindingFlags.Static | BindingFlags.Public);

            // Touching any member runs the static constructor, which installs the hooks.
            Assert.That((int)installed.GetValue(null), Is.EqualTo(1));
            ensure.Invoke(null, null);
            ensure.Invoke(null, null);
            Assert.That((int)installed.GetValue(null), Is.EqualTo(1), "the install must be idempotent, not counted");
        }

        [Test]
        public void TheUnhandledExceptionHookWritesTheFailingException()
        {
            MethodInfo hook = ErrorLogType.GetMethod("OnUnhandledException", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(() => hook.Invoke(null, new object[]
            {
                null,
                new UnhandledExceptionEventArgs(new InvalidOperationException("hook boom"), isTerminating: false)
            }), Throws.Nothing);

            string line = Lines(ReadLog())[0];
            Assert.That(line, Does.Contain(" | ERROR | UnhandledException | "));
            Assert.That(line, Does.Contain("System.InvalidOperationException: hook boom"));
        }

        [Test]
        public void TheUnobservedTaskExceptionHookMarksTheExceptionObserved()
        {
            // The event args constructor is internal in .NET Framework, so build an
            // uninitialized instance and fill the exception field it reads.
            Type argsType = typeof(System.Threading.Tasks.TaskScheduler).Assembly
                .GetType("System.Threading.Tasks.UnobservedTaskExceptionEventArgs", true);
            object args = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(argsType);
            argsType.GetField("m_exception", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(args, new AggregateException(new InvalidOperationException("task boom")));

            MethodInfo hook = ErrorLogType.GetMethod("OnUnobservedTaskException", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(() => hook.Invoke(null, new object[] { null, args }), Throws.Nothing);

            Assert.That(argsType.GetProperty("Observed").GetValue(args, null), Is.EqualTo(true));
            Assert.That(ReadLog(), Does.Contain("UnobservedTaskException"));
        }
    }
}
