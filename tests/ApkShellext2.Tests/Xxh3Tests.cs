using System;
using NUnit.Framework;
using ApkShellext2;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the XXH3-64 digests against vectors produced by the reference implementation
    /// (python xxhash 4.1.0). The content is the same fixed pseudo-random sequence in
    /// every case, and the expected values cover each input range of the specification:
    /// short, mid, the 129..240 range and the block based path, including block edges.
    /// </summary>
    [TestFixture]
    public class Xxh3Tests
    {
        private static readonly int[] Lengths = {
            0, 1, 3, 4, 8, 15, 16, 17, 31, 32, 63, 64, 65, 100, 127, 128, 129, 239, 240,
            241, 255, 256, 511, 512, 1023, 1024, 1025, 2048, 4096, 10000, 100000
        };

        private static readonly string[] Expected = {
            "2d06800538d394c2", "0de0f3cef1e76922", "a434d638feb579dc", "d7e619dd92e873ef",
            "5e894620868a925d", "56e6dcaf74b5fde2", "cd5d6976a0b7b4fe", "7f25e237f3bee0d9",
            "4a3e01628d6b6c5f", "664b90801938a34d", "4fa7641f4dfe6830", "3db7ac69db1ab07e",
            "edd7acc1d11fbb03", "ba56e21a399cd28e", "d4480c9614c1ddf0", "7a2c6c2e3e675ceb",
            "b6870b60c5a0d438", "3cb467f0387859a9", "a69bd66ccab5ebb5", "25ebd647d0cad5f9",
            "6efd3095a145609b", "f4cad8f70bf31718", "fea1d130a98611bf", "ff403a8d306fb88c",
            "161999fff8e1f9aa", "b67d9e9c04ceda9d", "f9c5ce44fe38d3c7", "92a40c7f370f228a",
            "201bfbe4dc0909df", "073c94c7f7527e21", "f22984bec9af7255"
        };

        private static readonly int[] ChunkSizes = { 1, 3, 7, 63, 64, 65, 255, 256, 257, 1000 };

        /// <summary>The content every vector hashes: a fixed linear congruential sequence.</summary>
        internal static byte[] Content(int length)
        {
            var data = new byte[length];
            uint state = 0x12345678;
            for (int i = 0; i < length; i++)
            {
                state = unchecked(state * 1664525 + 1013904223);
                data[i] = (byte)(state >> 24);
            }
            return data;
        }

        [Test]
        public void EveryVectorMatchesTheReferenceImplementation()
        {
            for (int i = 0; i < Lengths.Length; i++)
            {
                ulong actual = Xxh3.Hash(Content(Lengths[i]));
                Assert.That(actual.ToString("x16"), Is.EqualTo(Expected[i]), "length " + Lengths[i]);
            }
        }

        [Test]
        public void StreamingWithEveryChunkSizeMatchesTheOneShotDigest()
        {
            for (int i = 0; i < Lengths.Length; i++)
            {
                byte[] data = Content(Lengths[i]);
                foreach (int chunk in ChunkSizes)
                {
                    Xxh3 hash = Xxh3.Create();
                    for (int offset = 0; offset < data.Length; offset += chunk)
                    {
                        hash.Append(data, offset, Math.Min(chunk, data.Length - offset));
                    }
                    Assert.That(hash.Digest().ToString("x16"), Is.EqualTo(Expected[i]),
                        "length " + Lengths[i] + ", chunk " + chunk);
                }
            }
        }

        [Test]
        public void EmptyInputHasTheDocumentedDigest()
        {
            Assert.That(Xxh3.Hash(new byte[0]).ToString("x16"), Is.EqualTo("2d06800538d394c2"));
        }

        [Test]
        public void DigestDoesNotChangeTheIncrementalState()
        {
            Xxh3 hash = Xxh3.Create();
            hash.Append(new byte[] { 0x61, 0x62 }, 0, 2);
            ulong first = hash.Digest();
            Assert.That(hash.Digest(), Is.EqualTo(first));

            hash.Append(new byte[] { 0x63 }, 0, 1);
            Assert.That(hash.Digest().ToString("x16"), Is.EqualTo("78af5f94892f3950"));
        }

        [Test]
        public void ResetReturnsToTheEmptyState()
        {
            Xxh3 hash = Xxh3.Create();
            hash.Append(new byte[] { 1, 2, 3 }, 0, 3);
            hash.Reset();
            Assert.That(hash.Digest().ToString("x16"), Is.EqualTo("2d06800538d394c2"));
        }

        [Test]
        public void AppendRejectsARangeOutsideTheArray()
        {
            Xxh3 hash = Xxh3.Create();
            Assert.Throws<ArgumentNullException>(() => hash.Append(null, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => hash.Append(new byte[2], -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => hash.Append(new byte[2], 0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => hash.Append(new byte[2], 2, 1));
        }

        [Test]
        public void HashRejectsANullArray()
        {
            Assert.Throws<ArgumentNullException>(() => Xxh3.Hash(null));
        }
    }
}
