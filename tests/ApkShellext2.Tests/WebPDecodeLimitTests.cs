using System;
using System.IO;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// ValidateDecodeSize is what stops a malicious .webp from making explorer.exe
    /// allocate a pixel buffer for a claimed 16384x16384 canvas (~1 GiB). It is a
    /// pure static function, so the exact boundaries - not just "some rejection
    /// happens" - are cheap to pin here; ProductApi calls it reflectively, which
    /// needs neither a WebP instance nor the native libwebp decoder.
    /// </summary>
    [TestFixture]
    public class WebPDecodeLimitTests
    {
        [Test]
        public void Limits_AreTheDocumentedOnes()
        {
            Assert.That(ProductApi.DecodeLimitConstant("MaxWebPDecodeSide"), Is.EqualTo(4096));
            Assert.That(ProductApi.DecodeLimitConstant("MaxWebPDecodePixels"), Is.EqualTo(8L * 1024 * 1024));
        }

        [TestCase(1, 1, TestName = "Accepts_1x1")]
        [TestCase(4096, 1, TestName = "Accepts_4096x1")]
        [TestCase(1, 4096, TestName = "Accepts_1x4096")]
        // exactly 8 MiB of pixels: the pixel cap is inclusive, which is what makes
        // the 4096x2048 case pass and 4096x2049 fail
        [TestCase(4096, 2048, TestName = "Accepts_4096x2048")]
        public void Accepts_WithinTheLimits(int width, int height)
        {
            Assert.DoesNotThrow(() => ProductApi.ValidateDecodeSize(width, height),
                width + "x" + height + " is inside the decode limits and must be decodable");
        }

        [TestCase(0, 1, TestName = "Rejects_zeroWidth")]
        [TestCase(1, 0, TestName = "Rejects_zeroHeight")]
        [TestCase(0, 0, TestName = "Rejects_zeroByZero")]
        [TestCase(-1, 16, TestName = "Rejects_negativeWidth")]
        [TestCase(16, -1, TestName = "Rejects_negativeHeight")]
        [TestCase(4097, 1, TestName = "Rejects_sideOver4096")]
        [TestCase(1, 4097, TestName = "Rejects_heightOver4096")]
        [TestCase(4096, 2049, TestName = "Rejects_onePixelRowOver")]
        [TestCase(3000, 3000, TestName = "Rejects_3000x3000")]
        [TestCase(16384, 16384, TestName = "Rejects_16384x16384")]
        public void Rejects_OutsideTheLimits(int width, int height)
        {
            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => ProductApi.ValidateDecodeSize(width, height),
                width + "x" + height + " is outside the decode limits and must be refused before any buffer is allocated");

            // the message has to name the offending size: it is what the shell handler
            // logs when it falls back to the generic icon
            Assert.That(ex.Message, Does.Contain(width + "x" + height));
            Assert.That(ex.Message, Does.Contain("decode limits"));
        }
    }
}
