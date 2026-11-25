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

        [TestCase("spectroid-resources.arsc",
            "4afd28509ee4a71df24ee3409e8bf1ca7be19b7168f45a9bb6d4f623e51182da",
            "406ab5a10dc5885c")]
        [TestCase("spectroid-arsc-dup.bin",
            "6855bcdb764b0aa5f50b21adac0f9cdf15374f86ce19976bbfc5735af8d6566c",
            "d5928c211b6fa22e")]
        public void MatchesTheReferenceDigestsOfACheckedInFixture(string name, string sha256, string xxh3)
        {
            string path = RepoLayout.PathOf("tests/ApkShellext2.Tests/fixtures/" + name);
            Assert.That(File.Exists(path), Is.True, "fixture is missing: " + path);

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
