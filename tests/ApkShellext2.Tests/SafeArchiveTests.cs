using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using Archive = ApkShellext2.Tests.ProductApi.ArchiveHandle;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// SafeArchive.ReadEntryBounded is the one sanctioned way the readers materialise
    /// an entry from an attacker-controlled APK, and the declared sizes in the
    /// central directory are attacker data. These tests build real zips in memory
    /// (hermetic: no sample library, no network) and pin the two properties that
    /// matter - the cap is inclusive-exact, and hitting it stops the read instead
    /// of buffering the entry first.
    /// </summary>
    [TestFixture]
    public class SafeArchiveTests
    {
        private const string EntryName = "res/raw/payload.bin";
        private const string What = "test entry";

        [Test]
        public void ReturnsTheEntry_WhenItFitsTheCap()
        {
            byte[] payload = Payload(4096);
            using (Archive archive = ProductApi.OpenArchive(BuildZip(payload, CompressionLevel.NoCompression)))
            {
                object entry = archive.GetEntry(EntryName);
                Assert.That(entry, Is.Not.Null, EntryName + " is missing from the zip built by the test");

                byte[] read = archive.ReadEntryBounded(entry, payload.Length, What);
                Assert.That(read, Is.EqualTo(payload), "an entry of exactly the cap must be returned byte for byte");
            }
        }

        [Test]
        public void RejectsTheEntry_WhenItIsOneByteOverTheCap()
        {
            byte[] payload = Payload(1025);
            long cap = 1024;
            using (Archive archive = ProductApi.OpenArchive(BuildZip(payload, CompressionLevel.NoCompression)))
            {
                object entry = archive.GetEntry(EntryName);
                InvalidDataException ex = Assert.Throws<InvalidDataException>(
                    () => archive.ReadEntryBounded(entry, cap, What));

                Assert.That(ex.Message, Does.Contain(What), "the error has to name the role that hit the cap");
                Assert.That(ex.Message, Does.Contain(cap.ToString()), "the error has to report the cap that was hit");
            }
        }

        [Test]
        public void AppliesTheHardCap_ToStoredEntriesNotTheRatioAllowance()
        {
            // a Stored entry cannot expand, so the compression-ratio allowance must not
            // widen the cap: 2 MiB of STORED data against a 64 KiB cap has to fail
            byte[] payload = Payload(2 * 1024 * 1024);
            using (Archive archive = ProductApi.OpenArchive(BuildStoredZip(payload)))
            {
                object entry = archive.GetEntry(EntryName);
                Assert.That(archive.EntryCompressedLength(entry), Is.EqualTo((long)payload.Length),
                    "the zip must store this entry uncompressed");

                Assert.Throws<InvalidDataException>(() => archive.ReadEntryBounded(entry, 64 * 1024, What));
            }
        }

        [Test]
        [NonParallelizable]
        public void StopsReadingOnceTheCapIsHit_InsteadOfBufferingTheWholeEntry()
        {
            // 8 MiB stored payload, 64 KiB cap: a reader that materialised the entry
            // first would pull all 8 MiB through the archive stream before checking.
            byte[] payload = Payload(8 * 1024 * 1024);
            long cap = 64 * 1024;
            CountingStream source = new CountingStream(new MemoryStream(BuildStoredZip(payload)));

            using (Archive archive = ProductApi.OpenArchive(source))
            {
                object entry = archive.GetEntry(EntryName);
                Assert.That(entry, Is.Not.Null);

                long before = source.BytesRead;
                InvalidDataException ex = Assert.Throws<InvalidDataException>(
                    () => archive.ReadEntryBounded(entry, cap, What));
                long pulled = source.BytesRead - before;

                Assert.That(ex.Message, Does.Contain(cap.ToString()));
                // one 64 KiB read may complete before the total is checked, so allow
                // generous slack - but far below the 8 MiB a buffer-then-check would read
                Assert.That(pulled, Is.LessThanOrEqualTo(512 * 1024),
                    "ReadEntryBounded pulled " + pulled + " bytes from the archive for a " + cap +
                    "-byte cap; it must stop reading at the cap instead of buffering the entry");
                Assert.That(pulled, Is.GreaterThan(0), "the reader must actually read the entry it is bounding");
            }
        }

        [Test]
        public void RejectsAMissingEntry_InsteadOfReturningNull()
        {
            using (Archive archive = ProductApi.OpenArchive(BuildZip(Payload(16), CompressionLevel.NoCompression)))
            {
                InvalidDataException ex = Assert.Throws<InvalidDataException>(
                    () => archive.ReadEntryBounded(null, 1024, What));
                Assert.That(ex.Message, Does.Contain(What));
                Assert.That(ex.Message, Does.Contain("missing"));
            }
        }

        [Test]
        public void GetEntryExactPrefersTheOrdinalSpellingOverAFoldedTwin()
        {
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "res/Fd.xml", Data = Payload(8) },
                new ZipBuilder.EntrySpec { Name = "RES/FD.XML", Data = Payload(9) });

            using (Archive opened = ProductApi.OpenArchive(archive))
            {
                Assert.That(opened.EntryFullName(opened.GetEntryExact("res/Fd.xml")), Is.EqualTo("res/Fd.xml"),
                    "the ordinal-exact spelling must win even though a case-folded twin exists");
                Assert.That(opened.EntryFullName(opened.GetEntry("RES/FD.XML")), Is.EqualTo("res/Fd.xml"),
                    "the legacy fuzzy lookup must return the first central-directory match");
                Assert.That(opened.EntryFullName(opened.GetEntryExact("RES/FD.XML")), Is.EqualTo("RES/FD.XML"),
                    "when the requested spelling exists it must win over the earlier fuzzy match");
            }
        }

        [Test]
        public void FuzzyLookupMatchesAnyCase()
        {
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = Payload(32) });

            using (Archive opened = ProductApi.OpenArchive(archive))
            {
                Assert.That(opened.GetEntry("ANDROIDMANIFEST.XML"), Is.Not.Null,
                    "the legacy lookup is case-insensitive");
                Assert.That(opened.Contains("androidmanifest.xml"), Is.True);
                Assert.That(opened.Contains("AndroidManifest.xml"), Is.True);
            }
        }

        [Test]
        public void GetEntryExactFallsBackToFuzzyWhenOnlyAnotherCaseExists()
        {
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "res/Fd.xml", Data = Payload(8) });

            using (Archive opened = ProductApi.OpenArchive(archive))
            {
                object hit = opened.GetEntryExact("RES/FD.XML");
                Assert.That(hit, Is.Not.Null, "exact-first must not turn into exact-only");
                Assert.That(opened.EntryFullName(hit), Is.EqualTo("res/Fd.xml"));
            }
        }

        [Test]
        public void ContainsAndIsFileKeepTheLegacySemantics()
        {
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "res/", Data = new byte[0] },
                new ZipBuilder.EntrySpec { Name = "res/icon.png", Data = Payload(16) });

            using (Archive opened = ProductApi.OpenArchive(archive))
            {
                Assert.That(opened.Contains("RES/ICON.PNG"), Is.True);
                Assert.That(opened.Contains("res/missing.png"), Is.False);
                Assert.That(opened.IsFile(opened.GetEntry("res/")), Is.False, "a trailing slash marks a directory");
                Assert.That(opened.IsFile(opened.GetEntry("res/icon.png")), Is.True);
                Assert.That(opened.IsFile(null), Is.False, "a missing entry is not a file");
            }
        }

        [Test]
        public void RejectsAnArchiveOverTheEntryLimit_WithTheShippedMessage()
        {
            int limit = (int)ProductApi.SafeArchiveConstant("MAX_ENTRIES");
            byte[] archive = ZipBuilder.BuildWithEntryCount(limit + 1, zip64: true);

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            {
                ProductApi.OpenArchive(archive).Dispose();
            });

            Assert.That(ex.Message, Is.EqualTo(
                "zip declares " + (limit + 1) + " entries, over the " + limit + "-entry limit"),
                "the entry-count gate runs at open time and keeps the shipped wording");
        }

        [Test]
        public void AcceptsAnArchiveAtExactlyTheEntryLimit()
        {
            int limit = (int)ProductApi.SafeArchiveConstant("MAX_ENTRIES");
            byte[] archive = ZipBuilder.BuildWithEntryCount(limit, zip64: false);

            using (Archive opened = ProductApi.OpenArchive(archive))
            {
                Assert.That(opened.DeclaredEntryCount, Is.EqualTo(limit), "the gate is strictly greater-than");
            }
        }

        [Test]
        public void TreatsTheZip64SizeSentinelAsUnknown()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "assets/big.bin",
                SentinelSizes = true,
                Data = Payload(1024),
            });

            using (Archive opened = ProductApi.OpenArchive(archive))
            {
                object entry = opened.GetEntry("assets/big.bin");
                Assert.That(opened.DeclaredLength(entry), Is.EqualTo(-1L),
                    "0xFFFFFFFF is the zip64 'unknown' sentinel, not a 4 GiB size");

                long hardCap = 4 * 1024 * 1024;
                Assert.That(ProductApi.EntryByteLimit(opened.EntryCompressedLength(entry), hardCap),
                    Is.EqualTo(hardCap), "an unknown compressed size must fall back to the hard cap");
            }
        }

        [Test]
        public void RatioGateTightensTheCapBelowTheHardCap()
        {
            // A single DEFLATE stream cannot expand past RATIO_LIMIT, so no genuine archive
            // can trip this gate; what is pinnable is the limit it computes. For a small
            // compressed size the ratio allowance replaces a much larger hard cap.
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec { Name = EntryName, Data = Payload(4096) });

            using (Archive opened = ProductApi.OpenArchive(archive))
            {
                object entry = opened.GetEntry(EntryName);
                long compressed = opened.EntryCompressedLength(entry);
                long ratioLimit = ProductApi.SafeArchiveConstant("RATIO_LIMIT");
                long hardCap = long.MaxValue;
                long ratioAllowance = compressed * ratioLimit + 1024 * 1024;

                Assert.That(ProductApi.EntryByteLimit(compressed, hardCap), Is.EqualTo(ratioAllowance));
                Assert.That(ratioAllowance, Is.LessThan(hardCap), "the ratio allowance is the binding limit here");
                Assert.That(opened.ReadEntryBounded(entry, hardCap, What), Is.EqualTo(Payload(4096)),
                    "an entry below the ratio allowance is still readable");
            }
        }

        [Test]
        public void StopsAtTheCumulativeBudgetAcrossEntries()
        {
            byte[] first = Payload(600 * 1024);
            byte[] second = Payload(600 * 1024);
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "assets/first.bin", Data = first },
                new ZipBuilder.EntrySpec { Name = "assets/second.bin", Data = second });

            long budget = 1024 * 1024;
            using (Archive opened = ProductApi.OpenArchiveWithBudget(archive, budget))
            {
                Assert.That(opened.ReadEntryBounded(opened.GetEntry("assets/first.bin"), 2 * 1024 * 1024, "first"),
                    Is.EqualTo(first), "the first entry fits the budget");

                InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                    opened.ReadEntryBounded(opened.GetEntry("assets/second.bin"), 2 * 1024 * 1024, "second"));

                Assert.That(ex.Message, Does.Contain("second"), "the error names the read that crossed the budget");
                Assert.That(ex.Message, Does.Contain("handed out"));
                Assert.That(ex.Message, Does.Contain(budget.ToString()),
                    "the error reports the per-archive budget it crossed");
            }
        }

        [Test]
        public void KeepsReadingWhileTheCumulativeBudgetAllowsIt()
        {
            int length = 600 * 1024;
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "assets/first.bin", Data = Payload(length) },
                new ZipBuilder.EntrySpec { Name = "assets/second.bin", Data = Payload(length) });

            using (Archive opened = ProductApi.OpenArchiveWithBudget(archive, 2 * 1024 * 1024))
            {
                Assert.That(opened.ReadEntryBounded(opened.GetEntry("assets/first.bin"), 2 * 1024 * 1024, "first").Length,
                    Is.EqualTo(length));
                Assert.That(opened.ReadEntryBounded(opened.GetEntry("assets/second.bin"), 2 * 1024 * 1024, "second").Length,
                    Is.EqualTo(length), "the budget accumulates across entries but does not reset");
            }
        }

        [Test]
        public void BudgetIsPerArchiveInstance()
        {
            int length = 600 * 1024;
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "assets/first.bin", Data = Payload(length) });

            using (Archive first = ProductApi.OpenArchiveWithBudget(archive, length))
            using (Archive second = ProductApi.OpenArchiveWithBudget(archive, length))
            {
                Assert.That(first.ReadEntryBounded(first.GetEntry("assets/first.bin"), length, "first").Length,
                    Is.EqualTo(length));
                Assert.That(second.ReadEntryBounded(second.GetEntry("assets/first.bin"), length, "second").Length,
                    Is.EqualTo(length), "each SafeArchive instance gets its own budget");
            }
        }

        [Test]
        public void TheShippedBudgetIsOneGiB()
        {
            Assert.That(ProductApi.SafeArchiveConstant("MaxTotalReadBytes"), Is.EqualTo(1024L * 1024 * 1024),
                "production opens with MaxTotalReadBytes; the test seam must not drift from it");
        }

        [Test]
        public void RejectsAZeroCumulativeBudget()
        {
            byte[] archive = BuildStoredZip(Payload(16));
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => ProductApi.OpenArchiveWithBudget(archive, 0));
            Assert.That(ex.ParamName, Is.EqualTo("maxTotalReadBytes"));
        }

        [Test]
        public void RejectsANonSeekableStream_WithTheLegacyMessage()
        {
            byte[] archive = BuildStoredZip(Payload(64));
            using (NonSeekableStream stream = new NonSeekableStream(archive))
            {
                NotSupportedException ex = Assert.Throws<NotSupportedException>(
                    () => ProductApi.OpenArchive(stream));
                Assert.That(ex.Message, Is.EqualTo("zip containers must be opened from a seekable stream"),
                    "the seekability rejection text is part of the reader contract");
            }
        }

        [Test]
        public void NestedContainerBytesFeedTheInnerArchive()
        {
            byte[] manifest = Payload(2048);
            byte[] inner = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = manifest });
            byte[] outer = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "manifest.json", Data = Payload(64) },
                new ZipBuilder.EntrySpec { Name = "base.apk", Data = inner });

            using (Archive outerArchive = ProductApi.OpenArchive(outer))
            {
                object baseEntry = outerArchive.GetEntry("base.apk");
                Assert.That(baseEntry, Is.Not.Null);
                byte[] innerBytes = outerArchive.ReadEntryBounded(baseEntry, 512L * 1024 * 1024, "base apk");

                using (Archive innerArchive = ProductApi.OpenArchive(innerBytes))
                {
                    object manifestEntry = innerArchive.GetEntry("AndroidManifest.xml");
                    Assert.That(innerArchive.ReadEntryBounded(manifestEntry, 128L * 1024 * 1024, "AndroidManifest.xml"),
                        Is.EqualTo(manifest),
                        "the bytes handed out for a nested container must open as an archive of their own");
                }
            }
        }

        /// <summary>Deterministic bytes - no Random, so a failure is reproducible.</summary>
        private static byte[] Payload(int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(i * 31 + 7);
            }
            return payload;
        }

        /// <summary>
        /// A genuinely Stored entry: System.IO.Compression on .NET Framework emits
        /// Deflated even for CompressionLevel.NoCompression (this suite checks that),
        /// so the archive is assembled by TestSupport/ZipBuilder.
        /// </summary>
        private static byte[] BuildStoredZip(byte[] payload)
        {
            return ZipBuilder.Build(new ZipBuilder.EntrySpec { Name = EntryName, Data = payload });
        }

        private static byte[] BuildZip(byte[] payload, CompressionLevel level)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                using (ZipArchive archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(EntryName, level);
                    using (Stream entryStream = entry.Open())
                    {
                        entryStream.Write(payload, 0, payload.Length);
                    }
                }
                return buffer.ToArray();
            }
        }

        /// <summary>
        /// A readable stream that refuses to seek, like a pipe or a network response.
        /// ZipArchive needs random access, so SafeArchive must reject this before the
        /// archive is built, with the message the readers have always used.
        /// </summary>
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

            public override int Read(byte[] buffer, int offset, int count)
            {
                return inner.Read(buffer, offset, count);
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

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
