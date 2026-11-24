using System;
using System.Collections.Generic;
using System.IO;

namespace ApkShellext2 {
    /// <summary>
    /// Builds the text the copy commands put on the clipboard: one line per selected file,
    /// CRLF between lines and no trailing line break. The value rules live here so the menu
    /// model and the clipboard writer share one definition, and the class needs nothing but a
    /// reader, which keeps it in the parsing layer.
    /// </summary>
    public static class PackageClipboardText {
        /// <summary>The values a copy command can offer, in the order the submenu lists them.</summary>
        public enum Field {
            FilePath,
            AppName,
            PackageName,
            Version
        }

        /// <summary>The values in menu order.</summary>
        public static readonly Field[] Fields = {
            Field.FilePath, Field.AppName, Field.PackageName, Field.Version
        };

        public const string LineBreak = "\r\n";

        /// <summary>
        /// Reads one selected file. A null reader means the file could not be read as a
        /// package (an unsupported type or a failing reader); only its path is available then.
        /// </summary>
        public static PackageClipboardEntry ReadEntry(string path, IPackageReader reader) {
            string appName = "", packageName = "", version = "";
            if (reader != null) {
                appName = reader.AppName ?? "";
                packageName = reader.PackageName ?? "";
                string number = reader.Version ?? "";
                string revision = reader.Revision ?? "";
                // The version line follows the CSV columns: the revision rides in parentheses
                // and a missing version number keeps the line empty, never a lone revision.
                if (number != "") {
                    version = revision != "" ? number + " (" + revision + ")" : number;
                }
            }
            return new PackageClipboardEntry(path, appName, packageName, version);
        }

        /// <summary>Joins the per-file values into the four clipboard texts.</summary>
        public static PackageClipboardTextSet Join(IEnumerable<PackageClipboardEntry> entries) {
            if (entries == null) throw new ArgumentNullException("entries");
            return new PackageClipboardTextSet(entries);
        }

        /// <summary>
        /// The values of a selection the menu has not read yet: every item is offered while only
        /// the paths are known, so building the menu for many files costs nothing. The command
        /// reads the real values after the user picks one.
        /// </summary>
        public static PackageClipboardTextSet JoinPaths(IEnumerable<string> paths) {
            return JoinPaths(paths, true);
        }

        /// <summary>
        /// Same as <see cref="JoinPaths(IEnumerable{string})"/> for a selection where no file has
        /// a package type a reader accepts; only the path question has an answer then.
        /// </summary>
        public static PackageClipboardTextSet JoinPaths(IEnumerable<string> paths, bool packageValuesOffered) {
            if (paths == null) throw new ArgumentNullException("paths");
            var entries = new List<PackageClipboardEntry>();
            foreach (string path in paths) {
                entries.Add(new PackageClipboardEntry(path, "", "", ""));
            }
            return new PackageClipboardTextSet(entries, packageValuesOffered);
        }

        /// <summary>Reads the selection with the production reader factory.</summary>
        public static string Read(Field field, IEnumerable<string> paths) {
            return Read(field, paths, null);
        }

        /// <summary>
        /// Reads the selection and returns the text for one field. A file the factory rejects
        /// or cannot read still contributes its path, and every other field skips it.
        /// </summary>
        public static string Read(Field field, IEnumerable<string> paths, Func<string, IPackageReader> openReader) {
            if (paths == null) throw new ArgumentNullException("paths");
            Func<string, IPackageReader> create = openReader ?? PackageReaderFactory.Create;
            var entries = new List<PackageClipboardEntry>();
            foreach (string path in paths) {
                IPackageReader reader = null;
                try {
                    reader = create(path);
                    entries.Add(ReadEntry(path, reader));
                } catch (Exception) {
                    // an unsupported or unreadable file still answers the path question
                    entries.Add(ReadEntry(path, null));
                } finally {
                    if (reader != null) reader.Dispose();
                }
            }
            return Join(entries).Get(field);
        }

        /// <summary>
        /// The absolute path the CSV writer also puts in its File/Folder columns. A path the
        /// file system refuses stays as it was passed in, so the line still identifies it.
        /// </summary>
        internal static string AbsolutePath(string path) {
            if (string.IsNullOrEmpty(path)) return "";
            try {
                return Path.GetFullPath(path);
            } catch (Exception) {
                return path;
            }
        }
    }

    /// <summary>One selected file's copyable values; an empty string means "not available".</summary>
    public sealed class PackageClipboardEntry {
        internal PackageClipboardEntry(string path, string appName, string packageName, string version) {
            FilePath = PackageClipboardText.AbsolutePath(path);
            AppName = appName ?? "";
            PackageName = packageName ?? "";
            Version = version ?? "";
        }

        public string FilePath { get; private set; }
        public string AppName { get; private set; }
        public string PackageName { get; private set; }
        public string Version { get; private set; }

        internal string Get(PackageClipboardText.Field field) {
            switch (field) {
                case PackageClipboardText.Field.FilePath: return FilePath;
                case PackageClipboardText.Field.AppName: return AppName;
                case PackageClipboardText.Field.PackageName: return PackageName;
                case PackageClipboardText.Field.Version: return Version;
                default: return "";
            }
        }
    }

    /// <summary>
    /// The four clipboard texts for one selection: the values that exist, in selection order,
    /// separated by CRLF with no trailing line break. <see cref="Has"/> is what decides
    /// whether the submenu offers the item at all, and it can be true while the text is still
    /// empty, which is how an unread multi-file selection offers every value.
    /// </summary>
    public sealed class PackageClipboardTextSet {
        private readonly string filePath;
        private readonly string appName;
        private readonly string packageName;
        private readonly string version;
        private readonly bool[] offered = new bool[PackageClipboardText.Fields.Length];

        internal PackageClipboardTextSet(IEnumerable<PackageClipboardEntry> entries) {
            var filePaths = new List<string>();
            var appNames = new List<string>();
            var packageNames = new List<string>();
            var versions = new List<string>();
            foreach (PackageClipboardEntry entry in entries) {
                if (entry.FilePath != "") filePaths.Add(entry.FilePath);
                if (entry.AppName != "") appNames.Add(entry.AppName);
                if (entry.PackageName != "") packageNames.Add(entry.PackageName);
                if (entry.Version != "") versions.Add(entry.Version);
            }
            filePath = JoinLines(filePaths);
            appName = JoinLines(appNames);
            packageName = JoinLines(packageNames);
            version = JoinLines(versions);
            for (int i = 0; i < PackageClipboardText.Fields.Length; i++) {
                offered[i] = Get(PackageClipboardText.Fields[i]).Length > 0;
            }
        }

        /// <summary>
        /// The set for a selection that was not read: the package values are offered on the
        /// strength of the selected types alone, so their text stays empty until the command
        /// reads the files.
        /// </summary>
        internal PackageClipboardTextSet(IEnumerable<PackageClipboardEntry> entries, bool packageValuesOffered)
            : this(entries) {
            offered[(int)PackageClipboardText.Field.AppName] = packageValuesOffered;
            offered[(int)PackageClipboardText.Field.PackageName] = packageValuesOffered;
            offered[(int)PackageClipboardText.Field.Version] = packageValuesOffered;
        }

        public string FilePath {
            get { return filePath; }
        }

        public string AppName {
            get { return appName; }
        }

        public string PackageName {
            get { return packageName; }
        }

        public string Version {
            get { return version; }
        }

        public string Get(PackageClipboardText.Field field) {
            switch (field) {
                case PackageClipboardText.Field.FilePath: return filePath;
                case PackageClipboardText.Field.AppName: return appName;
                case PackageClipboardText.Field.PackageName: return packageName;
                case PackageClipboardText.Field.Version: return version;
                default: return "";
            }
        }

        /// <summary>
        /// True when the submenu should offer this value. An unread selection answers true from
        /// the selected types, so the item is visible before any file was opened.
        /// </summary>
        public bool Has(PackageClipboardText.Field field) {
            int index = (int)field;
            return index >= 0 && index < offered.Length && offered[index];
        }

        private static string JoinLines(List<string> lines) {
            return string.Join(PackageClipboardText.LineBreak, lines.ToArray());
        }
    }
}
