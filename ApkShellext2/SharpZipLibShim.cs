// SharpZipLib compatibility shim — the last three SharpZipLib types still referenced
// from the product, provided by System.IO.Compression now that the v1.4.2 submodule is
// gone (T-J).
//
// Why a shim instead of rewriting the caller: the only remaining references live in the
// pinned PNGDecrush submodule (ApkShellext2\PNGDecrush\PNGDecrush\PNGDecrusher.cs), which
// asks for them by fully-qualified name — ConvertDeflateToZlib() needs a zlib (RFC 1950)
// recompressor with a Finish() hook, and CalculateCRCForChunk() needs an incremental
// CRC-32. The submodule is pinned to an upstream commit we cannot push, so the parent
// keeps it byte-identical and satisfies those three names here instead of forking it.
//
// Scope is deliberately exactly what PNGDecrusher calls (constructor, Write, Finish,
// Update, Value) — not a general SharpZipLib reimplementation. The zip container work
// that used to sit on SharpZipLib now runs on ZipArchive via SafeArchive.
//
// Provenance change: the previous source-embedded copy was icsharpcode/SharpZipLib
// v1.4.2 (MIT, LICENSE.txt inside the removed submodule). Nothing from it remains
// compiled; RFC 1950 and CRC-32 are implemented here from their specifications.

using System;
using System.IO;
using System.IO.Compression;

namespace ApkShellext2.Compression
{
    /// <summary>Adler-32 (RFC 1950 trailer) with incremental updates.</summary>
    internal struct Adler32
    {
        private const uint ModAdler = 65521u;
        private uint a;
        private uint b;

        public static Adler32 Create()
        {
            Adler32 adler = new Adler32();
            adler.a = 1u;
            adler.b = 0u;
            return adler;
        }

        public void Update(byte[] buffer, int offset, int count)
        {
            // Chunked so the two sums never overflow before the modulo.
            while (count > 0)
            {
                int chunk = count > 5552 ? 5552 : count;
                count -= chunk;
                for (int i = 0; i < chunk; i++)
                {
                    a += buffer[offset++];
                    b += a;
                }
                a %= ModAdler;
                b %= ModAdler;
            }
        }

        public uint Value
        {
            get { return (b << 16) | a; }
        }
    }

    /// <summary>CRC-32 (IEEE 802.3, the PNG/zlib polynomial 0xEDB88320) with incremental updates.</summary>
    internal sealed class Crc32Calculator
    {
        private static readonly uint[] Table = BuildTable();

        private uint crc = 0xFFFFFFFFu;

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1u) != 0u ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }

        public void Update(byte[] buffer, int offset, int count)
        {
            uint c = crc;
            for (int i = 0; i < count; i++)
            {
                c = Table[(c ^ buffer[offset + i]) & 0xFFu] ^ (c >> 8);
            }
            crc = c;
        }

        public void Reset()
        {
            crc = 0xFFFFFFFFu;
        }

        public uint Value
        {
            get { return crc ^ 0xFFFFFFFFu; }
        }
    }
}

namespace ICSharpCode.SharpZipLib.Zip.Compression
{
    /// <summary>
    /// Compression-level carrier for <see cref="Streams.DeflaterOutputStream"/>. Kept in
    /// the SharpZipLib namespace only because the pinned PNGDecrush source names it that way.
    /// </summary>
    internal sealed class Deflater
    {
        public Deflater(int level)
        {
            Level = level;
        }

        /// <summary>zlib level 0-9; 9 asks for best compression (PNGDecrush passes 9).</summary>
        public int Level { get; private set; }
    }
}

namespace ICSharpCode.SharpZipLib.Zip.Compression.Streams
{
    /// <summary>
    /// Minimal zlib (RFC 1950) compressor: 2-byte header on construction, raw deflate
    /// from System.IO.Compression in the middle, Adler-32 trailer on Finish().
    /// PNGDecrush only needs <c>Write</c> + <c>Finish</c> + disposal.
    /// </summary>
    internal sealed class DeflaterOutputStream : Stream
    {
        private readonly Stream baseOutputStream;
        private readonly DeflateStream deflate;
        private ApkShellext2.Compression.Adler32 adler;
        private bool finished;

        public DeflaterOutputStream(Stream baseOutputStream, Deflater deflater)
        {
            if (baseOutputStream == null)
            {
                throw new ArgumentNullException("baseOutputStream");
            }

            this.baseOutputStream = baseOutputStream;
            adler = ApkShellext2.Compression.Adler32.Create();

            // CMF 0x78 = deflate, 32 KiB window. FLG's FCHECK byte must make
            // (CMF << 8 | FLG) a multiple of 31: 0x789C (default) / 0x78DA (best).
            baseOutputStream.WriteByte(0x78);
            baseOutputStream.WriteByte(deflater != null && deflater.Level >= 9 ? (byte)0xDA : (byte)0x9C);

            // leaveOpen: the caller owns the target stream (a MemoryStream it reads back).
            deflate = new DeflateStream(baseOutputStream, CompressionMode.Compress, true);
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            if (finished)
            {
                throw new InvalidOperationException("The zlib stream has already been finished.");
            }

            adler.Update(buffer, offset, count);
            deflate.Write(buffer, offset, count);
        }

        /// <summary>Flushes the deflate data and appends the Adler-32 trailer (idempotent).</summary>
        public void Finish()
        {
            if (finished)
            {
                return;
            }
            finished = true;

            deflate.Dispose(); // flushes, then releases the encoder; base stream stays open
            uint checksum = adler.Value;
            baseOutputStream.WriteByte((byte)(checksum >> 24));
            baseOutputStream.WriteByte((byte)(checksum >> 16));
            baseOutputStream.WriteByte((byte)(checksum >> 8));
            baseOutputStream.WriteByte((byte)checksum);
            baseOutputStream.Flush();
        }

        public override void Flush()
        {
            deflate.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Finish();
            }
            base.Dispose(disposing);
        }
    }
}

namespace ICSharpCode.SharpZipLib.Checksum
{
    /// <summary>Incremental CRC-32 under the name the pinned PNGDecrush source uses.</summary>
    internal sealed class Crc32
    {
        private readonly ApkShellext2.Compression.Crc32Calculator calculator =
            new ApkShellext2.Compression.Crc32Calculator();

        public void Update(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            calculator.Update(buffer, 0, buffer.Length);
        }

        public void Update(byte[] buffer, int offset, int count)
        {
            calculator.Update(buffer, offset, count);
        }

        public void Reset()
        {
            calculator.Reset();
        }

        /// <summary>CRC-32 of everything fed so far (SharpZipLib exposes long here).</summary>
        public long Value
        {
            get { return calculator.Value; }
        }
    }
}
