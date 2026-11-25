using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ApkQuickReader;
using ApkShellext2.Diagnostics;
using ApkShellext2.Properties;
using ApkShellext2.Shell.Commands;

namespace ApkShellext2 {
    /// <summary>
    /// The tooltip text the info tip handler shows.
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
                    List<string> lines = new List<string>();
                    bool sawAnyToken = false;
                    foreach (string line in Regex.Split(tipPattern, "\r\n|\n|\r")) {
                        bool sawToken;
                        bool allTokenEmpty;
                        string expanded = CommandRename.ReplaceVariables(line, reader, out sawToken, out allTokenEmpty);
                        sawAnyToken |= sawToken;
                        // a line that only carried facts this package does not have is omitted,
                        // so "Size: " does not appear without a size
                        if (sawToken && allTokenEmpty) {
                            continue;
                        }
                        string trimmed = expanded.TrimEnd();
                        if (trimmed.Length == 0 && lines.Count > 0 && lines[lines.Count - 1].Length == 0) {
                            continue;
                        }
                        lines.Add(trimmed);
                    }
                    while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) {
                        lines.RemoveAt(lines.Count - 1);
                    }
                    if (lines.Count == 0) {
                        // the pattern held only facts this package does not have
                        if (sawAnyToken) {
                            return string.IsNullOrEmpty(reader.AppName) ?
                                Properties.Resources.strReadFileFailed : reader.AppName;
                        }
                        return "";
                    }
                    return string.Join("\r\n", lines);
                }
            } catch (Exception ex) {
                ErrorLog.Warn(module, "Error happened during GetInfo", ex);
                return Properties.Resources.strReadFileFailed;
            }
        }
    }
}
