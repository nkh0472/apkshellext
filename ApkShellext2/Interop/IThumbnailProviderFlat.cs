using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// <c>IThumbnailProvider</c> (thumbcache.h), flat declaration.
    /// Vtable: slot 3 <c>GetThumbnail</c>.
    /// </summary>
    /// <remarks>
    /// Slot numbers follow the thumbcache.h vtable. The returned HBITMAP must be a 32bpp top-down
    /// DIB with premultiplied alpha (<c>WTS_ALPHATYPE.Argb</c>); the shell owns it and calls
    /// DeleteObject.
    /// </remarks>
    [ComImport]
    [Guid("e357fccd-a995-4576-b01f-234630154e96")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IThumbnailProviderFlat {
        [PreserveSig]
        int GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha);
    }

    /// <summary>
    /// <c>IInitializeWithStream</c> (propsys.h), flat declaration.
    /// Vtable: slot 3 <c>Initialize</c>.
    /// </summary>
    /// <remarks>
    /// Slot numbers follow the propsys.h vtable. <paramref name="pstream"/> is a raw
    /// <c>IStream*</c> owned by the caller; the handler must not release it.
    /// </remarks>
    [ComImport]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInitializeWithStreamFlat {
        [PreserveSig]
        int Initialize(IntPtr pstream, uint grfMode);
    }
}
