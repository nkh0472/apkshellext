using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// <c>IQueryInfo</c> (shlobj_core.h), flat declaration.
    /// Vtable: slot 3 <c>GetInfoTip</c>, slot 4 <c>GetInfoFlags</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>ApkShellext2\SharpShell\SharpShell\SharpShell\Interop\IQueryInfo.cs</c>
    /// (:11 guid, :23 GetInfoTip, :31 GetInfoFlags). The IID doubles as the registry value name
    /// (<c>ShellEx\{00021500-...}</c>) that makes the shell load an info tip handler.
    /// </remarks>
    [ComImport]
    [Guid("00021500-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IQueryInfoFlat {
        /// <summary>
        /// Returns the tooltip text. The string is allocated by the implementation with
        /// <c>CoTaskMemAlloc</c> (the <c>LPWStr</c> marshaller here frees it), and the shell only
        /// asks for it when the user hovers the item.
        /// </summary>
        [PreserveSig]
        int GetInfoTip(QITIPF dwFlags, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszTip);

        [PreserveSig]
        int GetInfoFlags(out int pdwFlags);
    }
}
