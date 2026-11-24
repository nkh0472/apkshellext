using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// <c>IShellFolder</c> (shobjidl_core.h), flat declaration - only what a context menu handler
    /// needs to resolve the children of the folder that hosts a selection.
    /// Vtable: slot 3 <c>ParseDisplayName</c>, 4 <c>EnumObjects</c>, 5 <c>BindToObject</c>,
    /// 6 <c>BindToStorage</c>, 7 <c>CompareIDs</c>, 8 <c>CreateViewObject</c>,
    /// 9 <c>GetAttributesOf</c>, 10 <c>GetUIObjectOf</c>, 11 <c>GetDisplayNameOf</c>,
    /// 12 <c>SetNameOf</c>.
    /// </summary>
    /// <remarks>
    /// Slot numbers follow the shlobj_core.h vtable; only the twelve members below are declared
    /// because the optional IShellFolder2 extension is not needed here. PIDLs stay
    /// <see cref="IntPtr"/>: the shell frees them with <c>ILFree</c>, the handler never owns them.
    /// </remarks>
    [ComImport]
    [Guid("000214e6-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellFolderFlat {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    /// <summary>
    /// <c>IClassFactory</c> (unknwn.h), flat declaration.
    /// Vtable: slot 3 <c>CreateInstance</c>, slot 4 <c>LockServer</c>.
    /// </summary>
    /// <remarks>
    /// Not part of the shipping registration shape: <c>mscoree.dll</c> is the in-proc server and it
    /// creates the managed class factory itself. The declaration is here because the tests create
    /// instances through it instead of depending on the shell.
    /// </remarks>
    [ComImport]
    [Guid("00000001-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IClassFactoryFlat {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }
}
