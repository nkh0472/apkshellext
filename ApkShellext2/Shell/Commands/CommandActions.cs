using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using ApkQuickReader;
using ApkShellext2.Diagnostics;
using ApkShellext2.Properties;

namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// Executes a <see cref="CommandAction"/> against a selection. Everything that does not
    /// need a window runs directly on the calling thread; the two windowed actions (the XML
    /// dump prompt and the Preferences form) are handed to <see cref="StaUiHost"/>, so the
    /// menu itself never has to touch WinForms.
    /// </summary>
    public static class CommandActions {
        // Utility.Log labels a line with the caller's type name. The menu's informational
        // lines carry the <ApkContextMenu> label today, so the model logs through a marker of
        // that name instead of introducing a second label for the same menu.
        private sealed class ApkContextMenu { }

        private static readonly ApkContextMenu LogCaller = new ApkContextMenu();

        private static Action<IReadOnlyList<string>> preferencesHandler;
        private static Action<IReadOnlyList<string>> exportHandler;
        private static Func<string, string, string> promptHandler;
        private static Action<string> clipboardHandler;

        /// <summary>Opens the Preferences form; replaceable so tests can observe the routing.</summary>
        public static Action<IReadOnlyList<string>> PreferencesHandler {
            get { return preferencesHandler ?? (preferencesHandler = StaUiHost.ShowPreferences); }
            set { preferencesHandler = value; }
        }

        /// <summary>Exports the selection to CSV; replaceable so tests never show a dialog.</summary>
        public static Action<IReadOnlyList<string>> ExportHandler {
            get { return exportHandler ?? (exportHandler = StaUiHost.ExportCsv); }
            set { exportHandler = value; }
        }

        /// <summary>Asks the user for the XML file name; replaceable so tests never show a dialog.</summary>
        public static Func<string, string, string> PromptHandler {
            get { return promptHandler ?? (promptHandler = StaUiHost.Prompt); }
            set { promptHandler = value; }
        }

        /// <summary>Writes text to the system clipboard; replaceable so tests never need one.</summary>
        public static Action<string> ClipboardHandler {
            get { return clipboardHandler ?? (clipboardHandler = StaUiHost.SetClipboardText); }
            set { clipboardHandler = value; }
        }

        public static void Execute(CommandAction action, IReadOnlyList<string> selectedPaths) {
            var paths = selectedPaths ?? new string[0];
            switch (action) {
                case CommandAction.Rename:
                    renameWithVersion(paths);
                    break;
                case CommandAction.Export:
                    try {
                        ExportHandler(paths);
                    } catch (Exception ex) {
                        ErrorLog.Error(LogCaller.GetType().Name, "Error happens during CSV export", ex);
                    }
                    break;
                case CommandAction.DumpManifest:
                    foreach (var path in paths) dumpXML(path, "androidmanifest.xml", paths);
                    break;
                case CommandAction.DumpXml: {
                        string xml = PromptHandler(Resources.strDumpXMLPrompt, Resources.strDumpXML);
                        foreach (var path in paths) dumpXML(path, xml, paths);
                    }
                    break;
                case CommandAction.GooglePlay:
                    gotoGooglePlay(paths);
                    break;
                case CommandAction.AmazonAppStore:
                    gotoAmazonAppStore(paths);
                    break;
                case CommandAction.ApkMirror:
                    gotoApkMirror(paths);
                    break;
                case CommandAction.AppleAppStore:
                    gotoAppleStore(paths);
                    break;
                case CommandAction.MicrosoftStore:
                    gotoMicrosoftStore(paths);
                    break;
                case CommandAction.Preferences:
                    try {
                        PreferencesHandler(paths);
                    } catch (Exception ex) {
                        ErrorLog.Warn(LogCaller.GetType().Name, "Error happens during showSettings", ex);
                    }
                    break;
                case CommandAction.CopyFilePath:
                    copyToClipboard(PackageClipboardText.Field.FilePath, paths);
                    break;
                case CommandAction.CopyAppName:
                    copyToClipboard(PackageClipboardText.Field.AppName, paths);
                    break;
                case CommandAction.CopyPackage:
                    copyToClipboard(PackageClipboardText.Field.PackageName, paths);
                    break;
                case CommandAction.CopyVersion:
                    copyToClipboard(PackageClipboardText.Field.Version, paths);
                    break;
            }
        }

        /// <summary>
        /// Puts one value of the selection on the clipboard. A selection where every file
        /// lacks the value writes nothing, and a reader or clipboard failure is logged instead
        /// of escaping into the shell. A multi-file selection is read in the background so the
        /// invocation returns at once; the clipboard write still happens where it is allowed.
        /// </summary>
        private static void copyToClipboard(PackageClipboardText.Field field, IReadOnlyList<string> selectedPaths) {
            if (selectedPaths.Count > 1) {
                string[] paths = new string[selectedPaths.Count];
                for (int i = 0; i < paths.Length; i++) paths[i] = selectedPaths[i];
                Task.Run(() => writeClipboard(field, paths));
                return;
            }
            writeClipboard(field, selectedPaths);
        }

        private static void writeClipboard(PackageClipboardText.Field field, IReadOnlyList<string> selectedPaths) {
            try {
                string text = PackageClipboardText.Read(field, selectedPaths);
                if (text.Length == 0) return;
                ClipboardHandler(text);
            } catch (Exception ex) {
                ErrorLog.Warn(LogCaller.GetType().Name, "Error happens during copying " + field, ex);
            }
        }

        private static void dumpXML(string apk, string xml, IReadOnlyList<string> selectedPaths) {
            using (ApkReader reader = PackageReaderFactory.Create(apk) as ApkReader) {
                if (reader == null) {
                    // mixed selection contained a non-apk (ipa/appx):
                    // dump only works on android binary xml, skip quietly
                    Log(selectedPaths, "Not an apk, skipping dump: " + apk);
                    return;
                }
                try {
                    XmlDocument doc = reader.ExtractCompressedXml(xml);
                    string filepath = apk + "." + Path.GetFileName(Regex.Replace(xml, @"[\\/:*?""<>|-]+", "_")); // attribute name, kept next to the apk
                    XmlWriterSettings setting = new XmlWriterSettings();
                    setting.Indent = true;
                    XmlWriter writer = XmlWriter.Create(filepath, setting);
                    doc.WriteTo(writer);
                    writer.Close();
                } catch (Exception ex) {
                    ErrorLog.Warn(LogCaller.GetType().Name, "Error happens during extracting " + xml + " in " + apk, ex);
                }
            }
        }

        private static void renameWithVersion(IReadOnlyList<string> selectedPaths) {
            foreach (var path in selectedPaths) {
                try {
                    string newFilename = CommandRename.GetNewFileName(path, selectedPaths);
                    File.Move(path, newFilename);
                } catch (Exception e) {
                    ErrorLog.Warn(LogCaller.GetType().Name, "Exception happens during rename: " + path, e);
                }
            }
        }

        private static void gotoGooglePlay(IReadOnlyList<string> selectedPaths) {
            foreach (var p in selectedPaths) {
                using (ApkReader reader = new ApkReader(p)) {
                    string package = reader.PackageName;
                    Process.Start(string.Format(Properties.NonLocalizeResources.urlGooglePlay, package));
                }
            }
        }

        private static void gotoAmazonAppStore(IReadOnlyList<string> selectedPaths) {
            foreach (var p in selectedPaths) {
                using (ApkReader reader = new ApkReader(p)) {
                    string package = reader.PackageName;
                    Process.Start(string.Format(Properties.NonLocalizeResources.urlAmazonAppStore, package));
                }
            }
        }

        private static void gotoApkMirror(IReadOnlyList<string> selectedPaths) {
            foreach (var p in selectedPaths) {
                using (ApkReader reader = new ApkReader(p)) {
                    string package = reader.PackageName;
                    Process.Start(string.Format(Properties.NonLocalizeResources.urlApkMirror, reader.Publisher, package));
                }
            }
        }

        private static void gotoAppleStore(IReadOnlyList<string> selectedPaths) {
            foreach (var p in selectedPaths) {
                if (p.EndsWith(AppPackageReader.extIPA)) {
                    using (IpaReader reader = PackageReaderFactory.Create(p) as IpaReader) {
                        try {
                            Process.Start(string.Format(Properties.NonLocalizeResources.urlAppleStore, reader.AppID));
                        } catch (Exception ex) {
                            Log(selectedPaths, Path.GetFileName(p) + ex.Message + Environment.NewLine + "Cannot get appid.");
                        }
                    }
                }
            }
        }

        private static void gotoMicrosoftStore(IReadOnlyList<string> selectedPaths) {
            foreach (var p in selectedPaths) {
                if (p.EndsWith(AppPackageReader.extAPPX) || p.EndsWith(AppPackageReader.extAPPXBUNDLE)) {
                    using (IPackageReader reader = PackageReaderFactory.Create(p)) {
                        Process.Start(string.Format(Properties.NonLocalizeResources.urlMicrosoftStore, reader.AppID));
                    }
                }
            }
        }

        private static void Log(IReadOnlyList<string> selectedPaths, string message) {
            string title = (selectedPaths.Count > 0) ? Path.GetFileName(selectedPaths[0]) : "";
            Utility.Log(LogCaller, title, message);
        }
    }
}
