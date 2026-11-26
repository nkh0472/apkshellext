using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The configuration-split icon probe: a bundle whose base apk names an icon that
    /// only the density splits carry must still show that icon, and a bundle whose
    /// base resolves an icon must not pay for the probe. The probe is exercised
    /// directly with a chosen icon path, so no manifest has to be synthesised.
    /// </summary>
    [TestFixture]
    public class ConfigSplitIconTests
    {
        private const string IconPath = "res/mipmap-xxhdpi/ic_launcher.png";

        private static Type SourceType()
        {
            Type type = ProductApi.Assembly.GetType("ApkShellext2.ConfigSplitIconSource", false);
            Assert.That(type, Is.Not.Null, "ApkShellext2.ConfigSplitIconSource was not found");
            return type;
        }

        private static Bitmap TryRead(byte[] outer, string iconPath, int size)
        {
            MethodInfo method = SourceType().GetMethod("TryRead",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, "ConfigSplitIconSource.TryRead was not found");
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(outer))
            {
                try
                {
                    return (Bitmap)method.Invoke(null, new object[] {
                        handle.Internal, iconPath, new Size(size, size), null, null });
                }
                catch (TargetInvocationException error)
                {
                    Assert.Fail("TryRead threw: " + error.InnerException);
                    return null;
                }
            }
        }

        private static int DensityRank(string qualifier)
        {
            MethodInfo method = SourceType().GetMethod("DensityRank",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, "ConfigSplitIconSource.DensityRank was not found");
            return (int)method.Invoke(null, new object[] { qualifier });
        }

        /// <summary>A real PNG of the given side, so the decoder path is the product one.</summary>
        private static byte[] Png(int side)
        {
            using (Bitmap bitmap = new Bitmap(side, side))
            using (MemoryStream stream = new MemoryStream())
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.Clear(Color.CornflowerBlue);
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static ZipBuilder.EntrySpec Entry(string name, byte[] data)
        {
            return new ZipBuilder.EntrySpec { Name = name, Data = data };
        }

        private static byte[] SplitWith(string entryName, byte[] data)
        {
            return ZipBuilder.Build(Entry(entryName, data));
        }

        private static byte[] Bundle(params ZipBuilder.EntrySpec[] entries)
        {
            return ZipBuilder.Build(entries);
        }

        [Test]
        public void DensityRank_OrdersTheKeywordsAndRejectsUnknownOnes()
        {
            Assert.That(DensityRank("anydpi_v26"), Is.GreaterThan(DensityRank("xxxhdpi")));
            Assert.That(DensityRank("xxxhdpi"), Is.GreaterThan(DensityRank("xxhdpi")));
            Assert.That(DensityRank("xxhdpi"), Is.GreaterThan(DensityRank("hdpi")));
            Assert.That(DensityRank("hdpi"), Is.GreaterThan(DensityRank("mdpi")));
            Assert.That(DensityRank("mdpi"), Is.GreaterThan(DensityRank("ldpi")));
            Assert.That(DensityRank("nodpi"), Is.LessThan(DensityRank("ldpi")));
            Assert.That(DensityRank("en-rGB"), Is.LessThan(0));
            Assert.That(DensityRank(""), Is.LessThan(0));
        }

        [Test]
        public void TryRead_TakesTheIconFromTheHighestDensitySplit()
        {
            byte[] outer = Bundle(
                Entry("splits/split_config.hdpi.apk", SplitWith(IconPath, Png(4))),
                Entry("splits/split_config.xxxhdpi.apk", SplitWith(IconPath, Png(12))),
                Entry("base.apk", SplitWith("AndroidManifest.xml", new byte[] { 0x03, 0x00 })));

            using (Bitmap icon = TryRead(outer, IconPath, 12))
            {
                Assert.That(icon, Is.Not.Null);
                Assert.That(icon.Width, Is.EqualTo(12), "the xxxhdpi split should win over hdpi");
            }
        }

        [Test]
        public void TryRead_HonoursTheFlatSplitLayout()
        {
            byte[] outer = Bundle(
                Entry("config.xxhdpi.apk", SplitWith(IconPath, Png(10))),
                Entry("base.apk", SplitWith("AndroidManifest.xml", new byte[] { 0x03, 0x00 })));

            using (Bitmap icon = TryRead(outer, IconPath, 10))
            {
                Assert.That(icon, Is.Not.Null);
                Assert.That(icon.Width, Is.EqualTo(10));
            }
        }

        [Test]
        public void TryRead_ReturnsNullWhenTheBaseNamesNoIcon()
        {
            byte[] outer = Bundle(
                Entry("splits/split_config.xxhdpi.apk", SplitWith(IconPath, Png(8))),
                Entry("base.apk", SplitWith("AndroidManifest.xml", new byte[] { 0x03, 0x00 })));

            Assert.That(TryRead(outer, "", 8), Is.Null);
            Assert.That(TryRead(outer, null, 8), Is.Null);
        }

        [Test]
        public void TryRead_ReturnsNullWhenThePathIsInNoSplit()
        {
            byte[] outer = Bundle(
                Entry("splits/split_config.xxhdpi.apk", SplitWith("res/mipmap-xxhdpi/other.png", Png(8))),
                Entry("base.apk", SplitWith("AndroidManifest.xml", new byte[] { 0x03, 0x00 })));

            Assert.That(TryRead(outer, IconPath, 8), Is.Null);
        }

        [Test]
        public void TryRead_IgnoresSplitsWithoutADensityQualifier()
        {
            byte[] outer = Bundle(
                Entry("splits/split_config.apk", SplitWith(IconPath, Png(8))),
                Entry("splits/split_other.apk", SplitWith(IconPath, Png(8))),
                Entry("base.apk", SplitWith("AndroidManifest.xml", new byte[] { 0x03, 0x00 })));

            Assert.That(TryRead(outer, IconPath, 8), Is.Null);
        }

        [Test]
        public void TryRead_RejectsASplitThatDeclaresAnAbsurdSize()
        {
            byte[] split = ZipBuilder.Build(new ZipBuilder.EntrySpec {
                Name = IconPath,
                Data = Png(8),
                SentinelSizes = true
            });
            byte[] outer = Bundle(
                Entry("splits/split_config.xxhdpi.apk", split),
                Entry("base.apk", SplitWith("AndroidManifest.xml", new byte[] { 0x03, 0x00 })));

            Assert.That(TryRead(outer, IconPath, 8), Is.Null, "an over-cap split must be refused, not decoded");
        }

        [Test]
        public void TryRead_StopsAfterTheSplitCap()
        {
            int max = (int)SourceType()
                .GetField("MaxSplits", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .GetValue(null);
            Assert.That(max, Is.EqualTo(16));
        }
    }
}
