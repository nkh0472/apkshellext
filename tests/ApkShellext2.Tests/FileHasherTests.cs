using System;
using System.IO;
using NUnit.Framework;
using ApkShellext2;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the file hashing contract: one lowercase SHA-256 and one lowercase XXH3-64
    /// value per file, computed in one streaming pass. The reference values come from
    /// python xxhash 4.1.0 and hashlib over the same files.
    /// </summary>
    [TestFixture]
    public class FileHasherTests
    {
        private string directory;

        [SetUp]
        public void CreateDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "apkshellext-hash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void ComputesBothDigestsOfATemporaryFile()
        {
            string path = Path.Combine(directory, "abc.bin");
            File.WriteAllBytes(path, new byte[] { 0x61, 0x62, 0x63 });

            FileHash hash = FileHasher.Compute(path);

            Assert.That(hash.Sha256, Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.That(hash.Xxh3, Is.EqualTo("78af5f94892f3950"));
            Assert.That(hash.Sha256.Length, Is.EqualTo(64));
            Assert.That(hash.Xxh3.Length, Is.EqualTo(16));
            Assert.That(hash.Sha256, Is.EqualTo(hash.Sha256.ToLowerInvariant()));
            Assert.That(hash.Xxh3, Is.EqualTo(hash.Xxh3.ToLowerInvariant()));
        }

        [TestCase(@"F:\Backups\apks\Spectroid_org.intoorbit.spectrum_1.1.2(31).apk",
            "f4de0b3ed7be3619d81061d793f08cc68ca05283452c9a3c52cfc792a98a4083",
            "2191664a09bf8272")]
        [TestCase(@"F:\Backups\apks\Magisk_com.topjohnwu.magisk_31.0(31000).apk",
            "2c8a488b9a5293e578e95ae4f07e3c57aba4feec4a52ca4dd852a2692d6dd4e8",
            "3b42c454a3483aac")]
        [TestCase(@"F:\Backups\apks\南方航空_com.csair.mbp_4.6.4(20231214).apk",
            "40727c55187a501694ad781b610ed6bfe730f54b049f0ca550ee25f72b2d5bee",
            "f29efd2728f366a5")]
        public void MatchesTheReferenceDigestsOfARealPackage(string path, string sha256, string xxh3)
        {
            Assert.That(File.Exists(path), Is.True, "sample package is missing: " + path);

            FileHash hash = FileHasher.Compute(path);

            Assert.That(hash.Sha256, Is.EqualTo(sha256));
            Assert.That(hash.Xxh3, Is.EqualTo(xxh3));
        }

        [Test]
        public void RejectsANullPath()
        {
            Assert.Throws<ArgumentNullException>(() => FileHasher.Compute(null));
        }

        [Test]
        public void ReportsAMissingFile()
        {
            Assert.Throws<FileNotFoundException>(
                () => FileHasher.Compute(Path.Combine(directory, "missing.apk")));
        }
    }
}
