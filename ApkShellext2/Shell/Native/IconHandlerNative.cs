using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Registration;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The icon handler: IExtractIconW produces the HICON and IPersistFile carries the item path.
    ///
    /// The icon is produced in Extract rather than named as a file, so GetIconLocation reports
    /// GIL_NOTFILENAME and leaves the shell's buffer untouched. GIL_DONTCACHE tells the shell not
    /// to cache the result: the icon depends on package contents, which the shell cannot observe.
    /// </summary>
    [ComVisible(true)]
    [Guid("1F869CEE-4FDA-35D9-896F-43975A87D1F6")]
    [ProgId(ShellGuids.IconHandlerProgId)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class IconHandlerNative : PersistFileNativeServer, IExtractIconWNative {
        private const string ModuleName = "IconHandlerNative";
        private const uint GilNotFilename = 0x00000008;
        private const uint GilDontCache = 0x00000010;

        public int GetIconLocation(uint uFlags, IntPtr szIconFile, int cchMax, out int piIndex, out uint pwFlags) {
            try {
                piIndex = 0;
                pwFlags = GilNotFilename | GilDontCache;
                return HResult.S_OK;
            } catch (Exception ex) {
                piIndex = 0;
                pwFlags = 0;
                ErrorLog.Error(ModuleName, "GetIconLocation failed", ex);
                return HResult.E_FAIL;
            }
        }

        public int Extract(string pszFile, uint nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize) {
            phiconLarge = IntPtr.Zero;
            phiconSmall = IntPtr.Zero;
            try {
                // A path the shell cannot read keeps the generic icon: reporting S_OK with null
                // handles is how IExtractIcon asks for it, and an error would be logged per item.
                if (string.IsNullOrEmpty(SelectedPath) || !File.Exists(SelectedPath))
                    return HResult.S_OK;

                int large = (int)(nIconSize & 0xFFFF);
                int small = (int)(nIconSize >> 16);
                if (large > 0)
                    phiconLarge = HandleOf(IconRenderer.Render(SelectedPath, (uint)large, ModuleName));
                if (small > 0)
                    phiconSmall = HandleOf(IconRenderer.Render(SelectedPath, (uint)small, ModuleName));
                return HResult.S_OK;
            } catch (Exception ex) {
                phiconLarge = IntPtr.Zero;
                phiconSmall = IntPtr.Zero;
                ErrorLog.Error(ModuleName, "Extract failed for " + Path.GetFileName(SelectedPath ?? string.Empty), ex);
                return HResult.E_FAIL;
            }
        }

        private static IntPtr HandleOf(Icon icon) {
            return icon == null ? IntPtr.Zero : icon.Handle;
        }
    }
}
