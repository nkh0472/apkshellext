using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ApkShellext2;

namespace ApkQuickReader
{
    /// <summary>
    /// Shared parser for the binary resource table (resources.arsc).
    ///
    /// ApkReader and AabReader used to carry a copy of this walk each. AabReader's copy
    /// was disabled long ago (it has no resources table), so the two had drifted apart:
    /// only apk side was hardened. Keeping one implementation means the stage-0 hard
    /// gates below stay in a single place and both readers inherit them.
    ///
    /// Every length, offset and count read here comes from the file, so each one is
    /// bounded before it is used; malformed input yields the values collected so far
    /// instead of an exception or an allocation the file asked for.
    /// </summary>
    internal sealed class ArscParser
    {
        // ---- stage-0 hard gates, kept here (the only place they live) --------------
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
                                            // the entry is null, or it's referencing in loop, skip it
                                            if (data != 0x00000000 && !searchstack.Contains(data))
                                                res.Add(QuickSearchResource((UInt32)data));
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
        /// Returns ApplyFilter() when the feature is off so legacy behaviour is untouched.
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
