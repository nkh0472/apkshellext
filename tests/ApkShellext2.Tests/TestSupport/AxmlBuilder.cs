using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Minimal writer for the resource-compiled AndroidManifest.xml (AXML) the readers
    /// consume. The real manifests in fixtures are Spectroid's; these helpers build the
    /// variants that sample does not contain: a numeric or a codename minSdk, no
    /// uses-sdk at all, an explicit true/false debuggable, the same attribute name in a
    /// non-android namespace, and an element whose attribute table claims more entries
    /// than its own chunk holds.
    ///
    /// Layout follows aapt: RES_XML header, a UTF-8 string pool, namespace chunks, then
    /// the element tree. Attributes are 20 bytes (namespace ref, name ref, raw value,
    /// typed value). Keep the shape identical to what aapt emits, because the readers
    /// walk it byte by byte.
    /// </summary>
    internal static class AxmlBuilder
    {
        public const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        public const string ToolsNamespace = "http://schemas.android.com/tools";

        private const short ResXmlType = 0x0003;
        private const short ResXmlStartNamespaceType = 0x0100;
        private const short ResXmlEndNamespaceType = 0x0101;
        private const short ResXmlStartElementType = 0x0102;
        private const short ResXmlEndElementType = 0x0103;
        private const short ResStringPoolType = 0x0001;
        private const int Utf8Flag = 0x0100;
        private const short AttributeStructSize = 20;
        private const short ElementHeaderSize = 16;
        private const short NodeHeaderSize = 16;
        private const uint NoIndex = 0xffffffff;
        private const short AttributeStart = 20;

        /// <summary>Res_value data types the facts walk cares about.</summary>
        public const byte TypeString = 0x03;
        public const byte TypeIntDec = 0x10;
        public const byte TypeIntBoolean = 0x12;

        public static Element Root(string name)
        {
            return new Element(name);
        }

        /// <summary>Compiles the element tree into the AXML byte stream.</summary>
        public static byte[] Build(Element root)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            // One pool collects the strings in the order the body writes them (namespace
            // uris first, then element and attribute names, then values), so the references
            // in the body and the pool at the front of the stream agree.
            Pool pool = new Pool();
            byte[] body = Body(root, pool);
            byte[] poolBytes = pool.Encode();
            int total = 8 + poolBytes.Length + body.Length;

            using (MemoryStream ms = new MemoryStream(total))
            using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(ResXmlType);
                writer.Write((short)8);
                writer.Write(total);
                writer.Write(poolBytes);
                writer.Write(body);
                writer.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] Body(Element root, Pool pool)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                WriteNamespace(writer, pool, ResXmlStartNamespaceType, AndroidNamespace);
                if (UsesToolsNamespace(root))
                {
                    WriteNamespace(writer, pool, ResXmlStartNamespaceType, ToolsNamespace);
                }
                WriteElement(writer, pool, root);
                if (UsesToolsNamespace(root))
                {
                    WriteNamespace(writer, pool, ResXmlEndNamespaceType, ToolsNamespace);
                }
                WriteNamespace(writer, pool, ResXmlEndNamespaceType, AndroidNamespace);
                writer.Flush();
                return ms.ToArray();
            }
        }

        private static bool UsesToolsNamespace(Element root)
        {
            foreach (Attribute attribute in EnumerateAttributes(root))
            {
                if (string.Equals(attribute.Namespace, ToolsNamespace, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<Attribute> EnumerateAttributes(Element element)
        {
            foreach (Attribute attribute in element.Attributes)
            {
                yield return attribute;
            }
            foreach (Element child in element.Children)
            {
                foreach (Attribute attribute in EnumerateAttributes(child))
                {
                    yield return attribute;
                }
            }
        }

        private static void WriteNamespace(BinaryWriter writer, Pool pool, short type, string uri)
        {
            writer.Write(type);
            writer.Write(NodeHeaderSize);
            writer.Write(24);
            writer.Write(1);            // line number
            writer.Write(NoIndex);      // comment
            writer.Write(NoIndex);      // prefix: the manifests in this repo use no prefix
            writer.Write((uint)pool.Intern(uri));
        }

        private static void WriteElement(BinaryWriter writer, Pool pool, Element element)
        {
            int written = element.Attributes.Count;
            int declared = element.DeclaredAttributeCount >= 0 ? element.DeclaredAttributeCount : written;
            int chunkSize = 36 + AttributeStructSize * written;

            writer.Write(ResXmlStartElementType);
            writer.Write(ElementHeaderSize);
            writer.Write(chunkSize);
            writer.Write(1);                    // line number
            writer.Write(NoIndex);              // comment
            writer.Write(NoIndex);              // element namespace
            writer.Write((uint)pool.Intern(element.Name));
            writer.Write(AttributeStart);
            writer.Write(AttributeStructSize);
            writer.Write((short)declared);
            writer.Write((short)0);             // id index
            writer.Write((short)0);             // class index
            writer.Write((short)0);             // style index

            foreach (Attribute attribute in element.Attributes)
            {
                WriteAttribute(writer, pool, attribute);
            }
            // A declared count above the written one leaves the header promising entries
            // past the end of this chunk - the shape the readers must reject, not follow.

            // Children sit between this element's start and its end, the way aapt nests them.
            foreach (Element child in element.Children)
            {
                WriteElement(writer, pool, child);
            }

            writer.Write(ResXmlEndElementType);
            writer.Write(NodeHeaderSize);
            writer.Write(24);
            writer.Write(1);
            writer.Write(NoIndex);
            writer.Write(NoIndex);
            writer.Write((uint)pool.Intern(element.Name));
        }

        private static void WriteAttribute(BinaryWriter writer, Pool pool, Attribute attribute)
        {
            uint data = attribute.Data;
            uint rawValue = NoIndex;
            if (attribute.DataType == TypeString)
            {
                data = (uint)pool.Intern(attribute.Text);
                rawValue = data;
            }

            writer.Write(attribute.Namespace == null ? NoIndex : (uint)pool.Intern(attribute.Namespace));
            writer.Write((uint)pool.Intern(attribute.Name));
            writer.Write(rawValue);
            writer.Write((short)8);     // size of the typed value
            writer.Write((byte)0);      // res0
            writer.Write(attribute.DataType);
            writer.Write(data);
        }

        public sealed class Element
        {
            private readonly List<Attribute> attributes = new List<Attribute>();
            private readonly List<Element> children = new List<Element>();

            public Element(string name)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("element name is required", "name");
                }
                Name = name;
            }

            public string Name { get; private set; }

            public IList<Attribute> Attributes
            {
                get { return attributes; }
            }

            public IList<Element> Children
            {
                get { return children; }
            }

            /// <summary>Overrides the attribute count in the element header without writing
            /// the entries: an element whose attribute table runs past its own chunk.</summary>
            public int DeclaredAttributeCount { get; set; } = -1;

            public Element Add(string name)
            {
                Element child = new Element(name);
                children.Add(child);
                return child;
            }

            /// <summary>Attribute without a namespace, as package/versionName/versionCode use.</summary>
            public Element Attr(string name, string value)
            {
                attributes.Add(Attribute.String(null, name, value));
                return this;
            }

            public Element AndroidAttr(string name, string value)
            {
                attributes.Add(Attribute.String(AndroidNamespace, name, value));
                return this;
            }

            public Element AndroidInt(string name, int value)
            {
                attributes.Add(Attribute.Integer(AndroidNamespace, name, value));
                return this;
            }

            public Element AndroidBool(string name, bool value)
            {
                attributes.Add(Attribute.Boolean(AndroidNamespace, name, value));
                return this;
            }

            public Element ToolsInt(string name, int value)
            {
                attributes.Add(Attribute.Integer(ToolsNamespace, name, value));
                return this;
            }
        }

        public sealed class Attribute
        {
            private Attribute(string ns, string name, byte dataType, uint data, string text)
            {
                Namespace = ns;
                Name = name;
                DataType = dataType;
                Data = data;
                Text = text;
            }

            public string Namespace { get; private set; }

            public string Name { get; private set; }

            public byte DataType { get; private set; }

            public uint Data { get; private set; }

            /// <summary>Pool text of a TYPE_STRING attribute; null for the typed kinds.</summary>
            public string Text { get; private set; }

            public static Attribute String(string ns, string name, string value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                return new Attribute(ns, name, TypeString, 0, value);
            }

            public static Attribute Integer(string ns, string name, int value)
            {
                return new Attribute(ns, name, TypeIntDec, unchecked((uint)value), null);
            }

            public static Attribute Boolean(string ns, string name, bool value)
            {
                return new Attribute(ns, name, TypeIntBoolean, value ? 1u : 0u, null);
            }
        }

        /// <summary>UTF-8 string pool with the sorted-by-insertion order aapt produces.</summary>
        private sealed class Pool
        {
            private const int HeaderSize = 28;
            private readonly List<string> items = new List<string>();
            private readonly Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);

            public int Intern(string value)
            {
                int position;
                if (index.TryGetValue(value, out position))
                {
                    return position;
                }
                position = items.Count;
                index.Add(value, position);
                items.Add(value);
                return position;
            }

            public byte[] Encode()
            {
                List<byte[]> encoded = new List<byte[]>(items.Count);
                int dataSize = 0;
                foreach (string item in items)
                {
                    byte[] bytes = EncodeString(item);
                    encoded.Add(bytes);
                    dataSize += bytes.Length;
                }

                int stringsStart = HeaderSize + 4 * items.Count;
                int chunkSize = Align4(stringsStart + dataSize);

                using (MemoryStream ms = new MemoryStream(chunkSize))
                using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
                {
                    writer.Write(ResStringPoolType);
                    writer.Write((short)HeaderSize);
                    writer.Write(chunkSize);
                    writer.Write(items.Count);
                    writer.Write(0);            // style count
                    writer.Write(Utf8Flag);
                    writer.Write(stringsStart);
                    writer.Write(0);            // styles start

                    int offset = 0;
                    foreach (byte[] bytes in encoded)
                    {
                        writer.Write(offset);
                        offset += bytes.Length;
                    }
                    foreach (byte[] bytes in encoded)
                    {
                        writer.Write(bytes);
                    }

                    writer.Flush();
                    byte[] written = ms.ToArray();
                    if (written.Length < chunkSize)
                    {
                        Array.Resize(ref written, chunkSize);
                    }
                    return written;
                }
            }

            private static byte[] EncodeString(string value)
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(value);
                if (utf8.Length > 0x7fff)
                {
                    throw new ArgumentException("string pool entry is longer than the u16 length prefix allows");
                }

                using (MemoryStream ms = new MemoryStream(utf8.Length + 5))
                using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
                {
                    WriteLength(writer, value.Length);
                    WriteLength(writer, utf8.Length);
                    writer.Write(utf8);
                    writer.Write((byte)0);
                    writer.Flush();
                    return ms.ToArray();
                }
            }

            /// <summary>The pool stores lengths in one byte below 0x80 and in two above it,
            /// the first one carrying the high bits.</summary>
            private static void WriteLength(BinaryWriter writer, int length)
            {
                if (length > 0x7fff)
                {
                    throw new ArgumentException("string pool entry is longer than the length prefix allows");
                }
                if (length > 0x7f)
                {
                    writer.Write((byte)(0x80 | (length >> 8)));
                    writer.Write((byte)(length & 0xff));
                }
                else
                {
                    writer.Write((byte)length);
                }
            }

            private static int Align4(int value)
            {
                return (value + 3) & ~3;
            }
        }
    }
}
