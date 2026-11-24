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
    /// whether the submenu offers the item at all.
    /// </summary>
    public sealed class PackageClipboardTextSet {
        private readonly string filePath;
        private readonly string appName;
        private readonly string packageName;
        private readonly string version;

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

        /// <summary>True when the selection produced at least one line for this value.</summary>
        public bool Has(PackageClipboardText.Field field) {
            return Get(field).Length > 0;
        }

        private static string JoinLines(List<string> lines) {
            return string.Join(PackageClipboardText.LineBreak, lines.ToArray());
        }
    }
}
