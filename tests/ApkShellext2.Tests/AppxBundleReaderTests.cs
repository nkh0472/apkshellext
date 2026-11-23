using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// AppxBundleReader: the .appxbundle path. The reader scans the bundle manifest
    /// for the application package, then hands
    /// the nested .appx to AppxReader. Each test pins one of the limits that keep a
    /// malformed bundle from looping forever or inflating an arbitrary manifest.
    /// </summary>
    [TestFixture]
    public class AppxBundleReaderTests
    {
        private static AppxBundleReader Open(byte[] bundle)
        {
            return new AppxBundleReader(new MemoryStream(bundle, false));
        }

        private static byte[] BundleWithManifest(string manifest, byte[] nestedAppx = null)
        {
            var manifestEntry = new ZipBuilder.EntrySpec
            {
                Name = AppxFixtures.BundleManifestEntry,
                Data = Encoding.UTF8.GetBytes(manifest)
            };
            if (nestedAppx == null)
                return ZipBuilder.Build(manifestEntry);
            return ZipBuilder.Build(manifestEntry, new ZipBuilder.EntrySpec
            {
                Name = "Test.appx",
                Data = nestedAppx
            });
        }

        [Test]
        public void ReadsTheApplicationFromTheNestedAppx()
        {
            byte[] bundle = AppxFixtures.Bundle(AppxFixtures.Appx());
            using (AppxBundleReader reader = Open(bundle))
            {
                Assert.That(reader.AppName, Is.EqualTo(AppxFixtures.DefaultAppName));
                Assert.That(reader.Version, Is.EqualTo(AppxFixtures.DefaultVersion));
                Assert.That(reader.PackageName, Is.EqualTo(AppxFixtures.DefaultPackageName));
                Assert.That(reader.Publisher, Is.EqualTo("Test Publisher"));
                Assert.That(reader.Type, Is.EqualTo(AppPackageReader.AppType.WindowsPhoneAppBundle));
            }
        }

        [Test]
        public void ManifestDeclaringOver16MiBIsRejectedBeforeInflate()
        {
            // The gate reads the declared size from the central directory, so a small
            // stored payload with a huge declared length is enough to exercise it -
            // nothing is inflated for this rejection.
            byte[] manifest = Encoding.UTF8.GetBytes(AppxFixtures.BundleManifest("Test.appx"));
            byte[] bundle = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = AppxFixtures.BundleManifestEntry,
                Data = manifest,
                DeclaredUncompressedSize = 16L * 1024 * 1024 + 1
            });

            Assert.That(
                () => Open(bundle),
                Throws.TypeOf<InvalidDataException>()
                      .With.Message.Contains("16777216")
                      .And.Message.Contains("declares"));
        }

        [Test]
        [Timeout(5000)]
        public void ManifestWithoutAnyPackageThrowsInsteadOfLooping()
        {
            // The scan must not read past a missing Package element: a manifest with
            // no such element ends in the "Cannot find application" error, not a spin.
            string manifest = "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<Bundle xmlns=\"http://schemas.microsoft.com/appx/2013/bundle\">"
                + "<Identity Name=\"Test.Bundle\" Version=\"1.0.0.0\" />"
                + "</Bundle>";
            byte[] bundle = BundleWithManifest(manifest);

            Assert.That(
                () => Open(bundle),
                Throws.Exception.With.Message.Contains("Cannot find application"));
        }

        [Test]
        [Timeout(15000)]
        public void ManifestWithOver4096PackagesIsRejected()
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.Append("<Bundle xmlns=\"http://schemas.microsoft.com/appx/2013/bundle\">");
            xml.Append("<Identity Name=\"Test.Bundle\" Version=\"1.0.0.0\" /><Packages>");
            for (int i = 0; i < 4097; i++)
            {
                xml.Append("<Package Type=\"resource\" FileName=\"r").Append(i).Append(".appx\" />");
            }
            xml.Append("</Packages></Bundle>");
            byte[] bundle = BundleWithManifest(xml.ToString());

            Assert.That(
                () => Open(bundle),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("4096"));
        }

        [Test]
        public void ApplicationPackageWithEmptyFileNameIsRejected()
        {
            byte[] bundle = BundleWithManifest(AppxFixtures.BundleManifest(""), AppxFixtures.Appx());

            Assert.That(
                () => Open(bundle),
                Throws.TypeOf<EntryPointNotFoundException>().With.Message.Contains("empty"));
        }

        [Test]
        public void ApplicationPackageWithOverlongFileNameIsRejected()
        {
            string name = new string('a', 1025) + ".appx";
            byte[] bundle = BundleWithManifest(AppxFixtures.BundleManifest(name), AppxFixtures.Appx());

            Assert.That(
                () => Open(bundle),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("1024"));
        }

        [Test]
        public void MissingNestedAppxIsReportedByName()
        {
            byte[] bundle = BundleWithManifest(AppxFixtures.BundleManifest("Missing.appx"));

            Assert.That(
                () => Open(bundle),
                Throws.TypeOf<EntryPointNotFoundException>().With.Message.Contains("Missing.appx"));
        }

        [Test]
        public void NestedAppxWithoutManifestIsRejected()
        {
            byte[] inner = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "Assets/Logo.png",
                Data = new byte[] { 1, 2, 3 }
            });
            byte[] bundle = AppxFixtures.Bundle(inner);

            Assert.That(
                () => Open(bundle),
                Throws.TypeOf<EntryPointNotFoundException>().With.Message.Contains("AppxManifest.xml"));
        }
    }
}
