using Microsoft.Win32;
using SharpShell.Attributes;
using SharpShell.Diagnostics;
using SharpShell.SharpThumbnailHandler;
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using SharpShell.Extensions;
using SharpShell.ServerRegistration;
using System.IO;
using ApkShellext2.Diagnostics;
using ApkShellext2.Properties;
using ApkQuickReader;
using System.Configuration;

namespace ApkShellext2 {
    /// <summary>
    /// The thumbnail provider the shell used to activate. It declares no COM identity any more: the
    /// class id and the ProgID belong to
    /// <see cref="ApkShellext2.Shell.Native.ThumbnailHandlerNative"/>. This class stays until the
    /// SharpShell dependency is removed.
    /// </summary>
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apk")]
    public class ApkThumbnailHandler : SharpThumbnailHandler {
        protected override Bitmap GetThumbnailImage(uint width) {
            return ThumbnailRenderer.Render(SelectedItemStream, width, GetType().Name, Log);
        }

        [CustomRegisterFunction]
        public static void postDoRegister(Type type, RegistrationType registrationType) {
            Console.WriteLine("Registering " + type.FullName );

            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"\CLSID\.apk")) {
                if (key != null) {
                    // disable the shadow under thumbnail, make it more looks like an icon
                    key.SetValue("Treatment", 0);
                }
            }

            #region Clean up older versions registry
            try {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"\CLSID\" +
                    type.GUID.ToRegistryString() + @"\InprocServer32")) {
                    if (key != null && key.GetSubKeyNames().Count() != 0) {
                        Console.WriteLine("Found old version in registry, cleaning up ...");
                        foreach (var k in key.GetSubKeyNames()) {
                            if (k != type.Assembly.GetName().Version.ToString()) {
                                Registry.ClassesRoot.DeleteSubKeyTree(@"\CLSID\" +
                        type.GUID.ToRegistryString() + @"\InprocServer32\" + k);
                            }
                        }
                    }
                }
            } catch (Exception e) {
                Logging.Error("Cleaning up older version but see exception. "
                     + e.Message);
            }
            #endregion
        }

        protected override void Log(string message) {
            Utility.Log(this, "", message);
        }

        /// <summary>
        /// Routes failures to the default-on error log; see ApkContextMenu.LogError.
        /// </summary>
        protected override void LogError(string message, Exception exception) {
            ErrorLog.Error(this.GetType().Name, message, exception);
        }
    }
}
