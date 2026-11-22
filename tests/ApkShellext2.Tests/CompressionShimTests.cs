using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using NUnit.Framework;
using PNGDecrush;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// T-J removed the source-embedded SharpZipLib. The pinned PNGDecrush submodule still
    /// names three SharpZipLib types (a zlib compressor and an incremental CRC-32), which
    /// SharpZipLibShim.cs now provides on System.IO.Compression. These tests pin the parts
    /// the decrushing path actually depends on, because a broken shim would silently
    /// corrupt every crushed iOS PNG instead of failing loudly.
    /// </summary>
    [TestFixture]
    public class CompressionShimTests
    {
        [Test]
        public void Crc32MatchesThePngIendChunk()
        {
            // The canonical PNG IEND chunk CRC: type "IEND" with an empty payload, which is
            // exactly how PNGDecrusher.CalculateCRCForChunk is called when it rebuilds chunks.
            Assert.That(PNGDecrusher.CalculateCRCForChunk("IEND", new byte[0]), Is.EqualTo(0xAE426082u));
        }

        [Test]
        public void Crc32IsIncrementalAcrossUpdates()
        {
            byte[] data = Payload(4096);
            byte[] head = new byte[1000];
            byte[] tail = new byte[data.Length - head.Length];
            Array.Copy(data, 0, head, 0, head.Length);
            Array.Copy(data, head.Length, tail, 0, tail.Length);

            // CalculateCRCForChunk seeds the CRC with the chunk type, so the expected value
            // is the CRC of type-then-data; feeding it through the incremental type in two
            // pieces must land on the same value (that is how PNGDecrusher uses it).
            Assert.That(PNGDecrusher.CalculateCRCForChunk("tEXt", data),
                Is.EqualTo(Crc32Of(Concat(new byte[] { (byte)'t', (byte)'E', (byte)'X', (byte)'t' }, data))));

            Type crc32Type = typeof(PNGDecrusher).Assembly.GetType(
                "ICSharpCode.SharpZipLib.Checksum.Crc32", throwOnError: true);
            object crc = Activator.CreateInstance(crc32Type, nonPublic: true);
            MethodInfo update = crc32Type.GetMethod("Update", new[] { typeof(byte[]) });
            Assert.That(update, Is.Not.Null);

            update.Invoke(crc, new object[] { head });
            update.Invoke(crc, new object[] { tail });

            object value = crc32Type.GetProperty("Value").GetValue(crc, null);
            Assert.That(Convert.ToUInt32(value), Is.EqualTo(Crc32Of(data)));
        }

        [Test]
        public void ConvertDeflateToZlibProducesAValidRfc1950Stream()
        {
            // This is the exact call the shim exists for: the decrushing path turns Apple's
            // headerless IDAT deflate data back into a zlib stream. The method stays private
            // in the pinned submodule, so it is reached by reflection.
            byte[] plain = Payload(20000);
            byte[] rawDeflate = DeflateRaw(plain);

            MethodInfo convert = typeof(PNGDecrusher).GetMethod(
                "ConvertDeflateToZlib", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(convert, Is.Not.Null, "PNGDecrusher.ConvertDeflateToZlib disappeared; re-point the shim");

            byte[] zlib = (byte[])convert.Invoke(null, new object[] { rawDeflate });

            Assert.That(zlib.Length, Is.GreaterThan(6));
            Assert.That(zlib[0], Is.EqualTo(0x78), "CMF must declare deflate with a 32 KiB window");
            Assert.That(((zlib[0] << 8) | zlib[1]) % 31, Is.EqualTo(0), "the zlib header must be FCHECK-valid");

            uint trailer = ((uint)zlib[zlib.Length - 4] << 24) | ((uint)zlib[zlib.Length - 3] << 16) |
                           ((uint)zlib[zlib.Length - 2] << 8) | zlib[zlib.Length - 1];
            Assert.That(trailer, Is.EqualTo(Adler32Of(plain)), "the RFC 1950 trailer must be the payload's Adler-32");

            // The middle must be plain deflate that a stock inflater accepts.
            byte[] inflated;
            using (MemoryStream middle = new MemoryStream(zlib, 2, zlib.Length - 6))
            using (DeflateStream inflater = new DeflateStream(middle, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                inflater.CopyTo(output);
                inflated = output.ToArray();
            }

            Assert.That(inflated, Is.EqualTo(plain), "the recompressed zlib stream must round-trip");
        }

        [Test]
        public void ConvertDeflateToZlibPrefersBestCompression()
        {
            // PNGDecrush passes level 9 (formerly Ionic's BestCompression default). The level
            // only picks the zlib FLEVEL bits in the BCL implementation, so assert the framing
            // the shim promises instead of a compressed size.
            byte[] rawDeflate = DeflateRaw(Payload(4096));

            MethodInfo convert = typeof(PNGDecrusher).GetMethod(
                "ConvertDeflateToZlib", BindingFlags.NonPublic | BindingFlags.Static);
            byte[] zlib = (byte[])convert.Invoke(null, new object[] { rawDeflate });

            Assert.That(zlib[1], Is.EqualTo(0xDA), "level 9 must set FLEVEL=3 (0x78 0xDA)");
        }

        private static byte[] Concat(byte[] head, byte[] tail)
        {
            byte[] combined = new byte[head.Length + tail.Length];
            Array.Copy(head, combined, head.Length);
            Array.Copy(tail, 0, combined, head.Length, tail.Length);
            return combined;
        }

        private static byte[] Payload(int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                data[i] = (byte)((i * 37 + 11) & 0xFF);
            }
            return data;
        }

        private static byte[] DeflateRaw(byte[] data)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    deflate.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        private static uint Adler32Of(byte[] data)
        {
            uint a = 1u;
            uint b = 0u;
            foreach (byte value in data)
            {
                a = (a + value) % 65521u;
                b = (b + a) % 65521u;
            }
            return (b << 16) | a;
        }

        private static uint Crc32Of(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1u) != 0u ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
