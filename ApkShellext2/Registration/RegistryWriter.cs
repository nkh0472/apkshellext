using System;
using Microsoft.Win32;

namespace ApkShellext2.Registration {
    /// <summary>
    /// A thin wrapper over one hive/view pair that opens the base key per operation. Everything the
    /// registrar writes are <c>REG_SZ</c> values, so the writer only deals with strings.
    ///
    /// Keys are always opened through <see cref="RegistryTarget.View"/> (the 64 bit view) and the 32
    /// bit tree is addressed by its explicit <c>Wow6432Node</c> path - see
    /// <see cref="RegistryTarget"/> for the measurement behind that.
    /// </summary>
    internal sealed class RegistryWriter {
        private readonly RegistryHive _hive;
        private readonly RegistryView _view;

        internal RegistryWriter(RegistryHive hive)
            : this(hive, RegistryView.Registry64) {
        }

        internal RegistryWriter(RegistryHive hive, RegistryView view) {
            _hive = hive;
            _view = view;
        }

        internal RegistryHive Hive {
            get { return _hive; }
        }

        internal RegistryView View {
            get { return _view; }
        }

        internal bool KeyExists(string path) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = baseKey.OpenSubKey(path, false)) {
                    return key != null;
                }
            }
        }

        internal bool ValueExists(string path, string valueName) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = baseKey.OpenSubKey(path, false)) {
                    return key != null && ContainsValueName(key, valueName);
                }
            }
        }

        /// <summary>Reads a value; returns null when the key or the value does not exist.</summary>
        internal string GetValue(string path, string valueName) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = baseKey.OpenSubKey(path, false)) {
                    if (key == null) {
                        return null;
                    }
                    // Environment variables are deliberately not expanded: a backed up DefaultIcon
                    // value must be restored byte for byte.
                    object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    return value as string;
                }
            }
        }

        internal string GetDefaultValue(string path) {
            return GetValue(path, null);
        }

        internal string[] GetValueNames(string path) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = baseKey.OpenSubKey(path, false)) {
                    return key == null ? new string[0] : key.GetValueNames();
                }
            }
        }

        internal string[] GetSubKeyNames(string path) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = baseKey.OpenSubKey(path, false)) {
                    return key == null ? new string[0] : key.GetSubKeyNames();
                }
            }
        }

        internal void SetValue(string path, string valueName, string value) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = CreateKeyOrThrow(baseKey, path)) {
                    key.SetValue(valueName, value, RegistryValueKind.String);
                }
            }
        }

        internal void SetDefaultValue(string path, string value) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = CreateKeyOrThrow(baseKey, path)) {
                    key.SetValue(null, value, RegistryValueKind.String);
                }
            }
        }

        /// <summary>Creates the key (and its parents) without touching any value.</summary>
        internal void CreateKey(string path) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = CreateKeyOrThrow(baseKey, path)) {
                    // The key is created on open; nothing else to do.
                }
            }
        }

        /// <summary>Deletes a value; returns false when it did not exist.</summary>
        internal bool DeleteValue(string path, string valueName) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                using (RegistryKey key = baseKey.OpenSubKey(path, true)) {
                    if (key == null || !ContainsValueName(key, valueName)) {
                        return false;
                    }
                    key.DeleteValue(valueName, false);
                    return true;
                }
            }
        }

        internal bool DeleteDefaultValue(string path) {
            return DeleteValue(path, null);
        }

        /// <summary>Deletes a key and everything below it; a missing key is not an error.</summary>
        internal void DeleteKey(string path) {
            using (RegistryKey baseKey = OpenBaseKey()) {
                baseKey.DeleteSubKeyTree(path, false);
            }
        }

        private RegistryKey OpenBaseKey() {
            return RegistryKey.OpenBaseKey(_hive, _view);
        }

        private static RegistryKey CreateKeyOrThrow(RegistryKey baseKey, string path) {
            RegistryKey key = baseKey.CreateSubKey(path);
            if (key == null) {
                throw new InvalidOperationException("The registry key '" + path + "' could not be created.");
            }
            return key;
        }

        /// <summary>The default value has no name; <c>GetValueNames</c> reports it as an empty string.</summary>
        private static bool ContainsValueName(RegistryKey key, string valueName) {
            string wanted = valueName ?? string.Empty;
            string[] names = key.GetValueNames();
            for (int i = 0; i < names.Length; i++) {
                if (string.Equals(names[i], wanted, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }
    }
}
