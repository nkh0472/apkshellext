using System;

namespace ApkShellext2.Registration {
    /// <summary>
    /// The Windows 11 side of the registration: the verb key that points a file class at an
    /// <c>IExplorerCommand</c>, and the place the shell reads it from.
    ///
    /// The verb lives at <c>{file class}\shell\ApkShellext2</c> - the shape the shell documents for
    /// a modern verb - and carries <c>ExplorerCommandHandler</c> (our command CLSID) plus
    /// <c>MultiSelectModel=Player</c>. The classic handlers keep their <c>ShellEx</c> entries
    /// untouched: everything below build 22621, and the "show more options" list on every build,
    /// keeps using them.
    ///
    /// The gate is a build number from <c>RtlGetVersion</c> (see <see cref="WindowsVersion"/>), never
    /// <c>Environment.OSVersion</c>, and it is parameterised so tests can evaluate both sides of
    /// 22621 without a Windows 11 machine.
    /// </summary>
    internal static class Win11CommandRegistration {
        /// <summary>The CLSID the verb points at, served by <see cref="ExplorerCommandInstance"/>.</summary>
        internal const string CommandClsid = ShellGuids.ExplorerCommandClsid;

        /// <summary>
        /// True when the Windows 11 command handler should be registered for a host of
        /// <paramref name="buildNumber"/> with the given threshold. Build 0 means "the build could not
        /// be read" and keeps the classic registration only.
        /// </summary>
        internal static bool ShouldRegister(int buildNumber, int threshold) {
            return buildNumber > 0 && buildNumber >= threshold;
        }

        /// <summary>The verb key of one file class.</summary>
        internal static string VerbKey(string classesRoot, string className) {
            return classesRoot + "\\" + className + "\\" + ShellGuids.ShellKey + "\\" + ShellGuids.VerbKeyName;
        }

        /// <summary>
        /// Writes the modern verb for one file class, and clears the <c>ShellEx</c> value an earlier
        /// build put there: a class that carries both shapes would be asked for a command and a
        /// context menu handler, and the shell would have two menus for one file.
        /// </summary>
        internal static void Write(RegistryWriter writer, string classesRoot, string className, RegistrationReport report) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }
            if (string.IsNullOrEmpty(className)) {
                throw new ArgumentException("A file class name is required.", nameof(className));
            }
            string verbKey = VerbKey(classesRoot, className);
            writer.SetValue(verbKey, ShellGuids.ExplorerCommandHandlerValue, CommandClsid);
            writer.SetValue(verbKey, ShellGuids.MultiSelectModelValue, ShellGuids.MultiSelectModelPlayer);
            report.AddKey(verbKey);
            RemoveShellExValue(writer, classesRoot, className, report);
        }

        /// <summary>
        /// Removes the modern verb of one file class, but only the parts that are ours: a verb key
        /// whose command belongs to another application, and its own <c>MultiSelectModel</c>, must
        /// survive an uninstall. The <c>ShellEx</c> value of an earlier build is removed the same way.
        /// </summary>
        internal static void Remove(RegistryWriter writer, string classesRoot, string className, RegistrationReport report) {
            if (writer == null || string.IsNullOrEmpty(className)) {
                return;
            }
            string verbKey = VerbKey(classesRoot, className);
            string command = writer.GetValue(verbKey, ShellGuids.ExplorerCommandHandlerValue);
            if (command == null) {
                //  Nothing points at a command: the key can only be a leftover of ours, and dropping
                //  it takes the MultiSelectModel value with it.
                if (writer.KeyExists(verbKey) && IsEmpty(writer, verbKey)) {
                    writer.DeleteKey(verbKey);
                    report.AddKey(verbKey);
                }
            } else if (string.Equals(command, CommandClsid, StringComparison.OrdinalIgnoreCase)) {
                writer.DeleteValue(verbKey, ShellGuids.ExplorerCommandHandlerValue);
                if (string.Equals(writer.GetValue(verbKey, ShellGuids.MultiSelectModelValue),
                                  ShellGuids.MultiSelectModelPlayer, StringComparison.OrdinalIgnoreCase)) {
                    writer.DeleteValue(verbKey, ShellGuids.MultiSelectModelValue);
                }
                report.AddKey(verbKey);
                if (IsEmpty(writer, verbKey)) {
                    writer.DeleteKey(verbKey);
                    report.AddKey(verbKey);
                }
                DropEmptyShellKey(writer, classesRoot, className, report);
            }
            RemoveShellExValue(writer, classesRoot, className, report);
        }

        /// <summary>
        /// Deletes <c>{class}\ShellEx\ExplorerCommandHandler</c> when it points at our command, i.e.
        /// the value shape an earlier release wrote. Another application's value is left alone.
        /// </summary>
        private static void RemoveShellExValue(RegistryWriter writer, string classesRoot, string className, RegistrationReport report) {
            string shellEx = classesRoot + "\\" + className + "\\" + ShellGuids.ShellExKey;
            if (!writer.ValueExists(shellEx, ShellGuids.ExplorerCommandHandlerValue)) {
                return;
            }
            if (!string.Equals(writer.GetValue(shellEx, ShellGuids.ExplorerCommandHandlerValue), CommandClsid,
                               StringComparison.OrdinalIgnoreCase)) {
                return;
            }
            writer.DeleteValue(shellEx, ShellGuids.ExplorerCommandHandlerValue);
            report.AddKey(shellEx);
        }

        /// <summary>
        /// Deletes <c>{class}\shell</c> once the verb was the last thing under it. The key itself is
        /// what Explorer lists as verbs, so an empty one is noise; a class with any other verb keeps
        /// it, because those verbs are not ours.
        /// </summary>
        private static void DropEmptyShellKey(RegistryWriter writer, string classesRoot, string className, RegistrationReport report) {
            string shellKey = classesRoot + "\\" + className + "\\" + ShellGuids.ShellKey;
            if (writer.KeyExists(shellKey) && IsEmpty(writer, shellKey)) {
                writer.DeleteKey(shellKey);
                report.AddKey(shellKey);
            }
        }

        private static bool IsEmpty(RegistryWriter writer, string keyPath) {
            return writer.KeyExists(keyPath)
                && writer.GetSubKeyNames(keyPath).Length == 0
                && writer.GetValueNames(keyPath).Length == 0;
        }
    }
}
