using System;
using System.IO;
using ApkQuickReader;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The packer name comes from the id-value pairs of the APK Signing Block. The block is
    /// spliced into a plain zip here, so the cases a real corpus rarely holds (an unknown id,
    /// a pair that overruns its block, a footer size that disagrees with the leading size, a
    /// block past the reader's cap, a missing end record) are still exercised against the
    /// shipped tail reader.
    /// </summary>
    [TestFixture]
    public class SigningBlockTests
    {
        private const string ManifestEntry = "AndroidManifest.xml";
        private const string ResourcesEntry = "resources.arsc";
        private const string ResourcesFixture = "spectroid-resources.arsc";

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf("tests/ApkShellext2.Tests/fixtures/" + name);
            Assert.That(path, Does.Exist, "fixture {0} is missing; regenerate with fixtures\\make-fixtures.ps1", name);
            return File.ReadAllBytes(path);
        }

        private static byte[] PlainZip()
        {
            return ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "assets/readme.txt",
                Data = new byte[] { 1, 2, 3, 4 }
            });
        }

        private static string PackerOf(byte[] block)
        {
            byte[] apk = SigningBlockBuilder.Insert(PlainZip(), block);
            using (MemoryStream stream = new MemoryStream(apk, 0, apk.Length, false, false))
            {
                return ProductApi.SigningBlockPacker(stream);
            }
        }

        private static string PackerOfRaw(byte[] apk)
        {
            using (MemoryStream stream = new MemoryStream(apk, 0, apk.Length, false, false))
            {
                return ProductApi.SigningBlockPacker(stream);
            }
        }

        private static byte[] ApkBytes(byte[] block)
        {
            AxmlBuilder.Element root = AxmlBuilder.Root("manifest")
                .Attr("package", "com.example.app")
                .Attr("versionName", "1.0")
                .Attr("versionCode", "1");
            root.Add("application").AndroidAttr("label", "Example");
            byte[] apk = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = ManifestEntry, Data = AxmlBuilder.Build(root) },
                new ZipBuilder.EntrySpec { Name = ResourcesEntry, Data = Fixture(ResourcesFixture) });
            return SigningBlockBuilder.Insert(apk, block);
        }

        private static string TempFile(string extension, byte[] bytes)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "apkshellext-signing-" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        [Test]
        public void TheWallePairIsNamed()
        {
            Assert.That(PackerOf(SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Walle))),
                Is.EqualTo("Walle"));
        }

        [Test]
        public void TheByteDancePairIsNamed()
        {
            Assert.That(PackerOf(SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.ByteDance))),
                Is.EqualTo("ByteDance"));
        }

        [Test]
        public void TheFrostingPairIsNamed()
        {
            Assert.That(PackerOf(SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Frosting))),
                Is.EqualTo("Frosting"));
        }

        [Test]
        public void ThePackerNgPairIsNamed()
        {
            Assert.That(PackerOf(SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.PackerNg))),
                Is.EqualTo("PackerNG"));
        }

        [Test]
        public void TheVasDollyV2PairIsNamed()
        {
            Assert.That(PackerOf(SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.VasDollyV2))),
                Is.EqualTo("VasDollyV2"));
        }

        [Test]
        public void TheFirstKnownPairWins()
        {
            // A store re-signs a packed apk and its own pair lands first; the first known id
            // names the tool, a later one is not consulted.
            byte[] block = SigningBlockBuilder.Block(
                SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Unknown, new byte[] { 9 }),
                SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Walle),
                SigningBlockBuilder.Pair.Of(SigningBlockBuilder.ByteDance));
            Assert.That(PackerOf(block), Is.EqualTo("Walle"));
        }

        [Test]
        public void AnUnknownOnlyBlockIsNotNamed()
        {
            byte[] block = SigningBlockBuilder.Block(
                SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Unknown),
                SigningBlockBuilder.Pair.Of(0x12345678u, new byte[] { 1, 2, 3 }));
            Assert.That(PackerOf(block), Is.Empty);
        }

        [Test]
        public void AZipWithoutABlockIsNotNamed()
        {
            Assert.That(PackerOfRaw(PlainZip()), Is.Empty);
        }

        [Test]
        public void AFooterSizeThatDisagreesWithTheBlockIsIgnored()
        {
            // the trailing size says the block is eight bytes longer than it is, so the
            // leading size field no longer matches and the pairs cannot be trusted
            byte[] block = SigningBlockBuilder.Block(8, SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Walle));
            Assert.That(PackerOf(block), Is.Empty);
        }

        [Test]
        public void APairThatOverrunsItsValueIsIgnored()
        {
            byte[] block = SigningBlockBuilder.Block(
                SigningBlockBuilder.Pair.Declaring(SigningBlockBuilder.Unknown, 4096, new byte[] { 1, 2, 3, 4 }));
            Assert.That(PackerOf(block), Is.Empty);
        }

        [Test]
        public void ABlockOverTheSizeCapIsIgnored()
        {
            byte[] value = new byte[1024 * 1024];
            byte[] block = SigningBlockBuilder.Block(
                SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Walle, value));
            Assert.That(PackerOf(block), Is.Empty);
        }

        [Test]
        public void AnArchiveWithoutAnEndRecordIsIgnored()
        {
            byte[] zip = PlainZip();
            int endOfCentralDirectory = SigningBlockBuilder.EndOfCentralDirectory(zip);
            Assert.That(endOfCentralDirectory, Is.GreaterThan(0));
            byte[] cut = new byte[endOfCentralDirectory];
            Array.Copy(zip, 0, cut, 0, cut.Length);
            Assert.That(PackerOfRaw(cut), Is.Empty);
        }

        [Test]
        public void ATruncatedEndRecordIsIgnored()
        {
            byte[] zip = PlainZip();
            byte[] cut = new byte[zip.Length - 10];
            Array.Copy(zip, 0, cut, 0, cut.Length);
            Assert.That(PackerOfRaw(cut), Is.Empty);
        }

        [Test]
        public void AnEmptyStreamIsNotNamed()
        {
            Assert.That(PackerOfRaw(new byte[0]), Is.Empty);
        }

        [Test]
        public void AnApkIsNamedFromItsOwnPath()
        {
            string path = TempFile(".apk", ApkBytes(
                SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Walle))));
            try
            {
                using (ApkReader reader = new ApkReader(path))
                {
                    Assert.That(reader.Packer, Is.EqualTo("Walle"));
                    Assert.That(reader.AppName, Is.EqualTo("Example"));
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        [Test]
        public void ASplitBundleTakesThePackersNameFromTheBaseApk()
        {
            // the container has no signing block of its own; the base apk inside it was packed
            byte[] baseApk = ApkBytes(
                SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.ByteDance)));
            byte[] bundle = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "base.apk", Data = baseApk },
                new ZipBuilder.EntrySpec { Name = "split_config.en.apk", Data = baseApk });
            string path = TempFile(".apks", bundle);
            try
            {
                using (SplitApkBundleReader reader = new SplitApkBundleReader(path))
                {
                    Assert.That(reader.Packer, Is.EqualTo("ByteDance"));
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        [Test]
        public void AnAabNamesNoPacker()
        {
            AxmlBuilder.Element root = AxmlBuilder.Root("manifest")
                .Attr("package", "com.example.app")
                .Attr("versionName", "1.0");
            root.Add("application").AndroidAttr("label", "Example");
            byte[] aab = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "base/manifest/AndroidManifest.xml",
                Data = AxmlBuilder.Build(root)
            });
            string path = TempFile(".aab", aab);
            try
            {
                using (AabReader reader = new AabReader(path))
                {
                    Assert.That(reader.Packer, Is.Empty);
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }
    }
}
