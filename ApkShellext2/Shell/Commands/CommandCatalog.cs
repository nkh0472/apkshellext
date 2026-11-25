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
            var root = new CommandItem(RootTitle()) {
                Icon = Utility.GetSetting("ShowMenuIcon", "True") == "True"
                    ? CommandIcon.PackageThumbnails
                    : CommandIcon.Logo
            };

            bool hasapk = false, hasipa = false, hasappx = false, hasappxbundle = false;
            bool canExport = false;
            foreach (var p in paths) {
                if (p.EndsWith(AppPackageReader.extAPK)) hasapk = true;
                if (p.EndsWith(AppPackageReader.extIPA)) hasipa = true;
                if (p.EndsWith(AppPackageReader.extAPPX)) hasappx = true;
                if (p.EndsWith(AppPackageReader.extAPPXBUNDLE)) hasappxbundle = true;
                if (!canExport && IsSupportedPackage(p)) canExport = true;
            }

            bool singleSelected = paths.Length == 1;
            string appname = "";
            string singleNewName = "";
            PackageClipboardTextSet copyValues;
            if (singleSelected) {
                // one parse feeds the app name, the rename preview and the copy values
                using (IPackageReader reader = PackageReaderFactory.Create(paths[0])) {
                    appname = reader.AppName;
                    singleNewName = CommandRename.GetNewFileName(paths[0], paths, reader);
                    copyValues = PackageClipboardText.Join(new[] {
                        PackageClipboardText.ReadEntry(paths[0], reader)
                    });
                }
            } else {
                // A multi-file selection can hold hundreds of packages and the shell waits for
                // this method before it can draw the menu, so the values are not read here. The
                // selected types decide which items are offered; the command reads the files
                // once the user picks one, which keeps the menu instant and the text exact.
                copyValues = PackageClipboardText.JoinPaths(paths, canExport);
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

            // Export: available when at least one selected file has a package type the
            // reader factory accepts, so a mixed selection still exports its readable files.
            if (canExport) {
                children.Add(Leaf(Resources.menuExportCsv, CommandAction.Export));
            }

            var copy = CopySubmenu(copyValues);
            if (copy != null) children.Add(copy);

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
                // Hashing happens after the click, so this entry never slows the menu down.
                if (canExport) {
                    storeChildren.Add(Leaf(Resources.menuCheckVirusTotal, CommandAction.OpenVirusTotal,
                        CommandIcon.ApkMirror, true));
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

        /// <summary>
        /// The label of the whole menu. The classic handler draws it as the submenu title and the
        /// modern verb returns it from IExplorerCommand.GetTitle, so both menus read the same
        /// resource and the same optional new-version suffix. The language setting is applied here
        /// because GetTitle reaches this method without building the menu first.
        /// </summary>
        internal static string RootTitle() {
            Utility.Localize();

            string newVerAvai = "";
            if (Utility.NewVersionAvailible() && Utility.GetSetting("ShowNewVersion", "True") == "True")
                newVerAvai = " (" + string.Format(Resources.strNewVersionAvailible, Utility.GetSetting("LatestVersion")) + ")";
            return Resources.menuMain + newVerAvai;
        }

        /// <summary>
        /// True when at least one of the selected paths is a package the extension handles. This
        /// is what decides whether the menu is offered at all, so the rule lives with the menu
        /// model instead of in a shell adapter.
        /// </summary>
        internal static bool IsSupportedSelection(IEnumerable<string> selectedPaths) {
            if (selectedPaths == null) return false;
            foreach (string path in selectedPaths) {
                if (!string.IsNullOrEmpty(path) && IsSupportedPackage(path)) return true;
            }
            return false;
        }

        /// <summary>
        /// The copy submenu. It exists only when at least one value is available for the whole
        /// selection, and every item is offered only when the selection can fill it, so an
        /// unsupported or unreadable selection still gets the path it was selected by.
        /// </summary>
        private static CommandItem CopySubmenu(PackageClipboardTextSet values) {
            var items = new List<CommandItem>();
            foreach (PackageClipboardText.Field field in PackageClipboardText.Fields) {
                if (values.Has(field)) items.Add(Leaf(CopyText(field), CopyAction(field)));
            }
            if (items.Count == 0) return null;

            var copy = new CommandItem(Resources.menuCopyInfo);
            copy.Children = items.ToArray();
            return copy;
        }

        private static string CopyText(PackageClipboardText.Field field) {
            switch (field) {
                case PackageClipboardText.Field.FilePath: return Resources.menuCopyFilePath;
                case PackageClipboardText.Field.AppName: return Resources.menuCopyAppName;
                case PackageClipboardText.Field.PackageName: return Resources.menuCopyPackage;
                case PackageClipboardText.Field.Version: return Resources.menuCopyVersion;
                default: return string.Empty;
            }
        }

        private static CommandAction CopyAction(PackageClipboardText.Field field) {
            switch (field) {
                case PackageClipboardText.Field.FilePath: return CommandAction.CopyFilePath;
                case PackageClipboardText.Field.AppName: return CommandAction.CopyAppName;
                case PackageClipboardText.Field.PackageName: return CommandAction.CopyPackage;
                case PackageClipboardText.Field.Version: return CommandAction.CopyVersion;
                default: return CommandAction.None;
            }
        }

        private static CommandItem Leaf(string text, CommandAction action) {
            return Leaf(text, action, CommandIcon.None, true);
        }

        internal static bool IsSupportedPackage(string path) {
            try {
                PackageReaderFactory.GetAppType(path);
                return true;
            } catch (NotSupportedException) {
                return false;
            } catch (ArgumentException) {
                return false;
            }
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
