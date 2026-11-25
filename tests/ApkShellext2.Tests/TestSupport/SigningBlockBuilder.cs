using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Builds the APK Signing Block that packers stamp into the bytes directly before the
    /// central directory, and splices a block into a plain zip so the tail reader can be
    /// driven over crafted files: the five known ids, an unknown id, a pair that overruns
    /// its block, a size that disagrees with the footer, a block over the reader's cap.
    /// </summary>
    internal static class SigningBlockBuilder
    {
        internal const uint Walle = 0x71777777;
        internal const uint ByteDance = 0x71777776;
        internal const uint Frosting = 0x2146444e;
        internal const uint PackerNg = 0x7a786b21;
        internal const uint VasDollyV2 = 0x881155ff;
        internal const uint Unknown = 0x0badf00d;

        private const int EndOfCentralDirectorySize = 22;
        private const int BlockFooterSize = 24;
        private const int EndOfCentralDirectorySignature = 0x06054b50;

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("APK Sig Block 42");

        internal sealed class Pair
        {
            public uint Id;
            public byte[] Value = new byte[0];

            /// <summary>Length declared in the pair header; -1 means "4 + Value.Length".</summary>
            public long DeclaredLength = -1;

            internal static Pair Of(uint id)
            {
                return new Pair { Id = id };
            }

            internal static Pair Of(uint id, byte[] value)
            {
                return new Pair { Id = id, Value = value };
            }

            /// <summary>A pair whose header claims more bytes than the block holds.</summary>
            internal static Pair Declaring(uint id, long declaredLength, byte[] value)
            {
                return new Pair { Id = id, Value = value, DeclaredLength = declaredLength };
            }
        }

        /// <summary>A block whose leading and trailing size fields agree.</summary>
        internal static byte[] Block(params Pair[] pairs)
        {
            return Block(0, pairs);
        }

        /// <summary>A block whose trailing size field is shifted by footerSizeDelta bytes.</summary>
        internal static byte[] Block(long footerSizeDelta, params Pair[] pairs)
        {
            List<byte> payload = new List<byte>();
            foreach (Pair pair in pairs)
            {
                long declared = pair.DeclaredLength >= 0 ? pair.DeclaredLength : 4 + pair.Value.Length;
                payload.AddRange(BitConverter.GetBytes((ulong)declared));
                payload.AddRange(BitConverter.GetBytes(pair.Id));
                payload.AddRange(pair.Value);
            }

            // the size field counts the pairs plus the footer (8-byte size + 16-byte magic)
            ulong size = (ulong)payload.Count + BlockFooterSize;
            List<byte> block = new List<byte>();
            block.AddRange(BitConverter.GetBytes(size));
            block.AddRange(payload);
            block.AddRange(BitConverter.GetBytes((ulong)((long)size + footerSizeDelta)));
            block.AddRange(Magic);
            return block.ToArray();
        }

        /// <summary>Splices the block in before the central directory and moves the record's offset.</summary>
        internal static byte[] Insert(byte[] zip, byte[] block)
        {
            int endOfCentralDirectory = EndOfCentralDirectory(zip);
            Assert.That(endOfCentralDirectory, Is.GreaterThanOrEqualTo(0), "the fixture zip carries an end record");
            int centralDirOffset = BitConverter.ToInt32(zip, endOfCentralDirectory + 16);

            byte[] result = new byte[zip.Length + block.Length];
            Array.Copy(zip, 0, result, 0, centralDirOffset);
            Array.Copy(block, 0, result, centralDirOffset, block.Length);
            Array.Copy(zip, centralDirOffset, result, centralDirOffset + block.Length, zip.Length - centralDirOffset);
            BitConverter.GetBytes(centralDirOffset + block.Length).CopyTo(result, endOfCentralDirectory + block.Length + 16);
            return result;
        }

        /// <summary>Offset of the end of central directory record in a zip written by ZipBuilder.</summary>
        internal static int EndOfCentralDirectory(byte[] zip)
        {
            for (int i = zip.Length - EndOfCentralDirectorySize; i >= 0; i--)
            {
                if (BitConverter.ToInt32(zip, i) == EndOfCentralDirectorySignature) return i;
            }
            return -1;
        }
    }
}
