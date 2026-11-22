using System;
using System.Collections.Generic;
using System.IO;

namespace ApkQuickReader {
    /// <summary>
    /// One entry of an <see cref="IArchive"/>. The declared sizes come from the
    /// container's central directory, i.e. they are attacker data: they are exposed
    /// here for messages and ordering decisions only, never for allocation or for a
    /// bound. Only <see cref="IArchive.ReadEntryBounded"/> materialises bytes, and
    /// only it applies the caps and the cumulative budget.
    /// </summary>
    internal interface IArchiveEntry {
        /// <summary>The full stored path (zip "FullName": directories end in '/').</summary>
        string FullName { get; }

        /// <summary>The uncompressed size the container declares (may be a sentinel).</summary>
        long Length { get; }

        /// <summary>The compressed size the container declares (may be a sentinel).</summary>
        long CompressedLength { get; }

        /// <summary>
        /// Opens the raw entry stream. Callers inside the product must not read it
        /// directly: it is unbounded. <see cref="IArchive.ReadEntryBounded"/> is the
        /// single sanctioned consumer.
        /// </summary>
        Stream Open();
    }

    /// <summary>
    /// Read-only view over one archive container as the package readers need it:
    /// bounded entry reads plus the name semantics the previous zip reader provided
    /// (case-insensitive first-hit lookup, ordinal-exact lookup with a fuzzy
    /// fallback, directory detection). SafeArchive is the only implementation; the
    /// readers depend on this interface so the container itself, and with it every
    /// cap, lives in exactly one place.
    ///
    /// There is deliberately no static "open" on this interface (C# 10 has no
    /// static abstract members): SafeArchive.Open is the factory and the only way
    /// a reader obtains an IArchive.
    /// </summary>
    internal interface IArchive : IDisposable {
        /// <summary>
        /// The number of central-directory entries the container declares, already
        /// gated against SafeArchive.MAX_ENTRIES before construction succeeds.
        /// </summary>
        int DeclaredEntryCount { get; }

        /// <summary>
        /// Every central-directory entry in container order. Enumerating allocates a
        /// short-lived wrapper per entry; the entries themselves are not buffered.
        /// </summary>
        IEnumerable<IArchiveEntry> Entries { get; }

        /// <summary>The legacy fuzzy lookup: first case-insensitive match, else null.</summary>
        IArchiveEntry GetEntry(string name);

        /// <summary>The ordinal-exact lookup, falling back to <see cref="GetEntry"/>.</summary>
        IArchiveEntry GetEntryExact(string name);

        /// <summary>Case-insensitive existence check.</summary>
        bool Contains(string name);

        /// <summary>True for a file entry, false for a directory (or null).</summary>
        bool IsFile(IArchiveEntry entry);

        /// <summary>The declared uncompressed size, or -1 when unknown (sentinel).</summary>
        long DeclaredLength(IArchiveEntry entry);

        /// <summary>
        /// Reads one entry under the hard cap, the compression-ratio allowance and the
        /// per-archive cumulative budget. Throws InvalidDataException when a cap is hit.
        /// </summary>
        byte[] ReadEntryBounded(IArchiveEntry entry, long hardCap, string what);
    }
}
