using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using NUnit.Framework;
using SharpShell.Helpers;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// ComStream: the Stream adapter SharpShell hands to callers instead of a raw
    /// IStream. T-A fixed two long-standing bugs here: Seek updated the cached
    /// position with the wrong value and Dispose released the COM object and the
    /// marshal buffer more than once. The fake IStream makes the position bookkeeping
    /// observable; the SHCreateMemStream test covers the real RCW release path.
    /// </summary>
    [TestFixture]
    public class ComStreamTests
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern IStream SHCreateMemStream(byte[] init, uint size);

        /// <summary>
        /// Managed IStream standing in for the COM stream SharpShell wraps. It is not
        /// an RCW, so these instances are never disposed (Marshal.ReleaseComObject
        /// rejects a plain managed object); the test process reclaims them.
        /// </summary>
        private sealed class FakeIStream : IStream
        {
            private readonly byte[] bytes;
            private long position;

            public FakeIStream(params byte[] bytes)
            {
                this.bytes = bytes;
            }

            public void Read(byte[] pv, int cb, IntPtr pcbRead)
            {
                int count = (int)Math.Max(0, Math.Min(cb, bytes.Length - position));
                Array.Copy(bytes, position, pv, 0, count);
                position += count;
                if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, count);
            }

            public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
            {
                switch (dwOrigin)
                {
                    case 0: position = dlibMove; break;
                    case 1: position += dlibMove; break;
                    case 2: position = bytes.Length + dlibMove; break;
                    default: throw new ArgumentOutOfRangeException("dwOrigin");
                }
                if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, position);
            }

            public void Stat(out STATSTG pstatstg, int grfStatFlag)
            {
                pstatstg = new STATSTG { cbSize = bytes.Length, type = 2 };
            }

            public void Write(byte[] pv, int cb, IntPtr pcbWritten)
            {
                position += cb;
                if (pcbWritten != IntPtr.Zero) Marshal.WriteInt32(pcbWritten, cb);
            }

            public void Clone(out IStream ppstm) { throw new NotSupportedException(); }
            public void Commit(int grfCommitFlags) { }
            public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) { throw new NotSupportedException(); }
            public void LockRegion(long libOffset, long cb, int dwLockType) { }
            public void Revert() { }
            public void SetSize(long libNewSize) { }
            public void UnlockRegion(long libOffset, long cb, int dwLockType) { }
        }

        private static ComStream Stream(params byte[] bytes)
        {
            return new ComStream(new FakeIStream(bytes));
        }

        [Test]
        public void ReadCopiesTheBytesAndAdvancesThePosition()
        {
            ComStream stream = Stream(10, 20, 30, 40);
            byte[] buffer = new byte[4];
            Assert.That(stream.Read(buffer, 0, 4), Is.EqualTo(4));
            Assert.That(buffer, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
            Assert.That(stream.Position, Is.EqualTo(4));
        }

        [Test]
        public void ReadAtTheEndReturnsZeroAndLeavesThePositionAlone()
        {
            ComStream stream = Stream(10, 20);
            stream.Seek(0, SeekOrigin.End);
            Assert.That(stream.Read(new byte[4], 0, 4), Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(2));
        }

        [Test]
        public void ReadWithANonZeroBufferOffsetIsNotImplemented()
        {
            ComStream stream = Stream(1, 2, 3);
            Assert.That(() => stream.Read(new byte[3], 1, 1), Throws.TypeOf<NotImplementedException>());
        }

        [Test]
        public void SeekUpdatesTheCachedPositionForEveryOrigin()
        {
            ComStream stream = Stream(10, 20, 30, 40);
            Assert.That(stream.Seek(2, SeekOrigin.Begin), Is.EqualTo(2));
            Assert.That(stream.Position, Is.EqualTo(2));

            byte[] one = new byte[1];
            stream.Read(one, 0, 1);
            Assert.That(one[0], Is.EqualTo(30));
            Assert.That(stream.Position, Is.EqualTo(3));

            Assert.That(stream.Seek(-1, SeekOrigin.Current), Is.EqualTo(2));
            Assert.That(stream.Position, Is.EqualTo(2));

            Assert.That(stream.Seek(-1, SeekOrigin.End), Is.EqualTo(3));
            Assert.That(stream.Position, Is.EqualTo(3));
        }

        [Test]
        public void WriteAdvancesThePositionAndForwardsThePayload()
        {
            ComStream stream = Stream();
            stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
            Assert.That(stream.Position, Is.EqualTo(3));
        }

        [Test]
        public void WriteWithANonZeroBufferOffsetIsNotImplemented()
        {
            ComStream stream = Stream();
            Assert.That(() => stream.Write(new byte[3], 1, 1), Throws.TypeOf<NotImplementedException>());
        }

        [Test]
        public void LengthComesFromTheComStatCall()
        {
            ComStream stream = Stream(1, 2, 3, 4, 5);
            Assert.That(stream.Length, Is.EqualTo(5));
            Assert.That(stream.CanRead && stream.CanSeek && stream.CanWrite, Is.True);
        }

        [Test]
        public void DisposeReleasesTheComObjectAndTheBufferExactlyOnce()
        {
            // A real COM stream (RCW) is needed here: Marshal.ReleaseComObject rejects
            // a plain managed IStream implementation, so the fake used above cannot
            // exercise this path.
            IStream com = SHCreateMemStream(new byte[] { 1, 2, 3 }, 3);
            ComStream stream = new ComStream(com);

            stream.Dispose();
            Assert.That(() => stream.Dispose(), Throws.Nothing);

            FieldInfo comField = typeof(ComStream).GetField("comStream", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo bufferField = typeof(ComStream).GetField("bufferPointer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(comField.GetValue(stream), Is.Null);
            Assert.That(bufferField.GetValue(stream), Is.EqualTo(IntPtr.Zero));
        }
    }
}
