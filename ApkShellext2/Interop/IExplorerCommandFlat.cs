using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// <c>IExplorerCommand</c> (shobjidl_core.h), flat declaration, Windows 11 only.
    /// Vtable: slot 3 <c>GetTitle</c>, 4 <c>GetIcon</c>, 5 <c>GetToolTip</c>,
    /// 6 <c>GetCanonicalName</c>, 7 <c>GetState</c>, 8 <c>Invoke</c>, 9 <c>GetFlags</c>,
    /// 10 <c>EnumSubCommands</c>.
    /// </summary>
    /// <remarks>
    /// No handler implements this interface: the registrar only writes the
    /// <c>ExplorerCommandHandler</c> registration when <c>RtlGetVersion</c> reports Windows 11
    /// 22H2 (build 22621) or newer, and that value points at the dedicated CLSID in
    /// <c>ShellGuids.ExplorerCommandClsid</c>. The declaration fixes the registration
    /// shape, which InteropLayoutTests pins.
    ///
    /// The <c>LPWSTR*</c> out parameters stay <see cref="IntPtr"/> on purpose: the implementation
    /// has to hand back <c>CoTaskMemAlloc</c> strings, and a marshalled <c>out string</c> would
    /// hide that ownership from the implementer.
    /// </remarks>
    [ComImport]
    [Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IExplorerCommandFlat {
        [PreserveSig]
        int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);

        [PreserveSig]
        int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);

        [PreserveSig]
        int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);

        [PreserveSig]
        int GetCanonicalName(out Guid pguidCommandName);

        [PreserveSig]
        int GetState(IntPtr psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);

        [PreserveSig]
        int Invoke(IntPtr psiItemArray, IntPtr pbc);

        [PreserveSig]
        int GetFlags(out uint pFlags);

        [PreserveSig]
        int EnumSubCommands(out IntPtr ppEnum);
    }

    /// <summary>
    /// <c>IEnumExplorerCommand</c> (shobjidl_core.h), flat declaration, Windows 11 only.
    /// Vtable: slot 3 <c>Next</c>, 4 <c>Skip</c>, 5 <c>Reset</c>, 6 <c>Clone</c>.
    /// </summary>
    /// <remarks>
    /// Note the order: unlike <c>IEnumString</c> / <c>IEnumIDList</c>, this enumeration interface
    /// declares <c>Next</c> first and <c>Clone</c> last (the classic <c>IEnumX</c> order is
    /// Next, Skip, Reset, Clone as well, but several shell enumerations omit Skip - this one does
    /// not).
    /// </remarks>
    [ComImport]
    [Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumExplorerCommandFlat {
        [PreserveSig]
        int Next(uint celt, out IntPtr pUICommand, out uint pulFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IntPtr ppenum);
    }
}
