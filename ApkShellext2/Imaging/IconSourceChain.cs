using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using ApkQuickReader;
using PNGDecrush;
using WebPWrapper;

namespace ApkShellext2
{
    /// <summary>
    /// Ordered list of <see cref="IIconSource"/>s: the first source that produces a
    /// bitmap wins, the rest are not asked. This is the single funnel every reader's
    /// getIcon/getImage goes through, so the lookup order and the failure handling
    /// live in one place instead of once per reader.
    ///
    /// Failure policy: a source may throw (attacker-controlled bytes reach every
    /// decoder), the chain catches it, logs it and moves on. The chain itself never
    /// throws, so an unrenderable icon is a null - never a COM exception escaping into
    /// Explorer.
    /// </summary>
    public sealed class IconSourceChain
    {
        private readonly List<IIconSource> sources = new List<IIconSource>();
        private readonly Action<string> log;

        public IconSourceChain()
        {
        }

        public IconSourceChain(Action<string> log)
        {
            this.log = log;
        }

        /// <summary>Sources currently registered, in the order they are tried.</summary>
        public int Count
        {
            get { return sources.Count; }
        }

        /// <summary>Appends one source; later sources only run when earlier ones fail.</summary>
        public IconSourceChain Add(IIconSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            sources.Add(source);
            return this;
        }

        /// <summary>
        /// Runs the chain. Returns true with the first bitmap produced; false when every
        /// source declined or failed. No source exception escapes this method.
        /// </summary>
        public bool TryGetIcon(IconRequest request, out Bitmap icon)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            // Let sources that resolve a reference (the manifest lookup) continue the
            // walk with the path they found: ForPath keeps this chain.
            request.Chain = this;

            foreach (IIconSource source in sources)
            {
                Bitmap candidate = null;
                try
                {
                    if (source.TryGetIcon(request, out candidate) && candidate != null)
                    {
                        icon = candidate;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    ReportFailure(request, source, ex);
                }

                // A source that said "not mine" must not leak the bitmap it allocated
                // on the way (a failed layer-list, a rejected decode...).
                if (candidate != null)
                {
                    candidate.Dispose();
                }
            }

            icon = null;
            return false;
        }

        /// <summary>
        /// Convenience for sources that resolved a concrete archive path and want the
        /// decode half of the same chain to run it (the manifest reference source).
        /// </summary>
        public bool TryDecode(IconRequest request, string path, out Bitmap icon)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            return TryGetIcon(request.ForPath(path), out icon);
        }

        private void ReportFailure(IconRequest request, IIconSource source, Exception ex)
        {
            string message = "icon source " + source.GetType().Name + " failed: " +
                (ex.Message == null ? ex.GetType().Name : ex.Message.Split('\n')[0]);
            // Debug channel first: this is the one that survives in ship builds.
            DbgLog.W(message);
            Action<string> sink = request.Log;
            if (sink != null)
            {
                try
                {
                    sink(message);
                }
                catch
                {
                    // logging must never break the chain
                }
            }
            Action<string> fallback = log;
            if (fallback != null && !ReferenceEquals(sink, fallback))
            {
                try
                {
                    fallback(message);
                }
                catch
                {
                    // logging must never break the chain
                }
            }
        }
    }

    /// <summary>
    /// ② A file entry inside the package, decoded straight from its bytes: the bitmap
    /// formats GDI+ reads (png/jpg/jpeg/gif/tif/tiff) and WebP through the vendored
    /// wrapper whose size gate (MaxWebPDecodeSide/MaxWebPDecodePixels) always applies.
    ///
    /// Every byte read goes through SafeArchive.ReadEntryBounded, the only sanctioned
    /// materialisation path, and the bytes are fully buffered before GDI+ sees them: a
    /// scaled DrawImage over a non-seekable zip entry stream renders blank pixels.
    /// </summary>
    public sealed class ArchiveImageIconSource : IIconSource
    {
        public bool TryGetIcon(IconRequest request, out Bitmap icon)
        {
            icon = null;
            if (request.Lookup != IconLookupKind.ArchivePath || string.IsNullOrEmpty(request.Path))
            {
                return false;
            }

            string ext = Path.GetExtension(request.Path).ToLowerInvariant();
            bool bitmapFormat = ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                ext == ".gif" || ext == ".tif" || ext == ".tiff";
            if (!bitmapFormat && ext != ".webp")
            {
                return false;
            }

            IArchive archive = request.Archive;
            if (archive == null || !archive.Contains(request.Path))
            {
                return false;
            }
            IArchiveEntry entry = archive.GetEntryExact(request.Path);
            if (entry == null)
            {
                return false;
            }

            if (ext == ".webp")
            {
                WebP webp = new WebP();
                byte[] bytes = archive.ReadEntryBounded(entry, SafeArchive.MAX_ICON, "webp " + request.Path);
                icon = Utility.ResizeBitmap(webp.Decode(bytes), request.Size);
                return icon != null;
            }

            // bounded read: attacker-declared size must never drive allocation or casts
            byte[] raw = archive.ReadEntryBounded(entry, SafeArchive.MAX_ICON, "image " + request.Path);
            if (ext == ".png")
            {
                // a small file may honestly declare an enormous bitmap; refuse it before
                // GDI+ allocates the surface
                IconDecodeLimits.ValidatePng(raw, "image " + request.Path);
            }
            using (MemoryStream ms = new MemoryStream(raw, 0, raw.Length, false, false))
            {
                icon = Utility.ResizeBitmap((Bitmap)Image.FromStream(ms), request.Size);
            }
            return icon != null;
        }
    }

    /// <summary>
    /// ③ An Android XML drawable (vector, adaptive-icon, layer-list, shape, bitmap).
    /// The source only recognises the case and defers to the reader's renderer: the apk
    /// and aab drawing code intentionally differ (the apk pins Svg PointsPerInch, has a
    /// recursion guard and a bitmap-drawable branch the aab renderer does not).
    /// </summary>
    public sealed class VectorDrawableIconSource : IIconSource
    {
        public bool TryGetIcon(IconRequest request, out Bitmap icon)
        {
            icon = null;
            if (request.Lookup != IconLookupKind.ArchivePath || string.IsNullOrEmpty(request.Path))
            {
                return false;
            }
            if (request.Renderer == null)
            {
                return false;
            }
            if (!string.Equals(Path.GetExtension(request.Path), ".xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            IArchive archive = request.Archive;
            if (archive == null || !archive.Contains(request.Path))
            {
                return false;
            }
            // the SVG rasteriser allocates the target surface: bound it before it does
            IconDecodeLimits.ValidateRenderTarget(request.Size, "drawable " + request.Path);
            icon = request.Renderer.RenderXmlDrawable(request, request.Path);
            return icon != null;
        }
    }

    /// <summary>
    /// ① The AndroidManifest icon attribute: the shared AxmlParser/ArscParser rules
    /// resolve tag.attribute to an archive path, then the same chain decodes that path.
    ///
    /// The resolved path is exposed even when decoding fails: ApkReader needs it to keep
    /// its legacy xml-density fallback and its "cannot find image" contract.
    /// </summary>
    public sealed class ManifestIconReferenceSource : IIconSource
    {
        /// <summary>The path the parser resolved, or "" when the manifest carries none.</summary>
        public string ResolvedPath { get; private set; }

        /// <summary>The resource id the typed search saw (0 when the value was a string).</summary>
        public uint ReferenceId { get; private set; }

        /// <summary>The DATA_TYPE the typed search saw, as an int (0 when absent).</summary>
        public int DataType { get; private set; }

        /// <summary>True once the source was asked (the path is valid from then on).</summary>
        public bool Attempted { get; private set; }

        public bool TryGetIcon(IconRequest request, out Bitmap icon)
        {
            icon = null;
            if (request.Lookup != IconLookupKind.ReferenceAttribute)
            {
                return false;
            }

            Attempted = true;
            IManifestIconLookup lookup = request.ManifestLookup;
            if (lookup == null)
            {
                ResolvedPath = "";
                return false;
            }

            uint referenceId;
            int dataType;
            string path = lookup.ResolveIconPath(request.Tag, request.Attr, out referenceId, out dataType);
            ResolvedPath = path ?? "";
            ReferenceId = referenceId;
            DataType = dataType;
            DbgLog.W("GETIMG tag " + request.Tag + "." + request.Attr + " size=" + request.Size.Width +
                " type=" + dataType + " id=0x" + referenceId.ToString("X8") + " -> " + ResolvedPath);

            if (ResolvedPath == "" || request.Chain == null)
            {
                return false;
            }
            return request.Chain.TryDecode(request, ResolvedPath, out icon);
        }
    }

    /// <summary>
    /// ④ An iOS icon: an Info.plist key chain (or a bare icon name) resolves to a file
    /// in the .app root. The bytes are PNG-crushed Apple variants, so the decrushing
    /// decoder runs first and the plain PNG decoder is the fallback - each decoded once.
    /// </summary>
    public sealed class IpaPlistIconSource : IIconSource
    {
        public bool TryGetIcon(IconRequest request, out Bitmap icon)
        {
            icon = null;
            if (request.Reader != IconReaderKind.Ipa)
            {
                return false;
            }

            string name;
            if (request.Lookup == IconLookupKind.PListKeys)
            {
                IPlistIconLookup lookup = request.PlistLookup;
                if (lookup == null || request.Keys == null)
                {
                    return false;
                }
                name = lookup.FindIconName(request.Keys);
            }
            else if (request.Lookup == IconLookupKind.ArchivePath)
            {
                name = request.Path;
            }
            else
            {
                return false;
            }

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            IArchive archive = request.Archive;
            if (archive == null)
            {
                return false;
            }

            string root = request.RootPrefix ?? "";
            string fullname = name;
            IArchiveEntry image;
            if (!name.EndsWith(".png"))
            {
                fullname = name + "@3x" + ".png";
                image = archive.GetEntry(root + fullname);
                if (image == null)
                {
                    fullname = name + "@2x" + ".png";
                    image = archive.GetEntry(root + fullname);
                }
                if (image == null)
                {
                    fullname = name + ".png";
                    image = archive.GetEntry(root + fullname);
                }
            }
            else
            {
                image = archive.GetEntry(root + name);
            }

            if (image == null)
            {
                return false;
            }
            byte[] imageBytes = archive.ReadEntryBounded(image, SafeArchive.MAX_ICON, "icon " + name);
            IconDecodeLimits.ValidatePng(imageBytes, "icon " + name);
            try
            {
                MemoryStream imageOut = new MemoryStream();
                PNGDecrusher.Decrush(new MemoryStream(imageBytes), imageOut);
                icon = new Bitmap(imageOut);
                return icon != null;
            }
            catch (InvalidDataException)
            {
                // image is not crushed
                icon = new Bitmap(new MemoryStream(imageBytes));
                return icon != null;
            }
        }
    }

    /// <summary>
    /// ⑤ An Appx / Appxbundle logo: the manifest declares a base logo path and the
    /// package ships scale-qualified variants (logo.scale-100.png, ...). An exact match
    /// wins, otherwise the biggest declared scale does.
    /// </summary>
    public sealed class AppxManifestLogoIconSource : IIconSource
    {
        public bool TryGetIcon(IconRequest request, out Bitmap icon)
        {
            icon = null;
            if (request.Lookup != IconLookupKind.ManifestLogo || string.IsNullOrEmpty(request.LogoPath))
            {
                return false;
            }
            IArchive archive = request.Archive;
            if (archive == null)
            {
                return false;
            }

            string iconPath = request.LogoPath;
            int dot = iconPath.LastIndexOf(".");
            if (dot < 0)
            {
                return false;
            }
            string name = iconPath.Substring(0, dot);
            string extension = iconPath.Substring(dot + 1);

            IArchiveEntry logo = null;
            int scale = -1;
            foreach (IArchiveEntry en in archive.Entries)
            {
                Match m = Regex.Match(en.FullName, name + @"(\.scale\-(\d+))?" + @"\." + extension);
                if (m.Success)
                {
                    if (m.Groups[1].Value == "")
                    {
                        // exactly matching, no scale
                        logo = en;
                        break;
                    }
                    else
                    {
                        // find the biggest scale
                        int newScale = int.Parse(m.Groups[2].Value);
                        if (newScale > scale)
                        {
                            logo = en;
                            scale = newScale;
                        }
                    }
                }
            }

            if (logo == null)
            {
                return false;
            }

            // buffer fully: a Bitmap over the live zip stream decodes lazily and turns
            // blank once the reader/stream is disposed (the shell draws later)
            byte[] imageBytes = archive.ReadEntryBounded(logo, SafeArchive.MAX_ICON, "logo " + iconPath);
            if (string.Equals(extension, "png", StringComparison.OrdinalIgnoreCase))
            {
                IconDecodeLimits.ValidatePng(imageBytes, "logo " + iconPath);
            }
            icon = new Bitmap(new MemoryStream(imageBytes, 0, imageBytes.Length, false, false));
            return icon != null;
        }
    }

    /// <summary>
    /// A split bundle (apks/apkm/xapk) ships a root-level icon.png next to the split
    /// apks. It is the cheap high-quality path the bundle reader prefers; the largest
    /// root variant wins. Nested icons belong to the base apk reader.
    /// </summary>
    public sealed class BundleRootIconSource : IIconSource
    {
        public bool TryGetIcon(IconRequest request, out Bitmap icon)
        {
            icon = null;
            if (request.Lookup != IconLookupKind.BundleRootIcon)
            {
                return false;
            }
            IArchive archive = request.Archive;
            if (archive == null)
            {
                return false;
            }

            IArchiveEntry best = null;
            foreach (IArchiveEntry e in archive.Entries)
            {
                if (!archive.IsFile(e))
                {
                    continue;
                }
                string leaf = e.FullName;
                int slash = leaf.LastIndexOf('/');
                if (slash >= 0)
                {
                    continue; // root only
                }
                if (!leaf.StartsWith("icon", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!leaf.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (best == null || SizeOf(archive, e) > SizeOf(archive, best))
                {
                    best = e; // prefer largest variant
                }
            }

            if (best == null)
            {
                return false;
            }

            byte[] raw = archive.ReadEntryBounded(best, SafeArchive.MAX_ICON, "root icon " + best.FullName);
            IconDecodeLimits.ValidatePng(raw, "root icon " + best.FullName);
            using (MemoryStream ms = new MemoryStream(raw, 0, raw.Length, false, false))
            {
                // full buffer before FromStream: GDI+ lazy decode + non-seekable zip stream
                icon = new Bitmap(ms, true);
            }
            return icon != null;
        }

        // an entry that declares no usable size (the 0xFFFFFFFF sentinel without a zip64
        // record) scores 0 instead of four gigabytes - same rule the bundle reader uses
        private static long SizeOf(IArchive archive, IArchiveEntry entry)
        {
            long declared = archive.DeclaredLength(entry);
            return declared < 0 ? 0 : declared;
        }
    }
}
