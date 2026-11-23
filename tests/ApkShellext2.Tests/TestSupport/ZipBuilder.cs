using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Minimal in-memory zip writer for the archives System.IO.Compression cannot
    /// produce: genuinely STORED entries (its NoCompression still deflates on
    /// .NET Framework), entries with a chosen general-purpose flag word, entries
    /// whose name bytes are not UTF-8, zip64 archives, and byte-level damage.
    ///
    /// Hermetic by design - every archive is assembled here, so the safety tests
    /// never depend on a sample library or the network.
    /// </summary>
    internal static class ZipBuilder
    {
        /// <summary>One entry to write.</summary>
        internal sealed class EntrySpec
        {
            /// <summary>Entry name; encoded as UTF-8 when <see cref="NameBytes"/> is null.</summary>
            public string Name;

            /// <summary>Verbatim name bytes for the archive, when the name is not UTF-8.</summary>
            public byte[] NameBytes;

            /// <summary>General-purpose flag word, e.g. 0x0800 for a UTF-8 name.</summary>
            public ushort Flags;

            /// <summary>Write a raw-deflate payload (method 8) instead of storing it.</summary>
            public bool Deflate;

            /// <summary>Declare the 0xFFFFFFFF size sentinel in both headers without a
            /// zip64 extra field: a container that hides its sizes.</summary>
            public bool SentinelSizes;

            /// <summary>Uncompressed size to declare in both headers; -1 declares
            /// <see cref="Data"/>.Length. A larger declared size lets a test exercise a
            /// declared-length gate without materialising (or deflating) that payload.</summary>
            public long DeclaredUncompressedSize = -1;

            public byte[] Data = new byte[0];
        }

        private const uint LocalHeaderSignature = 0x04034b50;
        private const uint CentralHeaderSignature = 0x02014b50;
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
        private const uint Zip64LocatorSignature = 0x07064b50;
        private const ushort DeflateMethod = 8;

        internal static byte[] Build(params EntrySpec[] entries)
        {
            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8))
            {
                List<byte[]> names = new List<byte[]>();
                List<long> offsets = new List<long>();
                List<uint> crcs = new List<uint>();
                List<uint> sizes = new List<uint>();
                List<uint> compressed = new List<uint>();
                List<ushort> flags = new List<ushort>();
                List<ushort> methods = new List<ushort>();

                foreach (EntrySpec entry in entries)
                {
                    byte[] data = entry.Data ?? new byte[0];
                    byte[] name = NameBytesOf(entry);
                    ushort method = 0;
                    byte[] payload = data;
                    if (entry.Deflate)
                    {
                        payload = Deflate(data);
                        method = DeflateMethod;
                    }
                    uint crc = data.Length == 0 ? 0u : Crc32.Compute(data, 0, data.Length);
                    long declaredLength = entry.DeclaredUncompressedSize >= 0
                        ? entry.DeclaredUncompressedSize
                        : data.Length;

                    names.Add(name);
                    offsets.Add(buffer.Position);
                    crcs.Add(crc);
                    sizes.Add(entry.SentinelSizes ? uint.MaxValue : (uint)declaredLength);
                    compressed.Add(entry.SentinelSizes ? uint.MaxValue : (uint)payload.Length);
                    flags.Add(entry.Flags);
                    methods.Add(method);

                    writer.Write(LocalHeaderSignature);
                    writer.Write((ushort)20);           // version needed to extract
                    writer.Write(entry.Flags);          // general purpose flags
                    writer.Write(method);
                    writer.Write((ushort)0);            // last mod time
                    writer.Write((ushort)0);            // last mod date
                    writer.Write(crc);
                    writer.Write(compressed[compressed.Count - 1]);   // compressed size
                    writer.Write(sizes[sizes.Count - 1]);             // uncompressed size
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);            // extra field length
                    writer.Write(name);
                    writer.Write(payload);
                }

                long directoryStart = buffer.Position;
                for (int i = 0; i < entries.Length; i++)
                {
                    writer.Write(CentralHeaderSignature);
                    writer.Write((ushort)20);           // version made by
                    writer.Write((ushort)20);           // version needed to extract
                    writer.Write(flags[i]);
                    writer.Write(methods[i]);
                    writer.Write((ushort)0);            // last mod time
                    writer.Write((ushort)0);            // last mod date
                    writer.Write(crcs[i]);
                    writer.Write(compressed[i]);
                    writer.Write(sizes[i]);
                    writer.Write((ushort)names[i].Length);
                    writer.Write((ushort)0);            // extra field length
                    writer.Write((ushort)0);            // comment length
                    writer.Write((ushort)0);            // disk number start
                    writer.Write((ushort)0);            // internal attributes
                    writer.Write(0u);                   // external attributes
                    writer.Write((uint)offsets[i]);
                    writer.Write(names[i]);
                }
                long directorySize = buffer.Position - directoryStart;

                writer.Write(EndOfCentralDirectorySignature);
                writer.Write((ushort)0);                // this disk
                writer.Write((ushort)0);                // disk with the central directory
                writer.Write((ushort)entries.Length);   // entries on this disk
                writer.Write((ushort)entries.Length);   // entries total
                writer.Write((uint)directorySize);
                writer.Write((uint)directoryStart);
                writer.Write((ushort)0);                // comment length
                writer.Flush();
                return buffer.ToArray();
            }
        }

        /// <summary>
        /// Build a single-entry zip64 archive: 0xFFFFFFFF size/offset sentinels, a
        /// zip64 extra field, zip64 end of central directory record and locator, and
        /// the legacy record with its "look in the zip64 record" sentinels.
        /// </summary>
        internal static byte[] BuildZip64(EntrySpec entry)
        {
            if (entry.Deflate)
            {
                throw new ArgumentException("zip64 fixture only stores entries", nameof(entry));
            }

            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8))
            {
                byte[] data = entry.Data ?? new byte[0];
                byte[] name = NameBytesOf(entry);
                uint crc = data.Length == 0 ? 0u : Crc32.Compute(data, 0, data.Length);

                writer.Write(LocalHeaderSignature);
                writer.Write((ushort)45);               // version needed to extract
                writer.Write(entry.Flags);
                writer.Write((ushort)0);                // method: stored
                writer.Write((ushort)0);                // last mod time
                writer.Write((ushort)0);                // last mod date
                writer.Write(crc);
                writer.Write(uint.MaxValue);            // compressed size: see zip64 extra
                writer.Write(uint.MaxValue);            // uncompressed size: see zip64 extra
                writer.Write((ushort)name.Length);
                writer.Write((ushort)20);               // extra field length
                writer.Write(name);
                writer.Write((ushort)0x0001);           // zip64 extended information
                writer.Write((ushort)16);
                writer.Write((ulong)data.Length);       // uncompressed
                writer.Write((ulong)data.Length);       // compressed
                writer.Write(data);

                long directoryStart = buffer.Position;
                writer.Write(CentralHeaderSignature);
                writer.Write((ushort)45);               // version made by
                writer.Write((ushort)45);               // version needed to extract
                writer.Write(entry.Flags);
                writer.Write((ushort)0);                // method: stored
                writer.Write((ushort)0);                // last mod time
                writer.Write((ushort)0);                // last mod date
                writer.Write(crc);
                writer.Write(uint.MaxValue);            // compressed size: see zip64 extra
                writer.Write(uint.MaxValue);            // uncompressed size: see zip64 extra
                writer.Write((ushort)name.Length);
                writer.Write((ushort)28);               // extra field length: id + size + 3 x 8
                writer.Write((ushort)0);                // comment length
                writer.Write((ushort)0);                // disk number start
                writer.Write((ushort)0);                // internal attributes
                writer.Write(0u);                       // external attributes
                writer.Write(uint.MaxValue);            // local header offset: see zip64 extra
                writer.Write(name);
                writer.Write((ushort)0x0001);           // zip64 extended information
                writer.Write((ushort)24);
                writer.Write((ulong)data.Length);       // uncompressed
                writer.Write((ulong)data.Length);       // compressed
                writer.Write((ulong)0);                 // local header offset
                long directorySize = buffer.Position - directoryStart;

                long zip64End = buffer.Position;
                writer.Write(Zip64EndOfCentralDirectorySignature);
                writer.Write((ulong)44);                // size of the remaining record
                writer.Write((ushort)45);               // version made by
                writer.Write((ushort)45);               // version needed to extract
                writer.Write(0u);                       // this disk
                writer.Write(0u);                       // disk with the central directory
                writer.Write(1ul);                      // entries on this disk
                writer.Write(1ul);                      // entries total
                writer.Write((ulong)directorySize);
                writer.Write((ulong)directoryStart);

                writer.Write(Zip64LocatorSignature);
                writer.Write(0u);                       // disk with the zip64 record
                writer.Write((ulong)zip64End);          // offset of the zip64 record
                writer.Write(1u);                       // total disks

                writer.Write(EndOfCentralDirectorySignature);
                writer.Write((ushort)0);                // this disk
                writer.Write((ushort)0);                // disk with the central directory
                writer.Write(ushort.MaxValue);          // entries on this disk: see zip64
                writer.Write(ushort.MaxValue);          // entries total: see zip64
                writer.Write(uint.MaxValue);            // directory size: see zip64
                writer.Write(uint.MaxValue);            // directory offset: see zip64
                writer.Write((ushort)0);                // comment length
                writer.Flush();
                return buffer.ToArray();
            }
        }

        /// <summary>
        /// Build an archive holding <paramref name="count"/> empty stored entries, for the
        /// entry-count gate. More than 65535 entries does not fit the legacy record, so
        /// those archives get the zip64 end of central directory (with 64-bit counts) and
        /// the legacy record keeps its "see the zip64 record" sentinels.
        /// </summary>
        internal static byte[] BuildWithEntryCount(int count, bool zip64)
        {
            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8))
            {
                List<byte[]> names = new List<byte[]>(count);
                List<long> offsets = new List<long>(count);
                for (int i = 0; i < count; i++)
                {
                    byte[] name = Encoding.ASCII.GetBytes("e" + i);
                    names.Add(name);
                    offsets.Add(buffer.Position);

                    writer.Write(LocalHeaderSignature);
                    writer.Write((ushort)20);
                    writer.Write((ushort)0);            // flags
                    writer.Write((ushort)0);            // method: stored
                    writer.Write((ushort)0);            // time
                    writer.Write((ushort)0);            // date
                    writer.Write(0u);                   // crc
                    writer.Write(0u);                   // compressed size
                    writer.Write(0u);                   // uncompressed size
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);            // extra field length
                    writer.Write(name);
                }

                long directoryStart = buffer.Position;
                for (int i = 0; i < count; i++)
                {
                    writer.Write(CentralHeaderSignature);
                    writer.Write((ushort)20);
                    writer.Write((ushort)20);
                    writer.Write((ushort)0);            // flags
                    writer.Write((ushort)0);            // method: stored
                    writer.Write((ushort)0);            // time
                    writer.Write((ushort)0);            // date
                    writer.Write(0u);                   // crc
                    writer.Write(0u);                   // compressed size
                    writer.Write(0u);                   // uncompressed size
                    writer.Write((ushort)names[i].Length);
                    writer.Write((ushort)0);            // extra field length
                    writer.Write((ushort)0);            // comment length
                    writer.Write((ushort)0);            // disk number start
                    writer.Write((ushort)0);            // internal attributes
                    writer.Write(0u);                   // external attributes
                    writer.Write((uint)offsets[i]);
                    writer.Write(names[i]);
                }
                long directorySize = buffer.Position - directoryStart;

                if (zip64)
                {
                    long zip64End = buffer.Position;
                    writer.Write(Zip64EndOfCentralDirectorySignature);
                    writer.Write((ulong)44);
                    writer.Write((ushort)45);
                    writer.Write((ushort)45);
                    writer.Write(0u);                   // this disk
                    writer.Write(0u);                   // disk with the central directory
                    writer.Write((ulong)count);
                    writer.Write((ulong)count);
                    writer.Write((ulong)directorySize);
                    writer.Write((ulong)directoryStart);

                    writer.Write(Zip64LocatorSignature);
                    writer.Write(0u);
                    writer.Write((ulong)zip64End);
                    writer.Write(1u);
                }

                writer.Write(EndOfCentralDirectorySignature);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write(zip64 ? ushort.MaxValue : (ushort)count);
                writer.Write(zip64 ? ushort.MaxValue : (ushort)count);
                writer.Write(zip64 ? uint.MaxValue : (uint)directorySize);
                writer.Write(zip64 ? uint.MaxValue : (uint)directoryStart);
                writer.Write((ushort)0);                // comment length
                writer.Flush();
                return buffer.ToArray();
            }
        }

        /// <summary>Drop <paramref name="count"/> bytes off the end of the archive.</summary>
        internal static byte[] Truncate(byte[] archive, int count)
        {
            byte[] damaged = new byte[archive.Length - count];
            Array.Copy(archive, damaged, damaged.Length);
            return damaged;
        }

        private static byte[] NameBytesOf(EntrySpec entry)
        {
            if (entry.NameBytes != null)
            {
                return entry.NameBytes;
            }
            return Encoding.UTF8.GetBytes(entry.Name ?? string.Empty);
        }

        private static byte[] Deflate(byte[] data)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (DeflateStream deflate = new DeflateStream(output, CompressionMode.Compress, true))
                {
                    deflate.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }
    }

    /// <summary>CRC-32 (IEEE 802.3, the zip polynomial) - the zip format needs it.</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                }
                table[i] = value;
            }
            return table;
        }

        internal static uint Compute(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
