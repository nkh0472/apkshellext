using System;
using System.Collections.Generic;
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
    /// readers; Open additionally rejects archives with an absurd number of
    /// central-directory entries before any caller can iterate them.
    ///
    /// This class is the only implementation of IArchive; the six package readers
    /// hold an IArchive, so the caps, the lookup semantics and the exception texts
    /// exist exactly once.
    /// </summary>
    internal sealed class SafeArchive : IArchive {
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

        /// <summary>Central-directory entry count cap. Bounds every caller's O(n) entry
        /// scan and the archive's per-entry bookkeeping no matter what a hostile archive
        /// claims.
        ///
        /// The number is the classic (non-zip64) end-of-central-directory limit,
        /// 65535 entries: above it a zip has to be zip64, which no real package in
        /// the corpus is. A tighter round number would be wrong: the 87-sample corpus
        /// contains packages that only fit here —
        /// 南方航空_com.csair.mbp_4.6.4(20231214).apk declares 15349 entries and
        /// com.ubercab's nested base apk 10074 — so the cap bounds work without
        /// rejecting legitimate packages.</summary>
        public const int MAX_ENTRIES = 65535;

        /// <summary>
        /// Hard ceiling on the decompressed bytes ONE SafeArchive instance may hand out
        /// over its lifetime. The per-entry gate already bounds a single entry; this
        /// bounds their sum, so a container cannot stay alive by feeding a reader
        /// thousands of individually-legal entries.
        ///
        /// 1 GiB is far above what a legitimate package reads: a 147 MB
        /// base apk hands out roughly its manifest, resource table and one icon (a few
        /// MB), a nested container is itself capped at MAX_NESTED_CONTAINER (512 MiB)
        /// and a split bundle buffers at most one base apk. Real packages all fit while
        /// a compression bomb is stopped by the per-entry gate first and this budget
        /// second.
        /// </summary>
        public const long MaxTotalReadBytes = 1024L * 1024 * 1024;

        /// <summary>The 32-bit "look in the zip64 record" sentinel the format uses for
        /// a size or offset that does not fit 32 bits.</summary>
        private const long Zip64SizeSentinel = 0xFFFFFFFFL;

        /// <summary>
        /// Encoding for entry names that do NOT carry the UTF-8 flag (0x800).
        ///
        /// The zip specification says CP437, but in practice Android/Windows tooling
        /// writes the system ANSI code page, and the entries must be readable as the
        /// user's system names them: that is what preserves lookups of real
        /// Chinese/Japanese resource paths in the samples. Names WITH the flag are
        /// always decoded as UTF-8 by ZipArchive regardless of this value.
        ///
        /// ZipArchive rejects UTF-8/UTF-16 here, so a host configured for the Windows
        /// UTF-8 beta (code page 65001) falls back to the spec code page 437.
        /// </summary>
        private static readonly Encoding EntryNameEncoding = ResolveEntryNameEncoding();

        private readonly ZipArchive zip;
        private readonly long maxTotalReadBytes;
        private long totalReadBytes;

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
        /// Opens a zip container for read-only access. The archive owns the stream:
        /// disposing the archive closes the stream. Rejects a central directory with more
        /// than MAX_ENTRIES entries — that is a hostile/absurd archive, and it is recorded
        /// in the error log before the InvalidDataException reaches the handlers.
        /// </summary>
        public static IArchive Open(Stream stream) {
            return new SafeArchive(stream, MaxTotalReadBytes);
        }

        /// <summary>
        /// Opens with an explicit cumulative budget. The budget is a constructor
        /// parameter rather than a constant read inside ReadEntryBounded so the tests can
        /// drive the 1 GiB path with a few kilobytes; production always goes through
        /// <see cref="Open"/> and therefore through MaxTotalReadBytes.
        /// </summary>
        internal SafeArchive(Stream stream, long maxTotalReadBytes) {
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (!stream.CanSeek)
                throw new NotSupportedException("zip containers must be opened from a seekable stream");
            if (maxTotalReadBytes <= 0)
                throw new ArgumentOutOfRangeException("maxTotalReadBytes");

            ZipArchive opened = new ZipArchive(stream, ZipArchiveMode.Read, false, EntryNameEncoding);
            int count = opened.Entries.Count;
            if (count > MAX_ENTRIES) {
                string message = "zip declares " + count + " entries, over the " + MAX_ENTRIES + "-entry limit";
                InvalidDataException error = new InvalidDataException(message);
                ErrorLog.Error("SafeArchive", message, error);
                opened.Dispose();
                throw error;
            }
            zip = opened;
            this.maxTotalReadBytes = maxTotalReadBytes;
        }

        /// <summary>
        /// Case-insensitive first-hit lookup: the first central-directory entry whose name
        /// matches case-insensitively. Callers that pass names from package metadata
        /// (manifest.json, AppxBundleManifest FileName, …) rely on it, because that
        /// metadata is written by other toolchains with their own casing.
        /// ZipArchive.GetEntry is ordinal case-sensitive, so the scan is explicit.
        /// </summary>
        public IArchiveEntry GetEntry(string name) {
            if (name == null) return null;
            foreach (ZipArchiveEntry entry in zip.Entries) {
                if (string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase))
                    return new SafeArchiveEntry(entry);
            }
            return null;
        }

        /// <summary>
        /// Resolves an entry whose name came from package metadata (arsc path
        /// strings, AXML refs). A case-insensitive first-hit lookup is fatal for
        /// AndResGuard-obfuscated packages, whose 2-char names collide by
        /// case (res/Fd.xml vs res/FD.xml vs res/fd.xml: 600+ such groups in real
        /// APKs). ZIP names are case-sensitive; try the ordinal-exact entry first and
        /// fall back to the case-insensitive match only when it is missing.
        /// </summary>
        public IArchiveEntry GetEntryExact(string name) {
            if (name == null) return null;
            foreach (ZipArchiveEntry entry in zip.Entries) {
                if (string.Equals(entry.FullName, name, StringComparison.Ordinal))
                    return new SafeArchiveEntry(entry);
            }
            return GetEntry(name); // case-insensitive fallback: a fuzzy hit beats giving up
        }

        /// <summary>Case-insensitive existence check, same semantics as GetEntry.</summary>
        public bool Contains(string name) {
            return GetEntry(name) != null;
        }

        /// <summary>A file entry, i.e. not a directory (name ending in '/').</summary>
        public bool IsFile(IArchiveEntry entry) {
            return entry != null && !entry.FullName.EndsWith("/", StringComparison.Ordinal);
        }

        public int DeclaredEntryCount {
            get { return zip.Entries.Count; }
        }

        public IEnumerable<IArchiveEntry> Entries {
            get {
                foreach (ZipArchiveEntry entry in zip.Entries)
                    yield return new SafeArchiveEntry(entry);
            }
        }

        /// <summary>
        /// The uncompressed size an entry declares, or -1 when the container carries no
        /// usable number.
        ///
        /// A well-formed zip64 entry resolves normally on .NET Framework (verified
        /// against a zip64 fixture), but a container that writes the 0xFFFFFFFF size
        /// sentinel and no zip64 record has no size at all. Read that as unknown rather
        /// than as exactly four gigabytes: callers that compare it against a cap would
        /// reject the entry with a misleading message, and callers that score entries by
        /// size would all score the same. The byte caps are what bound the read.
        /// </summary>
        public long DeclaredLength(IArchiveEntry entry) {
            if (entry == null) return -1;
            long declared = entry.Length;
            return declared < 0 || declared >= Zip64SizeSentinel ? -1 : declared;
        }

        /// <summary>
        /// The byte cap actually applied to one entry: the hard cap, tightened by the
        /// compression-ratio allowance (RATIO_LIMIT × compressed bytes + 1 MiB of
        /// slack). A Stored entry has CompressedLength == Length, so the allowance can
        /// never widen its cap; Stored and Deflated entries are accepted and rejected by
        /// the same rule.
        ///
        /// The multiply saturates instead of wrapping, so a container that lies about
        /// its compressed size cannot produce a negative (i.e. unbounded) limit. A
        /// container that declares only the 0xFFFFFFFF sentinel gives no ratio basis, so
        /// the hard cap is the only honest bound for it. Kept separate from
        /// ReadEntryBounded so the arithmetic is unit-testable: 1032:1 is above the
        /// physical ceiling of deflate (a 258-byte match coded as two Huffman symbols), so
        /// no valid stream can reach this gate — it is the belt to the hard cap's braces,
        /// and is exercised arithmetically.
        /// </summary>
        public static long EntryByteLimit(long compressedLength, long hardCap) {
            if (compressedLength < 0 || compressedLength >= Zip64SizeSentinel) return hardCap;
            long ratioCap = compressedLength > (long.MaxValue - (1L << 20)) / RATIO_LIMIT
                ? long.MaxValue
                : compressedLength * RATIO_LIMIT + (1L << 20);
            return Math.Min(hardCap, ratioCap);
        }

        /// <summary>
        /// Reads an entry with a hard byte cap, a compression-ratio cap and the
        /// per-archive cumulative budget.
        /// The declared Length/CompressedLength are never trusted and never used
        /// for preallocation; the buffer grows only as real bytes arrive.
        /// Throws InvalidDataException when a cap is hit — handlers catch and
        /// fall back to the generic icon.
        /// </summary>
        public byte[] ReadEntryBounded(IArchiveEntry entry, long hardCap, string what) {
            if (entry == null)
                throw new InvalidDataException(what + ": entry missing");
            long limit = EntryByteLimit(entry.CompressedLength, hardCap);
            // start small, grow with real data — never preallocate declared size
            int pre = 64 * 1024;
            long declared = DeclaredLength(entry);
            if (declared > 0 && declared < pre) pre = (int)declared;
            MemoryStream ms = new MemoryStream(pre);
            byte[] buf = new byte[64 * 1024];
            using (Stream s = entry.Open()) {
                long total = 0; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) {
                    total += n;
                    if (total > limit)
                        throw new InvalidDataException(what + ": decompressed size exceeds safety cap " + limit);
                    totalReadBytes += n;
                    if (totalReadBytes > maxTotalReadBytes)
                        throw new InvalidDataException(what + ": container has handed out " + totalReadBytes
                            + " decompressed bytes, over the " + maxTotalReadBytes + "-byte per-archive budget");
                    ms.Write(buf, 0, n);
                }
            }
            return ms.ToArray();
        }

        public void Dispose() {
            zip.Dispose();
        }

        /// <summary>
        /// A ZipArchiveEntry dressed as IArchiveEntry. Created per lookup/enumeration
        /// hit and intentionally not cached: the central directory already holds the
        /// real entries.
        /// </summary>
        private sealed class SafeArchiveEntry : IArchiveEntry {
            private readonly ZipArchiveEntry entry;

            internal SafeArchiveEntry(ZipArchiveEntry entry) {
                this.entry = entry;
            }

            public string FullName {
                get { return entry.FullName; }
            }

            public long Length {
                get { return entry.Length; }
            }

            public long CompressedLength {
                get { return entry.CompressedLength; }
            }

            public Stream Open() {
                return entry.Open();
            }
        }
    }
}
