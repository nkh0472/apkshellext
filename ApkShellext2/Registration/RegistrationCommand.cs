using System;
using System.IO;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;

namespace ApkShellext2.Registration {
    /// <summary>
    /// Entry point the per-user installers call once they have imported the regasm registration
    /// file into <c>HKCU\Software\Classes</c>.
    ///
    /// <c>regasm /regfile</c> only writes what regasm itself knows: the class keys and the
    /// InprocServer32 blocks. The association layer - which class serves each extension, the
    /// ShellEx slots, the <c>DefaultIcon</c> backup and the 32-bit class root mirror - is written
    /// by <see cref="ShellRegistrar"/>, which a machine-wide install reaches through the native
    /// handlers' <c>[ComRegisterFunction]</c>. A per-user install cannot register into HKCR
    /// without elevation, so it imports the regasm file and then calls this class, which runs the
    /// same registrar against the per-user class roots. Keeping it in the product assembly means
    /// the installers carry no second copy of the registry layout and no extra executable.
    /// </summary>
    [ComVisible(false)]
    public static class RegistrationCommand {
        /// <summary>
        /// Registers the handlers of the assembly at <paramref name="dllPath"/> for
        /// <paramref name="scope"/> ("User" or "Machine"). Returns 0 on success and 1 on failure,
        /// which is the installer's exit code.
        /// </summary>
        public static int Run(string scope, string dllPath) {
            try {
                RegistrationScope parsed = ParseScope(scope);
                if (string.IsNullOrEmpty(dllPath)) {
                    throw new ArgumentException("The assembly path is required.", nameof(dllPath));
                }
                string fullPath = Path.GetFullPath(dllPath);
                RegistrationReport report = ShellRegistrar.Register(parsed, fullPath);
                Console.WriteLine("ApkShellext2: registered " + report.KeysWritten.Count
                    + " keys (" + parsed + ") from " + fullPath);
                return 0;
            } catch (Exception ex) {
                ErrorLog.Error("RegistrationCommand", "Per-user registration failed.", ex);
                Console.Error.WriteLine("ApkShellext2: registration failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Removes what <see cref="Run"/> wrote for <paramref name="scope"/>: the association
        /// slots that still point at our class ids, the class keys, the file class this registrar
        /// invented and a <c>DefaultIcon</c> backup it took. Returns 0 on success and 1 on failure.
        /// </summary>
        public static int Remove(string scope) {
            try {
                RegistrationScope parsed = ParseScope(scope);
                RegistrationReport report = ShellRegistrar.Unregister(parsed);
                Console.WriteLine("ApkShellext2: removed " + report.KeysWritten.Count
                    + " keys (" + parsed + ")");
                return 0;
            } catch (Exception ex) {
                ErrorLog.Error("RegistrationCommand", "Per-user unregistration failed.", ex);
                Console.Error.WriteLine("ApkShellext2: unregistration failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// "User" and "Machine", in any casing. Anything else is a typo in an installer, not a
        /// scope, so it is rejected instead of silently registering into the machine hive.
        /// </summary>
        private static RegistrationScope ParseScope(string scope) {
            if (string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase)) {
                return RegistrationScope.User;
            }
            if (string.Equals(scope, "machine", StringComparison.OrdinalIgnoreCase)) {
                return RegistrationScope.Machine;
            }
            throw new ArgumentException("The registration scope must be User or Machine.", nameof(scope));
        }
    }
}
