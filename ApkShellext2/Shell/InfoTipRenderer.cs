using System;
using ApkQuickReader;
using ApkShellext2.Diagnostics;
using ApkShellext2.Properties;

namespace ApkShellext2 {
    /// <summary>
    /// The tooltip text shared by the SharpShell handler and the native handler.
    ///
    /// The text is whatever the localised ToolTipPattern setting expands to for the package; a
    /// package that cannot be read still produces text (the "read failed" resource) because the
    /// shell draws the tooltip either way and an empty string looks like a broken handler.
    /// </summary>
    internal static class InfoTipRenderer {
        /// <summary>
        /// Returns the tooltip for the package at <paramref name="path"/>.
        /// <paramref name="module"/> names the handler in the failure log.
        /// </summary>
        internal static string Render(string path, string module) {
            try {
                Utility.Localize();
                string tipPattern = Utility.GetSetting("ToolTipPattern", NonLocalizeResources.strInfoTipDefault);
                using (IPackageReader reader = PackageReaderFactory.Create(path)) {
                    return ApkContextMenu.ReplaceVariables(tipPattern, reader);
                }
            } catch (Exception ex) {
                ErrorLog.Warn(module, "Error happened during GetInfo", ex);
                return Properties.Resources.strReadFileFailed;
            }
        }
    }
}
