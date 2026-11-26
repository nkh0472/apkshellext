using System;
using System.Collections.Generic;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Builds the bytes of a bundletool table of contents (toc.pb, a BuildApksResult
    /// message) for the tests. The tests need the same wire shapes bundletool writes -
    /// BuildApksResult.variant(1) → Variant.targeting(1) →
    /// Targeting.sdk_version_targeting(1) → SdkVersionTargeting.value(1) →
    /// Value.min(1) → Int32Value.value(1) - and the raw writers below so a test can
    /// splice the broken links (a wrong wire type, a length past the end, deep nesting, a
    /// field flood) the tolerant reader must refuse.
    /// </summary>
    internal static class TocPbBuilder
    {
        /// <summary>A plain protobuf varint; negative int32 levels travel as ten bytes.</summary>
        internal static byte[] Varint(ulong value)
        {
            var bytes = new List<byte>(10);
            while (value >= 0x80)
            {
                bytes.Add((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            bytes.Add((byte)value);
            return bytes.ToArray();
        }

        /// <summary>A varint tag: field number in the high bits, wire type in the low three.</summary>
        internal static byte[] Tag(int field, int wire)
        {
            return Varint((ulong)((field << 3) | (wire & 7)));
        }

        internal static byte[] VarintField(int field, ulong value)
        {
            return Concat(Tag(field, 0), Varint(value));
        }

        internal static byte[] BytesField(int field, byte[] payload)
        {
            return Concat(Tag(field, 2), Varint((ulong)payload.Length), payload);
        }

        internal static byte[] Concat(params byte[][] parts)
        {
            int length = 0;
            foreach (byte[] part in parts) length += part.Length;
            var joined = new byte[length];
            int offset = 0;
            foreach (byte[] part in parts)
            {
                Buffer.BlockCopy(part, 0, joined, offset, part.Length);
                offset += part.Length;
            }
            return joined;
        }

        internal static byte[] RepeatedBytesField(int field, params byte[][] payloads)
        {
            var parts = new byte[payloads.Length][];
            for (int i = 0; i < payloads.Length; i++) parts[i] = BytesField(field, payloads[i]);
            return Concat(parts);
        }

        /// <summary>A BuildApksResult carrying the given variant messages.</summary>
        internal static byte[] Toc(params byte[][] variants)
        {
            return RepeatedBytesField(1, variants);
        }

        /// <summary>One variant whose sdk_version_targeting carries the given mins.</summary>
        internal static byte[] Variant(params int[] sdkMins)
        {
            return BytesField(1, Targeting(sdkMins));
        }

        internal static byte[] Targeting(params int[] sdkMins)
        {
            return BytesField(1, SdkVersionTargeting(sdkMins));
        }

        internal static byte[] SdkVersionTargeting(params int[] sdkMins)
        {
            var values = new byte[sdkMins.Length][];
            for (int i = 0; i < sdkMins.Length; i++) values[i] = Value(sdkMins[i]);
            return RepeatedBytesField(1, values);
        }

        /// <summary>Value.min, which is an Int32Value message.</summary>
        internal static byte[] Value(int min)
        {
            return BytesField(1, Int32Value(min));
        }

        internal static byte[] Int32Value(int value)
        {
            return VarintField(1, unchecked((ulong)(uint)value));
        }
    }
}
