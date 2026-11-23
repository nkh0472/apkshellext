using System;
using System.Drawing;

namespace ApkShellext2
{
    /// <summary>
    /// Which package reader asked for the icon. Sources use it to decide whether a
    /// request belongs to them at all (the iOS plist source, for example, only runs
    /// for <see cref="IconReaderKind.Ipa"/>).
    /// </summary>
    public enum IconReaderKind
    {
        Unknown = 0,
        Apk,
        Aab,
        Ipa,
        Appx,
        AppxBundle,
        SplitApk
    }

    /// <summary>
    /// What the request carries into the chain, i.e. the shape of the lookup the
    /// reader needs:
    /// <list type="bullet">
    /// <item><see cref="ReferenceAttribute"/>: an AndroidManifest tag/attribute that the
    /// shared AxmlParser/ArscParser rules resolve to an archive path (apk).</item>
    /// <item><see cref="ArchivePath"/>: a concrete path inside the package (or an IPA
    /// icon name without extension).</item>
    /// <item><see cref="PListKeys"/>: an Info.plist key chain naming the IPA icon (ipa).</item>
    /// <item><see cref="ManifestLogo"/>: the logo path declared by an Appx manifest.</item>
    /// <item><see cref="BundleRootIcon"/>: the root-level icon.png of a split bundle.</item>
    /// </list>
    /// </summary>
    public enum IconLookupKind
    {
        None = 0,
        ReferenceAttribute,
        ArchivePath,
        PListKeys,
        ManifestLogo,
        BundleRootIcon
    }

    /// <summary>
    /// Everything one icon lookup needs, and nothing a source has to know about the
    /// concrete reader that created it.
    ///
    /// A request is immutable in spirit: <see cref="ForPath"/> / <see cref="ForSize"/>
    /// clone it so a nested lookup (an adaptive-icon layer referencing another drawable,
    /// say) cannot corrupt the request its caller is still walking.
    /// </summary>
    public sealed class IconRequest
    {
        public IconRequest(Size size)
        {
            Size = size;
        }

        /// <summary>
        /// The container the request reads from. Object-typed on purpose: the archive
        /// abstraction (ApkQuickReader.IArchive) is internal and stays that way; only
        /// this assembly turns the handle back into an IArchive.
        /// </summary>
        public IconRequest(Size size, object container)
            : this(size)
        {
            Container = container;
        }

        /// <summary>Requested output size.</summary>
        public Size Size { get; set; }

        /// <summary>Which reader created the request.</summary>
        public IconReaderKind Reader { get; set; }

        /// <summary>Shape of the lookup (see <see cref="IconLookupKind"/>).</summary>
        public IconLookupKind Lookup { get; set; }

        /// <summary>The package archive handle (see the constructor remarks).</summary>
        public object Container { get; set; }

        /// <summary>Manifest tag for <see cref="IconLookupKind.ReferenceAttribute"/>.</summary>
        public string Tag { get; set; }

        /// <summary>Manifest attribute for <see cref="IconLookupKind.ReferenceAttribute"/>.</summary>
        public string Attr { get; set; }

        /// <summary>Archive path for <see cref="IconLookupKind.ArchivePath"/>.</summary>
        public string Path { get; set; }

        /// <summary>Prefix a path before opening it (the IPA app root, e.g. "Payload/X.app/").</summary>
        public string RootPrefix { get; set; }

        /// <summary>Info.plist key chain for <see cref="IconLookupKind.PListKeys"/>.</summary>
        public string[] Keys { get; set; }

        /// <summary>The logo path declared by an Appx manifest.</summary>
        public string LogoPath { get; set; }

        /// <summary>Render hook for Android XML drawables (vector/adaptive/layer/shape).</summary>
        public IVectorIconRenderer Renderer { get; set; }

        /// <summary>Resolve hook for the AndroidManifest icon attribute (apk).</summary>
        public IManifestIconLookup ManifestLookup { get; set; }

        /// <summary>Resolve hook for the Info.plist icon keys (ipa).</summary>
        public IPlistIconLookup PlistLookup { get; set; }

        /// <summary>The chain currently running this request (set by IconSourceChain).</summary>
        public IconSourceChain Chain { get; set; }

        /// <summary>Debug logger of the reader that created the request.</summary>
        public Action<string> Log { get; set; }

        /// <summary>How many nested drawable references were already resolved.</summary>
        public int Depth { get; set; }

        /// <summary>
        /// A clone of this request asking for the entry at <paramref name="path"/>,
        /// keeping size, container, renderer and logging.
        /// </summary>
        public IconRequest ForPath(string path)
        {
            IconRequest nested = (IconRequest)MemberwiseClone();
            nested.Lookup = IconLookupKind.ArchivePath;
            nested.Path = path;
            return nested;
        }

        /// <summary>A clone of this request at another size (bundle root icon resize).</summary>
        public IconRequest ForSize(Size size)
        {
            IconRequest nested = (IconRequest)MemberwiseClone();
            nested.Size = size;
            return nested;
        }

        /// <summary>The typed archive behind <see cref="Container"/> (null for a foreign handle).</summary>
        internal ApkQuickReader.IArchive Archive
        {
            get { return Container as ApkQuickReader.IArchive; }
        }

        /// <summary>Reader debug log, if any (never throws).</summary>
        internal void Trace(string message)
        {
            Action<string> log = Log;
            if (log == null)
            {
                return;
            }
            try
            {
                log(message);
            }
            catch
            {
                // a logging failure must never break icon extraction
            }
        }
    }

    /// <summary>
    /// One way to turn an icon request into a bitmap. A source returns false when the
    /// request is not its business (wrong lookup kind, path without a matching
    /// extension, entry missing...); a failure is a false, never an exception the
    /// caller has to handle.
    /// </summary>
    public interface IIconSource
    {
        /// <summary>
        /// True when this source produced the icon. Implementations must not throw for
        /// ordinary bad input; <see cref="IconSourceChain"/> still swallows whatever
        /// escapes so one broken source cannot take the whole icon down.
        /// </summary>
        bool TryGetIcon(IconRequest request, out Bitmap icon);
    }

    /// <summary>
    /// Renders an Android XML drawable (vector, adaptive-icon, layer-list, shape,
    /// bitmap drawable) that a reader owns. The drawing code stays with the reader
    /// because the apk and aab renderers deliberately differ; the source only decides
    /// that the request is an XML drawable this reader can render.
    /// </summary>
    public interface IVectorIconRenderer
    {
        /// <summary>Bitmap for the drawable entry at <paramref name="path"/>, or null.</summary>
        Bitmap RenderXmlDrawable(IconRequest request, string path);
    }

    /// <summary>
    /// Resolves the AndroidManifest icon attribute through the shared AxmlParser /
    /// ArscParser rules (the "B4 icon chain": exact-id reference, name fallback,
    /// "android:" refusal, res/ shorthand, smallest duplicated id).
    /// </summary>
    public interface IManifestIconLookup
    {
        /// <summary>
        /// Resolved icon path (may be ""), plus the raw typed value and reference id the
        /// parser saw, for logging/diagnostics.
        /// </summary>
        string ResolveIconPath(string tag, string attr, out uint referenceId, out int dataType);

        /// <summary>The manifest value found when XML drawables are ignored (legacy fallback).</summary>
        string ResolveLegacyIconPath(string tag, string attr);
    }

    /// <summary>Follows an Info.plist key chain to an IPA icon name.</summary>
    public interface IPlistIconLookup
    {
        /// <summary>The first icon file name for the key chain, or "" when absent.</summary>
        string FindIconName(string[] keys);
    }
}
