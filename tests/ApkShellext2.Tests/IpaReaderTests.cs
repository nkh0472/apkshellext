using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using PlistCS;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// IpaReader: the .ipa path. It is the only reader that parses a binary plist
    /// itself, so these tests both check the metadata mapping and prove that the
    /// plist depth cap (T-C) is reached through the reader's own constructor instead
    /// of only through Plist.readPlist.
    /// </summary>
    [TestFixture]
    public class IpaReaderTests
    {
        private const string AppRoot = "Payload/MyApp.app/";
        private const string InfoPlistEntry = AppRoot + "Info.plist";

        private static byte[] InfoPlist()
        {
            return BinaryPlistBuilder.Build(new List<byte[]>
            {
                BinaryPlistBuilder.DictionaryObject(
                    1, 2,   // CFBundleDisplayName -> "My App"
                    3, 4,   // CFBundleShortVersionString -> "2.0.1"
                    5, 6,   // CFBundleVersion -> "42"
                    7, 8,   // CFBundleIdentifier -> "com.example.app" (extended string form)
                    9, 10), // CFBundleIconFile -> "AppIcon60x60"
                BinaryPlistBuilder.StringObject("CFBundleDisplayName"),
                BinaryPlistBuilder.StringObject("My App"),
                BinaryPlistBuilder.StringObject("CFBundleShortVersionString"),
                BinaryPlistBuilder.StringObject("2.0.1"),
                BinaryPlistBuilder.StringObject("CFBundleVersion"),
                BinaryPlistBuilder.StringObject("42"),
                BinaryPlistBuilder.StringObject("CFBundleIdentifier"),
                BinaryPlistBuilder.StringObject("com.example.app"),
                BinaryPlistBuilder.StringObject("CFBundleIconFile"),
                BinaryPlistBuilder.StringObject("AppIcon60x60")
            });
        }

        private static byte[] ITunesMetadata()
        {
            return BinaryPlistBuilder.Build(new List<byte[]>
            {
                BinaryPlistBuilder.DictionaryObject(1, 2, 3, 4),
                BinaryPlistBuilder.StringObject("copyright"),
                BinaryPlistBuilder.StringObject("(c) Test Corp"),
                BinaryPlistBuilder.StringObject("itemId"),
                BinaryPlistBuilder.IntObject(123)
            });
        }

        private static byte[] Ipa(byte[] infoPlist, byte[] iTunesMetadata = null)
        {
            var entries = new List<ZipBuilder.EntrySpec>
            {
                new ZipBuilder.EntrySpec { Name = InfoPlistEntry, Data = infoPlist }
            };
            if (iTunesMetadata != null)
            {
                entries.Add(new ZipBuilder.EntrySpec { Name = "iTunesMetadata.plist", Data = iTunesMetadata });
            }
            return ZipBuilder.Build(entries.ToArray());
        }

        [Test]
        public void ReadsTheMetadataFromTheInfoPlist()
        {
            using (IpaReader reader = new IpaReader(new MemoryStream(Ipa(InfoPlist()), false)))
            {
                Assert.That(reader.AppName, Is.EqualTo("My App"));
                Assert.That(reader.Version, Is.EqualTo("2.0.1"));
                Assert.That(reader.Revision, Is.EqualTo("42"));
                Assert.That(reader.PackageName, Is.EqualTo("com.example.app"));
                Assert.That(reader.Type, Is.EqualTo(AppPackageReader.AppType.iOSApp));
            }
        }

        [Test]
        public void PublisherAndAppIdComeFromTheItunesMetadata()
        {
            using (IpaReader reader = new IpaReader(new MemoryStream(Ipa(InfoPlist(), ITunesMetadata()), false)))
            {
                Assert.That(reader.Publisher, Is.EqualTo("(c) Test Corp"));
                Assert.That(reader.AppID, Is.EqualTo("123"));
            }
        }

        [Test]
        public void MissingItunesMetadataLeavesPublisherEmpty()
        {
            using (IpaReader reader = new IpaReader(new MemoryStream(Ipa(InfoPlist()), false)))
            {
                Assert.That(reader.Publisher, Is.Empty);
                Assert.That(reader.AppID, Is.Empty);
            }
        }

        [Test]
        public void MissingInfoPlistThrowsEntryPointNotFound()
        {
            byte[] ipa = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = AppRoot + "AppIcon60x60.png",
                Data = new byte[] { 1, 2, 3 }
            });

            Assert.That(
                () => new IpaReader(new MemoryStream(ipa, false)),
                Throws.TypeOf<EntryPointNotFoundException>().With.Message.Contains("info.plist"));
        }

        [Test]
        public void InfoPlistAtTheArchiveRootIsNotAccepted()
        {
            // The reader anchors the app root with (Payload/.*\.app/)Info\.plist;
            // a stray root Info.plist must not be treated as an app bundle.
            byte[] ipa = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "Info.plist",
                Data = InfoPlist()
            });

            Assert.That(
                () => new IpaReader(new MemoryStream(ipa, false)),
                Throws.TypeOf<EntryPointNotFoundException>());
        }

        [Test]
        [Timeout(5000)]
        public void InfoPlistOver32LevelsIsRejectedByThePlistsDepthCap()
        {
            // Same shape as PlistDepthTests: 33 chained arrays and a leaf integer, so
            // the 34th parse (the leaf) hits the depth guard. Reaching it through
            // IpaReader proves the cap survives the reader's own boundary.
            const int depth = 33;
            List<byte[]> objects = new List<byte[]>(depth + 1);
            for (int i = 0; i < depth; i++)
            {
                objects.Add(BinaryPlistBuilder.ArrayObject((byte)(i + 1)));
            }
            objects.Add(BinaryPlistBuilder.IntObject(1));
            byte[] deepPlist = BinaryPlistBuilder.Build(objects);

            Assert.That(
                () => new IpaReader(new MemoryStream(Ipa(deepPlist), false)),
                Throws.TypeOf<PlistFormatException>().With.Message.Contains("nesting exceeds 32"));
        }

        [Test]
        public void IconIsNullWhenThePlistNamesAMissingFile()
        {
            using (IpaReader reader = new IpaReader(new MemoryStream(Ipa(InfoPlist()), false)))
            {
                Assert.That(reader.Icon, Is.Null);
            }
        }
    }
}
