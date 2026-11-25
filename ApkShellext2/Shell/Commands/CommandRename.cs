using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ApkQuickReader;
using ApkShellext2.Properties;

namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// Turns a package path into its rename target and expands the <c>%name%</c> variables of
    /// the rename pattern. Shared by the menu preview (shown while the menu is built) and the
    /// rename action itself, so the preview always matches what the action will do.
    /// </summary>
    public static class CommandRename {
        /// <summary>
        /// The full target path for <paramref name="path"/>, or an empty string when the
        /// package cannot be parsed. The result never leaves the package's own folder and is
        /// made unique by appending <c>_(n)</c> when the target already exists.
        /// </summary>
        public static string GetNewFileName(string path, IReadOnlyList<string> selectedPaths, IPackageReader reader = null) {
            string suffix = Path.GetExtension(path);
            string newFileName = "";
            string renamePattern = Utility.GetSetting("RenamePattern", NonLocalizeResources.strRenamePatternDefault);
            bool isapk = selectedPaths[0].EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
            bool isipa = selectedPaths[0].EndsWith(".ipa", StringComparison.OrdinalIgnoreCase);

            try {
                if (reader != null) {
                    // menu build already holds a parsed reader; reuse it
                    newFileName = ReplaceVariables(renamePattern, reader);
                } else {
                    using (IPackageReader ownReader = PackageReaderFactory.Create(path)) {
                        newFileName = ReplaceVariables(renamePattern, ownReader);
                    }
                }

                newFileName = Regex.Replace(newFileName, @"[\\/:*?""<>|-]+", ""); // remove invalid chars (incl. backslash: File.Move below)
                if (Utility.GetSetting("ReplaceSpace") == "True") {
                    newFileName = Regex.Replace(newFileName, @"\s+", "_");
                }
                string oldFileName = Path.GetFileName(path);
                string folderPath = Path.GetDirectoryName(path);
                // GetFileName: belt-and-braces, the rename target stays in the
                // package's own folder no matter what the pattern produced
                string tmpFileName = Path.GetFileName(newFileName) + suffix;

                int i = 0;
                while (File.Exists(folderPath + @"\" + tmpFileName) &&
                    (tmpFileName != oldFileName)) {
                    i++;
                    tmpFileName = newFileName + "_(" + i.ToString() + ")" + suffix;
                }
                newFileName = folderPath + @"\" + tmpFileName;
            } catch {
                newFileName = "";
            }
            return newFileName;
        }

        /// <summary>
        /// Replaces the <c>%variable%</c> tokens of a name pattern with values read from the
        /// package. Unknown tokens expand to an empty string; a single unpaired <c>%</c> keeps
        /// the literal text and is reported to the diagnostic log.
        /// </summary>
        public static string ReplaceVariables(string ori, IPackageReader reader) {
            string[] tokens = ori.Split(new char[] { '%' });
            string newstr = "";
            bool inpattern = false;
            foreach (string t in tokens) {
                if (!inpattern) {
                    newstr = newstr + t;
                } else {
                    string replacement = "";
                    if (t == NonLocalizeResources.varAppName)
                        replacement = reader.AppName;
                    else if (t == NonLocalizeResources.varPackage)
                        replacement = reader.PackageName;
                    else if (t == NonLocalizeResources.varPublisher)
                        replacement = reader.Publisher;
                    else if (t == NonLocalizeResources.varVersion)
                        replacement = reader.Version;
                    else if (t == NonLocalizeResources.varRevision)
                        replacement = reader.Revision;
                    else if (t == NonLocalizeResources.varFileSize)
                        replacement = Utility.getFileSize(reader.FileName);
                    else if (t == NonLocalizeResources.varMinSdk)
                        replacement = reader.MinSdk;
                    else if (t == NonLocalizeResources.varTargetSdk)
                        replacement = reader.TargetSdk;
                    else if (t == NonLocalizeResources.varPacker)
                        replacement = reader.Packer;
                    else if (t == NonLocalizeResources.varOS)
                        replacement = (reader.Type == AppPackageReader.AppType.AndroidApp ? "Android"
                            : (reader.Type == AppPackageReader.AppType.iOSApp ? "iOS"
                            : "Windows"));
                    else if (t == NonLocalizeResources.varLastModify)
                        replacement = File.GetLastWriteTime(reader.FileName).ToString("dd/MM/yy HH:mm:ss");
                    else if (t == NonLocalizeResources.varDebuggable)
                        // an unwritten flag means the platform default, which is not debuggable
                        replacement = (reader.Type == AppPackageReader.AppType.AndroidApp) ?
                            (reader.Debuggable == true ? "Debug" : "Release") : "";
                    else if (reader.Type == AppPackageReader.AppType.AndroidApp) {
                        try {
                            var apkreader = reader as ApkReader;
                            string regstr = NonLocalizeResources.varAttribute;
                            Regex rx = new Regex(regstr);
                            Match m = rx.Match(t);
                            if (m.Success) {
                                string tag = m.Groups[1].Value;
                                string attr = m.Groups[2].Value;
                                replacement = apkreader.QuickSearchManifestXml(tag, attr);
                            }
                        } catch (Exception ex) {
                            Utility.Log(null, "Replace variables", ex.Message + "\nError happens during replacing " + NonLocalizeResources.varAttribute);
                        }
                    }
                    newstr = newstr + replacement;
                }
                inpattern = !inpattern;
            }
            if (inpattern) {
                Utility.Log(null, "Replace variable", "% is not in pair.");
            }
            return newstr;
        }
    }
}
