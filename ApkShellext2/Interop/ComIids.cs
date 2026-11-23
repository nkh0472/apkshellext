using System;

namespace ApkShellext2.Interop {
    /// <summary>
    /// IID constants for the flat COM declarations, used where a raw pointer has to be queried
    /// (<c>Marshal.QueryInterface</c>, <c>CoCreateInstance</c>, <c>SHCreateItemFromParsingName</c>)
    /// instead of cast to an interface.
    ///
    /// Each value is read back from the <c>[Guid]</c> attribute of the corresponding declaration, so
    /// the attribute stays the single source of truth. <c>ComLayout.Verify</c> pins those attribute
    /// values against the Win32 literals, which keeps a typo from silently making a QueryInterface
    /// miss.
    /// </summary>
    internal static class ComIid {
        /// <summary>
        /// IID_IUnknown. The only value here without a declaration behind it: the CLR provides
        /// IUnknown for every COM pointer, so a declaration would only exist to carry the attribute.
        /// </summary>
        internal static readonly Guid IUnknown = new Guid("00000000-0000-0000-C000-000000000046");

        internal static readonly Guid IClassFactory = typeof(IClassFactoryFlat).GUID;
        internal static readonly Guid IContextMenu = typeof(IContextMenuFlat).GUID;
        internal static readonly Guid IContextMenu2 = typeof(IContextMenu2Flat).GUID;
        internal static readonly Guid IContextMenu3 = typeof(IContextMenu3Flat).GUID;
        internal static readonly Guid IShellExtInit = typeof(IShellExtInitFlat).GUID;
        internal static readonly Guid IQueryInfo = typeof(IQueryInfoFlat).GUID;
        internal static readonly Guid IExtractIconW = typeof(IExtractIconWFlat).GUID;
        internal static readonly Guid IThumbnailProvider = typeof(IThumbnailProviderFlat).GUID;
        internal static readonly Guid IInitializeWithStream = typeof(IInitializeWithStreamFlat).GUID;
        internal static readonly Guid IPersistFile = typeof(IPersistFileFlat).GUID;
        internal static readonly Guid IShellFolder = typeof(IShellFolderFlat).GUID;
        internal static readonly Guid IExplorerCommand = typeof(IExplorerCommandFlat).GUID;
        internal static readonly Guid IEnumExplorerCommand = typeof(IEnumExplorerCommandFlat).GUID;
    }
}
