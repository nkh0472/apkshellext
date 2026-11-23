using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ApkQuickReader;
using ApkShellext2.Properties;

namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// Builds the context menu model for a selection. This is the single place that owns the
    /// menu's texts, visibility rules, verbs and structure; both UI layers render what it
    /// returns and neither re-implements any of these decisions.
    /// </summary>
    public static class CommandCatalog {
        /// <summary>
        /// Describes the menu for the selected paths. The current UI culture is applied first,
        /// so the returned texts match the user's language preference.
        /// </summary>
        public static CommandModel Build(IEnumerable<string> selectedPaths) {
            if (selectedPaths == null) throw new ArgumentNullException("selectedPaths");

            Utility.Localize();

            var paths = selectedPaths.ToArray();
            var root = new CommandItem(RootText()) {
                Icon = Utility.GetSetting("ShowMenuIcon", "True") == "True"
                    ? CommandIcon.PackageThumbnails
                    : CommandIcon.Logo
            };

            bool hasapk = false, hasipa = false, hasappx = false, hasappxbundle = false;
            foreach (var p in paths) {
                if (p.EndsWith(AppPackageReader.extAPK)) hasapk = true;
                if (p.EndsWith(AppPackageReader.extIPA)) hasipa = true;
                if (p.EndsWith(AppPackageReader.extAPPX)) hasappx = true;
                if (p.EndsWith(AppPackageReader.extAPPXBUNDLE)) hasappxbundle = true;
            }

            bool singleSelected = paths.Length == 1;
            string appname = "";
            string singleNewName = "";
            if (singleSelected) {
                // one parse feeds both the app name and the rename preview
                using (IPackageReader reader = PackageReaderFactory.Create(paths[0])) {
                    appname = reader.AppName;
                    singleNewName = CommandRename.GetNewFileName(paths[0], paths, reader);
                }
            }

            var children = new List<CommandItem>();

            // Rename
            var rename = new CommandItem("") { Icon = CommandIcon.Rename, Action = CommandAction.Rename };
            if (singleSelected) {
                if (singleNewName != "") {
                    rename.Text = string.Format(Resources.menuRenameAs, Path.GetFileName(singleNewName));
                } else {
                    rename.Text = Resources.strReadFileFailed;
                    rename.Enabled = false;
                }
            } else {
                string renamePattern = Utility.GetSetting("RenamePattern", NonLocalizeResources.strRenamePatternDefault);
                rename.Text = string.Format(Resources.menuRenameAs, renamePattern);
            }
            rename.Verb = CommandVerb.Rename;
            children.Add(rename);

            // Dump submenu (Android packages only)
            if (hasapk) {
                var dump = new CommandItem(Resources.menuDump) { Icon = CommandIcon.DumpOutput };
                dump.Children = new[] {
                    Leaf(Resources.menuDumpManifest, CommandAction.DumpManifest),
                    Leaf(Resources.menuDumpOthers, CommandAction.DumpXml)
                };
                children.Add(dump);
            }

            children.Add(Separator());

            string storeAppName = singleSelected ? appname : AppPackageReader.extAPK;
            if (hasapk) {
                var store = new CommandItem(Resources.strSearchStore) { Icon = CommandIcon.GooglePlay };
                var storeChildren = new List<CommandItem>();
                bool storeEnabled = singleSelected || (Utility.GetSetting("ShowAppStoreWhenMultiSelected", "True") == "True");
                if (Utility.GetSetting("ShowGooglePlay", "True") == "True") {
                    storeChildren.Add(Leaf(string.Format(Resources.menuGotoGooglePlay, storeAppName),
                        CommandAction.GooglePlay, CommandIcon.GooglePlay, storeEnabled));
                }
                if (Utility.GetSetting("ShowAmazonStore", "True") == "True") {
                    storeChildren.Add(Leaf(string.Format(Resources.menuGotoAmazonAppStore, storeAppName),
                        CommandAction.AmazonAppStore, CommandIcon.AmazonStore, storeEnabled));
                }
                if (Utility.GetSetting("ShowApkMirror", "False") == "True") {
                    storeChildren.Add(Leaf(string.Format(Resources.menuGotoApkMirror, storeAppName),
                        CommandAction.ApkMirror, CommandIcon.ApkMirror, storeEnabled));
                }
                if (storeChildren.Count > 0) {
                    store.Children = storeChildren.ToArray();
                    children.Add(store);
                }
            }

            if (hasipa && Utility.GetSetting("ShowAppleStore", "True") == "True") {
                string appleAppName = singleSelected ? appname : AppPackageReader.extIPA;
                children.Add(Leaf(string.Format(Resources.menuGotoAppleAppStore, appleAppName),
                    CommandAction.AppleAppStore, CommandIcon.AppleStore,
                    singleSelected || Utility.GetSetting("ShowAppStoreWhenMultiSelected", "True") == "True"));
            }

            if ((hasappx || hasappxbundle) && Utility.GetSetting("ShowMSStore", "True") == "True") {
                string msAppName = singleSelected ? appname :
                    (AppPackageReader.extAPPX + @"/" + AppPackageReader.extAPPXBUNDLE);
                children.Add(Leaf(string.Format(Resources.menuGotoMicrosoftStore, msAppName),
                    CommandAction.MicrosoftStore, CommandIcon.MicrosoftStore,
                    singleSelected || Utility.GetSetting("ShowAppStoreWhenMultiSelected", "True") == "True"));
            }

            if (children.Count > 1) {
                children.Add(Separator());
            }
            children.Add(Leaf(Resources.menuPreferences, CommandAction.Preferences, CommandIcon.Logo, true));

            if (children.Count > CommandIds.MaxItems) {
                throw new InvalidOperationException("The context menu exceeds " + CommandIds.MaxItems + " items.");
            }

            root.Children = children.ToArray();
            return new CommandModel(root, paths);
        }

        private static string RootText() {
            string newVerAvai = "";
            if (Utility.NewVersionAvailible() && Utility.GetSetting("ShowNewVersion", "True") == "True")
                newVerAvai = " (" + string.Format(Resources.strNewVersionAvailible, Utility.GetSetting("LatestVersion")) + ")";
            return Resources.menuMain + newVerAvai;
        }

        private static CommandItem Leaf(string text, CommandAction action) {
            return Leaf(text, action, CommandIcon.None, true);
        }

        private static CommandItem Leaf(string text, CommandAction action, CommandIcon icon, bool enabled) {
            return new CommandItem(text) {
                Action = action,
                Icon = icon,
                Enabled = enabled,
                Verb = CommandVerb.For(action)
            };
        }

        private static CommandItem Separator() {
            return new CommandItem("") { IsSeparator = true };
        }
    }
}
