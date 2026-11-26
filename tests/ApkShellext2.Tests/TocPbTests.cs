using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using ApkQuickReader;
using ApkShellext2;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins both layers of the bundletool minimum-sdk support: the tolerant toc.pb walker
    /// (ApkQuickReader.TocPb) and the bundle reader that prefers the table of contents
    /// over the split's raised manifest value (bundletool writes 21 into every split).
    /// The wire bytes are built here so each broken link in the chain - an absent field, a
    /// wrong wire type, a truncated varint, a length past the end, deeper nesting, a field
    /// flood - is driven against the shipping parser.
    /// </summary>
    [TestFixture]
    public class TocPbTests
    {
        private static byte[] ResourcesFixture()
        {
            string path = RepoLayout.PathOf("tests/ApkShellext2.Tests/fixtures/spectroid-resources.arsc");
            Assert.That(path, Does.Exist, "fixture is missing; regenerate with fixtures\\make-fixtures.ps1");
            return File.ReadAllBytes(path);
        }

        /// <summary>A split apk whose manifest declares the sdk levels bundletool writes.</summary>
        private static byte[] BaseApk(int minSdk, int targetSdk)
        {
            AxmlBuilder.Element root = AxmlBuilder.Root("manifest")
                .Attr("package", "com.example.bundle")
                .Attr("versionName", "1.0")
                .Attr("versionCode", "1");
            root.Add("uses-sdk")
                .AndroidInt("minSdkVersion", minSdk)
                .AndroidInt("targetSdkVersion", targetSdk);
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = AxmlBuilder.Build(root) },
                new ZipBuilder.EntrySpec { Name = "resources.arsc", Data = ResourcesFixture() });
        }

        private static byte[] Apks(byte[] toc, int splitMinSdk, int splitTargetSdk)
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "toc.pb", Data = toc },
                new ZipBuilder.EntrySpec { Name = "splits/base-master.apk", Data = BaseApk(splitMinSdk, splitTargetSdk) });
        }

        private static string TempFile(string extension, byte[] bytes)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "apkshellext-toc-" + Guid.NewGuid().ToString("N") + extension);
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
        public void ASingleVariantGivesItsMinimum()
        {
            int minSdk;

            Assert.That(ProductApi.TryReadTocMinSdk(TocPbBuilder.Toc(TocPbBuilder.Variant(16)), out minSdk), Is.True);
            Assert.That(minSdk, Is.EqualTo(16));
        }

        [Test]
        public void TheSmallestVariantWins()
        {
            int minSdk;
            byte[] toc = TocPbBuilder.Toc(
                TocPbBuilder.Variant(23), TocPbBuilder.Variant(16), TocPbBuilder.Variant(30));

            Assert.That(ProductApi.TryReadTocMinSdk(toc, out minSdk), Is.True);
            Assert.That(minSdk, Is.EqualTo(16));
        }

        [Test]
        public void TheSmallestValueInsideOneTargetingWins()
        {
            int minSdk;

            Assert.That(ProductApi.TryReadTocMinSdk(TocPbBuilder.Toc(TocPbBuilder.Variant(23, 16)), out minSdk), Is.True);
            Assert.That(minSdk, Is.EqualTo(16));
        }

        [Test]
        public void TheSmallestValueAcrossVariantsAndValuesWins()
        {
            int minSdk;
            byte[] toc = TocPbBuilder.Toc(
                TocPbBuilder.Variant(24, 21), TocPbBuilder.Variant(16, 23));

            Assert.That(ProductApi.TryReadTocMinSdk(toc, out minSdk), Is.True);
            Assert.That(minSdk, Is.EqualTo(16));
        }

        [Test]
        public void AMissingLinkIsNotReadable()
        {
            int minSdk;
            // a variant whose targeting carries no sdk_version_targeting at all
            byte[] toc = TocPbBuilder.Toc(TocPbBuilder.BytesField(1, new byte[0]));

            Assert.That(ProductApi.TryReadTocMinSdk(toc, out minSdk), Is.False);
        }

        [Test]
        public void AnEmptyInputIsNotReadable()
        {
            int minSdk;

            Assert.That(ProductApi.TryReadTocMinSdk(null, out minSdk), Is.False);
            Assert.That(ProductApi.TryReadTocMinSdk(new byte[0], out minSdk), Is.False);
        }

        [Test]
        public void ATruncatedVarintIsNotReadable()
        {
            int minSdk;
            // a root link that is a varint field and ends before its value
            byte[] toc = { 0x08 };

            Assert.That(ProductApi.TryReadTocMinSdk(toc, out minSdk), Is.False);
        }

        [Test]
        public void ALengthPastTheEndIsNotReadable()
        {
            int minSdk;
            // field 1, wire 2, a length of 0x7F, and nothing behind it
            byte[] toc = { 0x0A, 0x7F, 0x00 };

            Assert.That(ProductApi.TryReadTocMinSdk(toc, out minSdk), Is.False);
        }

        [Test]
        public void ANestingDeeperThanTheChainIsNotReadable()
        {
            int minSdk;
            byte[] payload = TocPbBuilder.Int32Value(16);
            for (int i = 0; i < 12; i++) payload = TocPbBuilder.BytesField(1, payload);

            Assert.That(ProductApi.TryReadTocMinSdk(payload, out minSdk), Is.False);
        }

        [Test]
        public void AFieldFloodIsNotReadable()
        {
            int minSdk;
            int budget = (int)ProductApi.TocPbConstant("MaxFieldsPerMessage");
            var fields = new List<byte[]>();
            for (int i = 0; i <= budget; i++) fields.Add(TocPbBuilder.VarintField(2, 0));

            Assert.That(ProductApi.TryReadTocMinSdk(TocPbBuilder.Concat(fields.ToArray()), out minSdk), Is.False);
        }

        [Test]
        public void TheFieldBudgetIsExactAtTheLimit()
        {
            int minSdk;
            int budget = (int)ProductApi.TocPbConstant("MaxFieldsPerMessage");
            var fields = new List<byte[]>();
            fields.Add(TocPbBuilder.BytesField(1, TocPbBuilder.Variant(16)));
            for (int i = 1; i < budget; i++) fields.Add(TocPbBuilder.VarintField(2, 0));

            Assert.That(ProductApi.TryReadTocMinSdk(TocPbBuilder.Concat(fields.ToArray()), out minSdk), Is.True);
            Assert.That(minSdk, Is.EqualTo(16));
        }

        [Test]
        public void ANonPositiveMinimumIsNotReadable()
        {
            int minSdk;

            Assert.That(ProductApi.TryReadTocMinSdk(TocPbBuilder.Toc(TocPbBuilder.Variant(0)), out minSdk), Is.False);
            Assert.That(ProductApi.TryReadTocMinSdk(TocPbBuilder.Toc(TocPbBuilder.Variant(-1)), out minSdk), Is.False);
        }

        [Test]
        public void AGroupsWireTypeIsNotReadable()
        {
            int minSdk;
            // a readable variant, then a group (wire 3) this reader does not walk
            byte[] toc = TocPbBuilder.Concat(
                TocPbBuilder.BytesField(1, TocPbBuilder.Variant(16)),
                TocPbBuilder.Tag(2, 3));

            Assert.That(ProductApi.TryReadTocMinSdk(toc, out minSdk), Is.False);
        }

        [Test]
        public void UnknownFieldsInsideTheChainAreSkipped()
        {
            int minSdk;
            byte[] variant = TocPbBuilder.Concat(
                TocPbBuilder.VarintField(2, 1),                                       // variant_number
                TocPbBuilder.BytesField(1, TocPbBuilder.Targeting(16)),               // targeting
                TocPbBuilder.BytesField(5, new byte[] { 0x01 }));                     // variant_id
            byte[] toc = TocPbBuilder.Concat(
                TocPbBuilder.BytesField(1, variant),                                  // the variant to keep
                TocPbBuilder.VarintField(2, 40));                                     // apk_set on the root

            Assert.That(ProductApi.TryReadTocMinSdk(toc, out minSdk), Is.True);
            Assert.That(minSdk, Is.EqualTo(16));
        }

        [Test]
        public void TheDocumentCapIsEnforced()
        {
            int minSdk;
            byte[] tooBig = new byte[ProductApi.TocPbConstant("MaxTocPbBytes") + 1];

            Assert.That(ProductApi.TryReadTocMinSdk(tooBig, out minSdk), Is.False);
        }

        [Test]
        public void TheCapsArePinned()
        {
            Assert.That(ProductApi.TocPbConstant("MaxTocPbBytes"), Is.EqualTo(2L * 1024 * 1024));
            Assert.That(ProductApi.TocPbConstant("MaxDepth"), Is.EqualTo(8L));
            Assert.That(ProductApi.TocPbConstant("MaxFieldsPerMessage"), Is.EqualTo(4096L));
        }

        [Test]
        public void AnApksWithATocReportsTheTocMinimum()
        {
            byte[] toc = TocPbBuilder.Toc(TocPbBuilder.Variant(16), TocPbBuilder.Variant(23));
            string path = TempFile(".apks", Apks(toc, 21, 30));
            try
            {
                using (SplitApkBundleReader reader = new SplitApkBundleReader(path))
                {
                    // bundletool raised the split's manifest from 16 to 21; the toc has the real value
                    Assert.That(reader.MinSdk, Is.EqualTo("16"));
                    Assert.That(reader.TargetSdk, Is.EqualTo("30"));
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        [Test]
        public void AnApksWithoutATocKeepsTheSplitMinimum()
        {
            byte[] bundle = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "splits/base-master.apk",
                Data = BaseApk(21, 30)
            });
            string path = TempFile(".apks", bundle);
            try
            {
                using (SplitApkBundleReader reader = new SplitApkBundleReader(path))
                {
                    Assert.That(reader.MinSdk, Is.EqualTo("21"));
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        [Test]
        public void ABrokenTocKeepsTheSplitMinimum()
        {
            // field 1, wire 2, then a length that runs past the end
            byte[] toc = { 0x0A, 0x7F, 0x00 };
            string path = TempFile(".apks", Apks(toc, 21, 30));
            try
            {
                using (SplitApkBundleReader reader = new SplitApkBundleReader(path))
                {
                    Assert.That(reader.MinSdk, Is.EqualTo("21"));
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        [Test]
        public void TheCsvExportOfAnApksUsesTheTocMinimum()
        {
            byte[] toc = TocPbBuilder.Toc(TocPbBuilder.Variant(16), TocPbBuilder.Variant(23));
            string path = TempFile(".apks", Apks(toc, 21, 30));
            try
            {
                var output = new MemoryStream();
                using (output)
                {
                    PackageCsvWriter.Write(output, new[] { path }, null, default(CancellationToken), null);
                    string csv = CsvText(output);
                    Assert.That(CsvColumn(csv, "MinSdk"), Is.EqualTo("16"));
                    Assert.That(CsvColumn(csv, "TargetSdk"), Is.EqualTo("30"));
                    Assert.That(CsvColumn(csv, "Status"), Is.EqualTo(PackageCsvWriter.StatusOk));
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        private static string CsvText(MemoryStream output)
        {
            byte[] bytes = output.ToArray();
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        private static string CsvColumn(string csv, string column)
        {
            string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] header = lines[0].Split(',');
            string[] row = lines[1].Split(',');
            int index = Array.IndexOf(header, column);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "column " + column + " is missing");
            return row[index];
        }
    }
}
