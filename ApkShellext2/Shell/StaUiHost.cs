using System;
using System.Collections.Generic;
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
        /// Shows the XML dump prompt on the UI thread and returns the text the user entered
        /// (empty when the prompt is dismissed).
        /// </summary>
        public static string Prompt(string prompt, string title) {
            return Send(() => Interaction.InputBox(prompt, title));
        }

        /// <summary>
        /// Opens the Preferences form on the UI thread, reusing the single modeless instance
        /// that is already open.
        /// </summary>
        public static void ShowPreferences(IReadOnlyList<string> selectedPaths) {
            Send(() => ShowPreferencesCore(selectedPaths));
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
