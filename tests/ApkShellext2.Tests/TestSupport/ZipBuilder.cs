using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Minimal in-memory zip writer for the archives System.IO.Compression cannot
    /// produce: genuinely STORED entries (its NoCompression still deflates on
    /// .NET Framework).
    ///
    /// Hermetic by design - every archive is assembled here, so the safety tests
    /// never depend on a sample library or the network.
    /// </summary>
    internal static class ZipBuilder
    {
        /// <summary>One entry to write. All entries are STORED (method 0).</summary>
        internal sealed class EntrySpec
        {
            /// <summary>Entry name; ASCII names only (the writer encodes UTF-8).</summary>
            public string Name;

            public byte[] Data = new byte[0];
        }

        private const uint LocalHeaderSignature = 0x04034b50;
        private const uint CentralHeaderSignature = 0x02014b50;
        private const uint EndOfCentralDirectorySignature = 0x06054b50;

        internal static byte[] Build(params EntrySpec[] entries)
        {
            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8))
            {
                List<byte[]> names = new List<byte[]>();
                List<long> offsets = new List<long>();
                List<uint> crcs = new List<uint>();
                List<int> sizes = new List<int>();

                foreach (EntrySpec entry in entries)
                {
                    byte[] data = entry.Data ?? new byte[0];
                    byte[] name = Encoding.UTF8.GetBytes(entry.Name);
                    uint crc = data.Length == 0 ? 0u : Crc32.Compute(data, 0, data.Length);

                    names.Add(name);
                    offsets.Add(buffer.Position);
                    crcs.Add(crc);
                    sizes.Add(data.Length);

                    writer.Write(LocalHeaderSignature);
                    writer.Write((ushort)20);           // version needed to extract
                    writer.Write((ushort)0);            // general purpose flags
                    writer.Write((ushort)0);            // method: stored
                    writer.Write((ushort)0);            // last mod time
                    writer.Write((ushort)0);            // last mod date
                    writer.Write(crc);
                    writer.Write((uint)data.Length);     // compressed size == stored size
                    writer.Write((uint)data.Length);     // uncompressed size
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);            // extra field length
                    writer.Write(name);
                    writer.Write(data);
                }

                long directoryStart = buffer.Position;
                for (int i = 0; i < entries.Length; i++)
                {
                    writer.Write(CentralHeaderSignature);
                    writer.Write((ushort)20);           // version made by
                    writer.Write((ushort)20);           // version needed to extract
                    writer.Write((ushort)0);            // general purpose flags
                    writer.Write((ushort)0);            // method: stored
                    writer.Write((ushort)0);            // last mod time
                    writer.Write((ushort)0);            // last mod date
                    writer.Write(crcs[i]);
                    writer.Write((uint)sizes[i]);
                    writer.Write((uint)sizes[i]);
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
