using System;
using System.IO;
using System.Text;
using System.Xml;

namespace ApkQuickReader
{
    /// <summary>
    /// The binary XML (AXML) walk: string pool, resource map, chunk walker and the
    /// attribute value renderer. ApkReader and AabReader used to carry one copy each;
    /// the AabReader copy had already drifted (its chunk walk and string pool lookup
    /// had no bounds checks), so the hardened walk from ApkReader is the one that
    /// survives here and both readers now share it.
    /// The resource table this needs for reference attributes lives in ArscParser,
    /// which the four stage-0 hard gates were moved into earlier.
    /// </summary>
    internal sealed class AxmlParser
    {
        private readonly byte[] table;
        private readonly bool resolveReferences;
        private readonly bool densityRequested;
        private readonly bool ignoreXmlDensity;
        private readonly Action<string> log;
        private readonly ArscParser arsc;

        /// <summary>
        /// </summary>
        /// <param name="table">resources.arsc bytes, or null when the container has none</param>
        /// <param name="resolveReferences">when false a reference attribute keeps the
        /// pre-merge rendering as a bare number: an aab has no resource table, so there
        /// is nothing to resolve it against and the numbers must not start changing</param>
        /// <param name="densityRequested">the reader asked for one density bucket</param>
        /// <param name="ignoreXmlDensity">legacy-icon retry that ignores .xml entries</param>
        /// <param name="log">the owning reader's log sink (keeps its file name in the log)</param>
        internal AxmlParser(byte[] table, bool resolveReferences, bool densityRequested,
            bool ignoreXmlDensity, Action<string> log)
        {
            this.table = table;
            this.resolveReferences = resolveReferences;
            this.densityRequested = densityRequested;
            this.ignoreXmlDensity = ignoreXmlDensity;
            this.log = log ?? (delegate (string message) { });
            this.arsc = new ArscParser(table, QuickSearchStringPool);
        }

        // Every forward chunk walk in this class is driven by a
        // while (ms.Position < ms.Length) loop. A malformed chunk header
        // claiming size < 8 (the minimum RES chunk header) would make the
        // walk revisit the same offset forever and hang the shell UI thread.
        internal static void SeekNextChunk(MemoryStream ms, long chunkPos, int chunkSize)
        {
            if (chunkSize < 8)
                throw new InvalidDataException("chunk at offset " + chunkPos + " claims size " + chunkSize);
            ms.Seek(chunkPos + chunkSize, SeekOrigin.Begin);
        }

        /// <summary>
        /// Walk the document looking for the attribute of the element addressed by
        /// <paramref name="xpath"/> and render its value.
        /// </summary>
        internal string QuickSearchCompressedXml(byte[] xml, string xpath, string attribute, bool preferLocale)
        {
            using (MemoryStream ms = new MemoryStream(xml))
            using (BinaryReader br = new BinaryReader(ms))
            {
                log("Search xml for " + xpath + " " + attribute);
                string[] pathl = xpath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                ms.Seek(8, SeekOrigin.Begin); // skip header, no doubt it's an xml chunk
                ApkResource result = new ApkResource(0);
                int tagDepth = 0;
                int matchDepth = 0;

                // XML_START_ELEMENT CHUNK
                while (ms.Position < ms.Length)
                {
                    long chunkPos = ms.Position;
                    RES_TYPE chunkType = (RES_TYPE)br.ReadInt16();
                    short headerSize = br.ReadInt16();
                    int chunkSize = br.ReadInt32();
                    if (chunkType == RES_TYPE.RES_XML_START_ELEMENT_TYPE)
                    {
                        tagDepth++;

                        ms.Seek(8 + 4, SeekOrigin.Current); // skip line number & comment / namespace
                        string tag_s = QuickSearchCompressedXmlStringPoolAndResMap(xml, br.ReadUInt32());
                        if (tagDepth <= pathl.Length && tag_s.ToUpper() == pathl[tagDepth - 1].ToUpper())
                        {
                            matchDepth++;

                            if (matchDepth == pathl.Length)
                            { // match, read attributes
                                int attributeStart = br.ReadInt16();
                                int attributeSize = br.ReadInt16();
                                int attributeCount = br.ReadInt16();
                                for (int i = 0; i < attributeCount; i++)
                                {
                                    int offset = headerSize + attributeStart + attributeSize * i + 4;
                                    if (offset >= chunkSize)
                                    { // Error: comes to out of chunk
                                        throw new Exception("Out of Chunk when processing tag " + tag_s);
                                    }
                                    ms.Seek(chunkPos + offset, SeekOrigin.Begin); // ignore the ns
                                    uint ind = br.ReadUInt32();
                                    string name = QuickSearchCompressedXmlStringPoolAndResMap(xml, ind);
                                    if (name.ToUpper() == attribute.ToUpper())
                                    {
                                        ms.Seek(4 + 2 + 1, SeekOrigin.Current); // skip rawValue/size/0/
                                        DATA_TYPE dataType = (DATA_TYPE)br.ReadByte();
                                        uint data = br.ReadUInt32();
                                        return convertData(xml, dataType, data, preferLocale);
                                    }
                                }
                            }
                        }
                    }
                    else if (chunkType == RES_TYPE.RES_XML_END_ELEMENT_TYPE)
                    {
                        if (matchDepth == tagDepth)
                        {
                            matchDepth--;
                        }
                        tagDepth--;
                    }
                    SeekNextChunk(ms, chunkPos, chunkSize);
                }
                return "";
            }
        }

        /// <summary>
        /// Dump a binary XML document into a DOM. Throws with the byte offset of the
        /// failure, which is the only hint a bug report will carry.
        /// </summary>
        internal XmlDocument ExtractCompressedXml(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            using (BinaryReader br = new BinaryReader(ms))
            {
                long chunkPos = ms.Position;
                RES_TYPE chunkType = (RES_TYPE)br.ReadInt16();
                short headerSize = br.ReadInt16();
                int chunkSize = br.ReadInt32();
                ms.Seek(chunkPos + headerSize, SeekOrigin.Begin);

                XmlDocument doc = new XmlDocument();
                XmlNode currentNode = doc;
                XmlNamespaceManager nm = new XmlNamespaceManager(doc.NameTable);

                try
                {
                    // XML_START_ELEMENT CHUNK
                    while (ms.Position < ms.Length)
                    {
                        chunkPos = ms.Position;
                        chunkType = (RES_TYPE)br.ReadInt16();
                        headerSize = br.ReadInt16();
                        chunkSize = br.ReadInt32();
                        ms.Seek(chunkPos + headerSize, SeekOrigin.Begin);
                        if (chunkType == RES_TYPE.RES_XML_START_NAMESPACE_TYPE)
                        {
                            string prefix = QuickSearchStringPool(bytes, br.ReadUInt32());
                            string uri = QuickSearchStringPool(bytes, br.ReadUInt32());
                            nm.AddNamespace(prefix, uri);
                        }
                        else if (chunkType == RES_TYPE.RES_XML_CDATA_TYPE)
                        {
                            string cdatastr = QuickSearchStringPool(bytes, br.ReadUInt16());
                            XmlCDataSection cdata = doc.CreateCDataSection(cdatastr);
                            currentNode.AppendChild(cdata);
                        }
                        else if (chunkType == RES_TYPE.RES_XML_START_ELEMENT_TYPE)
                        {
                            string ns = QuickSearchCompressedXmlStringPoolAndResMap(bytes, br.ReadUInt32());
                            string tag_s = QuickSearchCompressedXmlStringPoolAndResMap(bytes, br.ReadUInt32());
                            XmlElement currentElem = doc.CreateElement(tag_s, ns);
                            currentNode.AppendChild(currentElem);
                            currentNode = currentElem;

                            int attributeStart = br.ReadInt16();
                            int attributeSize = br.ReadInt16();
                            int attributeCount = br.ReadInt16();
                            for (int i = 0; i < attributeCount; i++)
                            {
                                int offset = headerSize + attributeStart + attributeSize * i + 4;
                                ms.Seek(chunkPos + offset, SeekOrigin.Begin);
                                uint ind = br.ReadUInt32();
                                string name = QuickSearchCompressedXmlStringPoolAndResMap(bytes, ind);
                                ms.Seek(4 + 2 + 1, SeekOrigin.Current); // skip rawValue/size/0/
                                DATA_TYPE dataType = (DATA_TYPE)br.ReadByte();
                                uint data = br.ReadUInt32();
                                string val = convertData(bytes, dataType, data);
                                currentElem.SetAttribute(name, val);
                            }
                        }
                        else if (chunkType == RES_TYPE.RES_XML_END_ELEMENT_TYPE)
                        {
                            currentNode = currentNode.ParentNode;
                        }
                        SeekNextChunk(ms, chunkPos, chunkSize);
                    }
                    return doc;
                }
                catch (Exception ex)
                {
                    log(doc.InnerXml);
                    throw new Exception(ex.Message + "\nError happens during dump xml file @ bytes " + ms.Position.ToString());
                }
            }
        }

        private string convertData(byte[] bytes, DATA_TYPE type, UInt32 data)
        {
            return convertData(bytes, type, data, false);
        }

        private string convertData(byte[] bytes, DATA_TYPE type, UInt32 data, bool preferLocale)
        {
            switch (type)
            {
                case DATA_TYPE.TYPE_STRING:
                    return QuickSearchStringPool(bytes, data);
                case DATA_TYPE.TYPE_REFERENCE:
                    // an aab carries no resources.arsc: a reference keeps the bare
                    // number it was rendered as before the parsers were merged
                    if (!resolveReferences)
                        return data.ToString();
                    try
                    {
                        ApkResource r = arsc.QuickSearchResource((UInt32)data);
                        int ind = 0;
                        if (r.Count > 1)
                            // localized app names: label lookups prefer the configured language over density
                            ind = preferLocale
                                ? arsc.ApplyLocaleFilter(r, densityRequested, ignoreXmlDensity)
                                : arsc.ApplyFilter(r, densityRequested, ignoreXmlDensity);
                        return r.values[ind].ToString();
                    }
                    catch (Exception ex)
                    {
                        log("Error happen when finding resource with ID:0x" + data.ToString("X8") + ex.Message);
                        return "(0x" + data.ToString("X8") + ")";
                    }
                case DATA_TYPE.TYPE_INT_BOOLEAN:
                    return (data == 0) ? "false" : "true";
                case DATA_TYPE.TYPE_DIMENSION:
                    COMPLEX_TYPE t = (COMPLEX_TYPE)(data & 0xff);
                    string unit = (t == COMPLEX_TYPE.COMPLEX_UNIT_DIP) ? "dp" :
                                  (t == COMPLEX_TYPE.COMPLEX_UNIT_IN) ? "in" :
                                  (t == COMPLEX_TYPE.COMPLEX_UNIT_MM) ? "mm" :
                                  (t == COMPLEX_TYPE.COMPLEX_UNIT_PX) ? "px" :
                                  (t == COMPLEX_TYPE.COMPLEX_UNIT_SP) ? "sp" :
                                  (t == COMPLEX_TYPE.COMPLEX_UNIT_PT) ? "pt" : "";
                    return (data >> 8).ToString() + unit;
                case DATA_TYPE.TYPE_FLOAT:
                    float f = BitConverter.ToSingle(BitConverter.GetBytes(data), 0);
                    return f.ToString();
                case DATA_TYPE.TYPE_INT_COLOR_ARGB8:
                case DATA_TYPE.TYPE_INT_COLOR_ARGB4:
                case DATA_TYPE.TYPE_INT_COLOR_RGB4:
                case DATA_TYPE.TYPE_INT_COLOR_RGB8:
                    string hex = data.ToString("X8");
                    return "#" + hex;
                default:
                    return data.ToString();
            }
        }

        internal string QuickSearchCompressedXmlStringPoolAndResMap(byte[] xml, uint id)
        {
            if (id == 0xffffffff) return "";
            string result = QuickSearchStringPool(xml, id);
            if (result == "")
            {
                result = QuickSearchCompressedXMlResMap(xml, id);
                if (result == "") result = "(0x" + id.ToString("X8") + ")";
            }
            return result;
        }

        internal string QuickSearchStringPool(byte[] bytes, uint id)
        {
            if (id == 0xffffffff) return "";
            if (bytes == null || bytes.Length < 8) return "";
            using (MemoryStream ms = new MemoryStream(bytes))
            using (BinaryReader br = new BinaryReader(ms))
            {
                RES_TYPE chunkType = (RES_TYPE)br.ReadInt16();
                short headerSize = br.ReadInt16();
                if (headerSize < 8 || headerSize > ms.Length) return "";
                ms.Seek(headerSize, SeekOrigin.Begin);

                while (ms.Position + 8 <= ms.Length)
                {
                    long chunkPos = ms.Position;
                    chunkType = (RES_TYPE)br.ReadInt16();
                    headerSize = br.ReadInt16();
                    int chunkSize = br.ReadInt32();
                    // each walk step must land on a later chunk inside the buffer,
                    // otherwise a forged size would make this loop spin forever
                    if (headerSize < 8 || chunkSize < headerSize ||
                        chunkPos + chunkSize > ms.Length)
                        return "";
                    if (chunkType == RES_TYPE.RES_STRING_POOL_TYPE)
                    {
                        if (headerSize < 28) return "";
                        int stringcount = br.ReadInt32();
                        int stylecount = br.ReadInt32();
                        int flags = br.ReadInt32();
                        bool isUTF_8 = (flags & (1 << 8)) != 0;
                        int stringStart = br.ReadInt32();
                        // the offset array must contain id, and the declared data
                        // base must stay inside this chunk
                        if (stringcount < 0 || id >= (uint)stringcount ||
                            stringStart < 0 || stringStart > chunkSize ||
                            (long)id * 4 + 32 > chunkSize)
                            return "";
                        ms.Seek(4 + (long)id * 4, SeekOrigin.Current);
                        int stringPos = br.ReadInt32();
                        if (stringPos < 0 || (long)stringStart + stringPos > chunkSize)
                            return "";
                        ms.Seek(chunkPos + stringStart + stringPos, SeekOrigin.Begin);
                        if (isUTF_8)
                        {
                            int u16len = br.ReadByte(); // u16len
                            if ((u16len & 0x80) != 0)
                            {// larger than 128
                                u16len = ((u16len & 0x7F) << 8) + br.ReadByte();
                            }

                            int u8len = br.ReadByte(); // u8len
                            if ((u8len & 0x80) != 0)
                            {// larger than 128
                                u8len = ((u8len & 0x7F) << 8) + br.ReadByte();
                            }
                            // hostile pool entry may claim more bytes than the pool
                            // holds; ReadBytes would pre-allocate the claimed size (OOM)
                            if (u8len > ms.Length - ms.Position)
                                throw new InvalidDataException("string pool entry out of bounds");
                            return Encoding.UTF8.GetString(br.ReadBytes(u8len));
                        }
                        else // UTF_16
                        {
                            int u16len = br.ReadUInt16();
                            if ((u16len & 0x8000) != 0)
                            {// larger than 32768
                                u16len = ((u16len & 0x7FFF) << 16) + br.ReadUInt16();
                            }

                            if (u16len < 0 || u16len > (ms.Length - ms.Position) / 2)
                                throw new InvalidDataException("string pool entry out of bounds");
                            return Encoding.Unicode.GetString(br.ReadBytes(u16len * 2));
                        }
                    }
                    else
                    {
                        ms.Seek(chunkPos + chunkSize, SeekOrigin.Begin);
                    }
                }
                return "";
            }
        }

        internal string QuickSearchCompressedXMlResMap(byte[] xml, uint id)
        {
            if (id == 0xffffffff) return "";
            if (xml == null || xml.Length < 8) return "";
            using (MemoryStream ms = new MemoryStream(xml))
            using (BinaryReader br = new BinaryReader(ms))
            {
                RES_TYPE chunkType = (RES_TYPE)br.ReadInt16();
                short headerSize = br.ReadInt16();
                if (headerSize < 8 || headerSize > ms.Length) return "";
                ms.Seek(headerSize, SeekOrigin.Begin);

                while (ms.Position + 8 <= ms.Length)
                {
                    long chunkPos = ms.Position;
                    chunkType = (RES_TYPE)br.ReadInt16();
                    headerSize = br.ReadInt16();
                    int chunkSize = br.ReadInt32();
                    // as in QuickSearchStringPool: never walk backwards or outside
                    if (headerSize < 8 || chunkSize < headerSize ||
                        chunkPos + chunkSize > ms.Length)
                        return "";

                    if (chunkType == RES_TYPE.RES_XML_RESOURCE_MAP_TYPE)
                    {
                        //Resource map
                        if ((long)id * 4 + 12 > chunkSize) return "";
                        ms.Seek((long)id * 4, SeekOrigin.Current);
                        string result = Enum.GetName(typeof(R_attr), br.ReadUInt32());
                        if (result != null)
                            return result;
                        else
                            return "";
                    }
                    else
                    {
                        ms.Seek(chunkPos + chunkSize, SeekOrigin.Begin);
                    }
                }
                return "";
            }
        }
    }
}
