using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ApkShellext2.Diagnostics;

namespace ApkQuickReader {
    /// <summary>
    /// Every package reader here opens attacker-controlled ZIP archives from
    /// inside explorer.exe (icon/infotip/context-menu handlers). Declared entry
    /// sizes in the central directory are attacker data: never use them for
    /// (int) casts or buffer preallocation, and always bound total bytes read.
    /// ReadEntryBounded is the single sanctioned way to materialise an entry.
    /// (Ratio doctrine borrowed from apk-info, Apache-2.0: worst-case deflate
    /// expansion is a 258-byte match for ~2 bits ≈ 1032×.)
    ///
    /// Containers are opened with the BCL reader (System.IO.Compression.ZipArchive).
    /// ZipArchive performs no CRC/size validation and never bounds an entry by
    /// itself, which is exactly why the caps below stand between it and the
    /// readers; OpenZip additionally rejects archives with an absurd number of
    /// central-directory entries before any caller can iterate them.
    /// </summary>
    internal static class ZipSafety {
        // generous per-role caps — real payloads are far below these
        public const long MAX_BASE_APK = 512L * 1024 * 1024;
        public const long MAX_MANIFEST = 128L * 1024 * 1024;
        public const long MAX_XML = 64L * 1024 * 1024;
        public const long MAX_ICON = 32L * 1024 * 1024;
        public const long MAX_METADATA = 8L * 1024 * 1024;   // plist / json / itunes
        public const long RATIO_LIMIT = 1032;

        /// <summary>Nested container (apk inside .apks/.apkm/.xapk, appx inside an
        /// .appxbundle) that has to be buffered because ZipArchive needs a seekable
        /// stream. Same order of magnitude as a base apk.</summary>
        public const long MAX_NESTED_CONTAINER = 512L * 1024 * 1024;

        /// <summary>Central-directory entry count cap (roadmap C4). Bounds every
        /// caller's O(n) entry scan and the archive's per-entry bookkeeping no matter
        /// what a hostile archive claims.
        ///
        /// The number is the classic (non-zip64) end-of-central-directory limit,
        /// 65535 entries: above it a zip has to be zip64, which no real package in
        /// the corpus is. The roadmap draft said 10000, but the 87-sample corpus
        /// disagrees with that draft: 南方航空_com.csair.mbp_4.6.4(20231214).apk declares
        /// 15349 entries and com.ubercab's nested base apk 10074, so a 10000 cap
        /// throws away two real icons — the cap bounds work, it must not reject
        /// legitimate packages.</summary>
        public const int MAX_ENTRIES = 65535;

        /// <summary>
        /// Encoding for entry names that do NOT carry the UTF-8 flag (0x800).
        ///
        /// The zip specification says CP437, but in practice Android/Windows tooling
        /// writes the system ANSI code page, and the previous reader (SharpZipLib with
        /// its default StringCodec) decoded those names with Encoding.GetEncoding(0)
        /// too. Keeping the same code page here is what preserves lookups of real
        /// Chinese/Japanese resource paths in the samples. Names WITH the flag are
        /// always decoded as UTF-8 by ZipArchive regardless of this value.
        ///
        /// ZipArchive rejects UTF-8/UTF-16 here, so a host configured for the Windows
        /// UTF-8 beta (code page 65001) falls back to the spec code page 437.
        /// </summary>
        private static readonly Encoding EntryNameEncoding = ResolveEntryNameEncoding();

        private static Encoding ResolveEntryNameEncoding() {
            Encoding encoding = null;
            try { encoding = Encoding.GetEncoding(0); } catch { }
            if (encoding == null || encoding.CodePage == 65001 ||
                encoding.CodePage == 1200 || encoding.CodePage == 1201) {
                encoding = Encoding.GetEncoding(437);
            }
            return encoding;
        }

        /// <summary>
        /// Opens a zip container for read-only access. The archive owns the stream
        /// (matches the previous reader's IsStreamOwner default: disposing the archive
        /// closes the stream). Rejects a central directory with more than MAX_ENTRIES
        /// entries — that is a hostile/absurd archive, and it is recorded in the error
        /// log before the InvalidDataException reaches the handlers.
        /// </summary>
        public static ZipArchive OpenZip(Stream stream) {
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (!stream.CanSeek)
                throw new NotSupportedException("zip containers must be opened from a seekable stream");

            ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read, false, EntryNameEncoding);
            int count = zip.Entries.Count;
            if (count > MAX_ENTRIES) {
                string message = "zip declares " + count + " entries, over the " + MAX_ENTRIES + "-entry limit";
                InvalidDataException error = new InvalidDataException(message);
                ErrorLog.Error("ZipSafety", message, error);
                zip.Dispose();
                throw error;
            }
            return zip;
        }

        /// <summary>
        /// Legacy fuzzy lookup: the first central-directory entry whose name matches
        /// case-insensitively. This is the semantics of the previous reader's
        /// ZipFile.GetEntry, and callers that pass names from package metadata
        /// (manifest.json, AppxBundleManifest FileName, …) rely on it.
        /// ZipArchive.GetEntry is ordinal case-sensitive, so the scan is explicit.
        /// </summary>
        public static ZipArchiveEntry GetEntry(ZipArchive z, string name) {
            if (z == null || name == null) return null;
            foreach (ZipArchiveEntry entry in z.Entries) {
                if (string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// Resolves an entry whose name came from package metadata (arsc path
        /// strings, AXML refs). The previous reader's GetEntry matched
        /// case-insensitively and returned the first central-directory hit — fatal for
        /// AndResGuard-obfuscated packages, whose 2-char names deliberately collide by
        /// case (res/Fd.xml vs res/FD.xml vs res/fd.xml: 600+ such groups in real
        /// APKs). ZIP names are case-sensitive; try the ordinal-exact entry first, fall
        /// back to the legacy fuzzy match only when it is missing.
        /// </summary>
        public static ZipArchiveEntry GetEntryExact(ZipArchive z, string name) {
            if (z == null || name == null) return null;
            foreach (ZipArchiveEntry entry in z.Entries) {
                if (string.Equals(entry.FullName, name, StringComparison.Ordinal))
                    return entry;
            }
            return GetEntry(z, name); // legacy fallback: fuzzy match beats giving up
        }

        /// <summary>Case-insensitive existence check (the previous reader's FindEntry).</summary>
        public static bool Contains(ZipArchive z, string name) {
            return GetEntry(z, name) != null;
        }

        /// <summary>A file entry, i.e. not a directory (name ending in '/').</summary>
        public static bool IsFile(ZipArchiveEntry entry) {
            return entry != null && !entry.FullName.EndsWith("/", StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads an entry with a hard byte cap and a compression-ratio cap.
        /// The declared Length/CompressedLength are never trusted and never used
        /// for preallocation; the buffer grows only as real bytes arrive.
        /// Throws InvalidDataException when a cap is hit — handlers catch and
        /// fall back to the generic icon.
        /// </summary>
        public static byte[] ReadEntryBounded(ZipArchive z, ZipArchiveEntry entry, long hardCap, string what) {
            if (entry == null)
                throw new InvalidDataException(what + ": entry missing");
            // A Stored entry has CompressedLength == Length, so the ratio allowance
            // cannot widen its cap; the previous reader special-cased Stored only
            // because its zip library exposed the method. Same accept/reject outcome.
            long compressed = entry.CompressedLength;
            long ratioCap = compressed > (long.MaxValue - (1L << 20)) / RATIO_LIMIT
                ? long.MaxValue
                : compressed * RATIO_LIMIT + (1L << 20);
            long limit = Math.Min(hardCap, ratioCap);
            // start small, grow with real data — never preallocate declared size
            int pre = 64 * 1024;
            long declared = entry.Length;
            if (declared > 0 && declared < pre) pre = (int)declared;
            MemoryStream ms = new MemoryStream(pre);
            byte[] buf = new byte[64 * 1024];
            using (Stream s = entry.Open()) {
                long total = 0; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) {
                    total += n;
                    if (total > limit)
                        throw new InvalidDataException(what + ": decompressed size exceeds safety cap " + limit);
                    ms.Write(buf, 0, n);
                }
            }
            return ms.ToArray();
        }
    }
}
