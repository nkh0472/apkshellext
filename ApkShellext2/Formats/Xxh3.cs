using System;

namespace ApkShellext2 {
    /// <summary>
    /// XXH3-64 with a zero seed and the default secret, implemented from the xxHash
    /// specification. The one-shot entry point hashes a byte array, and
    /// <see cref="Create"/> returns an incremental instance for streamed input.
    /// </summary>
    /// <remarks>
    /// The public contract is the 64-bit digest of the unseeded XXH3 function. The
    /// implementation keeps the four input ranges of the specification apart because
    /// each one mixes the input differently: up to 16 bytes, 17 to 128 bytes, 129 to
    /// 240 bytes, and the block based path above 240 bytes.
    /// </remarks>
    public sealed class Xxh3 {
        private const int StripeLength = 64;
        private const int InternalBufferLength = 256;
        private const int SecretConsumeRate = 8;
        private const int SecretLength = 192;
        private const int SecretLimit = SecretLength - StripeLength;
        private const int StripesPerBlock = SecretLimit / SecretConsumeRate;
        private const int MidsizeMax = 240;
        private const int SecretLastAccStart = 7;
        private const int SecretMergeAccsStart = 11;

        private const ulong Prime32_1 = 0x9E3779B1UL;
        private const ulong Prime32_2 = 0x85EBCA77UL;
        private const ulong Prime32_3 = 0xC2B2AE3DUL;
        private const ulong Prime64_1 = 0x9E3779B185EBCA87UL;
        private const ulong Prime64_2 = 0xC2B2AE3D27D4EB4FUL;
        private const ulong Prime64_3 = 0x165667B19E3779F9UL;
        private const ulong Prime64_4 = 0x85EBCA77C2B2AE63UL;
        private const ulong Prime64_5 = 0x27D4EB2F165667C5UL;
        private const ulong PrimeMx1 = 0x165667919E3779F9UL;
        private const ulong PrimeMx2 = 0x9FB21C651E98DF25UL;

        private static readonly byte[] Secret = {
            0xb8, 0xfe, 0x6c, 0x39, 0x23, 0xa4, 0x4b, 0xbe, 0x7c, 0x01, 0x81, 0x2c, 0xf7, 0x21, 0xad, 0x1c,
            0xde, 0xd4, 0x6d, 0xe9, 0x83, 0x90, 0x97, 0xdb, 0x72, 0x40, 0xa4, 0xa4, 0xb7, 0xb3, 0x67, 0x1f,
            0xcb, 0x79, 0xe6, 0x4e, 0xcc, 0xc0, 0xe5, 0x78, 0x82, 0x5a, 0xd0, 0x7d, 0xcc, 0xff, 0x72, 0x21,
            0xb8, 0x08, 0x46, 0x74, 0xf7, 0x43, 0x24, 0x8e, 0xe0, 0x35, 0x90, 0xe6, 0x81, 0x3a, 0x26, 0x4c,
            0x3c, 0x28, 0x52, 0xbb, 0x91, 0xc3, 0x00, 0xcb, 0x88, 0xd0, 0x65, 0x8b, 0x1b, 0x53, 0x2e, 0xa3,
            0x71, 0x64, 0x48, 0x97, 0xa2, 0x0d, 0xf9, 0x4e, 0x38, 0x19, 0xef, 0x46, 0xa9, 0xde, 0xac, 0xd8,
            0xa8, 0xfa, 0x76, 0x3f, 0xe3, 0x9c, 0x34, 0x3f, 0xf9, 0xdc, 0xbb, 0xc7, 0xc7, 0x0b, 0x4f, 0x1d,
            0x8a, 0x51, 0xe0, 0x4b, 0xcd, 0xb4, 0x59, 0x31, 0xc8, 0x9f, 0x7e, 0xc9, 0xd9, 0x78, 0x73, 0x64,
            0xea, 0xc5, 0xac, 0x83, 0x34, 0xd3, 0xeb, 0xc3, 0xc5, 0x81, 0xa0, 0xff, 0xfa, 0x13, 0x63, 0xeb,
            0x17, 0x0d, 0xdd, 0x51, 0xb7, 0xf0, 0xda, 0x49, 0xd3, 0x16, 0x55, 0x26, 0x29, 0xd4, 0x68, 0x9e,
            0x2b, 0x16, 0xbe, 0x58, 0x7d, 0x47, 0xa1, 0xfc, 0x8f, 0xf8, 0xb8, 0xd1, 0x7a, 0xd0, 0x31, 0xce,
            0x45, 0xcb, 0x3a, 0x8f, 0x95, 0x16, 0x04, 0x28, 0xaf, 0xd7, 0xfb, 0xca, 0xbb, 0x4b, 0x40, 0x7e
        };

        private readonly byte[] buffer = new byte[InternalBufferLength];
        private readonly ulong[] acc = new ulong[8];
        private int bufferedSize;
        private ulong totalLength;
        private ulong stripesSoFar;

        private Xxh3() {
            Reset();
        }

        /// <summary>Creates an incremental instance with an empty state.</summary>
        public static Xxh3 Create() {
            return new Xxh3();
        }

        /// <summary>Hashes the whole array in one call.</summary>
        public static ulong Hash(byte[] data) {
            if (data == null) throw new ArgumentNullException("data");
            var hash = new Xxh3();
            hash.Append(data, 0, data.Length);
            return hash.Digest();
        }

        /// <summary>Returns the instance to the empty state so it can hash another input.</summary>
        public void Reset() {
            acc[0] = Prime32_3;
            acc[1] = Prime64_1;
            acc[2] = Prime64_2;
            acc[3] = Prime64_3;
            acc[4] = Prime64_4;
            acc[5] = Prime32_2;
            acc[6] = Prime64_5;
            acc[7] = Prime32_1;
            bufferedSize = 0;
            totalLength = 0;
            stripesSoFar = 0;
        }

        /// <summary>Appends a range of bytes to the hash.</summary>
        public void Append(byte[] data, int offset, int length) {
            if (data == null) throw new ArgumentNullException("data");
            if (offset < 0 || offset > data.Length) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || length > data.Length - offset) throw new ArgumentOutOfRangeException("length");
            if (length == 0) return;

            totalLength += (ulong)length;
            int end = offset + length;

            if (length <= InternalBufferLength - bufferedSize) {
                Buffer.BlockCopy(data, offset, buffer, bufferedSize, length);
                bufferedSize += length;
                return;
            }

            if (bufferedSize > 0) {
                int fill = InternalBufferLength - bufferedSize;
                Buffer.BlockCopy(data, offset, buffer, bufferedSize, fill);
                offset += fill;
                int consumedSoFar = (int)stripesSoFar;
                ConsumeStripes(acc, ref consumedSoFar, buffer, 0, InternalBufferLength / StripeLength);
                stripesSoFar = (ulong)consumedSoFar;
                bufferedSize = 0;
            }

            if (end - offset > InternalBufferLength) {
                int stripes = (end - 1 - offset) / StripeLength;
                int consumedSoFar = (int)stripesSoFar;
                offset = ConsumeStripes(acc, ref consumedSoFar, data, offset, stripes);
                stripesSoFar = (ulong)consumedSoFar;
                Buffer.BlockCopy(data, offset - StripeLength, buffer, InternalBufferLength - StripeLength, StripeLength);
            }

            int remaining = end - offset;
            Buffer.BlockCopy(data, offset, buffer, 0, remaining);
            bufferedSize = remaining;
        }

        /// <summary>Returns the 64-bit digest without changing the state.</summary>
        public ulong Digest() {
            if (totalLength > MidsizeMax) {
                var digestAcc = (ulong[])acc.Clone();
                int consumedSoFar = (int)stripesSoFar;
                if (bufferedSize >= StripeLength) {
                    int stripes = (bufferedSize - 1) / StripeLength;
                    ConsumeStripes(digestAcc, ref consumedSoFar, buffer, 0, stripes);
                    Accumulate512(digestAcc, buffer, bufferedSize - StripeLength, SecretLimit - SecretLastAccStart);
                } else {
                    var lastStripe = new byte[StripeLength];
                    int catchup = StripeLength - bufferedSize;
                    Buffer.BlockCopy(buffer, InternalBufferLength - catchup, lastStripe, 0, catchup);
                    Buffer.BlockCopy(buffer, 0, lastStripe, catchup, bufferedSize);
                    Accumulate512(digestAcc, lastStripe, 0, SecretLimit - SecretLastAccStart);
                }
                return MergeAccumulators(digestAcc, totalLength);
            }
            if (totalLength > 128) return Hash129To240(buffer, 0, (int)totalLength);
            if (totalLength > 16) return Hash17To128(buffer, 0, (int)totalLength);
            return HashShort(buffer, 0, (int)totalLength);
        }

        private static void Accumulate(ulong[] acc, byte[] input, int inputOffset, int stripes, int secretOffset) {
            for (int i = 0; i < stripes; i++) {
                Accumulate512(acc, input, inputOffset + i * StripeLength, secretOffset + i * SecretConsumeRate);
            }
        }

        private static void Accumulate512(ulong[] acc, byte[] input, int inputOffset, int secretOffset) {
            for (int lane = 0; lane < 8; lane++) {
                ulong dataValue = Read64(input, inputOffset + lane * 8);
                ulong dataKey = dataValue ^ Read64(Secret, secretOffset + lane * 8);
                acc[lane ^ 1] += dataValue;
                acc[lane] += (ulong)(uint)dataKey * (uint)(dataKey >> 32);
            }
        }

        private static void Scramble(ulong[] acc, int secretOffset) {
            for (int lane = 0; lane < 8; lane++) {
                ulong value = acc[lane];
                value ^= value >> 47;
                value ^= Read64(Secret, secretOffset + lane * 8);
                value *= Prime32_1;
                acc[lane] = value;
            }
        }

        private static int ConsumeStripes(ulong[] acc, ref int stripesSoFar, byte[] input, int inputOffset, int stripes) {
            int secretOffset = stripesSoFar * SecretConsumeRate;
            if (stripes >= StripesPerBlock - stripesSoFar) {
                int thisRound = StripesPerBlock - stripesSoFar;
                do {
                    Accumulate(acc, input, inputOffset, thisRound, secretOffset);
                    Scramble(acc, SecretLimit);
                    inputOffset += thisRound * StripeLength;
                    stripes -= thisRound;
                    thisRound = StripesPerBlock;
                    secretOffset = 0;
                } while (stripes >= StripesPerBlock);
                stripesSoFar = 0;
            }
            if (stripes > 0) {
                Accumulate(acc, input, inputOffset, stripes, secretOffset);
                inputOffset += stripes * StripeLength;
                stripesSoFar += stripes;
            }
            return inputOffset;
        }

        private static ulong MergeAccumulators(ulong[] acc, ulong length) {
            ulong result = length * Prime64_1;
            for (int i = 0; i < 4; i++) {
                result += Mul128Fold64(
                    acc[2 * i] ^ Read64(Secret, SecretMergeAccsStart + 16 * i),
                    acc[2 * i + 1] ^ Read64(Secret, SecretMergeAccsStart + 16 * i + 8));
            }
            return Avalanche(result);
        }

        private static ulong HashShort(byte[] input, int offset, int length) {
            if (length > 8) {
                ulong bitflip1 = Read64(Secret, 24) ^ Read64(Secret, 32);
                ulong bitflip2 = Read64(Secret, 40) ^ Read64(Secret, 48);
                ulong lo = Read64(input, offset) ^ bitflip1;
                ulong hi = Read64(input, offset + length - 8) ^ bitflip2;
                ulong acc = (ulong)length + Swap64(lo) + hi + Mul128Fold64(lo, hi);
                return Avalanche(acc);
            }
            if (length >= 4) {
                uint input1 = Read32(input, offset);
                uint input2 = Read32(input, offset + length - 4);
                ulong bitflip = Read64(Secret, 8) ^ Read64(Secret, 16);
                ulong input64 = input2 + ((ulong)input1 << 32);
                return Rrmxmx(input64 ^ bitflip, (ulong)length);
            }
            if (length > 0) {
                byte c1 = input[offset];
                byte c2 = input[offset + (length >> 1)];
                byte c3 = input[offset + length - 1];
                uint combined = ((uint)c1 << 16) | ((uint)c2 << 24) | c3 | ((uint)length << 8);
                ulong bitflip = Read32(Secret, 0) ^ Read32(Secret, 4);
                return Avalanche64((ulong)combined ^ bitflip);
            }
            return Avalanche64(Read64(Secret, 56) ^ Read64(Secret, 64));
        }

        private static ulong Hash17To128(byte[] input, int offset, int length) {
            ulong acc = (ulong)length * Prime64_1;
            if (length > 32) {
                if (length > 64) {
                    if (length > 96) {
                        acc += Mix16(input, offset + 48, 96);
                        acc += Mix16(input, offset + length - 64, 112);
                    }
                    acc += Mix16(input, offset + 32, 64);
                    acc += Mix16(input, offset + length - 48, 80);
                }
                acc += Mix16(input, offset + 16, 32);
                acc += Mix16(input, offset + length - 32, 48);
            }
            acc += Mix16(input, offset, 0);
            acc += Mix16(input, offset + length - 16, 16);
            return Avalanche(acc);
        }

        private static ulong Hash129To240(byte[] input, int offset, int length) {
            ulong acc = (ulong)length * Prime64_1;
            for (int i = 0; i < 8; i++) acc += Mix16(input, offset + 16 * i, 16 * i);
            ulong accEnd = Mix16(input, offset + length - 16, 136 - 17);
            acc = Avalanche(acc);
            int rounds = length / 16;
            for (int i = 8; i < rounds; i++) accEnd += Mix16(input, offset + 16 * i, 16 * (i - 8) + 3);
            return Avalanche(acc + accEnd);
        }

        private static ulong Mix16(byte[] input, int inputOffset, int secretOffset) {
            return Mul128Fold64(
                Read64(input, inputOffset) ^ Read64(Secret, secretOffset),
                Read64(input, inputOffset + 8) ^ Read64(Secret, secretOffset + 8));
        }

        private static ulong Avalanche(ulong hash) {
            hash ^= hash >> 37;
            hash *= PrimeMx1;
            hash ^= hash >> 32;
            return hash;
        }

        private static ulong Avalanche64(ulong hash) {
            hash ^= hash >> 33;
            hash *= Prime64_2;
            hash ^= hash >> 29;
            hash *= Prime64_3;
            hash ^= hash >> 32;
            return hash;
        }

        private static ulong Rrmxmx(ulong hash, ulong length) {
            hash ^= RotateLeft(hash, 49) ^ RotateLeft(hash, 24);
            hash *= PrimeMx2;
            hash ^= (hash >> 35) + length;
            hash *= PrimeMx2;
            return hash ^ (hash >> 28);
        }

        private static ulong Mul128Fold64(ulong left, ulong right) {
            ulong loLo = (ulong)(uint)left * (uint)right;
            ulong loHi = (ulong)(uint)left * (uint)(right >> 32);
            ulong hiLo = (ulong)(uint)(left >> 32) * (uint)right;
            ulong hiHi = (ulong)(uint)(left >> 32) * (uint)(right >> 32);
            ulong cross = (loLo >> 32) + (uint)loHi + (uint)hiLo;
            ulong low = (loLo & 0xFFFFFFFFUL) | (cross << 32);
            ulong high = hiHi + (loHi >> 32) + (hiLo >> 32) + (cross >> 32);
            return low ^ high;
        }

        private static ulong RotateLeft(ulong value, int count) {
            return (value << count) | (value >> (64 - count));
        }

        private static ulong Swap64(ulong value) {
            return (value >> 56)
                | ((value >> 40) & 0x000000000000FF00UL)
                | ((value >> 24) & 0x0000000000FF0000UL)
                | ((value >> 8) & 0x00000000FF000000UL)
                | ((value << 8) & 0x000000FF00000000UL)
                | ((value << 24) & 0x0000FF0000000000UL)
                | ((value << 40) & 0x00FF000000000000UL)
                | (value << 56);
        }

        private static uint Read32(byte[] data, int offset) {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }

        private static ulong Read64(byte[] data, int offset) {
            return Read32(data, offset) | ((ulong)Read32(data, offset + 4) << 32);
        }
    }
}
