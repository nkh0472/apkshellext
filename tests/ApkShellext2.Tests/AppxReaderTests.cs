using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// AppxReader: the plain-.appx path. These tests pin the manifest parse and the
    /// bounded manifest read that keeps a malformed package from inflating an
    /// arbitrary amount of XML into the shell process.
    /// </summary>
    [TestFixture]
    public class AppxReaderTests
    {
        private static AppxReader Open(byte[] package)
        {
            return new AppxReader(new MemoryStream(package, false));
        }

        [Test]
        public void ReadsTheIdentityAndTheDisplayProperties()
        {
            using (AppxReader reader = Open(AppxFixtures.Appx()))
            {
                Assert.That(reader.AppName, Is.EqualTo("Test App"));
                Assert.That(reader.Version, Is.EqualTo("1.2.3.4"));
                Assert.That(reader.PackageName, Is.EqualTo("Test.App"));
                Assert.That(reader.AppID, Is.EqualTo(AppxFixtures.DefaultProductId));
                Assert.That(reader.Type, Is.EqualTo(AppPackageReader.AppType.WindowsPhoneApp));
            }
        }

        [Test]
        public void PublisherIsUnwrappedFromTheCnComponent()
        {
            // Windows stores "CN=..., O=..." and the shell only shows the CN.
            using (AppxReader reader = Open(AppxFixtures.Appx()))
            {
                Assert.That(reader.Publisher, Is.EqualTo("Test Publisher"));
            }
        }

        [Test]
        public void MissingManifestThrowsEntryPointNotFound()
        {
            byte[] package = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "Assets/Logo.png",
                Data = new byte[] { 1, 2, 3 }
            });

            Assert.That(
                () => Open(package),
                Throws.TypeOf<EntryPointNotFoundException>().With.Message.Contains("AppxManifest.xml"));
        }

        [Test]
        public void ManifestOver128MiBIsRejectedByTheBoundedRead()
        {
            // MAX_MANIFEST is 128 MiB. A deflate bomb keeps the fixture small on disk
            // while the reader still has to walk past the 128 MiB cap byte by byte;
            // the point is that it stops there instead of handing the whole thing to
            // XmlDocument.
            const int declared = 129 * 1024 * 1024;
            byte[] bomb = new byte[declared];
            byte[] package = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = AppxFixtures.AppxManifestEntry,
                Deflate = true,
                Data = bomb
            });

            Assert.That(
                () => Open(package),
                Throws.TypeOf<InvalidDataException>()
                      .With.Message.Contains("AppxManifest.xml")
                      .And.Message.Contains("decompressed size exceeds safety cap"));
        }
    }
}
