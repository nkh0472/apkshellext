using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ApkShellext2.Shell.Native {
    //  The shell interfaces the native icon, info-tip and thumbnail handlers answer, declared the
    //  same way as the menu interfaces in NativeComInterfaces: one flat public interface per shell
    //  interface, every method [PreserveSig], the shell's own IID on the type.
    //
    //  Flatness and public visibility are requirements, not style. A COM vtable has no notion of
    //  interface inheritance, and the COM callable wrapper exposes only the methods declared on the
    //  interface the class implements - a derived managed interface would drop the inherited slots.
    //  Only public ComVisible interfaces are exposed through the wrapper at all: the flat
    //  declarations in ApkShellext2.Interop are internal and exist for the layout tests. Without
    //  [PreserveSig] the runtime turns an HRESULT into an exception instead of a return value.
    //
    //  Flag parameters stay raw uint: these signatures describe the shell's ABI, and Shell\Native
    //  must not take a dependency on the internal enums the flat declarations use.

    /// <summary>
    /// <c>IExtractIconW</c>: report where the icon lives, then hand back its handles. The icon
    /// location is a buffer the shell owns; a handler that answers GIL_NOTFILENAME never writes it.
    /// </summary>
    [ComVisible(true)]
    [Guid("000214fa-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExtractIconWNative {
        /// <summary>
        /// Reports the icon source. <paramref name="szIconFile"/> is the shell's buffer, kept as an
        /// opaque pointer: marshalling it as a <c>StringBuilder</c> would make the runtime write the
        /// (empty) builder back into that buffer on return, and no value is needed when the flags
        /// say the icon comes from <see cref="Extract"/> instead.
        /// </summary>
        [PreserveSig]
        int GetIconLocation(uint uFlags, IntPtr szIconFile, int cchMax, out int piIndex, out uint pwFlags);

        /// <summary>
        /// Returns the two HICONs packed into <paramref name="nIconSize"/>: the low word is the
        /// large size, the high word the small one. The shell frees the handles.
        /// </summary>
        [PreserveSig]
        int Extract([In, MarshalAs(UnmanagedType.LPWStr)] string pszFile, uint nIconIndex,
            out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);
    }

    /// <summary>
    /// <c>IPersistFile</c>: the shell hands the handler its item path through
    /// <see cref="Load"/> - without that call <c>GetIconLocation</c> and <c>GetInfoTip</c> would run
    /// against no path at all. The rest of the interface must answer even though a shell handler
    /// never saves anything.
    /// </summary>
    [ComVisible(true)]
    [Guid("0000010b-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFileNative {
        /// <summary>Returns the CLSID of the class the shell created.</summary>
        [PreserveSig]
        int GetClassID(out Guid pClassID);

        /// <summary>S_FALSE: the object is read-only and never has unsaved state.</summary>
        [PreserveSig]
        int IsDirty();

        /// <summary>Receives the path of the item the shell is about to draw.</summary>
        [PreserveSig]
        int Load([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        [PreserveSig]
        int Save([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
            [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        [PreserveSig]
        int SaveCompleted([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        /// <summary>S_FALSE with a null pointer: there is no current working file.</summary>
        [PreserveSig]
        int GetCurFile(out IntPtr ppszFileName);
    }

    /// <summary>
    /// <c>IQueryInfo</c>: the tooltip text of the item. The tip string must be allocated with
    /// <c>CoTaskMemAlloc</c> for the shell to free it; the runtime does that for an
    /// <c>out string</c> marked LPWStr.
    /// </summary>
    [ComVisible(true)]
    [Guid("00021500-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IQueryInfoNative {
        /// <summary>
        /// Returns the tip, or S_FALSE with a null pointer when the item has none.
        /// <paramref name="dwFlags"/> carries QITIPF_* bits (single line, name, shortcut target).
        /// </summary>
        [PreserveSig]
        int GetInfoTip(uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszTip);

        /// <summary>QITIPF_* flags of the tip; zero means "no special flags".</summary>
        [PreserveSig]
        int GetInfoFlags(out uint pdwFlags);
    }

    /// <summary>
    /// <c>IThumbnailProvider</c>: the image the shell shows in a preview or a thumbnail tile.
    /// </summary>
    [ComVisible(true)]
    [Guid("e357fccd-a995-4576-b01f-234630154e96")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProviderNative {
        /// <summary>
        /// Returns the thumbnail as an HBITMAP for a square of <paramref name="cx"/> pixels.
        /// <paramref name="pdwAlpha"/> is a WTS_ALPHATYPE: 0 unknown, 1 opaque, 2 per-pixel alpha.
        /// </summary>
        [PreserveSig]
        int GetThumbnail(uint cx, out IntPtr phbmp, out uint pdwAlpha);
    }

    /// <summary>
    /// <c>IInitializeWithFile</c>: the shell hands the item path to a thumbnail handler it loads
    /// in process. Out of process it offers <c>IInitializeWithStream</c> instead, and this one is not
    /// called at all, so a handler has to answer both.
    /// </summary>
    [ComVisible(true)]
    [Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithFileNative {
        /// <summary><paramref name="grfMode"/> is an STGM access mode; STGM_READ is the only one a handler may ask for.</summary>
        [PreserveSig]
        int Initialize([In, MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    /// <summary>
    /// <c>IInitializeWithStream</c>: the initialisation the shell uses for a handler it hosts out of
    /// process. The stream is the item's bytes; the shell creates it, keeps ownership and releases it,
    /// so the handler only reads it during <see cref="Initialize"/>.
    /// </summary>
    [ComVisible(true)]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStreamNative {
        /// <summary>
        /// <paramref name="grfMode"/> is an STGM mode, STGM_READ in practice. The handler copies the
        /// stream it needs before returning: the shell may release it as soon as this call completes.
        /// </summary>
        [PreserveSig]
        int Initialize([In, MarshalAs(UnmanagedType.Interface)] IStream pstream, uint grfMode);
    }
}
