using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Shell.Native {
    //  The server side of the modern context menu. Every type in this file follows the same rules
    //  as NativeComInterfaces.cs: public, ComVisible, one flat list of methods in shell slot order,
    //  [PreserveSig] on every method and no [ComImport]. The CLR builds the vtable from the
    //  declaration order of a public ComVisible interface, which is why each shell interface is
    //  repeated here instead of being derived from a shared base: a derived managed interface would
    //  move GetTitle out of slot 3 and the shell would call the wrong function.

    /// <summary>
    /// <c>IExplorerCommand</c> as the shell calls it: the command registered under a file class's
    /// <c>shell\&lt;verb&gt;\ExplorerCommandHandler</c> value exposes this IID, gets the selection as
    /// an <c>IShellItemArray</c> and describes itself in two levels - the verb itself and the
    /// subcommands returned by <see cref="EnumSubCommands"/>.
    /// </summary>
    /// <remarks>The two LPWSTR out parameters are declared as <see cref="IntPtr"/> because the
    /// callee allocates them with <c>CoTaskMemAlloc</c> and the shell frees them.</remarks>
    [ComVisible(true)]
    [Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerCommandNative {
        [PreserveSig]
        int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);

        [PreserveSig]
        int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);

        [PreserveSig]
        int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);

        [PreserveSig]
        int GetCanonicalName(out Guid pguidCommandName);

        /// <summary>Reports the <c>EXPCMDSTATE</c> of the command for the current selection.</summary>
        [PreserveSig]
        int GetState(IntPtr psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);

        [PreserveSig]
        int Invoke(IntPtr psiItemArray, IntPtr pbc);

        [PreserveSig]
        int GetFlags(out uint pFlags);

        /// <summary>Hands out an <see cref="IEnumExplorerCommandNative"/> pointer; the caller releases it.</summary>
        [PreserveSig]
        int EnumSubCommands(out IntPtr ppEnum);
    }

    /// <summary>
    /// <c>IEnumExplorerCommand</c> as the shell calls it: the cursor over the subcommands of a
    /// command.
    /// </summary>
    /// <remarks><see cref="Next"/> receives the address of a caller-allocated array of
    /// <c>celt</c> interface pointers rather than an <c>out IntPtr</c>: the shell always passes a
    /// one element array, but a declaration that could only write one slot would silently corrupt
    /// a caller that asks for more.</remarks>
    [ComVisible(true)]
    [Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumExplorerCommandNative {
        [PreserveSig]
        int Next(uint celt, IntPtr pUICommand, out uint pulFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IntPtr ppenum);
    }
}
