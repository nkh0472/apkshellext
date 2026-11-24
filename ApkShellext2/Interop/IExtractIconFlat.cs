using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ApkShellext2.Interop {
    /// <summary>
    /// <c>IExtractIconW</c> (shlobj_core.h), flat declaration.
    /// Vtable: slot 3 <c>GetIconLocation</c>, slot 4 <c>Extract</c>.
    /// </summary>
    /// <remarks>
    /// Slot numbers follow the shlobj_core.h vtable: the shell calls 3 then 4 by offset, so those
    /// numbers, not the method names, are the contract.
    /// </remarks>
    [ComImport]
    [Guid("000214fa-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IExtractIconWFlat {
        /// <summary>
        /// Writes the icon source (<paramref name="szIconFile"/>, at most
        /// <paramref name="cchMax"/> characters) and the icon index, plus the flags that tell the
        /// shell how to interpret them (<c>GILOutFlags.NotFilename</c> means "this is a resource
        /// id, not a path"). Return <c>S_FALSE</c> when there is no custom icon so the shell falls
        /// back to the default one.
        /// </summary>
        [PreserveSig]
        int GetIconLocation(GILInFlags uFlags, [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder szIconFile, int cchMax, out int piIndex, out GILOutFlags pwFlags);

        /// <summary>
        /// Draws the large and small icons. Only called when GetIconLocation returned
        /// <c>GILOutFlags.NotFilename</c>; the implementation returns two <c>HICON</c>s that the
        /// shell owns and destroys.
        /// </summary>
        [PreserveSig]
        int Extract([In][MarshalAs(UnmanagedType.LPWStr)] string pszFile, uint nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);
    }

    /// <summary>
    /// <c>IPersistFile</c> (objidl.h), flat declaration: the <c>IPersist</c> method plus the five
    /// <c>IPersistFile</c> methods.
    /// Vtable: slot 3 <c>GetClassID</c>, 4 <c>IsDirty</c>, 5 <c>Load</c>, 6 <c>Save</c>,
    /// 7 <c>SaveCompleted</c>, 8 <c>GetCurFile</c>.
    /// </summary>
    /// <remarks>
    /// The BCL exposes the same interface as
    /// <c>System.Runtime.InteropServices.ComTypes.IPersistFile</c>; it is declared here flat so a
    /// handler can keep one interface list instead of mixing both marshalling styles. Slot list
    /// from objidl.h. Handlers that need the file path are initialised through this interface,
    /// which is why it is declared here.
    /// </remarks>
    [ComImport]
    [Guid("0000010b-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersistFileFlat {
        [PreserveSig]
        int GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        [PreserveSig]
        int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        [PreserveSig]
        int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        [PreserveSig]
        int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        [PreserveSig]
        int GetCurFile(out IntPtr ppszFileName);
    }
}
