using Microsoft.Win32;
using SharpShell.Attributes;
using SharpShell.Diagnostics;
using SharpShell.Extensions;
using SharpShell.ServerRegistration;
using SharpShell.SharpIconHandler;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;

namespace ApkShellext2 {
    /// <summary>
    /// This is the icon handler, not only supporting apk, but also other type of apps.
    ///
    /// The class declares no COM identity: the icon handler the shell activates is
    /// <see cref="ApkShellext2.Shell.Native.IconHandlerNative"/>, which owns the class id and the
    /// ProgID. This class stays until the SharpShell dependency is removed.
    /// </summary>
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apk")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ipa")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".aab")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".appxbundle")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".appx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apks")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apkm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xapk")]
    public class ApkIconHandler : SharpIconHandler {
        protected override Icon GetIcon(bool smallIcon, uint iconSize) {
            DbgLog.W("ICON enter " + Path.GetFileName(SelectedItemPath) + " small=" + smallIcon + " size=" + iconSize);
            return IconRenderer.Render(SelectedItemPath, iconSize, GetType().Name);
        }

        [CustomRegisterFunction]
        public static void postDoRegister(Type type, RegistrationType registrationType) {
            Console.WriteLine("Registering " + type.FullName + " Version " + type.Assembly.GetName().Version.ToString());

            #region Clean up apkshellext registry
            try {
                string oldVersionClassName = @"KKHomeProj.ApkShellExt.ApkShellExt";
                string oldVersionCLID = "";
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(oldVersionClassName + @"\CLSID")) {
                    if (key != null) {
                        Console.WriteLine("Found old version in registry, cleaning up ...");
                        oldVersionCLID = (string)key.GetValue(null);
                        key.Close();
                        Registry.ClassesRoot.DeleteSubKeyTree(oldVersionClassName);
                    }
                }
                if (oldVersionCLID != "") {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + oldVersionCLID)) {
                        if (key != null) {
                            key.Close();
                            Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Classes\CLSID\" + oldVersionCLID);
                        }
                    }
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved\" + oldVersionCLID)) {
                        if (key != null) {
                            key.Close();
                            Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved\" + oldVersionCLID);
                        }
                    }
                }
            } catch (Exception e) {
                Logging.Error("Cleaning up apkshellext remaining registry items but see exception" +
                    e.Message);
            }
            #endregion

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

            // Registration runs elevated (installer); Explorer loads the handler as the
            // interactive user. Reading HKCU at this point would read the wrong hive, so
            // no settings are restored here.

            #region Enable debug log
#if DEBUG
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE", true).CreateSubKey(@"SharpShell", RegistryKeyPermissionCheck.ReadWriteSubTree)) {
                key.SetValue("LoggingMode", 4);
                key.SetValue("LogPath", @"%AppData%\apkshellext.log",RegistryValueKind.ExpandString);
            }
#endif
            #endregion
        }

        protected override void Log(string message) {
            Utility.Log(this, Path.GetFileName(SelectedItemPath), message);
        }

        /// <summary>
        /// Routes failures to the default-on error log; see ApkContextMenu.LogError.
        /// </summary>
        protected override void LogError(string message, Exception exception) {
            ErrorLog.Error(this.GetType().Name, message, exception);
        }
    }
}
