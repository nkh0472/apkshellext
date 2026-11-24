using System;
using System.Collections.Generic;

namespace ApkShellext2.Registration {
    /// <summary>
    /// Writes and removes the file associations: which ShellEx slots each of the eight extensions
    /// gets, and the <c>DefaultIcon</c> bookkeeping.
    ///
    /// The associations belong to the <b>file class</b> an extension points at (for example
    /// <c>apk.1</c>), not to the extension key and not to our ProgIDs. Writing
    /// <c>.apk\shellex\...</c> instead is the classic mistake: the shell never loads it. The class
    /// resolution below is the same one <c>install_user.bat</c> performs: read the extension's
    /// default value, and if there is none, invent
    /// <c>&lt;ext&gt;.1</c> with the display name <c>&lt;ext&gt; Application</c> and use it.
    ///
    /// Everything here is idempotent: registering twice produces the same tree, and a second
    /// <c>DefaultIcon</c> pass never overwrites the backup of the icon that belonged to the previous
    /// application, because that backup is the only record of what owned the extension before.
    /// </summary>
    internal sealed class AssociationRegistrar {
        private readonly RegistryWriter _writer;
        private readonly string _classesRoot;
        private readonly RegistrationOptions _options;
        private readonly RegistrationReport _report;

        internal AssociationRegistrar(RegistryWriter writer, string classesRoot, RegistrationOptions options, RegistrationReport report) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }
            if (string.IsNullOrEmpty(classesRoot)) {
                throw new ArgumentException("A class root path is required.", nameof(classesRoot));
            }
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }
            if (report == null) {
                throw new ArgumentNullException(nameof(report));
            }
            _writer = writer;
            _classesRoot = classesRoot;
            _options = options;
            _report = report;
        }

        /// <summary>
        /// Writes every association slot and the DefaultIcon value. Returns true when the Windows 11
        /// command handler was written as well (the gate was open and at least one file class received
        /// a context menu slot).
        /// </summary>
        internal bool Register() {
            IReadOnlyList<ExtensionClass> classes = ResolveAll(create: true);
            List<string> contextMenuClasses = new List<string>();
            foreach (ExtensionClass item in classes) {
                WriteShellEx(item, contextMenuClasses);
                WriteDefaultIcon(item);
            }
            if (!_options.ShouldRegisterExplorerCommand || contextMenuClasses.Count == 0) {
                return false;
            }
            foreach (string className in contextMenuClasses) {
                Win11CommandRegistration.Write(_writer, _classesRoot, className, _report);
            }
            return true;
        }

        /// <summary>
        /// Removes what <see cref="Register"/> wrote: only the slots that still point at our CLSIDs,
        /// only a DefaultIcon backup we created, and only a file class that is provably ours and now
        /// unused. Another application's slots, icon and verbs survive.
        ///
        /// This is narrower than <c>uninstall_user.bat</c>'s sweep (no <c>reg query</c> over the whole
        /// CLSID tree, no <c>Classes\Record</c> cleanup): the registrar removes what it wrote, and the
        /// installers keep their own sweep.
        /// </summary>
        internal void Unregister() {
            foreach (ExtensionClass item in ResolveAll(create: false)) {
                RemoveShellEx(item);
                Win11CommandRegistration.Remove(_writer, _classesRoot, item.ClassName, _report);
                RestoreDefaultIcon(item);
                DropEmptyShellKeys(item);
                CleanUpInventedClass(item);
            }
        }

        //  -------------------------------------------------------------------------------------
        //  Class resolution
        //  -------------------------------------------------------------------------------------

        private string ClassPath(string className) {
            return _classesRoot + "\\" + className;
        }

        private IReadOnlyList<ExtensionClass> ResolveAll(bool create) {
            List<ExtensionClass> result = new List<ExtensionClass>(ShellGuids.Extensions.Length);
            foreach (string extension in ShellGuids.Extensions) {
                string className = Resolve(extension, create);
                if (!string.IsNullOrEmpty(className)) {
                    result.Add(new ExtensionClass(extension, className));
                }
            }
            return result;
        }

        /// <summary>
        /// The file class an extension uses. With <paramref name="create"/> false a missing association
        /// simply yields null, which is what unregistration needs.
        /// </summary>
        private string Resolve(string extension, bool create) {
            string extensionKey = _classesRoot + "\\" + extension;
            string className = _writer.GetDefaultValue(extensionKey);
            if (!string.IsNullOrEmpty(className)) {
                return className;
            }
            if (!create) {
                return null;
            }
            className = ShellGuids.DefaultClassForExtension(extension);
            string classPath = ClassPath(className);
            if (!_writer.KeyExists(classPath)) {
                _writer.SetDefaultValue(classPath, ShellGuids.DefaultClassDisplayName(extension));
                _report.AddKey(classPath);
            }
            _writer.SetDefaultValue(extensionKey, className);
            _report.AddKey(extensionKey);
            return className;
        }

        //  -------------------------------------------------------------------------------------
        //  Association slots
        //  -------------------------------------------------------------------------------------

        private void WriteShellEx(ExtensionClass item, List<string> contextMenuClasses) {
            string shellEx = ClassPath(item.ClassName) + "\\" + ShellGuids.ShellExKey;
            foreach (ShellHandler handler in ShellGuids.Handlers) {
                if (!ShellGuids.AppliesTo(handler.Kind, item.Extension)) {
                    continue;
                }
                switch (handler.Kind) {
                    case ShellHandlerKind.ContextMenu:
                        string contextMenuKey = shellEx + "\\" + ShellGuids.ContextMenuHandlersKey + "\\" + ShellGuids.ContextMenuHandlerName;
                        _writer.SetDefaultValue(contextMenuKey, handler.Clsid);
                        _report.AddKey(contextMenuKey);
                        if (!contextMenuClasses.Contains(item.ClassName)) {
                            contextMenuClasses.Add(item.ClassName);
                        }
                        break;
                    case ShellHandlerKind.IconHandler:
                    case ShellHandlerKind.InfoTip:
                    case ShellHandlerKind.Thumbnail:
                        WriteHandlerSlot(shellEx, ShellGuids.SlotName(handler.Kind), handler.Clsid);
                        break;
                }
            }
        }

        /// <summary>
        /// Writes one handler slot the way the shell reads it: <c>ShellEx\&lt;name&gt;</c> as a key whose
        /// default value is the CLSID. The same name directly on <c>ShellEx</c> as a value is the shape an
        /// earlier version wrote; the shell ignores that shape, so it is cleared here as well - but only
        /// while it still names our CLSID.
        /// </summary>
        private void WriteHandlerSlot(string shellEx, string slotName, string clsid) {
            string slotKey = shellEx + "\\" + slotName;
            _writer.SetDefaultValue(slotKey, clsid);
            _report.AddKey(slotKey);
            if (IsOurs(_writer.GetValue(shellEx, slotName), clsid)) {
                _writer.DeleteValue(shellEx, slotName);
                _report.AddKey(shellEx);
            }
        }

        private void RemoveShellEx(ExtensionClass item) {
            string shellEx = ClassPath(item.ClassName) + "\\" + ShellGuids.ShellExKey;
            RemoveSlot(shellEx, ShellGuids.IconHandlerSlotName, ShellGuids.IconHandlerClsid);
            RemoveSlot(shellEx, ShellGuids.InfoTipSlotName, ShellGuids.InfoTipClsid);
            RemoveSlot(shellEx, ShellGuids.ThumbnailSlotName, ShellGuids.ThumbnailClsid);
            string contextMenuKey = shellEx + "\\" + ShellGuids.ContextMenuHandlersKey + "\\" + ShellGuids.ContextMenuHandlerName;
            if (_writer.DeleteKeyIfDefaultValue(contextMenuKey, ShellGuids.ContextMenuClsid)) {
                _report.AddKey(contextMenuKey);
            }
        }

        /// <summary>
        /// Removes a handler slot in either shape it can exist in: the subkey a current install writes,
        /// and the value an earlier version wrote. Only the shape that still names our CLSID is touched,
        /// so another application's slot survives.
        /// </summary>
        private void RemoveSlot(string shellEx, string slotName, string clsid) {
            RemoveValueIfOurs(shellEx, slotName, clsid);
            string slotKey = shellEx + "\\" + slotName;
            if (_writer.DeleteKeyIfDefaultValue(slotKey, clsid)) {
                _report.AddKey(slotKey);
            }
        }

        private bool RemoveValueIfOurs(string keyPath, string valueName, string expectedClsid) {
            if (!IsOurs(_writer.GetValue(keyPath, valueName), expectedClsid)) {
                return false;
            }
            _writer.DeleteValue(keyPath, valueName);
            _report.AddKey(keyPath);
            return true;
        }

        private static bool IsOurs(string registeredValue, string expectedClsid) {
            return string.Equals(registeredValue, expectedClsid, StringComparison.OrdinalIgnoreCase);
        }

        //  -------------------------------------------------------------------------------------
        //  DefaultIcon
        //  -------------------------------------------------------------------------------------

        private void WriteDefaultIcon(ExtensionClass item) {
            string defaultIconKey = ClassPath(item.ClassName) + "\\" + ShellGuids.DefaultIconKey;
            string currentIcon = _writer.GetDefaultValue(defaultIconKey);
            bool hasBackup = _writer.ValueExists(defaultIconKey, ShellGuids.DefaultIconBackupValueName);
            // Back up only once, and never back up our own value: a second Register must not turn
            // "%1" into the "original" icon.
            bool needsBackup = !hasBackup
                && !string.IsNullOrEmpty(currentIcon)
                && !string.Equals(currentIcon, ShellGuids.DefaultIconValue, StringComparison.OrdinalIgnoreCase);
            if (needsBackup) {
                _writer.SetValue(defaultIconKey, ShellGuids.DefaultIconBackupValueName, currentIcon);
            }
            _writer.SetDefaultValue(defaultIconKey, ShellGuids.DefaultIconValue);
            _report.AddKey(defaultIconKey);
        }

        private void RestoreDefaultIcon(ExtensionClass item) {
            string defaultIconKey = ClassPath(item.ClassName) + "\\" + ShellGuids.DefaultIconKey;
            string backup = _writer.GetValue(defaultIconKey, ShellGuids.DefaultIconBackupValueName);
            if (!string.IsNullOrEmpty(backup)) {
                _writer.SetDefaultValue(defaultIconKey, backup);
                _writer.DeleteValue(defaultIconKey, ShellGuids.DefaultIconBackupValueName);
                _report.AddKey(defaultIconKey);
                return;
            }
            string current = _writer.GetDefaultValue(defaultIconKey);
            if (!string.Equals(current, ShellGuids.DefaultIconValue, StringComparison.OrdinalIgnoreCase)) {
                return;
            }
            // No backup: the value is ours, drop it. The key goes too when nothing else lives there,
            // so a later uninstall pass can recognise an unused class (the shell has no use for an
            // empty DefaultIcon key).
            if (HasOnlyTheDefaultValue(defaultIconKey)) {
                _writer.DeleteKey(defaultIconKey);
            } else {
                _writer.DeleteDefaultValue(defaultIconKey);
            }
            _report.AddKey(defaultIconKey);
        }

        //  -------------------------------------------------------------------------------------
        //  Cleanup of the class shell keys and of a class we invented
        //  -------------------------------------------------------------------------------------

        private void DropEmptyShellKeys(ExtensionClass item) {
            string shellEx = ClassPath(item.ClassName) + "\\" + ShellGuids.ShellExKey;
            string contextMenuHandlers = shellEx + "\\" + ShellGuids.ContextMenuHandlersKey;
            if (_writer.KeyExists(contextMenuHandlers) && IsEmpty(contextMenuHandlers)) {
                _writer.DeleteKey(contextMenuHandlers);
                _report.AddKey(contextMenuHandlers);
            }
            if (_writer.KeyExists(shellEx) && IsEmpty(shellEx)) {
                _writer.DeleteKey(shellEx);
                _report.AddKey(shellEx);
            }
        }

        /// <summary>
        /// Deletes a file class this registrar invented (<c>&lt;ext&gt;.1</c> with the display name
        /// <c>&lt;ext&gt; Application</c>) once nothing references it any more, then clears the
        /// extension's default value if it still points there.
        ///
        /// Stricter than <c>uninstall_user.bat</c>'s <c>:cleancls</c> in one respect: the class must
        /// have no subkeys left at all, so a class another tool has since extended with verbs stays.
        /// </summary>
        private void CleanUpInventedClass(ExtensionClass item) {
            string classPath = ClassPath(item.ClassName);
            if (!_writer.KeyExists(classPath)) {
                return;
            }
            string classDefault = _writer.GetDefaultValue(classPath);
            bool invented = string.Equals(classDefault, ShellGuids.DefaultClassDisplayName(item.Extension), StringComparison.OrdinalIgnoreCase);
            bool shellExGone = !_writer.KeyExists(classPath + "\\" + ShellGuids.ShellExKey);
            bool defaultIconGone = !_writer.KeyExists(classPath + "\\" + ShellGuids.DefaultIconKey);
            bool nothingElseLeft = _writer.GetSubKeyNames(classPath).Length == 0
                && _writer.GetValueNames(classPath).Length == 1; // only the Default value
            if (!invented || !shellExGone || !defaultIconGone || !nothingElseLeft) {
                return;
            }
            _writer.DeleteKey(classPath);
            _report.AddKey(classPath);
            string extensionKey = _classesRoot + "\\" + item.Extension;
            string extensionDefault = _writer.GetDefaultValue(extensionKey);
            if (string.Equals(extensionDefault, item.ClassName, StringComparison.OrdinalIgnoreCase)) {
                _writer.DeleteDefaultValue(extensionKey);
                _report.AddKey(extensionKey);
            }
        }

        private bool IsEmpty(string keyPath) {
            return _writer.GetSubKeyNames(keyPath).Length == 0 && _writer.GetValueNames(keyPath).Length == 0;
        }

        /// <summary>
        /// True when the key has no subkeys and nothing but its default value - the state a
        /// <c>DefaultIcon</c> key is in when all it ever held was our <c>%1</c>.
        /// </summary>
        private bool HasOnlyTheDefaultValue(string keyPath) {
            if (_writer.GetSubKeyNames(keyPath).Length != 0) {
                return false;
            }
            foreach (string valueName in _writer.GetValueNames(keyPath)) {
                if (!string.IsNullOrEmpty(valueName)) {
                    return false;
                }
            }
            return true;
        }

        private sealed class ExtensionClass {
            internal ExtensionClass(string extension, string className) {
                Extension = extension;
                ClassName = className;
            }

            internal string Extension { get; }

            internal string ClassName { get; }
        }
    }
}
