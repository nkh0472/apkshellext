using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ApkShellext2.Diagnostics;
using ApkShellext2.Properties;
using Microsoft.VisualBasic;

namespace ApkShellext2.Shell {
    /// <summary>
    /// A single lazily started STA thread with its own message loop, used only for the menu
    /// actions that need a window (the XML dump prompt and the Preferences form). Nothing
    /// touches WinForms until one of those actions runs, so rendering the menu never loads it;
    /// the prompt and the form run on this thread, which keeps them off the shell's thread.
    /// </summary>
    public static class StaUiHost {
        private static readonly object Gate = new object();
        private static readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);

        private static Thread thread;
        private static Control marshal;
        private static ApartmentState apartment = ApartmentState.Unknown;
        private static Preferences settingFormInstance;

        /// <summary>True on the dedicated UI thread.</summary>
        public static bool IsPumpThread {
            get { return thread != null && Thread.CurrentThread == thread; }
        }

        /// <summary>The apartment of the UI thread (STA once it exists).</summary>
        public static ApartmentState PumpApartment {
            get {
                EnsureStarted();
                return apartment;
            }
        }

        /// <summary>
        /// Shows the XML entry prompt on the UI thread and returns the text the user entered
        /// (empty when the prompt is dismissed). <paramref name="defaultEntry"/> prefills the
        /// input box, so the common answer is accepted with one click.
        /// </summary>
        public static string Prompt(string prompt, string defaultEntry) {
            return Send(() => Interaction.InputBox(prompt, Resources.strXmlEntryTitle, defaultEntry ?? ""));
        }

        /// <summary>
        /// Shows the modern folder picker on the UI thread and returns the chosen folder, or
        /// null when the user cancels.
        /// </summary>
        public static string PickFolder() {
            return Send(() => FolderPicker.Pick(Resources.strPickFolderTitle));
        }

        /// <summary>
        /// Opens the Preferences form on the UI thread, reusing the single modeless instance
        /// that is already open.
        /// </summary>
        public static void ShowPreferences(IReadOnlyList<string> selectedPaths) {
            Send(() => ShowPreferencesCore(selectedPaths));
        }

        /// <summary>
        /// Puts text on the system clipboard. The clipboard belongs to the calling thread and
        /// only an STA thread can own it, so the write runs on this thread; a clipboard the
        /// shell or another process holds surfaces as an exception the caller logs.
        /// </summary>
        public static void SetClipboardText(string text) {
            if (string.IsNullOrEmpty(text)) return;
            Send(() => Clipboard.SetText(text));
        }

        /// <summary>
        /// Asks for a destination with the save dialog, then writes the selected files' package
        /// information on a background thread while a modeless progress window stays on this
        /// thread. Failures are logged and reported with a message box instead of escaping to
        /// the shell.
        /// </summary>
        public static void ExportCsv(IReadOnlyList<string> selectedPaths) {
            if (selectedPaths == null || selectedPaths.Count == 0) return;
            var paths = new List<string>(selectedPaths);

            string target = Send(() => AskExportPath(paths));
            if (string.IsNullOrEmpty(target)) return;

            var cancellation = new CancellationTokenSource();
            CsvExportProgressForm progress = null;
            try {
                progress = Send(() => ShowExportProgress(paths.Count, cancellation));
            } catch (Exception ex) {
                ErrorLog.Warn(typeof(StaUiHost).Name, "Could not show the CSV export progress window", ex);
            }

            CsvExportProgressForm window = progress;
            ThreadPool.QueueUserWorkItem(state => WriteCsvFile(paths, target, cancellation, window));
        }

        private static string AskExportPath(IReadOnlyList<string> paths) {
            using (var dialog = new SaveFileDialog()) {
                dialog.Title = Resources.strExportCsvTitle;
                dialog.FileName = PackageCsvWriter.DefaultFileName;
                dialog.DefaultExt = "csv";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*";
                try {
                    string folder = Path.GetDirectoryName(Path.GetFullPath(paths[0]));
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) {
                        dialog.InitialDirectory = folder;
                    }
                } catch (Exception) {
                    // an unusable first path leaves the dialog at its default folder
                }
                return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private static CsvExportProgressForm ShowExportProgress(int total, CancellationTokenSource cancellation) {
            var form = new CsvExportProgressForm(total, cancellation);
            form.Show();
            return form;
        }

        private static void WriteCsvFile(
            IReadOnlyList<string> paths,
            string target,
            CancellationTokenSource cancellation,
            CsvExportProgressForm progress) {
            string temp = null;
            try {
                // The export goes to a temporary file in the destination folder and replaces
                // the destination only after every row has been written, so a failure never
                // leaves a half-written CSV under the name the user chose.
                string folder = Path.GetDirectoryName(target);
                temp = Path.Combine(folder,
                    Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");

                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    PackageCsvWriter.Write(stream, paths, null, cancellation.Token,
                        (done, total) => ReportProgress(progress, done, total));
                }

                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                temp = null;
            } catch (Exception ex) {
                ErrorLog.Error(typeof(StaUiHost).Name, "Could not export the package list to " + target, ex);
                ShowExportFailed();
            } finally {
                if (temp != null) {
                    try {
                        File.Delete(temp);
                    } catch (Exception) {
                        // the temporary file is already best-effort cleanup
                    }
                }
                CloseProgress(progress);
            }
        }

        private static void ReportProgress(CsvExportProgressForm form, int done, int total) {
            if (form == null || form.IsDisposed) return;
            try {
                form.BeginInvoke((Action)(() => form.SetProgress(done, total)));
            } catch (ObjectDisposedException) {
                // the window is gone
            } catch (InvalidOperationException) {
                // the progress window was closed while the export was still running
            }
        }

        private static void CloseProgress(CsvExportProgressForm form) {
            if (form == null || form.IsDisposed) return;
            try {
                Send(() => {
                    if (!form.IsDisposed) form.Close();
                });
            } catch (Exception ex) {
                ErrorLog.Warn(typeof(StaUiHost).Name, "Could not close the CSV export progress window", ex);
            }
        }

        private static void ShowExportFailed() {
            try {
                Send(() => MessageBox.Show(Resources.strExportCsvFailed, Resources.strExportCsvTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error));
            } catch (Exception ex) {
                ErrorLog.Warn(typeof(StaUiHost).Name, "Could not show the CSV export error", ex);
            }
        }

        public static void Send(Action action) {
            if (action == null) throw new ArgumentNullException("action");
            Send(() => {
                action();
                return true;
            });
        }

        public static T Send<T>(Func<T> func) {
            if (func == null) throw new ArgumentNullException("func");
            if (IsPumpThread) return func();

            EnsureStarted();
            Control target = marshal;
            if (target == null) throw new InvalidOperationException("The menu UI thread is not available.");

            T result = default(T);
            Exception error = null;
            var done = new ManualResetEventSlim(false);
            target.BeginInvoke((Action)(() => {
                try {
                    result = func();
                } catch (Exception ex) {
                    error = ex;
                } finally {
                    done.Set();
                }
            }));
            done.Wait();
            if (error != null) throw new InvalidOperationException("The menu UI thread failed.", error);
            return result;
        }

        private static void EnsureStarted() {
            if (Ready.IsSet) return;
            lock (Gate) {
                if (Ready.IsSet) return;
                var t = new Thread(Pump) {
                    IsBackground = true,
                    Name = "ApkShellext2.UI"
                };
                t.SetApartmentState(ApartmentState.STA);
                thread = t;
                t.Start();
            }
            Ready.Wait();
        }

        private static void Pump() {
            try {
                var control = new Control();
                control.CreateControl();
                apartment = Thread.CurrentThread.GetApartmentState();
                marshal = control;
            } catch (Exception ex) {
                ErrorLog.Warn(typeof(StaUiHost).Name, "Could not start the menu UI thread", ex);
            } finally {
                Ready.Set();
            }
            if (marshal != null) {
                Application.Run();
            }
        }

        private static void ShowPreferencesCore(IReadOnlyList<string> selectedPaths) {
            if (settingFormInstance == null || settingFormInstance.IsDisposed) {
                settingFormInstance = new Preferences();
                settingFormInstance.FormClosed += (s, e) => settingFormInstance = null;
                settingFormInstance.currentFile = selectedPaths[0];
                settingFormInstance.Show();
            } else {
                settingFormInstance.currentFile = selectedPaths[0];
                if (settingFormInstance.WindowState == FormWindowState.Minimized)
                    settingFormInstance.WindowState = FormWindowState.Normal;
                settingFormInstance.Activate();
                settingFormInstance.BringToFront();
            }
        }
    }
}
