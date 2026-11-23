using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ApkShellext2.Interop {
    //  The context menu interfaces are declared FLAT: one interface per shell version, each one
    //  carrying its *complete* method list and inheriting nothing but IUnknown (which the CLR
    //  provides). Interface inheritance is what makes a CLR generated vtable put the derived
    //  methods before the base methods, which shifts every slot and crashes the shell when it
    //  calls HandleMenuMsg2 (see plan §3.1, measured 0xC0000005).
    //
    //  Vtable layout, identical to the native interfaces:
    //    slot 0 QueryInterface, 1 AddRef, 2 Release   <- IUnknown, supplied by the CLR
    //    slot 3 QueryContextMenu, 4 InvokeCommand, 5 GetCommandString
    //    slot 6 HandleMenuMsg                          <- IContextMenu2Flat
    //    slot 7 HandleMenuMsg2                         <- IContextMenu3Flat
    //
    //  Registering all three lets the shell QueryInterface to whichever version it prefers: the
    //  IContextMenu3 section is complete on its own instead of depending on a base interface's
    //  section being laid out at the right offset.

    /// <summary>
    /// <c>IContextMenu</c> (shlobj_core.h), flat declaration.
    /// </summary>
    /// <remarks>
    /// Slot order and parameter list mirror the vendored declaration at
    /// <c>ApkShellext2\SharpShell\SharpShell\SharpShell\Interop\IContextMenu.cs</c>
    /// (:12 guid, :25 QueryContextMenu, :33 InvokeCommand, :45 GetCommandString), except that every
    /// method here is <c>[PreserveSig]</c> so a handler can return an HRESULT encoded item count
    /// instead of the CLR translating it into an exception.
    /// </remarks>
    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenuFlat {
        /// <summary>
        /// Adds the verbs to the shell supplied <paramref name="hMenu"/>. Returns
        /// <c>MAKE_HRESULT(SEVERITY_SUCCESS, 0, itemCount)</c> - the shell reads the low 16 bits.
        /// </summary>
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, int idCmdFirst, int idCmdLast, CMF uFlags);

        /// <summary>
        /// Runs the verb identified by <paramref name="pici"/>, which points at a
        /// <see cref="CMINVOKECOMMANDINFO"/> or <see cref="CMINVOKECOMMANDINFOEX"/>; check
        /// <c>CMIC.UNICODE</c> in fMask before reading the wide strings.
        /// </summary>
        [PreserveSig]
        int InvokeCommand(IntPtr pici);

        /// <summary>Returns a verb, a help text or a verb icon for <paramref name="idCmd"/>.</summary>
        [PreserveSig]
        int GetCommandString(int idCmd, GCS uFlags, int reserved, StringBuilder commandString, int cchMax);
    }

    /// <summary>
    /// <c>IContextMenu2</c> (shlobj_core.h), flat declaration: the three IContextMenu methods plus
    /// <c>HandleMenuMsg</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Interop\IContextMenu2.cs</c> (:12 guid <c>000214f4</c>, :28/:37/:50 re-declared
    /// base methods, :62 HandleMenuMsg), flattened so no base section can move.
    /// </remarks>
    [ComImport]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu2Flat {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, int idCmdFirst, int idCmdLast, CMF uFlags);

        [PreserveSig]
        int InvokeCommand(IntPtr pici);

        [PreserveSig]
        int GetCommandString(int idCmd, GCS uFlags, int reserved, StringBuilder commandString, int cchMax);

        /// <summary>Ownerdraw support: forwards <c>WM_MEASUREITEM</c> / <c>WM_DRAWITEM</c>.</summary>
        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>
    /// <c>IContextMenu3</c> (shlobj_core.h), flat declaration: all five methods, slot 3..7.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Interop\IContextMenu3.cs</c> (:12 guid <c>bcfce0a0-ec17-11d0-8d10-00a0c90f2719</c>,
    /// :74 HandleMenuMsg2). Note the GUID: <c>000214e4</c> is IContextMenu, not IContextMenu3 - a
    /// flat IContextMenu3 has to carry the 3.0 IID to be hit by the shell's QueryInterface.
    /// </remarks>
    [ComImport]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu3Flat {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, int idCmdFirst, int idCmdLast, CMF uFlags);

        [PreserveSig]
        int InvokeCommand(IntPtr pici);

        [PreserveSig]
        int GetCommandString(int idCmd, GCS uFlags, int reserved, StringBuilder commandString, int cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Ownerdraw support with a result out parameter. The shell stores
        /// <paramref name="plResult"/> as the message result, so it must be written on every path
        /// that returns S_OK.
        /// </summary>
        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, ref IntPtr plResult);
    }
}
