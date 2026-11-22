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
    }
}
