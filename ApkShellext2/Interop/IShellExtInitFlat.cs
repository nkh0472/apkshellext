using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// <c>IShellExtInit</c> (shlobj_core.h), flat declaration.
    /// Vtable: slot 3 <c>Initialize</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>ApkShellext2\SharpShell\SharpShell\SharpShell\Interop\IShellExtInit.cs</c>
    /// (:10 guid, :19 Initialize). SharpShell declares <c>Initialize</c> as <c>void</c>, which makes
    /// the CLR translate the HRESULT into an exception on the shell's thread and exposes a
    /// signature mismatch to the vtable; here it is a <c>[PreserveSig] int</c> like the native
    /// method.
    ///
    /// <paramref name="pDataObj"/> is a raw <c>IDataObject*</c> owned by the shell (query it for
    /// <c>CF_HDROP</c>); do not Release it, and do not pass it to
    /// <c>Marshal.ReleaseComObject</c> - that premature release is the
    /// ShellExtInitServer.cs:52 defect described in plan §3.2.
    /// </remarks>
    [ComImport]
    [Guid("000214e8-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellExtInitFlat {
        [PreserveSig]
        int Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgID);
    }
}
