using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// <c>IShellExtInit</c> (shlobj_core.h), flat declaration.
    /// Vtable: slot 3 <c>Initialize</c>.
    /// </summary>
    /// <remarks>
    /// Slot numbers follow the shlobj_core.h vtable. <c>Initialize</c> is a
    /// <c>[PreserveSig] int</c>: declared as <c>void</c> the CLR would turn the HRESULT into an
    /// exception on the shell's thread, which makes a recoverable failure a handler that silently
    /// stops loading.
    ///
    /// <paramref name="pDataObj"/> is a raw <c>IDataObject*</c> owned by the shell (query it for
    /// <c>CF_HDROP</c>); do not Release it, and do not pass it to
    /// <c>Marshal.ReleaseComObject</c> - releasing an object the shell still owns takes the host
    /// down on the next use.
    /// </remarks>
    [ComImport]
    [Guid("000214e8-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellExtInitFlat {
        [PreserveSig]
        int Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgID);
    }
}
