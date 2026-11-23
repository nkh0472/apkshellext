using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace ApkShellext2.Registration {
    /// <summary>
    /// Which registration tree is written: the machine wide one under HKLM (needs elevation, visible
    /// to every user) or the per user one under HKCU (no elevation, this user only).
    /// </summary>
    internal enum RegistrationScope {
        Machine,
        User
    }

    /// <summary>A registry key location: a hive plus an absolute key path.</summary>
    internal sealed class RegistryLocation {
        internal RegistryLocation(RegistryHive hive, string path) {
            Hive = hive;
            Path = path;
        }

        internal RegistryHive Hive { get; }

        internal string Path { get; }

        public override string ToString() {
            return Hive + @"\" + Path;
        }
    }

    /// <summary>
    /// The hive and class root paths one registration writes. Every registration writes <b>two</b>
    /// class roots: the 64 bit view and its <c>Wow6432Node</c> mirror.
    ///
    /// Measured (plan §5.1.1): a per user registration that only writes
    /// <c>HKCU\Software\Classes\CLSID</c> is invisible to 32 bit processes - a 32 bit
    /// <c>CoCreateInstance</c> returns <c>0x80040154</c> (REGDB_E_CLASSNOTREG) because such a process
    /// looks at <c>HKCU\Software\Classes\Wow6432Node\CLSID</c>. The 32 bit mirror therefore has to be
    /// written explicitly, and it is written <b>by path</b> through a 64 bit view (see
    /// <see cref="View"/>), so the result does not depend on the bitness of the process that runs the
    /// registrar.
    /// </summary>
    internal sealed class RegistryTarget {
        internal RegistryTarget(RegistryHive hive, string classesPath64, string classesPath32, string blockedPath, string approvedPath) {
            if (string.IsNullOrEmpty(classesPath64)) {
                throw new ArgumentException("A class root path is required.", nameof(classesPath64));
            }
            if (string.IsNullOrEmpty(classesPath32)) {
                throw new ArgumentException("A 32 bit class root path is required.", nameof(classesPath32));
            }
            if (string.IsNullOrEmpty(blockedPath)) {
                throw new ArgumentException("A Blocked key path is required.", nameof(blockedPath));
            }
            if (string.IsNullOrEmpty(approvedPath)) {
                throw new ArgumentException("An Approved key path is required.", nameof(approvedPath));
            }
            Hive = hive;
            ClassesPath64 = classesPath64;
            ClassesPath32 = classesPath32;
            BlockedPath = blockedPath;
            ApprovedPath = approvedPath;
        }

        internal RegistryHive Hive { get; }

        /// <summary>
        /// Always <see cref="RegistryView.Registry64"/>: the 32 bit tree is reached by its
        /// <c>Wow6432Node</c> path inside the 64 bit view, never by switching the view. Switching the
        /// view would make the written location depend on the bitness of the calling process.
        /// </summary>
        internal RegistryView View {
            get { return RegistryView.Registry64; }
        }

        /// <summary>Class root of the 64 bit view (<c>Software\Classes</c> / <c>SOFTWARE\Classes</c>).</summary>
        internal string ClassesPath64 { get; }

        /// <summary>Class root of the 32 bit mirror (<c>...\Classes\Wow6432Node</c>).</summary>
        internal string ClassesPath32 { get; }

        /// <summary>Where "this extension is disabled" entries live for this target.</summary>
        internal string BlockedPath { get; }

        /// <summary>Where "this extension is approved" entries live; only the machine scope writes there.</summary>
        internal string ApprovedPath { get; }

        /// <summary>The class roots to write, 64 bit first.</summary>
        internal IReadOnlyList<string> ClassRoots() {
            return new[] { ClassesPath64, ClassesPath32 };
        }

        /// <summary>Defaults for a real installation; mirrors <c>install.bat</c> (machine) and <c>install_user.bat</c> (user).</summary>
        internal static RegistryTarget ForScope(RegistrationScope scope) {
            switch (scope) {
                case RegistrationScope.Machine:
                    return new RegistryTarget(
                        RegistryHive.LocalMachine,
                        @"SOFTWARE\Classes",
                        @"SOFTWARE\WOW6432Node\Classes",
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked",
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
                case RegistrationScope.User:
                    return new RegistryTarget(
                        RegistryHive.CurrentUser,
                        @"Software\Classes",
                        @"Software\Classes\Wow6432Node",
                        @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked",
                        @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown registration scope.");
            }
        }

        /// <summary>
        /// A throw-away tree under HKCU for tests and for the equivalence harness: the same two class
        /// roots (the 32 bit one nested under the 64 bit one, exactly like the real tree) plus its own
        /// Blocked and Approved keys, so nothing touches the real registry locations.
        /// </summary>
        /// <param name="rootPath">Absolute key path below HKCU, e.g. <c>Software\ApkShellext2\TestSandbox</c>.</param>
        internal static RegistryTarget Sandbox(string rootPath) {
            if (string.IsNullOrEmpty(rootPath)) {
                throw new ArgumentException("A sandbox root path is required.", nameof(rootPath));
            }
            string root = rootPath.TrimEnd('\\');
            return new RegistryTarget(
                RegistryHive.CurrentUser,
                root + @"\Classes",
                root + @"\Classes\Wow6432Node",
                root + @"\Blocked",
                root + @"\Approved");
        }

        /// <summary>
        /// The <c>Shell Extensions\Blocked</c> lists this registration must clear: the target's own
        /// list (<c>HKCU</c> for a per-user install, <c>HKLM</c> for a machine install, the sandbox
        /// key for tests) and, for a machine install, the 32 bit mirror of the machine list.
        ///
        /// A per-user install deliberately does not try the machine lists: a blocked entry there
        /// makes a handler invisible, but a non-elevated process cannot remove it, and failing the
        /// whole registration over it would be worse than leaving it to the elevated installer.
        /// </summary>
        internal IEnumerable<RegistryLocation> BlockedLocations(RegistrationScope scope) {
            yield return new RegistryLocation(Hive, BlockedPath);
            if (scope == RegistrationScope.Machine) {
                yield return new RegistryLocation(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
            }
        }
    }
}
