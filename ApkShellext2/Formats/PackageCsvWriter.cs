using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ApkShellext2 {
    /// <summary>
    /// Writes the information the package readers expose for a selection as one CSV table.
    /// The writer stays in the parsing layer because it needs nothing but a reader, the file
    /// system and a stream; the context menu supplies the reader factory and the destination.
    /// </summary>
    /// <remarks>
    /// The encoding is UTF-8 with a byte order mark because Excel uses the mark to detect the
    /// encoding, the line ending is CRLF and quoting follows RFC 4180 because Excel is the
    /// main consumer of the file. Values that begin or end with whitespace are quoted as well,
    /// because spreadsheet readers trim unquoted fields and package names can carry a space.
    /// </remarks>
    public static class PackageCsvWriter {
        /// <summary>The file name the save dialog starts with.</summary>
        public const string DefaultFileName = "apk-info.csv";

        /// <summary>The header row, in the order the columns are written.</summary>
        public static readonly string[] Columns = {
            "File", "Folder", "AppName", "Package", "Version", "Revision",
            "Publisher", "AppID", "Type", "SizeBytes", "Sha256", "Xxh3", "Modified", "Status"
        };

        public const string StatusOk = "ok";
        public const string StatusUnsupported = "unsupported type";
        public const string StatusUnreadable = "unreadable";
        public const string StatusCancelled = "cancelled";

        /// <summary>Writes the selection using the production reader factory.</summary>
        public static PackageCsvResult Write(Stream output, IEnumerable<string> paths) {
            return Write(output, paths, null, CancellationToken.None, null);
        }

        /// <summary>
        /// Writes one row per selected path. A path that cannot be read still produces a row
        /// with a <see cref="PackageCsvResult"/> status instead of failing the whole export,
        /// and cancellation ends the export after writing the row that reports it.
        /// </summary>
        public static PackageCsvResult Write(
            Stream output,
            IEnumerable<string> paths,
            Func<string, IPackageReader> openReader,
            CancellationToken cancellationToken,
            Action<int, int> progress) {

            if (output == null) throw new ArgumentNullException("output");
            if (!output.CanWrite) throw new ArgumentException("The output stream is not writable.", "output");
            if (paths == null) throw new ArgumentNullException("paths");

            var selected = new List<string>();
            foreach (string path in paths) selected.Add(path);
            int total = selected.Count;
            Func<string, IPackageReader> create = openReader ?? PackageReaderFactory.Create;
            var result = new PackageCsvResult();

            using (var writer = new StreamWriter(output, new UTF8Encoding(true), 4096, true)) {
                writer.NewLine = "\r\n";
                WriteRecord(writer, Columns);

                foreach (string path in selected) {
                    string fullPath, folder;
                    SplitPath(path, out fullPath, out folder);
                    string fileName = SafeFileName(fullPath);
                    string appName = "", package = "", version = "", revision = "";
                    string publisher = "", appId = "", type = "";
                    string size = "", modified = "";
                    string sha256 = "", xxh3 = "";
                    string status;

                    if (cancellationToken.IsCancellationRequested) {
                        status = StatusCancelled;
                        result.Cancelled = true;
                    } else {
                        FileInfo file = ReadFileInfo(path);
                        AppPackageReader.AppType appType;
                        if (!TryGetAppType(path, out appType)) {
                            status = StatusUnsupported;
                        } else {
                            type = appType.ToString();
                            if (file == null) {
                                status = StatusUnreadable;
                            } else {
                                ReadSizeAndModified(file, out size, out modified);
                                bool hashesRead = TryReadHashes(path, out sha256, out xxh3);
                                try {
                                    using (IPackageReader reader = create(path)) {
                                        if (reader == null)
                                            throw new InvalidOperationException("The reader factory returned no reader.");
                                        appName = reader.AppName ?? "";
                                        package = reader.PackageName ?? "";
                                        version = reader.Version ?? "";
                                        revision = reader.Revision ?? "";
                                        publisher = reader.Publisher ?? "";
                                        appId = reader.AppID ?? "";
                                        type = reader.Type.ToString();
                                    }
                                    status = hashesRead ? StatusOk : StatusUnreadable;
                                } catch (Exception) {
                                    status = StatusUnreadable;
                                }
                            }
                        }
                    }

                    WriteRecord(writer, new[] {
                        fileName, folder, appName, package, version, revision,
                        publisher, appId, type, size, sha256, xxh3, modified, status
                    });
                    result.Rows++;
                    if (status != StatusOk && status != StatusCancelled) result.Errors++;

                    if (progress != null) progress(result.Rows, total);
                    if (status == StatusCancelled) break;
                }
                writer.Flush();
            }
            return result;
        }

        private static void SplitPath(string path, out string fullPath, out string folder) {
            fullPath = path ?? "";
            folder = "";
            try {
                string full = Path.GetFullPath(path);
                fullPath = full;
                folder = Path.GetDirectoryName(full) ?? "";
            } catch (Exception) {
                // keep the raw path so the row still identifies the selection entry
            }
        }

        private static string SafeFileName(string path) {
            try {
                return Path.GetFileName(path);
            } catch (Exception) {
                return path ?? "";
            }
        }

        private static FileInfo ReadFileInfo(string path) {
            try {
                var file = new FileInfo(path);
                return file.Exists ? file : null;
            } catch (Exception) {
                return null;
            }
        }

        private static bool TryGetAppType(string path, out AppPackageReader.AppType type) {
            try {
                type = PackageReaderFactory.GetAppType(path);
                return true;
            } catch (NotSupportedException) {
                type = default(AppPackageReader.AppType);
                return false;
            } catch (ArgumentException) {
                type = default(AppPackageReader.AppType);
                return false;
            }
        }

        private static void ReadSizeAndModified(FileInfo file, out string size, out string modified) {
            size = "";
            modified = "";
            try {
                size = file.Length.ToString(CultureInfo.InvariantCulture);
            } catch (Exception) {
                // a file that disappears between the existence check and the read keeps a blank size
            }
            try {
                modified = new DateTimeOffset(file.LastWriteTime)
                    .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            } catch (Exception) {
                // same as the size: the row is still written
            }
        }

        private static bool TryReadHashes(string path, out string sha256, out string xxh3) {
            sha256 = "";
            xxh3 = "";
            try {
                FileHash hash = FileHasher.Compute(path);
                sha256 = hash.Sha256;
                xxh3 = hash.Xxh3;
                return true;
            } catch (Exception) {
                // a file that cannot be read keeps both hash columns blank
                return false;
            }
        }

        private static void WriteRecord(TextWriter writer, string[] values) {
            for (int i = 0; i < values.Length; i++) {
                if (i > 0) writer.Write(',');
                writer.Write(Escape(values[i]));
            }
            writer.WriteLine();
        }

        private static string Escape(string value) {
            if (string.IsNullOrEmpty(value)) return "";
            bool quotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                || char.IsWhiteSpace(value[0])
                || char.IsWhiteSpace(value[value.Length - 1]);
            if (!quotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>What one export produced: rows written, rows that are not <c>ok</c>, and whether the user cancelled.</summary>
    public sealed class PackageCsvResult {
        public int Rows { get; internal set; }
        public int Errors { get; internal set; }
        public bool Cancelled { get; internal set; }
    }
}
