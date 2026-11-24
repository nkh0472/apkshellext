using System;

namespace ApkShellext2.Registration {
    /// <summary>
    /// The Windows 11 side of the registration: the <c>ExplorerCommandHandler</c> value that points a
    /// file class at an <c>IExplorerCommand</c> implementation. Windows 11 22H2 (build 22621) is the
    /// first build whose context menu uses it; older builds ignore the value, so writing it there is
    /// pointless but harmless.
    ///
    /// The gate is a build number from <c>RtlGetVersion</c> (see <see cref="WindowsVersion"/>), never
    /// <c>Environment.OSVersion</c>, and it is parameterised so tests can evaluate both sides of
    /// 22621 without a Windows 11 machine.
    /// </summary>
    internal static class Win11CommandRegistration {
        /// <summary>
        /// The CLSID the registrar writes. No <c>IExplorerCommand</c> class is registered under it yet,
        /// so the shell fails to activate it and falls back to the classic context menu handlers -
        /// the intended behaviour on a Windows 11 host until the command class exists.
        /// </summary>
        internal const string CommandClsid = ShellGuids.ExplorerCommandClsid;

        /// <summary>
        /// True when the Windows 11 command handler should be registered for a host of
        /// <paramref name="buildNumber"/> with the given threshold. Build 0 means "the build could not
        /// be read" and keeps the classic registration only.
        /// </summary>
        internal static bool ShouldRegister(int buildNumber, int threshold) {
            return buildNumber > 0 && buildNumber >= threshold;
        }

        /// <summary>Writes <c>{class}\ShellEx\ExplorerCommandHandler</c> for one file class.</summary>
        internal static void Write(RegistryWriter writer, string classesRoot, string className, RegistrationReport report) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }
            if (string.IsNullOrEmpty(className)) {
                throw new ArgumentException("A file class name is required.", nameof(className));
            }
            string shellEx = classesRoot + "\\" + className + "\\" + ShellGuids.ShellExKey;
            writer.SetValue(shellEx, ShellGuids.ExplorerCommandHandlerValue, CommandClsid);
            report.AddKey(shellEx);
        }

        /// <summary>
        /// Removes the value, but only when it still points at our CLSID - another application's
        /// command handler on the same file class must survive an uninstall.
        /// </summary>
        internal static void Remove(RegistryWriter writer, string classesRoot, string className, RegistrationReport report) {
            if (writer == null || string.IsNullOrEmpty(className)) {
                return;
            }
            string shellEx = classesRoot + "\\" + className + "\\" + ShellGuids.ShellExKey;
            string current = writer.GetValue(shellEx, ShellGuids.ExplorerCommandHandlerValue);
            if (string.Equals(current, CommandClsid, StringComparison.OrdinalIgnoreCase)) {
                writer.DeleteValue(shellEx, ShellGuids.ExplorerCommandHandlerValue);
                report.AddKey(shellEx);
            }
        }
    }
}
