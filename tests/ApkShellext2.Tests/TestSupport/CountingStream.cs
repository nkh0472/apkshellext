using System;
using System.IO;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// A pass-through stream that counts how many bytes the code under test pulls
    /// from the archive. Both readers this repo has shipped (SharpZipLib, then BCL
    /// ZipArchive over the same stream) read a Stored entry straight out of the
    /// stream handed to them, so the counter measures exactly how much entry
    /// data ZipSafety.ReadEntryBounded materialised - the only portable way on
    /// .NET Framework 4.8 to prove "it stopped at the cap" (net48 has no
    /// GC.GetAllocatedBytesForCurrentThread, and surviving-heap deltas would hide
    /// a buffer that the throwing path immediately drops).
    /// </summary>
    internal sealed class CountingStream : Stream
    {
        private readonly Stream _inner;

        public CountingStream(Stream inner)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
        }

        /// <summary>Bytes handed to the caller since construction.</summary>
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            if (read > 0) BytesRead += read;
            return read;
        }

        public override int ReadByte()
        {
            int value = _inner.ReadByte();
            if (value >= 0) BytesRead++;
            return value;
        }

        public override bool CanRead { get { return _inner.CanRead; } }
        public override bool CanSeek { get { return _inner.CanSeek; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _inner.Length; } }

        public override long Position
        {
            get { return _inner.Position; }
            set { _inner.Position = value; }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
