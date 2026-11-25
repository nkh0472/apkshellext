using System;
using System.IO;
using System.Text;

namespace ApkQuickReader {
    /// <summary>
    /// Names the packer that produced an apk from the APK Signing Block. Walle,
    /// ByteDance's apk tooling, Frosting, PackerNG and VasDollyV2 each write one
    /// known id-value pair into that block, so the id alone identifies the tool
    /// without touching a certificate or a signature.
    ///
    /// The block is reached from the end of the file: the end of central directory
    /// record points at the central directory, and the block sits directly before it.
    /// Only the tail window and the block itself are read, and every count, length
    /// and offset is capped, so a crafted file can neither pull the whole apk into
    /// memory nor make the walk revisit bytes.
    /// </summary>
    internal static class SigningBlockReader {
        private static void Log(string message) {
            ApkShellext2.DbgLog.W(message);
        }
        // ids the known packers store; the value bytes are irrelevant, the id names them
        private const uint WalleId = 0x71777777;
        private const uint ByteDanceId = 0x71777776;
        private const uint FrostingId = 0x2146444e;
        private const uint PackerNGId = 0x7a786b21;
        private const uint VasDollyV2Id = 0x881155ff;

        private const int EndOfCentralDirectorySize = 22;
        private const int EndOfCentralDirectorySignature = 0x06054b50;
        private const int Zip64LocatorSize = 20;
        private const int Zip64LocatorSignature = 0x07064b50;
        private const int Zip64EndOfCentralDirectorySize = 56;
        private const int Zip64EndOfCentralDirectorySignature = 0x06064b50;

        // the block footer is the 8-byte size followed by "APK Sig Block 42"
        private const int BlockFooterSize = 24;
        private const int BlockMagicSize = 16;
        private const long MinimumBlockSize = 32;   // leading size + footer, no pairs

        // hard caps: a real signing block is a few kilobytes, the tail window is
        // bounded by the largest zip comment any producer writes
        private const long MaxBlockSize = 1024 * 1024;
        private const int MaxPairs = 64;
        private const int TailWindow = 64 * 1024;

        private static readonly byte[] BlockMagic = Encoding.ASCII.GetBytes("APK Sig Block 42");

        /// <summary>The packer name, or an empty string when no known one left a pair.</summary>
        internal static string Read(string path)
        {
            try {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete)) {
                    return Read(stream);
                }
            } catch (Exception ex) {
                Log("signing block unreadable for " + path + ": " + ex.Message);
                return "";
            }
        }

        /// <summary>The packer name, or an empty string when the block is absent or damaged.</summary>
        internal static string Read(Stream stream)
        {
            try {
                if (stream == null || !stream.CanSeek) return "";
                long length = stream.Length;
                if (length < EndOfCentralDirectorySize + BlockFooterSize + MinimumBlockSize) return "";

                long endOfCentralDirectory = FindEndOfCentralDirectory(stream, length);
                if (endOfCentralDirectory < 0) return "";

                byte[] record = ReadAt(stream, endOfCentralDirectory, EndOfCentralDirectorySize);
                long centralDirOffset = BitConverter.ToUInt32(record, 16);
                long centralDirSize = BitConverter.ToUInt32(record, 12);
                if (centralDirOffset == uint.MaxValue) {
                    centralDirOffset = ReadZip64CentralDirOffset(stream, endOfCentralDirectory);
                    if (centralDirOffset < 0) return "";
                }
                if (centralDirOffset < 0 || centralDirSize < 0 ||
                    centralDirOffset + centralDirSize > endOfCentralDirectory) return "";

                long footerOffset = centralDirOffset - BlockFooterSize;
                if (footerOffset < 0) return "";
                byte[] footer = ReadAt(stream, footerOffset, BlockFooterSize);
                for (int i = 0; i < BlockMagicSize; i++) {
                    if (footer[8 + i] != BlockMagic[i]) return "";
                }

                ulong blockSize = BitConverter.ToUInt64(footer, 0);
                if (blockSize < (ulong)MinimumBlockSize || blockSize > (ulong)MaxBlockSize) return "";
                // the size counts the pairs and the 24-byte footer, so the block begins eight
                // bytes before the footer's start minus the size, i.e. at centralDir - size - 8
                long blockStart = footerOffset - (long)blockSize + 16;
                if (blockStart < 0) return "";

                // the block repeats its size in the leading field; a pair that disagrees
                // with the footer means the file was patched and the pairs cannot be trusted
                byte[] block = ReadAt(stream, blockStart, (int)blockSize + 8);
                if (BitConverter.ToUInt64(block, 0) != blockSize) return "";

                return FirstKnownPair(block, blockSize);
            } catch (Exception ex) {
                Log("signing block walk aborted: " + ex.Message);
                return "";
            }
        }

        /// <summary>Walks the id-value pairs in order and names the first known id.</summary>
        private static string FirstKnownPair(byte[] block, ulong blockSize)
        {
            long pairsEnd = (long)blockSize + 8 - BlockFooterSize;
            long cursor = 8;
            int seen = 0;
            while (cursor + 12 <= pairsEnd && seen < MaxPairs) {
                ulong pairLength = BitConverter.ToUInt64(block, (int)cursor);
                if (pairLength < 4 || (long)pairLength > pairsEnd - cursor - 8) return "";
                uint id = BitConverter.ToUInt32(block, (int)cursor + 8);
                string name = NameOf(id);
                if (name != null) return name;
                cursor += 8 + (long)pairLength;
                seen++;
            }
            return "";
        }

        private static string NameOf(uint id)
        {
            switch (id) {
                case WalleId: return "Walle";
                case ByteDanceId: return "ByteDance";
                case FrostingId: return "Frosting";
                case PackerNGId: return "PackerNG";
                case VasDollyV2Id: return "VasDollyV2";
                default: return null;
            }
        }

        /// <summary>
        /// The end of central directory record sits inside the last 64 KiB, after the
        /// archive comment. Scanning backwards from the end finds the record a truncated
        /// or padded file still carries, and the comment length is checked against the
        /// real file length so a chance signature inside compressed data cannot win.
        /// </summary>
        private static long FindEndOfCentralDirectory(Stream stream, long length)
        {
            int windowLength = (int)Math.Min(length, TailWindow);
            long windowStart = length - windowLength;
            byte[] window = ReadAt(stream, windowStart, windowLength);
            for (long i = windowLength - EndOfCentralDirectorySize; i >= 0; i--) {
                if (window[i] != 0x50 || window[i + 1] != 0x4b ||
                    window[i + 2] != 0x05 || window[i + 3] != 0x06) continue;
                int commentLength = window[i + 20] | (window[i + 21] << 8);
                if (windowStart + i + EndOfCentralDirectorySize + commentLength > length) continue;
                return windowStart + i;
            }
            return -1;
        }

        /// <summary>Offset of the central directory from the zip64 record of an apk over 4 GiB.</summary>
        private static long ReadZip64CentralDirOffset(Stream stream, long endOfCentralDirectory)
        {
            long locatorOffset = endOfCentralDirectory - Zip64LocatorSize;
            if (locatorOffset < 0) return -1;
            byte[] locator = ReadAt(stream, locatorOffset, Zip64LocatorSize);
            if (BitConverter.ToUInt32(locator, 0) != Zip64LocatorSignature) return -1;
            ulong recordOffset = BitConverter.ToUInt64(locator, 8);
            if (recordOffset > (ulong)stream.Length - Zip64EndOfCentralDirectorySize) return -1;
            byte[] record = ReadAt(stream, (long)recordOffset, Zip64EndOfCentralDirectorySize);
            if (BitConverter.ToUInt32(record, 0) != Zip64EndOfCentralDirectorySignature) return -1;
            ulong offset = BitConverter.ToUInt64(record, 48);
            return offset > long.MaxValue ? -1 : (long)offset;
        }

        private static byte[] ReadAt(Stream stream, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > stream.Length)
                throw new InvalidDataException("signing block read past the end of the file");
            byte[] buffer = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);
            int read = 0;
            while (read < count) {
                int chunk = stream.Read(buffer, read, count - read);
                if (chunk <= 0) throw new InvalidDataException("signing block read came up short");
                read += chunk;
            }
            return buffer;
        }
    }
}
