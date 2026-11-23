using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Shell.Native {
    //  The shell interfaces the native menu handler answers, declared flat and public.
    //
    //  Three properties matter here. First, a managed class only shows public, ComVisible
    //  interfaces through its COM callable wrapper - the flat declarations in
    //  ApkShellext2.Interop are internal and exist for layout tests, so the handler cannot use
    //  them as its exposed surface. Second, every version repeats the methods of the version
    //  before it instead of inheriting from it: a COM vtable has no notion of interface
    //  inheritance, and the wrapper lays out a derived interface with just its own methods, which
    //  moves QueryContextMenu out of slot 3. Third, the methods keep [PreserveSig] and their slot
    //  order (QueryContextMenu, InvokeCommand, GetCommandString, HandleMenuMsg, HandleMenuMsg2);
    //  a missing [PreserveSig] makes the runtime translate the result into an exception HRESULT.
    //
    //  The IIDs are the shell's own interface identifiers, so QueryInterface from Explorer finds
    //  the version it asks for regardless of the class the CLSID names.

    /// <summary>The initialization interface the shell calls before it asks for a menu.</summary>
    [ComVisible(true)]
    [Guid("000214e8-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellExtInitNative {
        /// <summary>Receives the selected items. A handler that keeps none answers E_FAIL.</summary>
        [PreserveSig]
        int Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgID);
    }

    /// <summary>The base menu interface: build the items, then answer invocations and verbs.</summary>
    [ComVisible(true)]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenuNative {
        /// <summary>Adds the items to the shell's menu and reports how many command ids they use.</summary>
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        /// <summary>Runs the command the shell picked, by index or by verb.</summary>
        [PreserveSig]
        int InvokeCommand(IntPtr pici);

        /// <summary>Returns the canonical verb or the help text of one command id.</summary>
        [PreserveSig]
        int GetCommandString(int idCmd, uint uFlags, int reserved, IntPtr pszName, int cchMax);
    }

    /// <summary>Version 2 adds the owner draw message the shell forwards while the menu is open.</summary>
    [ComVisible(true)]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu2Native {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(IntPtr pici);

        [PreserveSig]
        int GetCommandString(int idCmd, uint uFlags, int reserved, IntPtr pszName, int cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>Version 3 adds the same message with a pointer to the window procedure result.</summary>
    [ComVisible(true)]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu3Native {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(IntPtr pici);

        [PreserveSig]
        int GetCommandString(int idCmd, uint uFlags, int reserved, IntPtr pszName, int cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        /// <summary><paramref name="plResult"/> is the address of the shell's LRESULT, not the value.</summary>
        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr plResult);
    }
}
