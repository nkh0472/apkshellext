using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ApkShellext2;

namespace ApkQuickReader
{
    /// <summary>
    /// Shared parser for the binary resource table (resources.arsc).
    ///
    /// Every apk-side reader goes through this one walk; aab/appx/ipa containers carry no
    /// resources table, so they never reach it. One implementation means the hard gates
    /// below exist in a single place and every reader inherits them.
    ///
    /// Every length, offset and count read here comes from the file, so each one is
    /// bounded before it is used; malformed input yields the values collected so far
    /// instead of an exception or an allocation the file asked for.
    /// </summary>
    internal sealed class ArscParser
    {
        // ---- hard gates on every table read (the only copy) ------------------------
        internal const int MaxResourceConfigSize = 1024;    // ResTable_config is 64 bytes today
        internal const int MaxResourcePackages = 256;       // a real table holds one package
        internal const int MaxResourceEntries = 0x10000;    // entry offset index is 16 bits
        internal const int MaxResourceRefDepth = 32;        // resource reference chains are shallow

        private readonly byte[] table;
        private readonly Func<byte[], uint, string> readStringPool;

        // for handling loop reference
        private readonly Stack<UInt32> searchstack = new Stack<UInt32>();

        /// <param name="table">resources.arsc bytes, or null when the container has none</param>
        /// <param name="readStringPool">string pool reader (owned by AxmlParser): (bytes, id) -&gt; text</param>
        public ArscParser(byte[] table, Func<byte[], uint, string> readStringPool)
        {
            this.table = table;
            this.readStringPool = readStringPool;
        }

        // Skip one chunk that starts at chunkPos and must end at or before limit.
        // The chunk size is file-declared, so it is only trusted after it has been
        // checked to advance and stay inside the parent. Returns the position of
        // the next chunk, or -1 when the header cannot be trusted.
        private static long SkipChunk(MemoryStream ms, BinaryReader br, long chunkPos, long limit)
        {
            if (chunkPos < 0 || chunkPos + 8 > limit) return -1;
            ms.Seek(chunkPos + 4, SeekOrigin.Begin);
            int chunkSize = br.ReadInt32();
            if (chunkSize < 8 || chunkPos + chunkSize > limit) return -1;
            long next = chunkPos + chunkSize;
            ms.Seek(next, SeekOrigin.Begin);
            return next;
        }

        /// <summary>
        /// Find the requested resource, according to config setting, if the config was set.
        /// </summary>
        /// <param name="id">resourceID</param>
        /// <returns>the resource, in string format, if resource id not found, return null value</returns>
        public ApkResource QuickSearchResource(UInt32 id)
        {
            ApkResource res = new ApkResource(id);
            // a TYPE_REFERENCE entry may point at another reference, and the chain
            // length is chosen by the file: stop before it can exhaust the stack
            if (searchstack.Count >= MaxResourceRefDepth)
                return res;
            searchstack.Push(id);
            try {
                if (table == null || table.Length < 12)
                    return res; // no table to read: behave like "resource not found"

                using (MemoryStream ms = new MemoryStream(table))
                using (BinaryReader br = new BinaryReader(ms)) {
                    ms.Seek(8, SeekOrigin.Begin); // jump type/headersize/chunksize
                    int packageCount = br.ReadInt32();
                    if (packageCount < 0 || packageCount > MaxResourcePackages)
                        return res; // a real table holds one package

                    // comes to stringpool chunk, skipit
                    long stringPoolPos = ms.Position;
                    ms.Seek(4, SeekOrigin.Current);
                    int stringPoolSize = br.ReadInt32();
                    // a forged pool size must not seek outside the table
                    if (stringPoolSize < 8 || stringPoolPos + stringPoolSize > ms.Length)
                        return res;
                    ms.Seek(stringPoolPos + stringPoolSize, SeekOrigin.Begin); // jump to the end

                    //Package chunk now
                    for (int pack = 0; pack < packageCount; pack++) {
                        long PackChunkPos = ms.Position;
                        // package header: type, headerSize, chunkSize, package id
                        if (PackChunkPos + 12 > ms.Length) break;
                        ms.Seek(2, SeekOrigin.Current); // jump type/headersize
                        int headerSize = br.ReadInt16();
                        int PackChunkSize = br.ReadInt32();
                        int packID = br.ReadInt32();
                        if (headerSize < 12 || PackChunkSize < headerSize ||
                            PackChunkPos + PackChunkSize > ms.Length)
                            break; // lying package header: stop with what we have
                        long packEnd = PackChunkPos + PackChunkSize;

                        if (packID != res.PackageID) { // check if the resource is in this pack
                            // goto next chunk
                            ms.Seek(packEnd, SeekOrigin.Begin);
                            continue;
                        }

                        // skip typestring chunk and keystring chunk
                        long typeStringsPos = SkipChunk(ms, br, PackChunkPos + headerSize, packEnd);
                        if (typeStringsPos < 0) return res;
                        long keyStringsPos = SkipChunk(ms, br, typeStringsPos, packEnd);
                        if (keyStringsPos < 0) return res;

                        // come to typespec chunks and type chunks
                        // typespec and type chunks may happen in a row.
                        long chunkPos = keyStringsPos;
                        while (chunkPos + 8 <= packEnd) {
                            ms.Seek(chunkPos, SeekOrigin.Begin);
                            short chunkType = br.ReadInt16();
                            short chunkHeaderSize = br.ReadInt16();
                            int chunkSize = br.ReadInt32();
                            // chunkSize < 8 would make the walk stand still, and a
                            // chunk crossing the package end would read foreign bytes
                            if (chunkHeaderSize < 8 || chunkSize < 8 ||
                                chunkPos + chunkSize > packEnd)
                                break;
                            long chunkEnd = chunkPos + chunkSize;

                            if (chunkType == (short)RES_TYPE.RES_TABLE_TYPE_TYPE &&
                                chunkHeaderSize >= 24) { // 20 fixed bytes + config size
                                byte typeid = br.ReadByte();
                                if (typeid == res.TypeID) {
                                    ms.Seek(3, SeekOrigin.Current); // skip 0
                                    int entryCount = br.ReadInt32();
                                    int entryStart = br.ReadInt32();

                                    // read the config section: 4-byte size prefix + payload
                                    int config_size = br.ReadInt32();
                                    int configBytes = config_size - 4;
                                    // entry counts and the config block drive a seek and
                                    // an allocation, so both must fit this chunk and the caps
                                    if (entryCount < 0 || entryCount > MaxResourceEntries ||
                                        entryStart < 0 || entryStart > chunkSize ||
                                        configBytes < 0 || config_size > MaxResourceConfigSize ||
                                        configBytes > chunkEnd - ms.Position ||
                                        res.EntryID >= (uint)entryCount)
                                        break;
                                    byte[] conf = br.ReadBytes(configBytes);

                                    long indexPos = chunkPos + chunkHeaderSize + (long)res.EntryID * 4;
                                    if (indexPos + 4 > chunkEnd) break;
                                    ms.Seek(indexPos, SeekOrigin.Begin); // goto index
                                    uint entryIndic = br.ReadUInt32();
                                    if (entryIndic != 0xffffffff) { // else no entry here
                                        long entryPos = chunkPos + entryStart + (long)entryIndic;
                                        if (entryPos + 16 > chunkEnd) break;
                                        ms.Seek(entryPos, SeekOrigin.Begin);

                                        // get to the entry
                                        ms.Seek(11, SeekOrigin.Current); // skip entry size, flags, key, size, 0
                                        byte dataType = br.ReadByte();
                                        uint data = br.ReadUInt32();
                                        if (dataType == (byte)DATA_TYPE.TYPE_STRING) {
                                            res.Add(conf, readStringPool(table, data));
                                        } else if (dataType == (byte)DATA_TYPE.TYPE_REFERENCE) {
                                            // self reference: the entry points back at itself and the chain
                                            // would circle here. searchstack already stops the loop; the log
                                            // line is what keeps a missing icon diagnosable.
                                            if (data == id) {
                                                ApkShellext2.DbgLog.W("ARSC self reference 0x" + id.ToString("X8") + " ignored");
                                            } else if (data != 0x00000000 && !searchstack.Contains(data)) {
                                                // the entry is null, or it's referencing in loop, skip it
                                                res.Add(QuickSearchResource((UInt32)data));
                                            }
                                        } else { // I would like to expect we only will receive TYPE_STRING/TYPE_REFERENCE/any integer type, complex is not considering here,yet
                                            res.Add(conf, data.ToString());
                                        }
                                    }
                                }
                            }
                            chunkPos = chunkEnd; // chunkSize >= 8 keeps this strictly forward
                        }
                    }
                }
                return res;
            } catch (Exception ex) when (!(ex is OutOfMemoryException)) {
                // malformed table: return what was collected (- neither a truncated
                // value nor a partially parsed key may escape to the shell UI thread)
                return res;
            } finally {
                searchstack.Pop();
            }
        }

        /// <summary>
        /// Look a resource up by name.
        ///
        /// <paramref name="entryName"/> is the key inside the type (for example
        /// "ic_launcher"); <paramref name="typeName"/> is the parent type ("mipmap").
        /// The same name can appear in several packages, and in several config chunks of
        /// one type: the smallest id of all candidates wins, which is what makes the
        /// choice stable when a table ships duplicated names. Returns 0 when the name is
        /// not in this table.
        /// </summary>
        public uint FindResourceId(string typeName, string entryName)
        {
            if (table == null || table.Length < 12 ||
                string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(entryName))
                return 0;
            try {
                using (MemoryStream ms = new MemoryStream(table))
                using (BinaryReader br = new BinaryReader(ms)) {
                    ms.Seek(8, SeekOrigin.Begin);
                    int packageCount = br.ReadInt32();
                    if (packageCount < 0 || packageCount > MaxResourcePackages)
                        return 0;

                    long stringPoolPos = ms.Position;
                    ms.Seek(4, SeekOrigin.Current);
                    int stringPoolSize = br.ReadInt32();
                    if (stringPoolSize < 8 || stringPoolPos + stringPoolSize > ms.Length)
                        return 0;
                    ms.Seek(stringPoolPos + stringPoolSize, SeekOrigin.Begin);

                    uint best = 0;
                    for (int pack = 0; pack < packageCount; pack++) {
                        long packPos = ms.Position;
                        if (packPos + 12 > ms.Length) break;
                        ms.Seek(2, SeekOrigin.Current);
                        int headerSize = br.ReadInt16();
                        int packChunkSize = br.ReadInt32();
                        uint packID = br.ReadUInt32();
                        if (headerSize < 12 || packChunkSize < headerSize ||
                            packPos + packChunkSize > ms.Length)
                            break;
                        long packEnd = packPos + packChunkSize;

                        // ResTable_package hides the two pool offsets behind the 256-byte
                        // name field: typeStrings at +268 and keyStrings at +276, both
                        // relative to the package start and only present in a full header
                        if (headerSize < 284 || packEnd - packPos < 284) {
                            ms.Seek(packEnd, SeekOrigin.Begin);
                            continue;
                        }
                        ms.Seek(packPos + 268, SeekOrigin.Begin);
                        long typePoolPos = packPos + br.ReadUInt32();
                        ms.Seek(packPos + 276, SeekOrigin.Begin);
                        long keyPoolPos = packPos + br.ReadUInt32();
                        if (typePoolPos <= packPos || keyPoolPos <= packPos ||
                            typePoolPos + 8 > packEnd || keyPoolPos + 8 > packEnd) {
                            ms.Seek(packEnd, SeekOrigin.Begin);
                            continue;
                        }

                        long chunkPos = packPos + headerSize;
                        while (chunkPos + 8 <= packEnd) {
                            ms.Seek(chunkPos, SeekOrigin.Begin);
                            short chunkType = br.ReadInt16();
                            short chunkHeaderSize = br.ReadInt16();
                            int chunkSize = br.ReadInt32();
                            if (chunkHeaderSize < 8 || chunkSize < 8 ||
                                chunkPos + chunkSize > packEnd)
                                break;
                            long chunkEnd = chunkPos + chunkSize;

                            if (chunkType == (short)RES_TYPE.RES_TABLE_TYPE_TYPE &&
                                chunkHeaderSize >= 24) {
                                FindEntryIdInTypeChunk(ms, br, packID, typePoolPos, keyPoolPos,
                                    chunkPos, chunkEnd, chunkHeaderSize, typeName, entryName, ref best);
                            }
                            chunkPos = chunkEnd;
                        }
                        ms.Seek(packEnd, SeekOrigin.Begin);
                    }
                    return best;
                }
            } catch (Exception ex) when (!(ex is OutOfMemoryException)) {
                // a malformed table must not surface as an exception from an icon lookup
                ApkShellext2.DbgLog.W("ARSC name lookup failed: " + ex.Message.Split('\n')[0]);
                return 0;
            }
        }

        /// <summary>
        /// Resolves a manifest icon attribute to an archive path. <paramref name="value"/>
        /// is what the attribute already rendered to, <paramref name="referenceId"/> is its
        /// original TYPE_REFERENCE payload (0 when the attribute is not a reference).
        ///
        /// A value that starts with "res/" is a file path inside the apk: take the file and
        /// never consult the table, because a table lookup could hand back a different,
        /// "nicer" name and change the icon of apps whose resources are renamed.
        /// An exact reference id is tried before the name.
        /// "android:" names the framework package, not an app resource, so it is refused.
        /// Self references are stopped inside QuickSearchResource().
        ///
        /// Returns <paramref name="value"/> unchanged when nothing resolves, so the caller
        /// keeps its original failure path.
        /// </summary>
        public string ResolveIconValue(string value, UInt32 referenceId,
            bool densityRequested, bool ignoreXmlDensity)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // a res/ value is a zip entry path, not a resource name
            if (value.StartsWith("res/", StringComparison.OrdinalIgnoreCase)) return value;
            // packers rename the res/ prefix away (r/w/x6.png): anything that looks like a
            // file is a path as well, and looking it up by name would only break it
            if (LooksLikeFilePath(value)) return value;

            // the exact reference id wins over any name
            if (referenceId != 0) {
                string byId = ResolveIdToValue(referenceId, densityRequested, ignoreXmlDensity);
                if (!string.IsNullOrEmpty(byId)) return byId;
            }

            // name fallback; NormalizeResourceName refuses foreign packages
            string name = NormalizeResourceName(value);
            if (name == null) return value; // not a "type/name" value: return it verbatim

            int slash = name.IndexOf('/');
            uint id = FindResourceId(name.Substring(0, slash), name.Substring(slash + 1));
            if (id == 0) {
                ApkShellext2.DbgLog.W("ICON name lookup missed: " + value);
                return value;
            }
            string resolved = ResolveIdToValue(id, densityRequested, ignoreXmlDensity);
            if (string.IsNullOrEmpty(resolved)) return value;
            ApkShellext2.DbgLog.W("ICON name lookup " + value + " -> 0x" + id.ToString("X8") + " -> " + resolved);
            return resolved;
        }

        /// <summary>
        /// Picks the config entry to use for the icon/app-name. <paramref name="densityRequested"/>
        /// mirrors the reader's "Density" flag; <paramref name="ignoreXmlDensity"/> mirrors the
        /// reader's legacy-drawable retry.
        /// </summary>
        public int ApplyFilter(ApkResource r, bool densityRequested, bool ignoreXmlDensity)
        {
            int best = 0;
            if (densityRequested) {
                int bestDensity = 0;
                bool supAI = Utility.GetSetting("SupportAdaptiveIcon", "False") == "True" && !ignoreXmlDensity;
                System.Text.StringBuilder dbg = ApkShellext2.DbgLog.Enabled ? new System.Text.StringBuilder() : null;
                for (int i = 0; i < r.configs.Count; i++) {
                    if (!supAI) {
                        if (r.values[i].ToString().EndsWith(".xml")) continue;
                    }
                    byte[] bytes = r.configs[i];
                    int density = bytes[(int)ResourceConfig.Density + 1] * 256 + bytes[(int)ResourceConfig.Density];
                    if (density > bestDensity) {
                        bestDensity = density;
                        best = i;
                    }
                    if (dbg != null) dbg.Append("[" + i + " " + r.values[i] + " d=" + density + "]");
                }
                if (dbg != null) ApkShellext2.DbgLog.W("APPLYFILTER supAI=" + supAI + " ignXml=" + ignoreXmlDensity +
                    " raw=" + Utility.GetSetting("SupportAdaptiveIcon", "False") + " n=" + r.configs.Count + dbg + " -> " + r.values[best]);
            }
            return best;
        }

        /// <summary>
        /// Picks the entry whose locale best matches the requested app-name language (localized names
        /// feature, kkguo/apkshellext). Ranking: exact language+region > region-neutral entry of the
        /// same language > same language with a different region > locale-less default config > 0.
        /// Returns ApplyFilter() when the feature is off, so the density-only choice stands.
        /// </summary>
        public int ApplyLocaleFilter(ApkResource r, bool densityRequested, bool ignoreXmlDensity)
        {
            CultureInfo want = Utility.requestedNameCulture();
            if (want == null) return ApplyFilter(r, densityRequested, ignoreXmlDensity);

            string wantLang = want.TwoLetterISOLanguageName;
            string wantRegion = "";
            int dash = want.Name.IndexOf('-');
            if (dash > 0) wantRegion = want.Name.Substring(dash + 1);

            int neutral = -1, sameLang = -1, defaultCfg = -1;
            for (int i = 0; i < r.configs.Count; i++) {
                byte[] conf = r.configs[i];
                if (conf == null || conf.Length < 8) continue; // ResTable_config minus size prefix: lang@4 country@6
                byte[] code = new byte[4];
                Array.Copy(conf, 4, code, 0, 4);
                CultureInfo cul;
                try { cul = GetCulture(code); } catch { continue; }
                if (cul == null) {
                    if (defaultCfg < 0) defaultCfg = i; // locale-less = default configuration
                    continue;
                }
                if (!string.Equals(cul.TwoLetterISOLanguageName, wantLang, StringComparison.OrdinalIgnoreCase))
                    continue;
                int culDash = cul.Name.IndexOf('-');
                string culRegion = culDash > 0 ? cul.Name.Substring(culDash + 1) : "";
                if (culRegion.Length == 0) {
                    if (neutral < 0) neutral = i;
                } else if (string.Equals(culRegion, wantRegion, StringComparison.OrdinalIgnoreCase)) {
                    return i; // exact language + region
                } else if (sameLang < 0) {
                    sameLang = i;
                }
            }
            if (neutral >= 0) return neutral;
            if (sameLang >= 0) return sameLang;
            if (defaultCfg >= 0) return defaultCfg;
            return 0;
        }

        // Walks one ResTable_type chunk and records the smallest resource id whose key
        // equals entryName. Every count and offset below comes from the file, so each is
        // checked against the chunk before it is used to seek.
        private void FindEntryIdInTypeChunk(MemoryStream ms, BinaryReader br, uint packID,
            long typePoolPos, long keyPoolPos, long chunkPos, long chunkEnd,
            short chunkHeaderSize, string typeName, string entryName, ref uint best)
        {
            ms.Seek(chunkPos + 8, SeekOrigin.Begin);
            uint typeId = br.ReadByte(); // type ids are 1-based
            int flags = br.ReadByte();
            if (typeId == 0) return;
            // the type name lives in the package type pool at index typeId - 1
            if (ReadPoolString(typePoolPos, typeId - 1) != typeName) return;

            ms.Seek(chunkPos + 12, SeekOrigin.Begin);
            int entryCount = br.ReadInt32();
            int entryStart = br.ReadInt32();
            int configSize = br.ReadInt32();
            if (entryCount < 0 || entryCount > MaxResourceEntries ||
                entryStart < 0 || entryStart > chunkEnd - chunkPos ||
                configSize < 4 || configSize > MaxResourceConfigSize ||
                configSize > chunkEnd - (chunkPos + 20))
                return;

            bool sparse = (flags & 0x01) != 0;          // ResTable_type FLAG_SPARSE
            bool offset16 = !sparse && (flags & 0x02) != 0; // FLAG_OFFSET16
            int indexUnit = offset16 ? 2 : 4;
            long indexPos = chunkPos + chunkHeaderSize;
            if (indexPos + (long)entryCount * indexUnit > chunkEnd) return;

            for (int i = 0; i < entryCount; i++) {
                uint entryId;
                long offset;
                if (sparse) {
                    // a sparse chunk stores the real entry id next to the offset: the
                    // position in the vector is not the entry id
                    ms.Seek(indexPos + (long)i * 4, SeekOrigin.Begin);
                    entryId = br.ReadUInt16();
                    offset = (long)br.ReadUInt16() * 4; // offsets are in 4-byte units
                } else if (offset16) {
                    ms.Seek(indexPos + (long)i * 2, SeekOrigin.Begin);
                    ushort raw = br.ReadUInt16();
                    if (raw == 0xffff) continue;
                    entryId = (uint)i;
                    offset = (long)raw * 4;
                } else {
                    ms.Seek(indexPos + (long)i * 4, SeekOrigin.Begin);
                    uint raw = br.ReadUInt32();
                    if (raw == 0xffffffff) continue;
                    entryId = (uint)i;
                    offset = raw;
                }

                long entryPos = chunkPos + entryStart + offset;
                if (entryPos + 16 > chunkEnd) continue; // entry header + Res_value
                ms.Seek(entryPos + 4, SeekOrigin.Begin); // ResTable_entry.key
                uint key = br.ReadUInt32();
                if (ReadPoolString(keyPoolPos, key) != entryName) continue;

                uint id = (packID << 24) | (typeId << 16) | entryId;
                // the same id can appear in several config chunks, and a name can exist
                // in several packages: the smallest id is the stable answer
                if (best == 0 || id < best) best = id;
            }
        }

        // Reads one string out of a string pool that lives *inside* a package chunk.
        // AxmlParser.QuickSearchStringPool can only start at a file/pool boundary, and
        // these pools sit at an arbitrary offset, so the same checks are repeated here.
        private string ReadPoolString(long poolPos, uint index)
        {
            if (table == null || poolPos < 0 || poolPos + 28 > table.Length) return "";
            using (MemoryStream ms = new MemoryStream(table, 0, table.Length, false))
            using (BinaryReader br = new BinaryReader(ms)) {
                ms.Seek(poolPos, SeekOrigin.Begin);
                short chunkType = br.ReadInt16();
                short headerSize = br.ReadInt16();
                int chunkSize = br.ReadInt32();
                if (chunkType != (short)RES_TYPE.RES_STRING_POOL_TYPE ||
                    headerSize < 28 || chunkSize < headerSize ||
                    poolPos + chunkSize > table.Length)
                    return "";
                long chunkEnd = poolPos + chunkSize;

                int stringCount = br.ReadInt32();
                br.ReadInt32(); // styleCount
                int flags = br.ReadInt32();
                bool isUTF8 = (flags & (1 << 8)) != 0;
                int stringStart = br.ReadInt32();
                if (stringCount < 0 || index >= (uint)stringCount ||
                    stringStart < 0 || stringStart > chunkSize)
                    return "";

                long offsetPos = poolPos + headerSize + (long)index * 4;
                if (offsetPos + 4 > chunkEnd) return "";
                ms.Seek(offsetPos, SeekOrigin.Begin);
                int stringPos = br.ReadInt32();
                if (stringPos < 0 || poolPos + stringStart + stringPos > chunkEnd) return "";
                ms.Seek(poolPos + stringStart + stringPos, SeekOrigin.Begin);

                if (isUTF8) {
                    int u16len = br.ReadByte();
                    if ((u16len & 0x80) != 0) u16len = ((u16len & 0x7F) << 8) + br.ReadByte();
                    int u8len = br.ReadByte();
                    if ((u8len & 0x80) != 0) u8len = ((u8len & 0x7F) << 8) + br.ReadByte();
                    if (u8len > chunkEnd - ms.Position) return "";
                    return Encoding.UTF8.GetString(br.ReadBytes(u8len));
                } else {
                    int u16len = br.ReadUInt16();
                    if ((u16len & 0x8000) != 0) u16len = ((u16len & 0x7FFF) << 16) + br.ReadUInt16();
                    if (u16len < 0 || u16len > (chunkEnd - ms.Position) / 2) return "";
                    return Encoding.Unicode.GetString(br.ReadBytes(u16len * 2));
                }
            }
        }

        // Resolves an id and applies the density/locale preference the same way
        // convertData() does for a TYPE_REFERENCE attribute.
        private string ResolveIdToValue(uint id, bool densityRequested, bool ignoreXmlDensity)
        {
            ApkResource r = QuickSearchResource(id);
            if (r == null || r.Count == 0) return null;
            int ind = 0;
            if (r.Count > 1) ind = ApplyFilter(r, densityRequested, ignoreXmlDensity);
            if (ind < 0 || ind >= r.values.Count) return null;
            object v = r.values[ind];
            return v == null ? null : v.ToString();
        }

        // Accepts "@type/name", "?type/name", "type/name" and "pkg:type/name".
        // Returns null when the value is not a resource name this table may answer for.
        private string NormalizeResourceName(string value)
        {
            string v = value.Trim();
            if (v.Length == 0) return null;
            if (v[0] == '@' || v[0] == '?') v = v.Substring(1);
            int colon = v.IndexOf(':');
            if (colon >= 0) {
                string prefix = v.Substring(0, colon);
                v = v.Substring(colon + 1);
                // "android:" names the framework package, never an app resource
                if (string.Equals(prefix, "android", StringComparison.OrdinalIgnoreCase)) {
                    ApkShellext2.DbgLog.W("ICON android: reference refused: " + value);
                    return null;
                }
                string ownPackage = PackageName();
                if (ownPackage != null &&
                    !string.Equals(prefix, ownPackage, StringComparison.OrdinalIgnoreCase)) {
                    ApkShellext2.DbgLog.W("ICON foreign package reference refused: " + value);
                    return null;
                }
            }
            int slash = v.IndexOf('/');
            if (slash <= 0 || slash == v.Length - 1) return null; // not "type/name"
            return v;
        }

        private static readonly string[] FileExtensions = {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".xml", ".svg", ".tif", ".tiff", ".bmp"
        };

        private static bool LooksLikeFilePath(string value)
        {
            if (value.IndexOf('/') < 0) return false; // "type/name" has a slash too
            foreach (string ext in FileExtensions)
                if (value.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The package name from the package header (128 UTF-16 characters). Used to tell
        // "ownpkg:type/name" from a foreign one.
        private string PackageName()
        {
            if (table == null || table.Length < 12) return null;
            try {
                using (MemoryStream ms = new MemoryStream(table))
                using (BinaryReader br = new BinaryReader(ms)) {
                    ms.Seek(8, SeekOrigin.Begin);
                    int packageCount = br.ReadInt32();
                    if (packageCount < 0 || packageCount > MaxResourcePackages) return null;
                    long stringPoolPos = ms.Position;
                    ms.Seek(4, SeekOrigin.Current);
                    int stringPoolSize = br.ReadInt32();
                    if (stringPoolSize < 8 || stringPoolPos + stringPoolSize > ms.Length) return null;
                    ms.Seek(stringPoolPos + stringPoolSize, SeekOrigin.Begin);

                    long packPos = ms.Position;
                    if (packPos + 12 > ms.Length) return null;
                    ms.Seek(2, SeekOrigin.Current);
                    int headerSize = br.ReadInt16();
                    int packChunkSize = br.ReadInt32();
                    if (headerSize < 272 || packChunkSize < headerSize ||
                        packPos + packChunkSize > ms.Length)
                        return null;
                    ms.Seek(packPos + 12, SeekOrigin.Begin);
                    string name = Encoding.Unicode.GetString(br.ReadBytes(256));
                    int nul = name.IndexOf('\0');
                    if (nul >= 0) name = name.Substring(0, nul);
                    name = name.Trim();
                    return name.Length == 0 ? null : name;
                }
            } catch (Exception ex) when (!(ex is OutOfMemoryException)) {
                return null;
            }
        }

        /// <summary>
        /// Get culture info from string
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        internal static CultureInfo GetCulture(byte[] code)
        {
            string language;
            string country;
            byte[] decode;
            if (code[1] > 0x80) { // ISO-639-2
                decode = new byte[3];
                decode[0] = (byte)(code[0] & 0x1F);
                decode[1] = (byte)(((code[1] & 0x3) << 3) + (code[0] & 0xE0) >> 1);
                decode[2] = (byte)((code[1] & 0x7C) >> 2);
            } else { //ISO-639-1
                decode = new byte[2];
                decode[0] = code[0];
                decode[1] = code[1];
            }
            language = System.Text.Encoding.ASCII.GetString(decode);
            decode = new byte[2];
            if (code[3] > 0x80) {
                decode[0] = (byte)(code[2] & 0x1F);
                decode[1] = (byte)(((code[3] & 0x3) << 3) + (code[2] & 0xE0) >> 1);
            } else {
                decode[0] = code[2];
                decode[1] = code[3];
            }
            country = System.Text.Encoding.ASCII.GetString(decode);
            // locale-less configs (0x00,0x00...) or garbage BCP47 bytes are not cultures: null
            language = language.Trim((char)0, ' ');
            country = country.Trim((char)0, ' ');
            if (language.Length == 0 || !IsAsciiAlpha(language) || (country.Length > 0 && !IsAsciiAlpha(country)))
                return null;
            try {
                return country.Length == 0
                    ? CultureInfo.GetCultureInfo(language)
                    : CultureInfo.GetCultureInfo(language.ToLowerInvariant() + "-" + country.ToUpperInvariant());
            } catch {
                try { return CultureInfo.GetCultureInfo(language); } catch { return null; }
            }
        }

        private static bool IsAsciiAlpha(string s)
        {
            foreach (char c in s)
                if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z')) return false;
            return true;
        }
    }
}
