using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using Microsoft.Win32;

namespace ApkShellext2.Registration {
    /// <summary>
    /// The registrar: one idempotent, symmetric implementation of what <c>regasm /codebase</c> and a
    /// <c>ComRegisterFunction</c> hook would otherwise write and remove.
    ///
    /// What one <see cref="Register"/> writes, in both class roots (the 64 bit view and its
    /// <c>Wow6432Node</c> mirror):
    /// <list type="number">
    /// <item>the four COM servers - <c>CLSID\{guid}</c> with <c>InprocServer32</c>, the version
    /// subkey, <c>ProgId</c>, <c>Implemented Categories</c>, and the <c>&lt;ProgID&gt;</c> key with its
    /// <c>CLSID</c> subkey - in the exact shape regasm produces, which
    /// <c>RegistrationTests</c> pins;</item>
    /// <item>the eight file associations, on the file class each extension points at;</item>
    /// <item>the <c>DefaultIcon</c> value with a one time backup of the previous icon;</item>
    /// <item>on a Windows 11 22H2+ host, <c>ExplorerCommandHandler</c> on the classes that received a
    /// context menu slot.</item>
    /// </list>
    /// It also retires what an install of the first generation handlers left behind - the old class
    /// key, its class id and the <c>Approved</c> entry that keeps the dead handler enabled - and
    /// unblocks the four CLSIDs in all three <c>Shell Extensions\Blocked</c> lists; for the machine
    /// scope it approves them in the 64 bit <c>Approved</c> list, because a blocked CLSID would
    /// silently disable the freshly registered handlers.
    ///
    /// <see cref="Unregister"/> removes exactly that, and only where the values still belong to us.
    /// It does not run the installers' whole registry sweep: <c>install*.bat</c> and
    /// <c>uninstall*.bat</c> keep their own logic.
    /// </summary>
    internal static class ShellRegistrar {
        /// <summary>
        /// Registers the four handlers and the eight associations. Safe to run repeatedly: a second
        /// run writes the same values and, importantly, does not replace the <c>DefaultIcon</c> backup.
        /// </summary>
        /// <param name="scope">
        /// <see cref="RegistrationScope.Machine"/> writes HKLM (64 bit and 32 bit views) and the
        /// <c>Approved</c> list; <see cref="RegistrationScope.User"/> writes HKCU (64 bit view plus its
        /// <c>Wow6432Node</c> mirror). The scope also decides whether <c>Approved</c> is written even
        /// when <paramref name="options"/> supplies a different target.
        /// </param>
        /// <param name="dllPath">The built <c>ApkShellext2.dll</c>; its assembly name and path become the
        /// registration's <c>Assembly</c> and <c>CodeBase</c> values.</param>
        /// <param name="options">Target and Windows 11 gate overrides; null means
        /// <see cref="RegistrationOptions.ForScope"/>.</param>
        internal static RegistrationReport Register(RegistrationScope scope, string dllPath, RegistrationOptions options = null) {
            if (string.IsNullOrEmpty(dllPath)) {
                throw new ArgumentException("The path of the built ApkShellext2.dll is required.", nameof(dllPath));
            }
            string fullPath = Path.GetFullPath(dllPath);
            if (!File.Exists(fullPath)) {
                throw new FileNotFoundException("The assembly to register does not exist.", fullPath);
            }
            AssemblyName assembly = AssemblyName.GetAssemblyName(fullPath);
            string assemblyFullName = assembly.FullName;
            if (string.IsNullOrEmpty(assemblyFullName)) {
                throw new InvalidOperationException("The assembly '" + fullPath + "' has no full name.");
            }
            Version version = assembly.Version ?? new Version(0, 0, 0, 0);
            string codeBase = new Uri(fullPath).AbsoluteUri;

            RegistrationOptions effective = options ?? RegistrationOptions.ForScope(scope);
            RegistryWriter writer = new RegistryWriter(effective.Target.Hive, effective.Target.View);
            RegistrationReport report = new RegistrationReport(scope, effective.EffectiveBuildNumber);

            RemoveLegacyInstallation(writer, effective.Target, report);

            foreach (string classRoot in effective.Target.ClassRoots()) {
                WriteServerRegistration(writer, classRoot, assemblyFullName, version.ToString(), codeBase, report);
                AssociationRegistrar associations = new AssociationRegistrar(writer, classRoot, effective, report);
                if (associations.Register()) {
                    report.ExplorerCommandRegistered = true;
                }
            }

            ClearBlockedEntries(scope, effective, report);
            if (scope == RegistrationScope.Machine) {
                WriteApproved(writer, effective.Target.ApprovedPath, report);
            }
            NativeMethods.NotifyAssociationsChanged();
            return report;
        }

        /// <summary>
        /// Removes the COM servers and the associations this registrar writes. Slots, icons and
        /// <c>ExplorerCommandHandler</c> values that point at somebody else's CLSID are left alone.
        /// </summary>
        internal static RegistrationReport Unregister(RegistrationScope scope, RegistrationOptions options = null) {
            RegistrationOptions effective = options ?? RegistrationOptions.ForScope(scope);
            RegistryWriter writer = new RegistryWriter(effective.Target.Hive, effective.Target.View);
            RegistrationReport report = new RegistrationReport(scope, effective.EffectiveBuildNumber);

            foreach (string classRoot in effective.Target.ClassRoots()) {
                AssociationRegistrar associations = new AssociationRegistrar(writer, classRoot, effective, report);
                associations.Unregister();
                DeleteServerRegistration(writer, classRoot, report);
            }

            if (scope == RegistrationScope.Machine) {
                RemoveApproved(writer, effective.Target.ApprovedPath, report);
            }
            NativeMethods.NotifyAssociationsChanged();
            return report;
        }

        //  -------------------------------------------------------------------------------------
        //  Assembly-level entry points and the regasm hooks
        //  -------------------------------------------------------------------------------------

        /// <summary>
        /// Registers every handler in <paramref name="assembly"/>, the way one
        /// <c>regasm</c> run registers every class in it.
        /// </summary>
        internal static RegistrationReport RegisterAssembly(RegistrationScope scope, Assembly assembly, RegistrationOptions options = null) {
            return Register(scope, AssemblyPathOf(assembly), options);
        }

        /// <summary>
        /// Removes what <see cref="RegisterAssembly"/> wrote.
        /// </summary>
        internal static RegistrationReport UnregisterAssembly(RegistrationScope scope, Assembly assembly, RegistrationOptions options = null) {
            // Unregister deletes keys, it never resolves anything from the file, so an assembly
            // that has already been replaced by a newer build still unregisters cleanly.
            AssemblyPathOf(assembly);
            return Unregister(scope, options);
        }

        /// <summary>
        /// The body of the handlers' <c>[ComRegisterFunction]</c>. regasm runs the hook only when it
        /// writes the registry itself; <c>/regfile</c> generates a file and never calls it, which is
        /// what the per-user installer relies on. A regasm run registers into HKCR, i.e. the machine
        /// hive, so the associations go next to the server registration regasm just wrote.
        ///
        /// regasm calls the hook once per class in the assembly, so this batch runs several times per
        /// install. That is deliberate: registration is idempotent and tested to be, and a
        /// process-wide "already ran" flag would be state nobody can reset between test cases.
        /// </summary>
        internal static void RegisterFromComRegistration(Type serverType) {
            if (serverType == null) {
                throw new ArgumentNullException(nameof(serverType));
            }
            RegisterAssembly(RegistrationScope.Machine, serverType.Assembly);
        }

        /// <summary>
        /// The body of the handlers' <c>[ComUnregisterFunction]</c>, mirroring
        /// <see cref="RegisterFromComRegistration"/>.
        /// </summary>
        internal static void UnregisterFromComUnregistration(Type serverType) {
            if (serverType == null) {
                throw new ArgumentNullException(nameof(serverType));
            }
            UnregisterAssembly(RegistrationScope.Machine, serverType.Assembly);
        }

        private static string AssemblyPathOf(Assembly assembly) {
            if (assembly == null) {
                throw new ArgumentNullException(nameof(assembly));
            }
            string location;
            try {
                location = assembly.Location;
            } catch (NotSupportedException) {
                // A dynamic assembly has no file, and its Location getter throws instead of
                // returning an empty string.
                location = null;
            }
            if (string.IsNullOrEmpty(location)) {
                throw new ArgumentException("The assembly '" + assembly.FullName + "' has no file location; pass the deployment path instead.", nameof(assembly));
            }
            return location;
        }

        //  -------------------------------------------------------------------------------------
        //  COM server registration (the regasm /codebase shape)
        //  -------------------------------------------------------------------------------------

        //  -------------------------------------------------------------------------------------
        //  Leftovers of the first generation install
        //  -------------------------------------------------------------------------------------

        /// <summary>
        /// Retires the class key, class id and <c>Approved</c> entry that an install of the first
        /// generation handlers left behind. The class id those handlers registered is dead, but the
        /// shell keeps offering the handler for <c>.apk</c> files for as long as the class key still
        /// resolves, and no step of the registration below ever touches these keys: the install is the
        /// only place that can remove them. A clean machine has none of them, so the pass writes
        /// nothing there. A hive this process may not write leaves the registration itself intact -
        /// the leftovers are cosmetic next to a missing handler - so a failure is logged, not thrown.
        /// </summary>
        private static void RemoveLegacyInstallation(RegistryWriter writer, RegistryTarget target, RegistrationReport report) {
            try {
                HashSet<string> classIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string classRoot in target.ClassRoots()) {
                    string classKey = classRoot + "\\" + ShellGuids.LegacyHandlerClassName;
                    // The class id is read before the key holding it is deleted; the reading works the
                    // same on a key that is about to go away as on one that is shared with something
                    // else, and an old install may have written it under either class root.
                    string classId = writer.GetDefaultValue(classKey + "\\" + ShellGuids.ClsidKey);
                    if (!string.IsNullOrEmpty(classId)) {
                        classIds.Add(classId);
                    }
                    if (writer.KeyExists(classKey)) {
                        writer.DeleteKey(classKey);
                        report.AddKey(classKey);
                    }
                }

                foreach (string classId in classIds) {
                    foreach (string classRoot in target.ClassRoots()) {
                        string classIdKey = classRoot + "\\" + ShellGuids.ClsidKey + "\\" + classId;
                        if (writer.KeyExists(classIdKey)) {
                            writer.DeleteKey(classIdKey);
                            report.AddKey(classIdKey);
                        }
                    }
                    // The shell lists approved handlers as values, but at least one early installer
                    // wrote the class id as a subkey here; remove both shapes so the dead class id
                    // cannot keep the handler enabled.
                    if (writer.DeleteValue(target.ApprovedPath, classId)) {
                        report.AddKey(target.ApprovedPath);
                    }
                    string approvedSubKey = target.ApprovedPath + "\\" + classId;
                    if (writer.KeyExists(approvedSubKey)) {
                        writer.DeleteKey(approvedSubKey);
                        report.AddKey(approvedSubKey);
                    }
                }
            } catch (UnauthorizedAccessException ex) {
                ErrorLog.Warn("ShellRegistrar", "The installation of an earlier version could not be cleaned up", ex);
            } catch (System.Security.SecurityException ex) {
                ErrorLog.Warn("ShellRegistrar", "The installation of an earlier version could not be cleaned up", ex);
            } catch (IOException ex) {
                ErrorLog.Warn("ShellRegistrar", "The installation of an earlier version could not be cleaned up", ex);
            }
        }

        private static void WriteServerRegistration(RegistryWriter writer, string classRoot, string assemblyFullName, string version, string codeBase, RegistrationReport report) {
            foreach (ShellHandler handler in ShellGuids.Handlers) {
                string clsidKey = classRoot + "\\" + ShellGuids.ClsidKey + "\\" + handler.Clsid;
                string inprocKey = clsidKey + "\\" + ShellGuids.InprocServer32Key;

                // The class and the class id name the native handler: regasm records the type full name
                // in the CLSID key's default value and in the ProgID key's default value, and the shell
                // reads InprocServer32\Class to decide which class to activate.
                writer.SetDefaultValue(clsidKey, handler.ClassName);
                writer.SetDefaultValue(inprocKey, ShellGuids.InprocServerModule);
                writer.SetValue(inprocKey, ShellGuids.ValueThreadingModel, ShellGuids.ThreadingModel);
                writer.SetValue(inprocKey, ShellGuids.ValueClass, handler.ClassName);
                writer.SetValue(inprocKey, ShellGuids.ValueAssembly, assemblyFullName);
                writer.SetValue(inprocKey, ShellGuids.ValueRuntimeVersion, ShellGuids.RuntimeVersion);
                writer.SetValue(inprocKey, ShellGuids.ValueCodeBase, codeBase);
                report.AddKey(clsidKey);

                // regasm also writes the assembly version subkey (mscoree reads it when several
                // versions of a server are installed). It carries the same four values but neither a
                // default value nor ThreadingModel - 28 keys for the four handlers in total.
                string versionKey = inprocKey + "\\" + version;
                writer.SetValue(versionKey, ShellGuids.ValueClass, handler.ClassName);
                writer.SetValue(versionKey, ShellGuids.ValueAssembly, assemblyFullName);
                writer.SetValue(versionKey, ShellGuids.ValueRuntimeVersion, ShellGuids.RuntimeVersion);
                writer.SetValue(versionKey, ShellGuids.ValueCodeBase, codeBase);
                report.AddKey(versionKey);
                RemoveStaleVersionKeys(writer, inprocKey, version, report);

                writer.SetDefaultValue(clsidKey + "\\" + ShellGuids.ProgIdKey, handler.ProgId);
                string progIdKey = classRoot + "\\" + handler.ProgId;
                writer.SetDefaultValue(progIdKey, handler.ClassName);
                writer.SetDefaultValue(progIdKey + "\\" + ShellGuids.ClsidKey, handler.Clsid);
                report.AddKey(progIdKey);

                writer.CreateKey(clsidKey + "\\" + ShellGuids.ImplementedCategoriesKey + "\\" + ShellGuids.ManagedCategoryClsid);
            }
        }

        private static void DeleteServerRegistration(RegistryWriter writer, string classRoot, RegistrationReport report) {
            foreach (ShellHandler handler in ShellGuids.Handlers) {
                string clsidKey = classRoot + "\\" + ShellGuids.ClsidKey + "\\" + handler.Clsid;
                writer.DeleteKey(clsidKey);
                report.AddKey(clsidKey);
                string progIdKey = classRoot + "\\" + handler.ProgId;
                writer.DeleteKey(progIdKey);
                report.AddKey(progIdKey);
            }
        }

        /// <summary>
        /// Keeps only the current version subkey under <c>InprocServer32</c>. mscoree resolves the
        /// assembly from whichever subkey carries its version, so an upgrade that left the previous
        /// version's subkey behind would keep loading the old <c>CodeBase</c>.
        /// </summary>
        private static void RemoveStaleVersionKeys(RegistryWriter writer, string inprocKey, string currentVersion, RegistrationReport report) {
            foreach (string subKeyName in writer.GetSubKeyNames(inprocKey)) {
                if (string.Equals(subKeyName, currentVersion, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                string staleKey = inprocKey + "\\" + subKeyName;
                writer.DeleteKey(staleKey);
                report.AddKey(staleKey);
            }
        }

        //  -------------------------------------------------------------------------------------
        //  Approved / Blocked guard lists
        //  -------------------------------------------------------------------------------------

        /// <summary>
        /// Clears the four CLSIDs from the target's <c>Shell Extensions\Blocked</c> list (and, for a
        /// machine install, its 32 bit mirror too). A blocked CLSID is skipped by the shell no matter
        /// how well it is registered, and Explorer's "disable this extension" UI is what usually put
        /// it there. A machine list that this process may not write is left alone instead of failing
        /// the registration.
        /// </summary>
        private static void ClearBlockedEntries(RegistrationScope scope, RegistrationOptions options, RegistrationReport report) {
            foreach (RegistryLocation location in options.Target.BlockedLocations(scope)) {
                RegistryWriter writer = new RegistryWriter(location.Hive, RegistryView.Registry64);
                try {
                    foreach (string clsid in ShellGuids.AllClsids()) {
                        if (writer.DeleteValue(location.Path, clsid)) {
                            report.AddKey(location.Path);
                        }
                    }
                } catch (UnauthorizedAccessException) {
                    // A per-user run cannot lift a machine level block; the elevated installer does.
                } catch (System.Security.SecurityException) {
                    // Same as above for a policy-restricted hive.
                }
            }
        }

        private static void WriteApproved(RegistryWriter writer, string approvedPath, RegistrationReport report) {
            foreach (ShellHandler handler in ShellGuids.Handlers) {
                writer.SetValue(approvedPath, handler.Clsid, handler.ProgId);
            }
            report.AddKey(approvedPath);
        }

        private static void RemoveApproved(RegistryWriter writer, string approvedPath, RegistrationReport report) {
            foreach (ShellHandler handler in ShellGuids.Handlers) {
                if (writer.DeleteValue(approvedPath, handler.Clsid)) {
                    report.AddKey(approvedPath);
                }
            }
        }
    }

    /// <summary>
    /// What one registration run touched. Used by the equivalence harness and by tests; the shell
    /// never sees it.
    /// </summary>
    internal sealed class RegistrationReport {
        private readonly List<string> _keys = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal RegistrationReport(RegistrationScope scope, int buildNumber) {
            Scope = scope;
            BuildNumber = buildNumber;
        }

        internal RegistrationScope Scope { get; }

        /// <summary>The build number the Windows 11 gate was evaluated with.</summary>
        internal int BuildNumber { get; }

        /// <summary>True when <c>ExplorerCommandHandler</c> was written.</summary>
        internal bool ExplorerCommandRegistered { get; set; }

        /// <summary>
        /// Absolute key paths this run wrote or deleted, in the order it touched them, without
        /// duplicates. A key that already held the value this run would write is not rewritten and
        /// therefore not reported, so a second identical run reports a subset of the first.
        /// </summary>
        internal IReadOnlyList<string> KeysWritten {
            get { return _keys; }
        }

        internal void AddKey(string keyPath) {
            if (string.IsNullOrEmpty(keyPath)) {
                return;
            }
            if (_seen.Add(keyPath)) {
                _keys.Add(keyPath);
            }
        }
    }
}
