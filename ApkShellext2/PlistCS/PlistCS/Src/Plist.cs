//
//   PlistCS Property List (plist) serialization and parsing library.
//
//   https://github.com/animetrics/PlistCS
//   
//   Copyright (c) 2011 Animetrics Inc. (marc@animetrics.com)
//   
//   Permission is hereby granted, free of charge, to any person obtaining a copy
//   of this software and associated documentation files (the "Software"), to deal
//   in the Software without restriction, including without limitation the rights
//   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//   copies of the Software, and to permit persons to whom the Software is
//   furnished to do so, subject to the following conditions:
//   
//   The above copyright notice and this permission notice shall be included in
//   all copies or substantial portions of the Software.
//   
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//   THE SOFTWARE.


using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace PlistCS
{
    /// <summary>
    /// Thrown when a plist file is structurally invalid in a way the parser can
    /// detect from the bytes alone, e.g. an object reference outside the offset
    /// table or nesting deeper than the parser accepts.
    /// </summary>
    public class PlistFormatException : FormatException
    {
        public PlistFormatException()
        {
        }

        public PlistFormatException(string message) : base(message)
        {
        }

        public PlistFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public static class Plist
    {
        // bplist containers reference each other by index, so one file can chain
        // dictionaries/arrays as deep as it likes; every level costs a recursive
        // parseBinary frame. A cyclic reference recurses forever. Both are file
        // data and must not be able to exhaust the stack: real Apple plists stay
        // well below ten levels.
        private const int MaxBinaryPlistDepth = 32;

        private static List<int> writeOffsetTable = new List<int>();
        private static List<byte> writeObjectTable = new List<byte>();
        private static int writeRefCount;
        private static int writeObjRefSize;
        private static int writeOffsetByteSize;
        private static long writeOffsetTableOffset;

        #region Public Functions

        public static object readPlist(string path)
        {
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                return readPlist(f, plistType.Auto);
            }
        }

        public static object readPlistSource(string source)
        {
            return readPlist(System.Text.Encoding.UTF8.GetBytes(source));
        }

        public static object readPlist(byte[] data)
        {
            return readPlist(new MemoryStream(data), plistType.Auto);
        }

        public static plistType getPlistType(Stream stream)
        {
            byte[] magicHeader = new byte[8];
            stream.Read(magicHeader, 0, 8);

            if (BitConverter.ToInt64(magicHeader, 0) == 3472403351741427810)
            {
                return plistType.Binary;
            }
            else
            {
                return plistType.Xml;
            }
        }

        public static object readPlist(Stream stream, plistType type)
        {
            if (type == plistType.Auto)
            {
                type = getPlistType(stream);
                stream.Seek(0, SeekOrigin.Begin);
            }

            if (type == plistType.Binary)
            {
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    byte[] data = reader.ReadBytes((int) reader.BaseStream.Length);
                    return readBinary(data);
                }
            }
            else
            {
                XmlDocument xml = new XmlDocument();
                xml.XmlResolver = null;
                xml.Load(stream);
                return readXml(xml);
            }
        }

        public static void writeXml(object value, string path)
        {
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.Write(writeXml(value));
            }
        }

        public static void writeXml(object value, Stream stream)
        {
            using (StreamWriter writer = new StreamWriter(stream))
            {
                writer.Write(writeXml(value));
            }
        }

        public static string writeXml(object value)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                XmlWriterSettings xmlWriterSettings = new XmlWriterSettings();
                xmlWriterSettings.Encoding = new System.Text.UTF8Encoding(false);
                xmlWriterSettings.ConformanceLevel = ConformanceLevel.Document;
                xmlWriterSettings.Indent = true;

                using (XmlWriter xmlWriter = XmlWriter.Create(ms, xmlWriterSettings))
                {
                    xmlWriter.WriteStartDocument(); 
                    //xmlWriter.WriteComment("DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" " + "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\"");
                    xmlWriter.WriteDocType("plist", "-//Apple Computer//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null);
                    xmlWriter.WriteStartElement("plist");
                    xmlWriter.WriteAttributeString("version", "1.0");
                    compose(value, xmlWriter);
                    xmlWriter.WriteEndElement();
                    xmlWriter.WriteEndDocument();
                    xmlWriter.Flush();
                    xmlWriter.Close();
                    return System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        public static void writeBinary(object value, string path)
        {
            using (BinaryWriter writer = new BinaryWriter(new FileStream(path, FileMode.Create)))
            {
                writer.Write(writeBinary(value));
            }
        }

        public static void writeBinary(object value, Stream stream)
        {
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(writeBinary(value));
            }
        }

        public static byte[] writeBinary(object value)
        {
            writeOffsetTable.Clear();
            writeObjectTable.Clear();
            writeRefCount = 0;
            writeObjRefSize = 0;
            writeOffsetByteSize = 0;
            writeOffsetTableOffset = 0;

            //Do not count the root node, subtract by 1
            int totalRefs = countObject(value) - 1;

            writeRefCount = totalRefs;

            writeObjRefSize = RegulateNullBytes(BitConverter.GetBytes(writeRefCount)).Length;

            composeBinary(value);

            writeBinaryString("bplist00", false);

            writeOffsetTableOffset = (long)writeObjectTable.Count;

            writeOffsetTable.Add(writeObjectTable.Count - 8);

            writeOffsetByteSize = RegulateNullBytes(BitConverter.GetBytes(writeOffsetTable[writeOffsetTable.Count-1])).Length;

            List<byte> offsetBytes = new List<byte>();

            writeOffsetTable.Reverse();

            for (int i = 0; i < writeOffsetTable.Count; i++)
            {
                writeOffsetTable[i] = writeObjectTable.Count - writeOffsetTable[i];
                byte[] buffer = RegulateNullBytes(BitConverter.GetBytes(writeOffsetTable[i]), writeOffsetByteSize);
                Array.Reverse(buffer);
                offsetBytes.AddRange(buffer);
            }

            writeObjectTable.AddRange(offsetBytes);

            writeObjectTable.AddRange(new byte[6]);
            writeObjectTable.Add(Convert.ToByte(writeOffsetByteSize));
            writeObjectTable.Add(Convert.ToByte(writeObjRefSize));

            var a = BitConverter.GetBytes((long) totalRefs + 1);
            Array.Reverse(a);
            writeObjectTable.AddRange(a);

            writeObjectTable.AddRange(BitConverter.GetBytes((long)0));
            a = BitConverter.GetBytes(writeOffsetTableOffset);
            Array.Reverse(a);
            writeObjectTable.AddRange(a);

            return writeObjectTable.ToArray();
        }

        #endregion

        #region Private Functions

        private static object readXml(XmlDocument xml)
        {
            XmlNode rootNode = xml.DocumentElement.ChildNodes[0];
            return parse(rootNode);
        }

        private static object readBinary(byte[] data)
        {
            return new BinaryPlistReader(data).Parse();
        }

        private static Dictionary<string, object> parseDictionary(XmlNode node)
        {
            XmlNodeList children = node.ChildNodes;
            if (children.Count % 2 != 0)
            {
                throw new DataMisalignedException("Dictionary elements must have an even number of child nodes");
            }

            Dictionary<string, object> dict = new Dictionary<string, object>();

            for (int i = 0; i < children.Count; i += 2)
            {
                XmlNode keynode = children[i];
                XmlNode valnode = children[i + 1];

                if (keynode.Name != "key")
                {
                    throw new ApplicationException("expected a key node");
                }

                object result = parse(valnode);

                if (result != null)
                {
                    dict.Add(keynode.InnerText, result);
                }
            }

            return dict;
        }

        private static List<object> parseArray(XmlNode node)
        {
            List<object> array = new List<object>();

            foreach (XmlNode child in node.ChildNodes)
            {
                object result = parse(child);
                if (result != null)
                {
                    array.Add(result);
                }
            }

            return array;
        }

        private static void composeArray(List<object> value, XmlWriter writer)
        {
            writer.WriteStartElement("array");
            foreach (object obj in value)
            {
                compose(obj, writer);
            }
            writer.WriteEndElement();
        }

        private static object parse(XmlNode node)
        {
            switch (node.Name)
            {
                case "dict":
                    return parseDictionary(node);
                case "array":
                    return parseArray(node);
                case "string":
                    return node.InnerText;
                case "integer":
                  //  int result;
                    //int.TryParse(node.InnerText, System.Globalization.NumberFormatInfo.InvariantInfo, out result);
                    return Convert.ToInt32(node.InnerText, System.Globalization.NumberFormatInfo.InvariantInfo);
                case "real":
                    return Convert.ToDouble(node.InnerText,System.Globalization.NumberFormatInfo.InvariantInfo);
                case "false":
                    return false;
                case "true":
                    return true;
                case "null":
                    return null;
                case "date":
                    return XmlConvert.ToDateTime(node.InnerText, XmlDateTimeSerializationMode.Utc);
                case "data":
                    return Convert.FromBase64String(node.InnerText);
            }

            throw new ApplicationException(String.Format("Plist Node `{0}' is not supported", node.Name));
        }

        private static void compose(object value, XmlWriter writer)
        {

            if (value == null || value is string)
            {
                writer.WriteElementString("string", value as string);
            }
            else if (value is int || value is long)
            {
                writer.WriteElementString("integer", ((int)value).ToString(System.Globalization.NumberFormatInfo.InvariantInfo));
            }
            else if (value is System.Collections.Generic.Dictionary<string, object> ||
              value.GetType().ToString().StartsWith("System.Collections.Generic.Dictionary`2[System.String"))
            {
                //Convert to Dictionary<string, object>
                Dictionary<string, object> dic = value as Dictionary<string, object>;
                if (dic == null)
                {
                    dic = new Dictionary<string, object>();
                    IDictionary idic = (IDictionary)value;
                    foreach (var key in idic.Keys)
                    {
                        dic.Add(key.ToString(), idic[key]);
                    }
                }
                writeDictionaryValues(dic, writer);
            }
            else if (value is List<object>)
            {
                composeArray((List<object>)value, writer);
            }
            else if (value is byte[])
            {
                writer.WriteElementString("data", Convert.ToBase64String((Byte[])value));
            }
            else if (value is float || value is double)
            {
                writer.WriteElementString("real", ((double)value).ToString(System.Globalization.NumberFormatInfo.InvariantInfo));
            }
            else if (value is DateTime)
            {
                DateTime time = (DateTime)value;
                string theString = XmlConvert.ToString(time, XmlDateTimeSerializationMode.Utc);
                writer.WriteElementString("date", theString);//, "yyyy-MM-ddTHH:mm:ssZ"));
            }
            else if (value is bool)
            {
                writer.WriteElementString(value.ToString().ToLower(), "");
            }
            else
            {
                throw new Exception(String.Format("Value type '{0}' is unhandled", value.GetType().ToString()));
            }
        }

        private static void writeDictionaryValues(Dictionary<string, object> dictionary, XmlWriter writer)
        {
            writer.WriteStartElement("dict");
            foreach (string key in dictionary.Keys)
            {
                object value = dictionary[key];
                writer.WriteElementString("key", key);
                compose(value, writer);
            }
            writer.WriteEndElement();
        }

        private static int countObject(object value)
        {
            int count = 0;
            switch (value.GetType().ToString())
            {
                case "System.Collections.Generic.Dictionary`2[System.String,System.Object]":
                    Dictionary<string, object> dict = (Dictionary<string, object>)value;
                    foreach (string key in dict.Keys)
                    {
                        count += countObject(dict[key]);
                    }
                    count += dict.Keys.Count;
                    count++;
                    break;
                case "System.Collections.Generic.List`1[System.Object]":
                    List<object> list = (List<object>)value;
                    foreach (object obj in list)
                    {
                        count += countObject(obj);
                    }
                    count++;
                    break;
                default:
                    count++;
                    break;
            }
            return count;
        }

        private static byte[] writeBinaryDictionary(Dictionary<string, object> dictionary)
        {
            List<byte> buffer = new List<byte>();
            List<byte> header = new List<byte>();
            List<int> refs = new List<int>();
            for (int i = dictionary.Count - 1; i >= 0; i--)
            {
                var o = new object[dictionary.Count];
                dictionary.Values.CopyTo(o, 0);
                composeBinary(o[i]);
                writeOffsetTable.Add(writeObjectTable.Count);
                refs.Add(writeRefCount);
                writeRefCount--;
            }
            for (int i = dictionary.Count - 1; i >= 0; i--)
            {
                var o = new string[dictionary.Count];
                dictionary.Keys.CopyTo(o, 0);
                composeBinary(o[i]);//);
                writeOffsetTable.Add(writeObjectTable.Count);
                refs.Add(writeRefCount);
                writeRefCount--;
            }

            if (dictionary.Count < 15)
            {
                header.Add(Convert.ToByte(0xD0 | Convert.ToByte(dictionary.Count)));
            }
            else
            {
                header.Add(0xD0 | 0xf);
                header.AddRange(writeBinaryInteger(dictionary.Count, false));
            }


            foreach (int val in refs)
            {
                byte[] refBuffer = RegulateNullBytes(BitConverter.GetBytes(val), writeObjRefSize);
                Array.Reverse(refBuffer);
                buffer.InsertRange(0, refBuffer);
            }

            buffer.InsertRange(0, header);


            writeObjectTable.InsertRange(0, buffer);

            return buffer.ToArray();
        }

        private static byte[] composeBinaryArray(List<object> objects)
        {
            List<byte> buffer = new List<byte>();
            List<byte> header = new List<byte>();
            List<int> refs = new List<int>();

            for (int i = objects.Count - 1; i >= 0; i--)
            {
                composeBinary(objects[i]);
                writeOffsetTable.Add(writeObjectTable.Count);
                refs.Add(writeRefCount);
                writeRefCount--;
            }

            if (objects.Count < 15)
            {
                header.Add(Convert.ToByte(0xA0 | Convert.ToByte(objects.Count)));
            }
            else
            {
                header.Add(0xA0 | 0xf);
                header.AddRange(writeBinaryInteger(objects.Count, false));
            }

            foreach (int val in refs)
            {
                byte[] refBuffer = RegulateNullBytes(BitConverter.GetBytes(val), writeObjRefSize);
                Array.Reverse(refBuffer);
                buffer.InsertRange(0, refBuffer);
            }

            buffer.InsertRange(0, header);

            writeObjectTable.InsertRange(0, buffer);

            return buffer.ToArray();
        }

        private static byte[] composeBinary(object obj)
        {
            byte[] value;
            switch (obj.GetType().ToString())
            {
                case "System.Collections.Generic.Dictionary`2[System.String,System.Object]":
                    value = writeBinaryDictionary((Dictionary<string, object>)obj);
                    return value;

                case "System.Collections.Generic.List`1[System.Object]":
                    value = composeBinaryArray((List<object>)obj);
                    return value;

                case "System.Byte[]":
                    value = writeBinaryByteArray((byte[])obj);
                    return value;

                case "System.Double":
                    value = writeBinaryDouble((double)obj);
                    return value;

                case "System.Int32":
                    value = writeBinaryInteger((int)obj, true);
                    return value;

                case "System.String":
                    value = writeBinaryString((string)obj, true);
                    return value;

                case "System.DateTime":
                    value = writeBinaryDate((DateTime)obj);
                    return value;

                case "System.Boolean":
                    value = writeBinaryBool((bool)obj);
                    return value;

                default:
                    return new byte[0];
            }
        }

        public static byte[] writeBinaryDate(DateTime obj)
        {
            List<byte> buffer =new List<byte>(RegulateNullBytes(BitConverter.GetBytes(PlistDateConverter.ConvertToAppleTimeStamp(obj)), 8));
            buffer.Reverse();
            buffer.Insert(0, 0x33);
            writeObjectTable.InsertRange(0, buffer);
            return buffer.ToArray();
        }

        public static byte[] writeBinaryBool(bool obj)
        {
            List<byte> buffer = new List<byte>(new byte[1] { (bool)obj ? (byte)9 : (byte)8 });
            writeObjectTable.InsertRange(0, buffer);
            return buffer.ToArray();
        }

        private static byte[] writeBinaryInteger(int value, bool write)
        {
            List<byte> buffer = new List<byte>(BitConverter.GetBytes((long) value));
            buffer =new List<byte>(RegulateNullBytes(buffer.ToArray()));
            while (buffer.Count != Math.Pow(2, Math.Log(buffer.Count) / Math.Log(2)))
                buffer.Add(0);
            int header = 0x10 | (int)(Math.Log(buffer.Count) / Math.Log(2));

            buffer.Reverse();

            buffer.Insert(0, Convert.ToByte(header));

            if (write)
                writeObjectTable.InsertRange(0, buffer);

            return buffer.ToArray();
        }

        private static byte[] writeBinaryDouble(double value)
        {
            List<byte> buffer =new List<byte>(RegulateNullBytes(BitConverter.GetBytes(value), 4));
            while (buffer.Count != Math.Pow(2, Math.Log(buffer.Count) / Math.Log(2)))
                buffer.Add(0);
            int header = 0x20 | (int)(Math.Log(buffer.Count) / Math.Log(2));

            buffer.Reverse();

            buffer.Insert(0, Convert.ToByte(header));

            writeObjectTable.InsertRange(0, buffer);

            return buffer.ToArray();
        }

        private static byte[] writeBinaryByteArray(byte[] value)
        {
            List<byte> buffer = new List<byte>(value);
            List<byte> header = new List<byte>();
            if (value.Length < 15)
            {
                header.Add(Convert.ToByte(0x40 | Convert.ToByte(value.Length)));
            }
            else
            {
                header.Add(0x40 | 0xf);
                header.AddRange(writeBinaryInteger(buffer.Count, false));
            }

            buffer.InsertRange(0, header);

            writeObjectTable.InsertRange(0, buffer);

            return buffer.ToArray();
        }

        private static byte[] writeBinaryString(string value, bool head)
        {
            List<byte> buffer = new List<byte>();
            List<byte> header = new List<byte>();
            foreach (char chr in value.ToCharArray())
                buffer.Add(Convert.ToByte(chr));

            if (head)
            {
                if (value.Length < 15)
                {
                    header.Add(Convert.ToByte(0x50 | Convert.ToByte(value.Length)));
                }
                else
                {
                    header.Add(0x50 | 0xf);
                    header.AddRange(writeBinaryInteger(buffer.Count, false));
                }
            }

            buffer.InsertRange(0, header);

            writeObjectTable.InsertRange(0, buffer);

            return buffer.ToArray();
        }

        private static byte[] RegulateNullBytes(byte[] value)
        {
            return RegulateNullBytes(value, 1);
        }

        private static byte[] RegulateNullBytes(byte[] value, int minBytes)
        {
            Array.Reverse(value);
            List<byte> bytes = new List<byte>(value);
            for (int i = 0; i < bytes.Count; i++)
            {
                if (bytes[i] == 0 && bytes.Count > minBytes)
                {
                    bytes.Remove(bytes[i]);
                    i--;
                }
                else
                    break;
            }

            if (bytes.Count < minBytes)
            {
                int dist = minBytes - bytes.Count;
                for (int i = 0; i < dist; i++)
                    bytes.Insert(0, 0);
            }

            value = bytes.ToArray();
            Array.Reverse(value);
            return value;
        }

        /// <summary>
        /// Parses one binary plist. Every piece of mutable state the binary
        /// format needs lives in an instance of this class, so concurrent readers
        /// cannot corrupt each other's offset tables.
        /// </summary>
        private sealed class BinaryPlistReader
        {
            private readonly byte[] data;
            private readonly List<int> offsetTable = new List<int>();
            private List<byte> objectTable = new List<byte>();
            private int refCount;
            private int objRefSize;
            private int offsetByteSize;
            private long offsetTableOffset;

            internal BinaryPlistReader(byte[] data)
            {
                this.data = data;
            }

            internal object Parse()
            {
                List<byte> offsetTableBytes = new List<byte>();

                // "bplist00" header plus the 32-byte trailer
                if (data.Length < 40)
                    throw new PlistFormatException("binary plist is " + data.Length + " bytes, shorter than its header and trailer");

                List<byte> bList = new List<byte>(data);

                List<byte> trailer = bList.GetRange(bList.Count - 32, 32);

                parseTrailer(trailer);

                // every value just parsed came from the file; check it against the
                // bytes that actually exist before slicing anything out of them
                if (offsetTableOffset < 8 || offsetTableOffset > data.Length - 32)
                    throw new PlistFormatException("offset table offset " + offsetTableOffset + " is outside the " + data.Length + "-byte file");
                if (refCount < 1 || refCount > offsetTableOffset)
                    throw new PlistFormatException("object count " + refCount + " is outside the " + offsetTableOffset + "-byte object table");

                objectTable = bList.GetRange(0, (int)offsetTableOffset);

                offsetTableBytes = bList.GetRange((int)offsetTableOffset, bList.Count - (int)offsetTableOffset - 32);

                if (offsetTableBytes.Count % offsetByteSize != 0)
                    throw new PlistFormatException("offset table is " + offsetTableBytes.Count + " bytes, not a multiple of the " + offsetByteSize + "-byte offset size");

                parseOffsetTable(offsetTableBytes);

                if (offsetTable.Count < refCount)
                    throw new PlistFormatException("offset table holds " + offsetTable.Count + " entries for " + refCount + " declared objects");

                return parseBinary(0, 0);
            }

            private void parseTrailer(List<byte> trailer)
            {
                // the trailer is 32 bytes of file data: 6 unused, the offset size,
                // the object reference size, then three big-endian 64-bit values
                offsetByteSize = trailer.GetRange(6, 1)[0];
                objRefSize = trailer.GetRange(7, 1)[0];
                if (offsetByteSize < 1 || offsetByteSize > 8)
                    throw new PlistFormatException("offset size " + offsetByteSize + " is not between 1 and 8");
                if (objRefSize < 1 || objRefSize > 8)
                    throw new PlistFormatException("object reference size " + objRefSize + " is not between 1 and 8");

                byte[] refCountBytes = trailer.GetRange(8, 8).ToArray();
                Array.Reverse(refCountBytes);
                long declaredObjects = BitConverter.ToInt64(refCountBytes, 0);
                if (declaredObjects < 1 || declaredObjects > int.MaxValue)
                    throw new PlistFormatException("object count " + declaredObjects + " is not a valid count");
                refCount = (int)declaredObjects;

                byte[] offsetTableOffsetBytes = trailer.GetRange(24, 8).ToArray();
                Array.Reverse(offsetTableOffsetBytes);
                offsetTableOffset = BitConverter.ToInt64(offsetTableOffsetBytes, 0);
            }

            private void parseOffsetTable(List<byte> offsetTableBytes)
            {
                for (int i = 0; i < offsetTableBytes.Count; i += offsetByteSize)
                {
                    byte[] buffer = offsetTableBytes.GetRange(i, offsetByteSize).ToArray();
                    Array.Reverse(buffer);
                    offsetTable.Add(BitConverter.ToInt32(RegulateNullBytes(buffer, 4), 0));
                }
            }

            /// <summary>
            /// Resolves an object reference to the position of its header byte,
            /// rejecting references and offsets the file cannot back with bytes.
            /// </summary>
            private int getObjectPosition(int objRef)
            {
                if (objRef < 0 || objRef >= offsetTable.Count)
                    throw new PlistFormatException("object reference " + objRef + " is outside the " + offsetTable.Count + "-entry offset table");
                int position = offsetTable[objRef];
                if (position < 0 || position >= objectTable.Count)
                    throw new PlistFormatException("object offset " + position + " is outside the " + objectTable.Count + "-byte object table");
                return position;
            }

            private void requireObjectBytes(int start, long count, string what)
            {
                if (start < 0 || count < 0 || count > (long)objectTable.Count - start)
                    throw new PlistFormatException(what + " (" + count + " bytes at " + start + ") is outside the " + objectTable.Count + "-byte object table");
            }

            private object parseBinaryDictionary(int objRef, int depth)
            {
                Dictionary<string, object> buffer = new Dictionary<string, object>();
                List<int> refs = new List<int>();
                int refCount = 0;

                int refStartPosition;
                refCount = getCount(getObjectPosition(objRef), out refStartPosition);

                if (refCount < 15)
                    refStartPosition = getObjectPosition(objRef) + 1;
                else
                    refStartPosition = getObjectPosition(objRef) + 2 + RegulateNullBytes(BitConverter.GetBytes(refCount), 1).Length;

                requireObjectBytes(refStartPosition, (long)refCount * 2 * objRefSize, "dictionary reference list");

                for (int i = refStartPosition; i < refStartPosition + refCount * 2 * objRefSize; i += objRefSize)
                {
                    byte[] refBuffer = objectTable.GetRange(i, objRefSize).ToArray();
                    Array.Reverse(refBuffer);
                    refs.Add(BitConverter.ToInt32(RegulateNullBytes(refBuffer, 4), 0));
                }

                for (int i = 0; i < refCount; i++)
                {
                    string key = parseBinary(refs[i], depth) as string;
                    if (key == null)
                        throw new PlistFormatException("dictionary key at object " + refs[i] + " is not a string");
                    buffer.Add(key, parseBinary(refs[i + refCount], depth));
                }

                return buffer;
            }

            private object parseBinaryArray(int objRef, int depth)
            {
                List<object> buffer = new List<object>();
                List<int> refs = new List<int>();
                int refCount = 0;

                int refStartPosition;
                refCount = getCount(getObjectPosition(objRef), out refStartPosition);


                if (refCount < 15)
                    refStartPosition = getObjectPosition(objRef) + 1;
                else
                    //The following integer has a header aswell so we increase the refStartPosition by two to account for that.
                    refStartPosition = getObjectPosition(objRef) + 2 + RegulateNullBytes(BitConverter.GetBytes(refCount), 1).Length;

                requireObjectBytes(refStartPosition, (long)refCount * objRefSize, "array reference list");

                for (int i = refStartPosition; i < refStartPosition + refCount * objRefSize; i += objRefSize)
                {
                    byte[] refBuffer = objectTable.GetRange(i, objRefSize).ToArray();
                    Array.Reverse(refBuffer);
                    refs.Add(BitConverter.ToInt32(RegulateNullBytes(refBuffer, 4), 0));
                }

                for (int i = 0; i < refCount; i++)
                {
                    buffer.Add(parseBinary(refs[i], depth));
                }

                return buffer;
            }

            private int getCount(int bytePosition, out int newBytePosition)
            {
                if (bytePosition < 0 || bytePosition >= objectTable.Count)
                    throw new PlistFormatException("object header position " + bytePosition + " is outside the " + objectTable.Count + "-byte object table");
                byte headerByte = objectTable[bytePosition];
                byte headerByteTrail = Convert.ToByte(headerByte & 0xf);
                int count;
                if (headerByteTrail < 15)
                {
                    count = headerByteTrail;
                    newBytePosition = bytePosition + 1;
                }
                else
                    count = (int)parseBinaryInt(bytePosition + 1, out newBytePosition);
                return count;
            }

            private object parseBinary(int objRef, int depth)
            {
                // containers recurse through parseBinary; stop before file data can
                // overflow the stack (depth 32 is far above any real plist)
                if (depth > MaxBinaryPlistDepth)
                    throw new PlistFormatException("binary plist nesting exceeds " + MaxBinaryPlistDepth + " levels");

                int objPosition = getObjectPosition(objRef);
                byte header = objectTable[objPosition];
                switch (header & 0xF0)
                {
                    case 0:
                        {
                            //If the byte is
                            //0 return null
                            //9 return true
                            //8 return false
                            return (objectTable[objPosition] == 0) ? (object)null : ((objectTable[objPosition] == 9) ? true : false);
                        }
                    case 0x10:
                        {
                            return parseBinaryInt(objPosition);
                        }
                    case 0x20:
                        {
                            return parseBinaryReal(objPosition);
                        }
                    case 0x30:
                        {
                            return parseBinaryDate(objPosition);
                        }
                    case 0x40:
                        {
                            return parseBinaryByteArray(objPosition);
                        }
                    case 0x50://String ASCII
                        {
                            return parseBinaryAsciiString(objPosition);
                        }
                    case 0x60://String Unicode
                        {
                            return parseBinaryUnicodeString(objPosition);
                        }
                    case 0xD0:
                        {
                            return parseBinaryDictionary(objRef, depth + 1);
                        }
                    case 0xA0:
                        {
                            return parseBinaryArray(objRef, depth + 1);
                        }
                }
                throw new Exception("This type is not supported");
            }

            private object parseBinaryDate(int headerPosition)
            {
                requireObjectBytes(headerPosition + 1, 8, "date payload");
                byte[] buffer = objectTable.GetRange(headerPosition + 1, 8).ToArray();
                Array.Reverse(buffer);
                double appleTime = BitConverter.ToDouble(buffer, 0);
                DateTime result = PlistDateConverter.ConvertFromAppleTimeStamp(appleTime);
                return result;
            }
        
            private object parseBinaryInt(int headerPosition)
            {
                int output;
                return parseBinaryInt(headerPosition, out output);
            }

            private object parseBinaryInt(int headerPosition, out int newHeaderPosition)
            {
                if (headerPosition < 0 || headerPosition >= objectTable.Count)
                    throw new PlistFormatException("integer header position " + headerPosition + " is outside the " + objectTable.Count + "-byte object table");
                byte header = objectTable[headerPosition];
                int byteCount = 1 << (header & 0xf);
                requireObjectBytes(headerPosition + 1, byteCount, "integer payload");
                byte[] buffer = objectTable.GetRange(headerPosition + 1, byteCount).ToArray();
                Array.Reverse(buffer);
                //Add one to account for the header byte
                newHeaderPosition = headerPosition + byteCount + 1;
                return BitConverter.ToInt32(RegulateNullBytes(buffer, 4), 0);
            }

            private object parseBinaryReal(int headerPosition)
            {
                if (headerPosition < 0 || headerPosition >= objectTable.Count)
                    throw new PlistFormatException("real header position " + headerPosition + " is outside the " + objectTable.Count + "-byte object table");
                byte header = objectTable[headerPosition];
                int byteCount = 1 << (header & 0xf);
                requireObjectBytes(headerPosition + 1, byteCount, "real payload");
                byte[] buffer = objectTable.GetRange(headerPosition + 1, byteCount).ToArray();
                Array.Reverse(buffer);

                return BitConverter.ToDouble(RegulateNullBytes(buffer, 8), 0);
            }

            private object parseBinaryAsciiString(int headerPosition)
            {
                int charStartPosition;
                int charCount = getCount(headerPosition, out charStartPosition);

                requireObjectBytes(charStartPosition, charCount, "ASCII string payload");
                var buffer = objectTable.GetRange(charStartPosition, charCount);
                return buffer.Count > 0 ? Encoding.ASCII.GetString(buffer.ToArray()) : string.Empty;
            }

            private object parseBinaryUnicodeString(int headerPosition)
            {
                int charStartPosition;
                int charCount = getCount(headerPosition, out charStartPosition);
                requireObjectBytes(charStartPosition, (long)charCount * 2, "unicode string payload");
                charCount = charCount * 2;

                byte[] buffer = new byte[charCount];
                byte one, two;

                for (int i = 0; i < charCount; i+=2)
                {
                    one = objectTable.GetRange(charStartPosition+i,1)[0];
                    two = objectTable.GetRange(charStartPosition + i+1, 1)[0];

                    if (BitConverter.IsLittleEndian)
                    {
                        buffer[i] = two;
                        buffer[i + 1] = one;
                    }
                    else
                    {
                        buffer[i] = one;
                        buffer[i + 1] = two;
                    }
                }

                return Encoding.Unicode.GetString(buffer);
            }

            private object parseBinaryByteArray(int headerPosition)
            {
                int byteStartPosition;
                int byteCount = getCount(headerPosition, out byteStartPosition);
                requireObjectBytes(byteStartPosition, byteCount, "byte array payload");
                return objectTable.GetRange(byteStartPosition, byteCount).ToArray();
            }
        }


        #endregion
    }
    
    public enum plistType
    {
        Auto, Binary, Xml
    }

    public static class PlistDateConverter
    {
        public static long timeDifference = 978307200;

        public static long GetAppleTime(long unixTime)
        {
            return unixTime - timeDifference;
        }

        public static long GetUnixTime(long appleTime)
        {
            return appleTime + timeDifference;
        }

        public static DateTime ConvertFromAppleTimeStamp(double timestamp)
        {
            DateTime origin = new DateTime(2001, 1, 1, 0, 0, 0, 0);
            return origin.AddSeconds(timestamp);
        }

        public static double ConvertToAppleTimeStamp(DateTime date)
        {
            DateTime begin = new DateTime(2001, 1, 1, 0, 0, 0, 0);
            TimeSpan diff = date - begin;
            return Math.Floor(diff.TotalSeconds);
        }
    }
}
