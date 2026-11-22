using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Coverage for the guards the BCL zip reader does not provide: the entry-count
    /// cap, the byte/ratio caps on one entry, and the name decoding plus exact-name
    /// lookup the readers depend on.
    ///
    /// Every archive is assembled in memory by ZipBuilder, so the tests are hermetic
    /// and can express hostile shapes (zip64 counts, raw legacy name bytes, truncated
    /// central directories) that no sample library should have to ship.
    /// </summary>
    [TestFixture]
    public class ZipGuardTests
    {
        [Test]
        public void RejectsAnArchiveWithMoreEntriesThanTheCap()
        {
            int limit = (int)ProductApi.ZipSafetyConstant("MAX_ENTRIES");
            // Above 65535 entries the legacy end-of-central-directory cannot express the
            // count, so the archive has to be zip64 - exactly how a hostile archive would
            // hide a huge central directory.
            byte[] archive = ZipBuilder.BuildWithEntryCount(limit + 1, zip64: true);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() => Close(Opened(archive)));

            Assert.That(
                error.Message,
                Is.EqualTo("zip declares " + (limit + 1) + " entries, over the " + limit + "-entry limit"));
        }

        [Test]
        public void AcceptsAnArchiveAtTheEntryCountLimit()
        {
            int limit = (int)ProductApi.ZipSafetyConstant("MAX_ENTRIES");
            byte[] archive = ZipBuilder.BuildWithEntryCount(limit, zip64: false);

            using (ZipArchive zip = Opened(archive))
            {
                Assert.That(zip.Entries.Count, Is.EqualTo(limit));
            }
        }

        [Test]
        public void DecodesAnEntryNameFlaggedAsUtf8()
        {
            const string name = "资源/图标.png";
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                NameBytes = Encoding.UTF8.GetBytes(name),
                Flags = 0x0800, // the zip general-purpose "name is UTF-8" bit
            });

            using (ZipArchive zip = Opened(archive))
            {
                ZipArchiveEntry entry = ProductApi.GetEntryExact(zip, name);
                Assert.That(entry, Is.Not.Null, "a UTF-8 flagged name must decode to the same string");
                Assert.That(entry.FullName, Is.EqualTo(name));
            }
        }

        /// <summary>
        /// Unflagged names decode with the system ANSI code page, which is what the
        /// previous reader did (and what Chinese/Japanese resource paths in real packages
        /// rely on). A host whose code page cannot represent the fixture skips instead of
        /// asserting a code-page-dependent expectation.
        /// </summary>
        [Test]
        public void DecodesAnUnflaggedEntryNameWithTheLegacyCodePage()
        {
            const string name = "资源/图标.png";
            Encoding legacy = Encoding.GetEncoding(0);
            byte[] nameBytes = legacy.GetBytes(name);
            if (legacy.GetString(nameBytes) != name)
            {
                Assert.Ignore("the host ANSI code page cannot represent the fixture name");
            }

            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec { NameBytes = nameBytes });

            using (ZipArchive zip = Opened(archive))
            {
                ZipArchiveEntry entry = ProductApi.GetEntryExact(zip, name);
                Assert.That(entry, Is.Not.Null, "an unflagged name must decode with the legacy code page");
                Assert.That(entry.FullName, Is.EqualTo(name));
            }
        }

        /// <summary>
        /// AndResGuard-obfuscated packages hold case-colliding names; the exact lookup
        /// must return the ordinal match even when a case-folded twin sits earlier in the
        /// central directory, while the legacy fuzzy lookup keeps its first-hit order.
        /// </summary>
        [Test]
        public void GetEntryExactPrefersTheOrdinalMatchOverTheLegacyFoldedOne()
        {
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "res/Fd.xml", Data = new byte[] { 1 } },
                new ZipBuilder.EntrySpec { Name = "res/fd.xml", Data = new byte[] { 2 } });

            using (ZipArchive zip = Opened(archive))
            {
                Assert.That(ProductApi.GetEntryExact(zip, "res/fd.xml").FullName, Is.EqualTo("res/fd.xml"));
                // The legacy lookup ignores case and therefore keeps the first hit.
                Assert.That(ProductApi.GetEntry(zip, "RES/FD.XML").FullName, Is.EqualTo("res/Fd.xml"));
                Assert.That(ProductApi.Contains(zip, "Res/FD.xml"), Is.True);
            }
        }

        /// <summary>
        /// Lookups match the whole path: ZipArchiveEntry.Name is only the leaf, so a
        /// bare "icon.png" query must not resolve "META-INF/icon.png".
        /// </summary>
        [Test]
        public void GetEntryExactMatchesTheFullPathNotTheLeafName()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec { Name = "META-INF/icon.png" });

            using (ZipArchive zip = Opened(archive))
            {
                Assert.That(ProductApi.GetEntryExact(zip, "icon.png"), Is.Null);
                Assert.That(ProductApi.GetEntryExact(zip, "META-INF/icon.png"), Is.Not.Null);
                Assert.That(zip.Entries[0].Name, Is.EqualTo("icon.png"));
                Assert.That(zip.Entries[0].FullName, Is.EqualTo("META-INF/icon.png"));
            }
        }

        [Test]
        public void GetEntryReturnsTheFirstDuplicate()
        {
            byte[] first = { 1, 2, 3 };
            byte[] second = { 4, 5, 6 };
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "dup.bin", Data = first },
                new ZipBuilder.EntrySpec { Name = "dup.bin", Data = second });

            using (ZipArchive zip = Opened(archive))
            {
                ZipArchiveEntry entry = ProductApi.GetEntry(zip, "dup.bin");
                Assert.That(ProductApi.ReadEntryBounded(zip, entry, 1024, "duplicate"), Is.EqualTo(first));
            }
        }

        [Test]
        public void IsFileSkipsDirectoryEntries()
        {
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "assets/" },
                new ZipBuilder.EntrySpec { Name = "assets/icon.png", Data = new byte[] { 7 } });

            using (ZipArchive zip = Opened(archive))
            {
                ZipArchiveEntry directory = ProductApi.GetEntryExact(zip, "assets/");
                Assert.That(directory, Is.Not.Null);
                Assert.That(ProductApi.IsFile(directory), Is.False);
                Assert.That(ProductApi.IsFile(ProductApi.GetEntryExact(zip, "assets/icon.png")), Is.True);
                Assert.That(ProductApi.IsFile(null), Is.False);
            }
        }

        [Test]
        public void RejectsATruncatedCentralDirectory()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec { Name = "a.bin", Data = new byte[32] });
            byte[] damaged = ZipBuilder.Truncate(archive, 8);

            Assert.Throws<InvalidDataException>(() => Close(Opened(damaged)));
        }

        [Test]
        public void RejectsANonSeekableStream()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec { Name = "a.bin", Data = new byte[4] });

            using (NonSeekableStream stream = new NonSeekableStream(archive))
            {
                Assert.Throws<NotSupportedException>(() => Close(ProductApi.OpenZip(stream)));
            }
        }

        /// <summary>
        /// Real packages from the store can be zip64 (large appx bundles, apks over 4 GiB of
        /// entries); the BCL reader handles the format, and this pins that the migrated
        /// layer still finds and bounds such entries.
        /// </summary>
        [Test]
        public void ReadsAZip64Archive()
        {
            byte[] payload = new byte[64 * 1024];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i * 7 + 3);
            }
            byte[] archive = ZipBuilder.BuildZip64(new ZipBuilder.EntrySpec { Name = "assets/big.bin", Data = payload });

            using (ZipArchive zip = Opened(archive))
            {
                ZipArchiveEntry entry = ProductApi.GetEntryExact(zip, "assets/big.bin");
                Assert.That(entry, Is.Not.Null, "a zip64 entry must be visible to the exact lookup");
                Assert.That(entry.Length, Is.EqualTo(payload.Length));
                Assert.That(entry.CompressedLength, Is.EqualTo(payload.Length));
                Assert.That(ProductApi.ReadEntryBounded(zip, entry, 1024 * 1024, "zip64"), Is.EqualTo(payload));
            }
        }

        /// <summary>
        /// A container that declares only the 0xFFFFFFFF sentinel (no zip64 record) has no
        /// size to compare against a cap. Reading it as "exactly four gigabytes" would make
        /// every appx bundle with such a manifest fail with a nonsense message, and would
        /// make every bundle-size score identical, so the readers see "unknown".
        /// </summary>
        [Test]
        public void TreatsTheSizeSentinelAsUnknown()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "payload.bin",
                Data = new byte[16],
                SentinelSizes = true,
            });

            using (ZipArchive zip = Opened(archive))
            {
                ZipArchiveEntry entry = ProductApi.GetEntryExact(zip, "payload.bin");
                Assert.That(entry, Is.Not.Null);
                Assert.That(entry.Length, Is.EqualTo(0xFFFFFFFFL));
                Assert.That(ProductApi.DeclaredLength(entry), Is.EqualTo(-1L));
                // with no compression ratio to apply, the hard cap is the only bound
                Assert.That(ProductApi.EntryByteLimit(entry.CompressedLength, 1L << 20), Is.EqualTo(1L << 20));
            }
        }

        [Test]
        public void EntryByteLimitAllowsTheRatioSlackForSmallCompressedEntries()
        {
            long ratioLimit = ProductApi.ZipSafetyConstant("RATIO_LIMIT");

            Assert.That(ProductApi.EntryByteLimit(1024, 1L << 40), Is.EqualTo(1024 * ratioLimit + (1L << 20)));
        }

        [Test]
        public void EntryByteLimitNeverWidensTheHardCap()
        {
            Assert.That(ProductApi.EntryByteLimit(64L * 1024 * 1024, 4096), Is.EqualTo(4096));
        }

        [Test]
        public void EntryByteLimitSaturatesInsteadOfOverflowing()
        {
            Assert.That(ProductApi.EntryByteLimit(long.MaxValue, long.MaxValue), Is.EqualTo(long.MaxValue));
            Assert.That(ProductApi.EntryByteLimit(long.MaxValue / 2, long.MaxValue), Is.EqualTo(long.MaxValue));
        }

        /// <summary>
        /// The ratio allowance exists to admit legitimately compressible payloads, so a
        /// near-maximum deflate ratio must still be read when the hard cap allows it.
        /// (A stream cannot exceed 1032:1 - a 258-byte match costs at least two Huffman
        /// symbols - hence the allowance is tested arithmetically above and behaviourally
        /// here.)
        /// </summary>
        [Test]
        public void ReadsAHighlyCompressibleEntryBelowBothCaps()
        {
            byte[] payload = new byte[4 * 1024 * 1024]; // zeros: deflate reaches roughly 1000:1
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "zeros.bin",
                Data = payload,
                Deflate = true,
            });

            using (ZipArchive zip = Opened(archive))
            {
                ZipArchiveEntry entry = ProductApi.GetEntryExact(zip, "zeros.bin");
                Assert.That(
                    entry.CompressedLength,
                    Is.LessThan(payload.Length / 100),
                    "the fixture must actually exercise the ratio allowance");
                Assert.That(
                    ProductApi.ReadEntryBounded(zip, entry, 32L * 1024 * 1024, "zeros").Length,
                    Is.EqualTo(payload.Length));
            }
        }

        private static ZipArchive Opened(byte[] archive)
        {
            return ProductApi.OpenZip(new MemoryStream(archive, 0, archive.Length, false, false));
        }

        private static void Close(ZipArchive zip)
        {
            using (zip)
            {
            }
        }

        /// <summary>A stream that cannot seek - the shape ZipArchive must refuse.</summary>
        private sealed class NonSeekableStream : Stream
        {
            private readonly MemoryStream inner;

            public NonSeekableStream(byte[] data)
            {
                inner = new MemoryStream(data, 0, data.Length, false, false);
            }

            public override bool CanRead { get { return true; } }

            public override bool CanSeek { get { return false; } }

            public override bool CanWrite { get { return false; } }

            public override long Length { get { throw new NotSupportedException(); } }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return inner.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
