using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PlistCS;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// BinaryPlistReader is a hand-written parser fed by attacker data (a plist inside
    /// an APK), so its recursion has to be bounded by the file, not by the stack. The
    /// bytes below are assembled by TestSupport/BinaryPlistBuilder because only a
    /// malformed file can reach these paths - Plist.writeBinary cannot emit them.
    ///
    /// Depth accounting: parseBinary(root, 0) enters an array at depth d and parses its
    /// children with parseBinary(child, d); the array case calls parseBinaryArray(objRef,
    /// depth + 1), so the value at nesting level k is parsed at depth k. The guard fires
    /// on depth &gt; 32, which means 32 nested arrays are legal and 33 are not.
    /// </summary>
    [TestFixture]
    public class PlistDepthTests
    {
        [Test]
        public void Parses32NestedArrays()
        {
            object parsed = Plist.readPlist(BuildNestedArrays(32));

            object cursor = parsed;
            for (int level = 0; level < 32; level++)
            {
                // the reader parses arrays into List<object>, not object[]
                Assert.That(cursor, Is.InstanceOf<IList<object>>(), "nesting level " + level + " is not an array");
                IList<object> array = (IList<object>)cursor;
                Assert.That(array, Has.Count.EqualTo(1));
                cursor = array[0];
            }
            Assert.That(cursor, Is.EqualTo(1), "the innermost value must survive the round trip");
        }

        [Test]
        public void Rejects33NestedArrays()
        {
            PlistFormatException ex = Assert.Throws<PlistFormatException>(() => Plist.readPlist(BuildNestedArrays(33)));
            Assert.That(ex.Message, Does.Contain("nesting exceeds 32"),
                "the guard must name its limit; the message was: " + ex.Message);
        }

        [Test]
        [Timeout(10000)]
        public void RejectsASelfReferencingArray_WithoutRecursingForever()
        {
            // object 0 is an array whose only element is object 0: without the depth
            // guard this is an infinite recursion and a StackOverflowException, which
            // would kill explorer.exe (the process hosting this parser)
            byte[] plist = BinaryPlistBuilder.Build(new List<byte[]> { BinaryPlistBuilder.ArrayObject(0) });

            PlistFormatException ex = Assert.Throws<PlistFormatException>(() => Plist.readPlist(plist));
            Assert.That(ex.Message, Does.Contain("nesting exceeds 32"));
        }

        [Test]
        public void RejectsAnObjectReferenceOutsideTheOffsetTable()
        {
            byte[] plist = BinaryPlistBuilder.Build(new List<byte[]> { BinaryPlistBuilder.ArrayObject(5) });

            PlistFormatException ex = Assert.Throws<PlistFormatException>(() => Plist.readPlist(plist));
            Assert.That(ex.Message, Does.Contain("object reference").And.Contain("offset table"));
        }

        [Test]
        public void RejectsAnObjectOffsetOutsideTheObjectTable()
        {
            // the offset table says object 1 starts past the end of the object table;
            // the reader must reject the offset instead of slicing outside the file
            List<byte[]> objects = new List<byte[]>
            {
                BinaryPlistBuilder.ArrayObject(1),
                BinaryPlistBuilder.IntObject(1),
            };
            byte[] plist = BinaryPlistBuilder.Build(objects);

            // corrupt the second offset in the offset table to point at the offset table itself
            int offsetTableOffset = ReadTrailerOffsetTableOffset(plist);
            int offsetByteSize = plist[plist.Length - 26];
            int index = offsetTableOffset + offsetByteSize; // entry for object 1
            for (int i = 0; i < offsetByteSize; i++)
            {
                plist[index + i] = (byte)(offsetTableOffset >> (8 * (offsetByteSize - 1 - i)));
            }

            PlistFormatException ex = Assert.Throws<PlistFormatException>(() => Plist.readPlist(plist));
            Assert.That(ex.Message, Does.Contain("object offset").And.Contain("object table"),
                "the guard must report the offset it could not back with bytes; message was: " + ex.Message);
        }

        [Test]
        [Timeout(10000)]
        public void RejectsATruncatedFile()
        {
            // the trailer is read from the last 32 bytes, so cutting the file shifts
            // whatever guard notices it - what matters is that a truncated file is a
            // PlistFormatException and not an unbounded walk into the garbage
            byte[] complete = BuildNestedArrays(4);
            byte[] truncated = new byte[complete.Length - 10];
            Array.Copy(complete, truncated, truncated.Length);

            Assert.Throws<PlistFormatException>(() => Plist.readPlist(truncated));
        }

        [Test]
        public void RejectsAnOffsetTableOffsetBeyondTheFile()
        {
            byte[] plist = BuildNestedArrays(4);
            WriteBigEndian64(plist, plist.Length - 8, plist.Length + 64);

            PlistFormatException ex = Assert.Throws<PlistFormatException>(() => Plist.readPlist(plist));
            Assert.That(ex.Message, Does.Contain("offset table offset"));
        }

        [Test]
        public void RejectsAFileShorterThanItsHeaderAndTrailer()
        {
            // starts with the binary magic, so it is parsed as a binary plist, but is
            // far too short to hold the 32-byte trailer
            byte[] plist = Encoding.ASCII.GetBytes("bplist00");
            PlistFormatException ex = Assert.Throws<PlistFormatException>(() => Plist.readPlist(plist));
            Assert.That(ex.Message, Does.Contain("shorter than its header and trailer"));
        }

        private static byte[] BuildNestedArrays(int levels)
        {
            List<byte[]> objects = new List<byte[]>(levels + 1);
            for (int level = 0; level < levels; level++)
            {
                objects.Add(BinaryPlistBuilder.ArrayObject((byte)(level + 1)));
            }
            objects.Add(BinaryPlistBuilder.IntObject(1));
            return BinaryPlistBuilder.Build(objects);
        }

        private static int ReadTrailerOffsetTableOffset(byte[] plist)
        {
            byte[] value = new byte[8];
            Array.Copy(plist, plist.Length - 8, value, 0, 8);
            if (BitConverter.IsLittleEndian) Array.Reverse(value);
            return (int)BitConverter.ToInt64(value, 0);
        }

        private static void WriteBigEndian64(byte[] target, int offset, long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Array.Copy(bytes, 0, target, offset, 8);
        }
    }
}
