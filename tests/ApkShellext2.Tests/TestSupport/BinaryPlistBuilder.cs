using System;
using System.Collections.Generic;
using System.Text;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Assembles a binary plist ("bplist00") from raw object bodies.
    ///
    /// The point of hand-writing the bytes is that these tests pin the parser's own
    /// limits: a malformed file cannot be produced through Plist.writeBinary, and
    /// adding a plist-writing dependency to test a parser would be backwards. The
    /// layout follows the reader in PlistCS\PlistCS\Src\Plist.cs: the 8-byte magic
    /// header, the object table (object offsets are ABSOLUTE file positions), the
    /// offset table, then the 32-byte trailer
    /// (6 unused bytes, offset size, object reference size, object count BE64,
    /// top object BE64, offset table offset BE64).
    /// </summary>
    internal static class BinaryPlistBuilder
    {
        public static byte[] Build(IList<byte[]> objects, int topObject = 0, int objRefSize = 1)
        {
            if (objects == null || objects.Count == 0) throw new ArgumentException("at least one object is required", "objects");
            if (topObject < 0 || topObject >= objects.Count) throw new ArgumentOutOfRangeException("topObject");
            if (objRefSize < 1 || objRefSize > 8) throw new ArgumentOutOfRangeException("objRefSize");

            List<byte> file = new List<byte>();
            file.AddRange(Encoding.ASCII.GetBytes("bplist00"));

            int[] offsets = new int[objects.Count];
            for (int i = 0; i < objects.Count; i++)
            {
                offsets[i] = file.Count; // absolute, header included
                file.AddRange(objects[i]);
            }

            int offsetTableOffset = file.Count;
            int offsetByteSize = BytesNeeded(offsetTableOffset);
            foreach (int offset in offsets)
            {
                file.AddRange(BigEndian(offset, offsetByteSize));
            }

            file.AddRange(new byte[6]);
            file.Add((byte)offsetByteSize);
            file.Add((byte)objRefSize);
            file.AddRange(BigEndian64(objects.Count));
            file.AddRange(BigEndian64(topObject));
            file.AddRange(BigEndian64(offsetTableOffset));

            return file.ToArray();
        }

        /// <summary>An array object holding the given object references (one-byte refs).</summary>
        public static byte[] ArrayObject(params byte[] objectRefs)
        {
            if (objectRefs.Length > 14) throw new ArgumentOutOfRangeException("objectRefs", "the builder only emits the inline-count form");
            List<byte> bytes = new List<byte>();
            bytes.Add((byte)(0xA0 | objectRefs.Length));
            bytes.AddRange(objectRefs);
            return bytes.ToArray();
        }

        /// <summary>A 1-byte integer object holding <paramref name="value"/> (0-255).</summary>
        public static byte[] IntObject(byte value)
        {
            return new byte[] { 0x10, value };
        }

        /// <summary>An ASCII string object (literal, so the tests stay readable).</summary>
        public static byte[] StringObject(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            byte[] text = Encoding.ASCII.GetBytes(value);
            if (text.Length > 14) throw new ArgumentOutOfRangeException("value", "the builder only emits the inline-count form");
            List<byte> bytes = new List<byte>();
            bytes.Add((byte)(0x50 | text.Length));
            bytes.AddRange(text);
            return bytes.ToArray();
        }

        private static int BytesNeeded(int value)
        {
            for (int size = 1; size <= 4; size++)
            {
                if (value <= (1L << (8 * size)) - 1) return size;
            }
            return 8;
        }

        private static byte[] BigEndian(int value, int size)
        {
            byte[] full = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) System.Array.Reverse(full);
            byte[] trimmed = new byte[size];
            System.Array.Copy(full, full.Length - size, trimmed, 0, size);
            return trimmed;
        }

        private static byte[] BigEndian64(long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) System.Array.Reverse(bytes);
            return bytes;
        }
    }
}
