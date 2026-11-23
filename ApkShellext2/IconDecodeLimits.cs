using System;
using System.Drawing;
using System.IO;

namespace ApkShellext2
{
    /// <summary>
    /// Declared-size guards for icon decoding, applied *before* a decoder allocates
    /// anything. The byte caps in <c>SafeArchive</c> stop one entry from being huge;
    /// this stops a small, valid-looking header from declaring a four-gigapixel bitmap
    /// that GDI+ would then try to allocate. A 32 MB PNG can honestly declare more than
    /// 8 megapixels, so the file-size cap alone cannot catch that case.
    ///
    /// The numbers match the WebP wrapper's own gate (MaxWebPDecodeSide=4096,
    /// MaxWebPDecodePixels=8Mpx) on purpose: every decoder in the icon path must refuse
    /// the same declared sizes.
    /// </summary>
    public static class IconDecodeLimits
    {
        /// <summary>Longest declared side an icon may have, in pixels.</summary>
        public const int MaxIconSide = 4096;

        /// <summary>Most pixels an icon may declare, in total.</summary>
        public const long MaxIconPixels = 8L * 1024 * 1024;

        // Enough chunks for the crushed-PNG variant (CgBI before IHDR) and nothing more;
        // a PNG without IHDR in the first few chunks is malformed either way.
        private const int MaxChunksBeforeIhdr = 16;

        /// <summary>
        /// Throws when a decoder-declared width/height is outside the icon limits. A
        /// zero side means "no declared size" (a native-size render request) and is not
        /// a declaration to police.
        /// </summary>
        public static void ValidateDeclaredSize(int width, int height, string what)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }
            if (width > MaxIconSide || height > MaxIconSide ||
                (long)width * (long)height > MaxIconPixels)
            {
                throw new InvalidDataException(what + " declares " + width + "x" + height +
                    ", over the icon decode limits (side <= " + MaxIconSide +
                    ", total pixels <= " + MaxIconPixels + ")");
            }
        }

        /// <summary>
        /// The render target of a vector/SVG draw. Same gate: a hostile caller cannot
        /// make the rasteriser allocate an unbounded surface through the size argument.
        /// </summary>
        public static void ValidateRenderTarget(Size size, string what)
        {
            ValidateDeclaredSize(size.Width, size.Height, what);
        }

        /// <summary>
        /// Reads the PNG IHDR width/height without decoding pixels. Returns false when
        /// the bytes are not a PNG or carry no readable IHDR (the caller then lets the
        /// real decoder produce its own error, so unsupported input keeps its old
        /// behaviour instead of getting a new exception).
        /// </summary>
        public static bool TryReadPngSize(byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (data == null || data.Length < 8 + 8 + 8)
            {
                return false;
            }
            // 89 50 4E 47 0D 0A 1A 0A
            if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47 ||
                data[4] != 0x0D || data[5] != 0x0A || data[6] != 0x1A || data[7] != 0x0A)
            {
                return false;
            }

            int offset = 8;
            for (int chunk = 0; chunk < MaxChunksBeforeIhdr; chunk++)
            {
                if (offset + 8 > data.Length)
                {
                    return false;
                }
                // length is attacker-controlled: check it against the real buffer before
                // using it as a stride, but never allocate from it.
                long length = ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) |
                              ((long)data[offset + 2] << 8) | data[offset + 3];
                if (length < 0 || length > int.MaxValue)
                {
                    return false;
                }
                string type = new string(new char[] {
                    (char)data[offset + 4], (char)data[offset + 5],
                    (char)data[offset + 6], (char)data[offset + 7] });
                if (type == "IHDR")
                {
                    if (length < 8 || offset + 8 + 8 > data.Length)
                    {
                        return false;
                    }
                    int w = (data[offset + 8] << 24) | (data[offset + 9] << 16) |
                            (data[offset + 10] << 8) | data[offset + 11];
                    int h = (data[offset + 12] << 24) | (data[offset + 13] << 16) |
                            (data[offset + 14] << 8) | data[offset + 15];
                    // a declared side must be a positive signed int; the sign bit is
                    // how GDI+ reads it too, so negative means corrupt
                    if (w <= 0 || h <= 0)
                    {
                        return false;
                    }
                    width = w;
                    height = h;
                    return true;
                }
                // skip chunk data + CRC, guarding the arithmetic against overflow
                long next = (long)offset + 12 + length;
                if (next > data.Length)
                {
                    return false;
                }
                offset = (int)next;
            }
            return false;
        }

        /// <summary>
        /// Rejects a PNG whose IHDR declares a size beyond the icon limits, before any
        /// pixel buffer exists. Non-PNG or unreadable-header input is left to the
        /// decoder (unchanged behaviour).
        /// </summary>
        public static void ValidatePng(byte[] data, string what)
        {
            int width;
            int height;
            if (!TryReadPngSize(data, out width, out height))
            {
                return;
            }
            ValidateDeclaredSize(width, height, what + " (png IHDR)");
        }
    }
}
