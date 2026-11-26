using System;

namespace ApkQuickReader {
    /// <summary>
    /// Reads the minimum sdk level that a bundletool table of contents (toc.pb, a
    /// BuildApksResult message) declares for the whole app.
    ///
    /// bundletool raises the sdk levels written into every split's AndroidManifest.xml to
    /// the level the split itself can be installed from (21), so a split's manifest cannot
    /// tell what the app really supports. The real minimum stays in the table of contents,
    /// which is why an .apks reports MinSdk 21 for an app that installs from 16.
    ///
    /// Field numbers and meaning follow delvinru/apk-info's core/src/toc.rs (Apache-2.0) and
    /// this file is an independent implementation of that field chain over the raw protobuf
    /// wire format:
    ///   BuildApksResult.variant(1) → Variant.targeting(1) →
    ///   Targeting.sdk_version_targeting(1) → SdkVersionTargeting.value(1) →
    ///   Value.min(1) → Int32Value.value(1, varint)
    /// The smallest min over all variants and all their values wins.
    ///
    /// The walk is bounded on every axis: at most <see cref="MaxTocPbBytes"/> of input,
    /// <see cref="MaxDepth"/> nested messages, <see cref="MaxFieldsPerMessage"/> fields per
    /// message, ten bytes per varint, and a length prefix that may never run past the bytes
    /// that remain. A broken link (a field that is absent, a wire type that does not match, a
    /// truncated varint, a length past the end, an overflow) only makes
    /// <see cref="TryReadMinSdk"/> return false, and the caller keeps the split's own value.
    /// Nothing here throws, and no allocation depends on the input length.
    /// </summary>
    internal static class TocPb {
        /// <summary>
        /// The table of contents of even a huge bundle is a few hundred kilobytes; 2 MiB is
        /// generous for it and small enough that a container claiming a giant toc.pb is refused
        /// before anything is copied.
        /// </summary>
        internal const int MaxTocPbBytes = 2 * 1024 * 1024;

        /// <summary>
        /// The known chain is six messages deep; a walker that meets deeper nesting stops
        /// instead of following it.
        /// </summary>
        internal const int MaxDepth = 8;

        /// <summary>
        /// A message with more fields than this is not a table of contents but a synthetic
        /// blob meant to make the walk expensive.
        /// </summary>
        internal const int MaxFieldsPerMessage = 4096;

        private const int MaxVarintBytes = 10;

        /// <summary>
        /// BuildApksResult → Variant → Targeting → SdkVersionTargeting → Value → Int32Value:
        /// the Int32Value is the leaf of the known chain.
        /// </summary>
        private const int ChainDepth = 6;

        private const int WireVarint = 0;
        private const int WireFixed64 = 1;
        private const int WireLengthDelimited = 2;
        private const int WireFixed32 = 5;

        /// <summary>
        /// The smallest minSdk the table of contents declares, or false when the bytes are not
        /// a table of contents of the known shape (then the caller keeps the split's manifest
        /// value). A declared min at or below zero is not a usable sdk level and is false too.
        /// </summary>
        internal static bool TryReadMinSdk(byte[] data, out int minSdk) {
            minSdk = 0;
            if (data == null || data.Length == 0 || data.Length > MaxTocPbBytes) return false;
            int value;
            if (!TryReadMessage(data, 0, data.Length, 1, out value)) return false;
            if (value <= 0) return false;
            minSdk = value;
            return true;
        }

        /// <summary>
        /// Walks one message of the chain. The only field that continues the chain is field 1:
        /// length-delimited on the way down, the int32 varint in the leaf. Every other field is
        /// skipped by its wire type, so the targeting payloads (abi, density, texture, …) and the
        /// split graph cost nothing.
        /// </summary>
        private static bool TryReadMessage(byte[] data, int start, int end, int depth, out int minSdk) {
            minSdk = 0;
            if (depth < 1 || depth > MaxDepth) return false;
            if (start < 0 || end > data.Length || start > end) return false;

            bool leaf = depth == ChainDepth;
            bool found = false;
            int best = int.MaxValue;
            int value = 0;
            int position = start;
            int fields = 0;

            while (position < end) {
                if (++fields > MaxFieldsPerMessage) return false;
                int field, wire;
                if (!TryReadTag(data, ref position, end, out field, out wire)) return false;
                if (field == 1 && wire == (leaf ? WireVarint : WireLengthDelimited)) {
                    if (leaf) {
                        // Int32Value.value: a negative level travels as a ten-byte varint
                        ulong raw;
                        if (!TryReadVarint(data, ref position, end, out raw)) return false;
                        value = unchecked((int)(uint)raw);
                        found = true;
                    } else {
                        int length;
                        if (!TryReadLength(data, ref position, end, out length)) return false;
                        int child;
                        if (!TryReadMessage(data, position, position + length, depth + 1, out child)) return false;
                        if (child < best) best = child;
                        found = true;
                        position += length;
                    }
                } else if (!TrySkipField(data, ref position, end, wire)) {
                    return false;
                }
            }

            if (!found) return false;
            minSdk = leaf ? value : best;
            return true;
        }

        /// <summary>One varint tag: field number in the high bits, wire type in the low three.</summary>
        private static bool TryReadTag(byte[] data, ref int position, int end, out int field, out int wire) {
            field = 0;
            wire = 0;
            ulong tag;
            if (!TryReadVarint(data, ref position, end, out tag)) return false;
            if (tag > uint.MaxValue) return false;   // a tag is 32-bit by definition
            field = (int)(tag >> 3);
            wire = (int)(tag & 7);
            return field > 0;                        // field number 0 does not exist
        }

        /// <summary>The length prefix of a length-delimited field, bounded by what remains.</summary>
        private static bool TryReadLength(byte[] data, ref int position, int end, out int length) {
            length = 0;
            ulong value;
            if (!TryReadVarint(data, ref position, end, out value)) return false;
            if (value > (ulong)(end - position)) return false;
            length = (int)value;
            return true;
        }

        /// <summary>
        /// A varint of at most ten bytes. The tenth byte may only carry the last bit, so a longer
        /// or oversized encoding is a broken message, not a large number.
        /// </summary>
        private static bool TryReadVarint(byte[] data, ref int position, int end, out ulong value) {
            value = 0;
            for (int i = 0; i < MaxVarintBytes; i++) {
                if (position >= end) return false;
                byte b = data[position++];
                if (i == MaxVarintBytes - 1) {
                    if (b > 1) return false;        // bit 64 and the continuation bit would not fit
                    value |= (ulong)b << 63;
                    return true;
                }
                value |= (ulong)(b & 0x7F) << (7 * i);
                if ((b & 0x80) == 0) return true;
            }
            return false;                            // no terminator within ten bytes
        }

        /// <summary>
        /// Skips a field the chain does not use. Groups (wire 3/4) and the undefined wire types
        /// are refused: this reader knows the flat wire format only.
        /// </summary>
        private static bool TrySkipField(byte[] data, ref int position, int end, int wire) {
            switch (wire) {
                case WireVarint: {
                    ulong ignored;
                    return TryReadVarint(data, ref position, end, out ignored);
                }
                case WireFixed64:
                    return SkipBytes(ref position, end, 8);
                case WireLengthDelimited: {
                    int length;
                    if (!TryReadLength(data, ref position, end, out length)) return false;
                    position += length;
                    return true;
                }
                case WireFixed32:
                    return SkipBytes(ref position, end, 4);
                default:
                    return false;
            }
        }

        private static bool SkipBytes(ref int position, int end, int count) {
            if (end - position < count) return false;
            position += count;
            return true;
        }
    }
}
