using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ApkShellext2.Interop {
    /// <summary>
    /// The Win32 entry points stage 4 needs, one region per DLL.
    ///
    /// Conventions (house style, keep them when adding a declaration):
    /// <list type="bullet">
    /// <item>every import sets <c>CharSet = CharSet.Unicode</c> and, where Win32 has an A/W pair,
    /// names the W entry point explicitly, so a 32 bit build can never pick the ANSI variant;</item>
    /// <item>DLLs without an A/W pair (ole32, ntdll) use <c>ExactSpelling = true</c>;</item>
    /// <item><c>bool</c> is always <c>[MarshalAs(UnmanagedType.Bool)]</c>, both for parameters and
    /// for the return value (a bare C# <c>bool</c> would be marshalled as VARIANT_BOOL on some
    /// paths and as a 1 byte value under <c>UnmanagedType.I1</c>);</item>
    /// <item>COM style APIs return <c>int</c> HRESULTs and are not translated into exceptions;</item>
    /// <item><c>SetLastError = true</c> on everything that can fail and report through
    /// <c>Marshal.GetLastWin32Error</c>.</item>
    /// </list>
    /// </summary>
    internal static class NativeMethods {
        #region ole32.dll

        /// <summary>
        /// Creates a COM object from its CLSID. Callers should prefer the generic overloads below,
        /// which take the IID from the interface declaration (<c>typeof(T).GUID</c>) instead of
        /// repeating a literal.
        /// </summary>
        [DllImport("ole32.dll", ExactSpelling = true, SetLastError = false)]
        internal static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        /// <summary>CoCreateInstance against <typeparamref name="TInterface"/>'s IID, in-proc only.</summary>
        internal static int CreateInstance<TInterface>(Guid clsid, out IntPtr instance) where TInterface : class {
            Guid classId = clsid;
            Guid iid = typeof(TInterface).GUID;
            return CoCreateInstance(ref classId, IntPtr.Zero, (uint)CLSCTX.InprocServer, ref iid, out instance);
        }

        /// <summary>Same as the <see cref="Guid"/> overload, for a CLSID that is only known as text.</summary>
        internal static int CreateInstance<TInterface>(string clsid, out IntPtr instance) where TInterface : class {
            return CreateInstance<TInterface>(new Guid(clsid), out instance);
        }

        [DllImport("ole32.dll", ExactSpelling = true, SetLastError = false)]
        internal static extern IntPtr CoTaskMemAlloc(IntPtr byteCount);

        [DllImport("ole32.dll", ExactSpelling = true, SetLastError = false)]
        internal static extern void CoTaskMemFree(IntPtr pointer);

        /// <summary>
        /// Frees the payload of a <see cref="STGMEDIUM"/> through COM, honouring
        /// <c>pUnkForRelease</c>. Never call <c>GlobalFree</c> / <c>CloseHandle</c> on the medium
        /// yourself - for an IStream medium the stream is reference counted, not a handle.
        /// </summary>
        [DllImport("ole32.dll", ExactSpelling = true, SetLastError = false)]
        internal static extern void ReleaseStgMedium(ref STGMEDIUM medium);

        #endregion

        #region shell32.dll

        /// <summary>
        /// <c>IShellItem</c> from a file system path. The intended consumer is the S3 thumbnail /
        /// icon path resolver, which needs an item for <c>IShellItemImageFactory</c>.
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHCreateItemFromParsingName", SetLastError = true)]
        internal static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        /// <summary>Concatenates two PIDLs into a newly allocated one; free it with <see cref="ILFree"/>.</summary>
        [DllImport("shell32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

        [DllImport("shell32.dll", ExactSpelling = true, SetLastError = false)]
        internal static extern void ILFree(IntPtr pidl);

        /// <summary>
        /// Tells the shell that associations changed, so icon caches are dropped after a
        /// registration or unregistration run. No item pointers are passed, hence
        /// <c>SHCNF.Flush</c> only.
        /// </summary>
        [DllImport("shell32.dll", ExactSpelling = true, SetLastError = false)]
        internal static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        /// <summary>Convenience wrapper: <c>SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, ...)</c>.</summary>
        internal static void NotifyAssociationsChanged() {
            SHChangeNotify((uint)SHCNE.AssocChanged, (uint)SHCNF.Flush, IntPtr.Zero, IntPtr.Zero);
        }

        #endregion

        #region user32.dll

        /// <summary>Creates the empty menu the context menu handler fills in QueryContextMenu.</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreatePopupMenu", SetLastError = true)]
        internal static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DestroyMenu", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InsertMenuItemW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InsertMenuItemW(IntPtr hMenu, uint uItem, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFO lpmii);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuItemInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMenuItemInfoW(IntPtr hMenu, uint uItem, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFO lpmii);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuItemCount", SetLastError = true)]
        internal static extern int GetMenuItemCount(IntPtr hMenu);

        /// <summary>Shows a popup menu; returns the chosen command id when TPM_RETURNCMD is set.</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "TrackPopupMenuEx", SetLastError = true)]
        internal static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetCursorPos", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        #endregion

        #region kernel32.dll

        internal const uint GET_MODULE_HANDLE_EX_FLAG_PIN = 0x00000001;
        internal const uint GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x00000002;
        internal const uint GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;

        /// <summary>
        /// Module handle by name (or of the current module when <paramref name="lpModuleName"/> is
        /// null). Intended consumer: the S3 icon path, which needs the module that owns our icon
        /// resources to build an <c>@{path},-id</c> icon location.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleExW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetModuleHandleExW(uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string lpModuleName, out IntPtr phModule);

        /// <summary>Same API in its FROM_ADDRESS form, where the second argument is a code address.</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleExW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetModuleHandleExFromAddress(uint dwFlags, IntPtr lpModuleName, out IntPtr phModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleFileNameW", SetLastError = true)]
        internal static extern uint GetModuleFileNameW(IntPtr hModule, [Out] StringBuilder lpFilename, uint nSize);

        #endregion

        #region ntdll.dll

        /// <summary>
        /// The only truthful source for the OS build number: <c>Environment.OSVersion</c> reports
        /// 6.2 on Windows 10+ unless the process has a manifest that opts into the newer version
        /// lies, and the stage 4 registrar may run from an unmanifested host. Returns an NTSTATUS
        /// (0 = STATUS_SUCCESS).
        /// </summary>
        [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)]
        internal static extern int RtlGetVersion(ref OSVERSIONINFOEXW versionInfo);

        #endregion
    }
}
