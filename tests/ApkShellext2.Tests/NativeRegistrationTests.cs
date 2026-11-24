using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The registration side of the native handlers: the hooks regasm calls, the batch entry point
    /// behind them, and the evidence that a regasm run and the registrar describe the same
    /// installation.
    ///
    /// Every registration here lands in a sandbox under HKCU\Software\ApksS3aNative. The machine
    /// these tests run on has a real per-user installation (3.5.1.0 under D:\Software), and nothing
    /// in this file may touch it.
    /// </summary>
    [TestFixture]
    public class NativeRegistrationTests
    {
        private const string HkcrPrefix = "HKEY_CLASSES_ROOT\\";
        private const string ShellExKey = "ShellEx";
        private const string IconHandlerValue = "IconHandler";
        private const string InfoTipValue = "{00021500-0000-0000-C000-000000000046}";
        private const string ThumbnailValue = "{E357FCCD-A995-4576-B01F-234630154E96}";
        private const string ContextMenuHandlersKey = "ContextMenuHandlers";
        private const string ContextMenuHandlerName = "ApkContextMenu";
        private const string DefaultIconKey = "DefaultIcon";
        private const string DefaultIconBackupValue = "SharpShell_Backup_DefaultIcon";
        private const string DefaultIconValue = "%1";
        private const int Windows10Build = 19045;

        private static readonly string[] s_extensions =
        {
            ".apk", ".ipa", ".aab", ".appx", ".appxbundle", ".apks", ".apkm", ".xapk",
        };

        // The four class ids the product registers, hard-coded the way ShellRegistrarTests does it:
        // a mistake in ShellGuids must not be able to agree with itself here.
        private const string ContextMenuClsid = "{DCB629FC-F86F-456F-8E24-98B9B2643A9B}";
        private const string IconHandlerClsid = "{1F869CEE-4FDA-35D9-896F-43975A87D1F6}";
        private const string InfoTipClsid = "{946435A5-FE96-416D-99DB-E94EE9FB46C8}";
        private const string ThumbnailClsid = "{D747C5A7-2F66-4B7D-8301-8531838E4ED3}";

        // The class id the native menu handler carries. The registrar does not write it: a caller
        // that wants the native menu chooses that class id itself.
        private const string NativeMenuClsid = "{A8D3F2E1-6C4B-4D9A-B7F5-2E1D0C9B8A76}";

        private static readonly string[] s_clsids =
        {
            ContextMenuClsid, IconHandlerClsid, InfoTipClsid, ThumbnailClsid,
        };

        private static readonly string[] s_progIds =
        {
            "ApkShellext2.ApkContextMenu",
            "ApkShellext2.ApkIconHandler",
            "ApkShellext2.ApkInfoTipHandler",
            "ApkShellext2.ApkThumbnailHandler",
        };

        // The type that serves a class id today. The registrar writes these names as the
        // InprocServer32 "Class" value, and they are what regasm's first block says as well.
        private static readonly (string Clsid, string ServingType)[] s_servingTypes =
        {
            (ContextMenuClsid, "ApkShellext2.ApkContextMenu"),
            (IconHandlerClsid, "ApkShellext2.ApkIconHandler"),
            (InfoTipClsid, "ApkShellext2.ApkInfoTipHandler"),
            (ThumbnailClsid, "ApkShellext2.ApkThumbnailHandler"),
        };

        // The native classes and the class id / ProgId regasm finds on them.
        private static readonly (string TypeName, string Clsid)[] s_nativeHandlers =
        {
            ("ContextMenuHandler", NativeMenuClsid),
            ("IconHandlerNative", IconHandlerClsid),
            ("InfoTipHandlerNative", InfoTipClsid),
            ("ThumbnailHandlerNative", ThumbnailClsid),
        };

        private readonly List<string> _sandboxes = new List<string>();

        [TearDown]
        public void DeleteSandboxes()
        {
            using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                foreach (string root in _sandboxes)
                {
                    try
                    {
                        hkcu.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
                    }
                    catch (Exception ex)
                    {
                        TestContext.Out.WriteLine("could not delete sandbox " + root + ": " + ex.Message);
                    }
                }
            }

            _sandboxes.Clear();
        }

        //  -----------------------------------------------------------------------------------------
        //  The hooks regasm calls
        //  -----------------------------------------------------------------------------------------

        [Test]
        public void EveryNativeHandlerDeclaresTheComRegistrationHooks()
        {
            foreach ((string typeName, string clsid) in s_nativeHandlers)
            {
                Type type = ProductType("ApkShellext2.Shell.Native." + typeName);
                MethodInfo register = SingleHook(type, typeof(ComRegisterFunctionAttribute));
                MethodInfo unregister = SingleHook(type, typeof(ComUnregisterFunctionAttribute));

                Assert.That(register.Name, Is.EqualTo("Register"), typeName + " must name its register hook 'Register'.");
                Assert.That(unregister.Name, Is.EqualTo("Unregister"), typeName + " must name its unregister hook 'Unregister'.");

                foreach (MethodInfo hook in new[] { register, unregister })
                {
                    string what = typeName + "." + hook.Name;
                    Assert.That(hook.IsPublic, Is.True, what + " must be public: regasm only calls public hooks.");
                    Assert.That(hook.IsStatic, Is.True, what + " must be static: regasm calls it without an instance.");
                    Assert.That(hook.ReturnType, Is.EqualTo(typeof(void)), what + " must return void.");
                    ParameterInfo[] parameters = hook.GetParameters();
                    Assert.That(parameters.Length, Is.EqualTo(1), what + " must take the registered type.");
                    Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(Type)), what + " must take a System.Type.");
                }

                // The class ids must stay what the product registers, otherwise a regasm run writes
                // a class id nothing points at.
                Assert.That(type.GUID.ToString("B"), Is.EqualTo(clsid).IgnoreCase, typeName + " carries the wrong class id.");
            }
        }

        [Test]
        public void TheRegistrarExposesTheEntryPointsTheHooksCall()
        {
            Type registrar = ProductType("ApkShellext2.Registration.ShellRegistrar");
            Assert.That(
                registrar.GetMethod("RegisterFromComRegistration", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(Type) }, null),
                Is.Not.Null,
                "the hooks forward to ShellRegistrar.RegisterFromComRegistration(Type).");
            Assert.That(
                registrar.GetMethod("UnregisterFromComUnregistration", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(Type) }, null),
                Is.Not.Null,
                "the hooks forward to ShellRegistrar.UnregisterFromComUnregistration(Type).");
            Assert.That(
                registrar.GetMethod("RegisterAssembly", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
                Is.Not.Null,
                "the batch entry point is ShellRegistrar.RegisterAssembly.");
            Assert.That(
                registrar.GetMethod("UnregisterAssembly", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
                Is.Not.Null,
                "the batch entry point is ShellRegistrar.UnregisterAssembly.");
        }

        //  -----------------------------------------------------------------------------------------
        //  The batch entry point
        //  -----------------------------------------------------------------------------------------

        [Test]
        public void TheBatchEntryPointWritesWhatOneRegistrarCallWrites()
        {
            string viaAssembly = NewSandbox();
            string viaPath = NewSandbox();

            RegistrationRun batch = RegistrationApi.RegisterAssembly(
                "User", ProductApi.Assembly, RegistrationApi.WithBuild(RegistrationApi.Sandbox(viaAssembly), Windows10Build));
            RegistrationRun single = RegistrationApi.Register(
                "User", ProductDllPath, RegistrationApi.WithBuild(RegistrationApi.Sandbox(viaPath), Windows10Build));

            string[] expected = DumpRecords(viaPath);
            string[] actual = DumpRecords(viaAssembly);
            Assert.That(actual.Length, Is.GreaterThan(0), "the batch entry point wrote nothing.");
            Assert.That(actual, Is.EquivalentTo(expected), "registering through the assembly must write the same tree as registering a path.");

            // KeysWritten holds absolute paths, so the two sandbox roots have to come off before the
            // two runs can be compared; the tree comparison above already proves the same content.
            Assert.That(
                batch.KeysWritten.Select(key => key.Replace(viaAssembly, string.Empty)).ToList(),
                Is.EquivalentTo(single.KeysWritten.Select(key => key.Replace(viaPath, string.Empty)).ToList()),
                "both entry points must report the same keys.");
            Assert.That(batch.Scope, Is.EqualTo("User"));
        }

        [Test]
        public void UnregisteringThroughTheAssemblyRemovesWhatTheBatchWrote()
        {
            string root = NewSandbox();
            object options = RegistrationApi.WithBuild(RegistrationApi.Sandbox(root), Windows10Build);

            RegistrationApi.RegisterAssembly("User", ProductApi.Assembly, options);
            Assert.That(DumpRecords(root).Length, Is.GreaterThan(0));

            RegistrationApi.UnregisterAssembly("User", ProductApi.Assembly, options);
            Assert.That(DumpRecords(root), Is.Empty, "the batch unregister left records behind.");
        }

        [Test]
        public void TheBatchEntryPointRejectsAnAssemblyWithoutAFile()
        {
            Assembly dynamicAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                new AssemblyName("ApksS3aNoFile"), AssemblyBuilderAccess.Run);

            // A dynamic assembly has no file, and reading its Location throws instead of returning an
            // empty string, so the batch entry point has to turn that into a useful error.
            Assert.Throws<ArgumentException>(() =>
                RegistrationApi.RegisterAssembly("User", dynamicAssembly, RegistrationApi.Sandbox(NewSandbox())));
        }

        //  -----------------------------------------------------------------------------------------
        //  regasm and the registrar describe the same installation
        //  -----------------------------------------------------------------------------------------

        /// <summary>
        /// The server registration is what a regasm run and what the registrar write. Both describe
        /// the same classes, the same InprocServer32 values, the same version sub key and the same
        /// ProgId links. The assembly also contains a native class per class id next to the
        /// SharpShell class that serves it today, and regasm writes both blocks; a .reg import
        /// applies the last one, so regasm's file names the native type in the four places that
        /// record which class serves a class id, while the registrar keeps naming the class that
        /// actually runs.
        /// </summary>
        [Test]
        public void TheRegasmFileAndTheRegistrarDescribeTheSameServerRegistration()
        {
            // /regfile only writes a file: the probe in this repository showed that regasm does not
            // run the [ComRegisterFunction] for it, so this test performs no registration.
            string regFile = GenerateRegasmFile();
            try
            {
                string root = NewSandbox();
                RegistrationApi.Register("User", ProductDllPath, RegistrationApi.WithBuild(RegistrationApi.Sandbox(root), Windows10Build));

                Dictionary<string, string> fromRegasm = ServerRecords(ReadRegFile(regFile));
                Dictionary<string, string> fromRegistrar = ServerRecords(ToRecords(DumpRecords(root)));

                Assert.That(fromRegasm.Count, Is.GreaterThan(20), "the .reg file holds no server registration; the parser or regasm changed.");

                List<string> regasmOnly = fromRegasm.Keys.Except(fromRegistrar.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                List<string> registrarOnly = fromRegistrar.Keys.Except(fromRegasm.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                Assert.That(regasmOnly, Is.Empty, "regasm writes server values the registrar does not: " + string.Join("; ", regasmOnly));
                Assert.That(registrarOnly, Is.Empty, "the registrar writes server values regasm does not: " + string.Join("; ", registrarOnly));

                // The only values the two disagree on, and the values they must hold. regasm writes
                // one block per class; for a class id the native handler shares with the SharpShell
                // handler the last block names the native type in the four places that record which
                // class serves the id. The registrar keeps naming the class that runs today.
                string version = AssemblyName.GetAssemblyName(ProductDllPath).Version.ToString();
                Dictionary<string, (string Regasm, string Registrar)> expectedDiffering =
                    new Dictionary<string, (string Regasm, string Registrar)>(StringComparer.OrdinalIgnoreCase);
                foreach ((string clsid, string nativeType) in s_sharedClsids)
                {
                    string inproc = "CLSID\\" + clsid + "\\InprocServer32";
                    string progId = ServingTypeOf(clsid);
                    expectedDiffering["CLSID\\" + clsid + "::(default)"] = (nativeType, progId);
                    expectedDiffering[inproc + "::Class"] = (nativeType, progId);
                    expectedDiffering[inproc + "\\" + version + "::Class"] = (nativeType, progId);
                    expectedDiffering[progId + "::(default)"] = (nativeType, progId);
                }

                List<string> differing = fromRegistrar.Keys
                    .Where(k => !string.Equals(fromRegistrar[k], fromRegasm[k], StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                string report = string.Join(
                    Environment.NewLine,
                    differing.Select(k => k + ": regasm=" + fromRegasm[k] + " registrar=" + fromRegistrar[k]));
                Assert.That(
                    differing,
                    Is.EquivalentTo(expectedDiffering.Keys),
                    "the only values the two disagree on are the ones that name the class of a class id the native handlers share with the SharpShell handlers." +
                    Environment.NewLine + report);

                foreach (KeyValuePair<string, (string Regasm, string Registrar)> expected in expectedDiffering)
                {
                    Assert.That(fromRegasm[expected.Key], Is.EqualTo(expected.Value.Regasm), "regasm's last block names the native class: " + expected.Key);
                    Assert.That(fromRegistrar[expected.Key], Is.EqualTo(expected.Value.Registrar), "the registrar names the class that serves today: " + expected.Key);
                }

                // The native menu handler is registered by regasm under its own class id; the
                // registrar deliberately writes nothing there, so a caller can reach the native
                // class without the product advertising it.
                Assert.That(
                    ReadRegFile(regFile).Keys.Any(k => k.IndexOf(NativeMenuClsid, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.True,
                    "regasm must register the native menu handler with its own class id.");
                Assert.That(
                    DumpRecords(root).Any(r => r.IndexOf(NativeMenuClsid, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    "the registrar must not write the native menu class id.");
            }
            finally
            {
                File.Delete(regFile);
            }
        }

        /// <summary>
        /// The registrar keeps the 32 bit view in step with the 64 bit view, because a 32 bit host
        /// reads only that view. A .reg file has no mirror: it is imported into one view at a time.
        /// </summary>
        [Test]
        public void TheRegistrarMirrorsTheWholeTreeIntoTheThirtyTwoBitView()
        {
            string root = NewSandbox();
            RegistrationApi.Register("User", ProductDllPath, RegistrationApi.WithBuild(RegistrationApi.Sandbox(root), Windows10Build));

            string[] main = DumpRecords(root)
                .Where(r => !r.StartsWith("Wow6432Node\\", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string[] mirror = DumpRecords(root, @"Classes\Wow6432Node");

            Assert.That(main.Length, Is.GreaterThan(20));
            Assert.That(mirror, Is.EquivalentTo(main), "the Wow6432Node mirror must be a copy of the 64 bit tree.");
        }

        // The native handlers that carry the class id of a handler that serves today, and the type
        // regasm names for them. Both classes are in one assembly, so regasm writes two blocks for
        // one class id and a .reg import applies the last one.
        private static readonly (string Clsid, string NativeType)[] s_sharedClsids =
        {
            (IconHandlerClsid, "ApkShellext2.Shell.Native.IconHandlerNative"),
            (InfoTipClsid, "ApkShellext2.Shell.Native.InfoTipHandlerNative"),
            (ThumbnailClsid, "ApkShellext2.Shell.Native.ThumbnailHandlerNative"),
        };

        /// <summary>
        /// The association slots are what <c>install_user.bat</c> writes, and the registrar is the
        /// managed replacement for that loop. This replays the loop from the installer's own rules
        /// against one sandbox and compares it with what the registrar wrote into another.
        /// </summary>
        [Test]
        public void TheAssociationSlotsMatchThePerUserInstallScript()
        {
            string replayRoot = NewSandbox();
            string registrarRoot = NewSandbox();

            // A normal machine already points each extension at a file class but has no shell slots
            // for our handlers yet. Both sides start from that state, so the comparison covers the
            // slots and the DefaultIcon bookkeeping and nothing else.
            foreach (string extension in s_extensions)
            {
                SeedExtension(replayRoot, extension);
                SeedExtension(registrarRoot, extension);
            }

            ReplayInstallScript(replayRoot);
            RegistrationApi.Register("User", ProductDllPath, RegistrationApi.WithBuild(RegistrationApi.Sandbox(registrarRoot), Windows10Build));

            string[] expected = AssociationRecords(replayRoot);
            string[] actual = AssociationRecords(registrarRoot);
            Assert.That(expected.Length, Is.GreaterThan(20), "the replayed installer wrote no association records.");
            Assert.That(actual, Is.EquivalentTo(expected), "the association slots must be what the per-user installer writes.");
        }

        //  -----------------------------------------------------------------------------------------
        //  Helpers
        //  -----------------------------------------------------------------------------------------

        private static string ProductDllPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ApkShellext2.dll"); }
        }

        private static Type ProductType(string typeName)
        {
            Type type = ProductApi.Assembly.GetType(typeName, throwOnError: false);
            Assert.That(type, Is.Not.Null, "type " + typeName + " was not found in the product assembly.");
            return type;
        }

        private static MethodInfo SingleHook(Type type, Type attributeType)
        {
            List<MethodInfo> hooks = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.GetCustomAttributes(attributeType, inherit: false).Length > 0)
                .ToList();
            Assert.That(hooks.Count, Is.EqualTo(1), type.Name + " must declare exactly one method with " + attributeType.Name + ".");
            return hooks[0];
        }

        private static string ServingTypeOf(string clsid)
        {
            foreach ((string registered, string servingType) in s_servingTypes)
            {
                if (string.Equals(registered, clsid, StringComparison.OrdinalIgnoreCase))
                {
                    return servingType;
                }
            }

            throw new ArgumentException("no serving type is recorded for " + clsid, nameof(clsid));
        }

        private string NewSandbox()
        {
            string root = @"Software\ApksS3aNative\" + Guid.NewGuid().ToString("N");
            using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey key = hkcu.CreateSubKey(root))
            {
                Assert.That(key, Is.Not.Null, "could not create the sandbox " + root);
            }

            _sandboxes.Add(root);
            return root;
        }

        /// <summary>Every value under the sandbox's classes tree as "path::name=value", sorted.</summary>
        private static string[] DumpRecords(string root, string relativePath = "Classes")
        {
            List<string> records = new List<string>();
            using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey key = hkcu.OpenSubKey(root + "\\" + relativePath))
            {
                if (key != null)
                {
                    Walk(key, string.Empty, records);
                }
            }

            records.Sort(StringComparer.OrdinalIgnoreCase);
            return records.ToArray();
        }

        private static void Walk(RegistryKey key, string prefix, List<string> records)
        {
            foreach (string name in key.GetValueNames())
            {
                object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                records.Add(prefix + "::" + (string.IsNullOrEmpty(name) ? "(default)" : name) + "=" + value);
            }

            foreach (string subKey in key.GetSubKeyNames())
            {
                using (RegistryKey child = key.OpenSubKey(subKey))
                {
                    string childPrefix = prefix.Length == 0 ? subKey : prefix + "\\" + subKey;
                    if (child != null)
                    {
                        Walk(child, childPrefix, records);
                    }
                }
            }
        }

        /// <summary>
        /// Splits `key::name=value` records. The split point is the first '=' after the separator
        /// because a value may contain one - the Assembly value carries its own key/value pairs.
        /// </summary>
        private static Dictionary<string, string> ToRecords(string[] records)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string record in records)
            {
                int separator = record.IndexOf("::", StringComparison.Ordinal);
                int equals = record.IndexOf('=', separator);
                result[record.Substring(0, equals)] = record.Substring(equals + 1);
            }

            return result;
        }

        /// <summary>The records that describe a COM server registration, from either side.</summary>
        private static Dictionary<string, string> ServerRecords(IEnumerable<KeyValuePair<string, string>> records)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> record in records)
            {
                if (IsServerPath(PathOf(record.Key)))
                {
                    result[record.Key] = record.Value;
                }
            }

            return result;
        }

        private static bool IsServerPath(string path)
        {
            foreach (string clsid in s_clsids)
            {
                string prefix = "CLSID\\" + clsid;
                if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (string progId in s_progIds)
            {
                if (path.Equals(progId, StringComparison.OrdinalIgnoreCase) || path.StartsWith(progId + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string PathOf(string recordKey)
        {
            int separator = recordKey.IndexOf("::", StringComparison.Ordinal);
            return separator < 0 ? recordKey : recordKey.Substring(0, separator);
        }

        /// <summary>The slot and DefaultIcon records of the eight extensions, without the mirror.</summary>
        private static string[] AssociationRecords(string root)
        {
            return DumpRecords(root)
                .Where(record => !PathOf(record).StartsWith("Wow6432Node\\", StringComparison.OrdinalIgnoreCase))
                .Where(record =>
                {
                    string path = PathOf(record);
                    return path.IndexOf(@"\ShellEx", StringComparison.OrdinalIgnoreCase) >= 0
                        || path.EndsWith(@"\" + DefaultIconKey, StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
        }

        private static void SeedExtension(string root, string extension)
        {
            WriteDefaultValue(root + @"\Classes\" + extension, SeededClass(extension));
            WriteDefaultValue(root + @"\Classes\" + SeededClass(extension), "Seeded file class for " + extension);
        }

        private static string SeededClass(string extension)
        {
            return "s3a" + extension.Substring(1) + ".1";
        }

        /// <summary>
        /// The registration loop of <c>ApkShellext2\Resources\install_user.bat</c>: resolve the file
        /// class of each extension, give it the slots the handlers apply to, and point its
        /// DefaultIcon at the handler, after backing up the icon that was there before.
        /// </summary>
        private static void ReplayInstallScript(string root)
        {
            string classes = root + @"\Classes";
            foreach (string extension in s_extensions)
            {
                string className = ReadDefaultValue(classes + @"\" + extension);
                Assert.That(className, Is.Not.Null.And.Not.Empty, "the replay needs a seeded file class for " + extension);
                string classPath = classes + @"\" + className;
                string shellEx = classPath + @"\" + ShellExKey;

                WriteValue(shellEx, IconHandlerValue, IconHandlerClsid);
                if (!string.Equals(extension, ".aab", StringComparison.OrdinalIgnoreCase))
                {
                    WriteValue(shellEx, InfoTipValue, InfoTipClsid);
                    WriteDefaultValue(shellEx + @"\" + ContextMenuHandlersKey + @"\" + ContextMenuHandlerName, ContextMenuClsid);
                }

                if (string.Equals(extension, ".apk", StringComparison.OrdinalIgnoreCase))
                {
                    WriteValue(shellEx, ThumbnailValue, ThumbnailClsid);
                }

                string iconKey = classPath + @"\" + DefaultIconKey;
                string previous = ReadDefaultValue(iconKey);
                bool hasBackup = ValueExists(iconKey, DefaultIconBackupValue);
                if (!hasBackup
                    && !string.IsNullOrEmpty(previous)
                    && !string.Equals(previous, DefaultIconValue, StringComparison.OrdinalIgnoreCase))
                {
                    WriteValue(iconKey, DefaultIconBackupValue, previous);
                }

                WriteDefaultValue(iconKey, DefaultIconValue);
            }
        }

        private static string ReadDefaultValue(string keyPath)
        {
            using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey key = hkcu.OpenSubKey(keyPath))
            {
                return key == null ? null : key.GetValue(string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }

        private static bool ValueExists(string keyPath, string valueName)
        {
            using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey key = hkcu.OpenSubKey(keyPath))
            {
                return key != null && key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null;
            }
        }

        private static void WriteValue(string keyPath, string valueName, string value)
        {
            using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey key = hkcu.CreateSubKey(keyPath))
            {
                Assert.That(key, Is.Not.Null, "could not create " + keyPath);
                key.SetValue(valueName, value, RegistryValueKind.String);
            }
        }

        private static void WriteDefaultValue(string keyPath, string value)
        {
            WriteValue(keyPath, string.Empty, value);
        }

        /// <summary>
        /// The .reg file regasm writes for the product assembly. <c>/regfile</c> only records the
        /// registration in a file, and it never runs the [ComRegisterFunction] (a probe in this
        /// repository checked that), so this performs no registry write at all.
        /// </summary>
        private static string GenerateRegasmFile()
        {
            string regasm = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "regasm.exe");
            if (!File.Exists(regasm))
            {
                Assert.Ignore("regasm.exe is not next to the runtime: " + regasm);
            }

            Assert.That(File.Exists(ProductDllPath), Is.True, "the product assembly must sit next to the test assembly: " + ProductDllPath);
            string regFile = Path.Combine(Path.GetTempPath(), "apkshellext-regasm-" + Guid.NewGuid().ToString("N") + ".reg");
            ProcessStartInfo startInfo = new ProcessStartInfo(
                regasm,
                "/nologo /codebase /regfile:\"" + regFile + "\" \"" + ProductDllPath + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (Process process = Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.EqualTo(0), "regasm failed for " + ProductDllPath + ": " + output + error);
            }

            Assert.That(File.Exists(regFile), Is.True, "regasm wrote no .reg file at " + regFile);
            return regFile;
        }

        /// <summary>
        /// The .reg file as "path::name=value" records, last value wins the way a `reg import`
        /// would apply it. Only the shapes regasm emits for this assembly are handled: strings,
        /// a default value and key headers, with backslash continuation.
        /// </summary>
        private static SortedDictionary<string, string> ReadRegFile(string path)
        {
            string[] lines = File.ReadAllLines(path);
            SortedDictionary<string, string> records = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string current = null;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                while (line.EndsWith("\\", StringComparison.Ordinal) && index + 1 < lines.Length && lines[index + 1].TrimStart().StartsWith("\"", StringComparison.Ordinal))
                {
                    line = line.Substring(0, line.Length - 1) + lines[++index].Trim();
                }

                line = line.Trim();
                if (line.Length == 0 || line[0] == ';')
                {
                    continue;
                }

                if (line[0] == '[')
                {
                    int closing = line.LastIndexOf(']');
                    current = NormalizeKeyPath(line.Substring(1, closing - 1));
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                if (line[0] == '@')
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0)
                    {
                        records[current + "::(default)"] = ParseRegValue(line.Substring(equals + 1));
                    }

                    continue;
                }

                if (line[0] == '"')
                {
                    int closing = FindClosingQuote(line, 1);
                    if (closing < 0)
                    {
                        continue;
                    }

                    string name = line.Substring(1, closing - 1);
                    int equals = line.IndexOf('=', closing);
                    if (equals > 0)
                    {
                        records[current + "::" + name] = ParseRegValue(line.Substring(equals + 1));
                    }
                }
            }

            return records;
        }

        private static int FindClosingQuote(string line, int start)
        {
            for (int index = start; index < line.Length; index++)
            {
                if (line[index] == '\\')
                {
                    index++;
                    continue;
                }

                if (line[index] == '"')
                {
                    return index;
                }
            }

            return -1;
        }

        private static string ParseRegValue(string text)
        {
            text = text.Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                return text.Substring(1, text.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            return text;
        }

        private static string NormalizeKeyPath(string key)
        {
            if (key.StartsWith(HkcrPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return key.Substring(HkcrPrefix.Length);
            }

            return key;
        }

    }
}

