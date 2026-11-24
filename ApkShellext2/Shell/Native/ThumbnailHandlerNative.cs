using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Registration;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The thumbnail handler: IInitializeWithFile carries the item path and IThumbnailProvider
    /// produces the bitmap.
    ///
    /// The bitmap is returned as an HBITMAP that the shell owns and frees with DeleteObject, so it
    /// must be a copy the handler never touches again. A request with no thumbnail answers S_FALSE
    /// with a null handle, which is how the shell is told to fall back to the item's icon.
    /// </summary>
    [ComVisible(true)]
    [Guid("D747C5A7-2F66-4B7D-8301-8531838E4ED3")]
    [ProgId(ShellGuids.ThumbnailProgId)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ThumbnailHandlerNative : IThumbnailProviderNative, IInitializeWithFileNative {
        private const string ModuleName = "ThumbnailHandlerNative";
        private const uint WtsatArgb = 2;

        private string selectedPath;

        public int Initialize(string pszFilePath, uint grfMode) {
            try {
                selectedPath = pszFilePath;
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "Initialize failed", ex);
                return HResult.E_FAIL;
            }
        }

        public int GetThumbnail(uint cx, out IntPtr phbmp, out uint pdwAlpha) {
            phbmp = IntPtr.Zero;
            pdwAlpha = 0;
            try {
                if (string.IsNullOrEmpty(selectedPath) || !File.Exists(selectedPath))
                    return HResult.S_FALSE;
                if (cx == 0)
                    return HResult.S_FALSE;

                // The shell names the size it wants; anything beyond the icon decode budget is
                // refused rather than allocating a bitmap of that size.
                try {
                    IconDecodeLimits.ValidateRenderTarget(new Size((int)cx, (int)cx), "thumbnail");
                } catch (InvalidDataException ex) {
                    ErrorLog.Warn(ModuleName, "Thumbnail size " + cx + " is refused: " + ex.Message, ex);
                    return HResult.S_FALSE;
                }

                Bitmap thumbnail = ThumbnailRenderer.Render(selectedPath, cx, ModuleName);
                if (thumbnail == null)
                    return HResult.S_FALSE;

                phbmp = thumbnail.GetHbitmap();
                pdwAlpha = WtsatArgb;
                return HResult.S_OK;
            } catch (Exception ex) {
                phbmp = IntPtr.Zero;
                pdwAlpha = 0;
                ErrorLog.Error(ModuleName, "GetThumbnail failed for " + Path.GetFileName(selectedPath ?? string.Empty), ex);
                return HResult.S_FALSE;
            }
        }
    }
}
