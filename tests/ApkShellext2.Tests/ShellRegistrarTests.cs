using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins what the self-hosted registrar writes: the regasm-equivalent COM registration, the
    /// per-user association slots of install_user.bat, the 32 bit mirror that fixes the
    /// 0x80040154 bug that a per-user install hits without the WOW6432Node mirror, the DefaultIcon backup rule, idempotency, the Win11 build
    /// gate and a symmetric Unregister.
    ///
    /// Every test runs against a sandbox root under HKCU\Software\ApkShellext2\RegistrationTests,
    /// never against a real installation, and reads the result back with Microsoft.Win32.Registry
    /// so the assertions compare against hard-coded key paths and GUIDs rather than against the
    /// product's own ShellGuids table.
    /// </summary>
    [TestFixture]
    internal sealed class ShellRegistrarTests
    {
        private const string ContextMenuClsid = "{DCB629FC-F86F-456F-8E24-98B9B2643A9B}";
        private const string IconHandlerClsid = "{1F869CEE-4FDA-35D9-896F-43975A87D1F6}";
        private const string InfoTipClsid = "{946435A5-FE96-416D-99DB-E94EE9FB46C8}";
        private const string ThumbnailClsid = "{D747C5A7-2F66-4B7D-8301-8531838E4ED3}";
        private const string ContextMenuProgId = "ApkShellext2.ApkContextMenu";
        private const string IconHandlerProgId = "ApkShellext2.ApkIconHandler";
        private const string InfoTipProgId = "ApkShellext2.ApkInfoTipHandler";
        private const string ThumbnailProgId = "ApkShellext2.ApkThumbnailHandler";
        private const string ManagedCategoryClsid = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}";
        private const string InfoTipSlotName = "{00021500-0000-0000-C000-000000000046}";
        private const string ThumbnailSlotName = "{e357fccd-a995-4576-b01f-234630154e96}";
        private const string IconHandlerSlotName = "IconHandler";
        private const string ExplorerCommandClsid = "{0B6C1F2A-7D34-4E5B-9A18-3C2D4E5F6071}";
        private const string DefaultIconBackupValueName = "SharpShell_Backup_DefaultIcon";
        private const int Windows10Build = 19045;
        private const int Windows11Build = 22621;

        private static readonly string[] s_extensions =
        {
            ".apk", ".ipa", ".aab", ".appx", ".appxbundle", ".apks", ".apkm", ".xapk"
        };

        private static readonly (string Clsid, string ProgId)[] s_handlers =
        {
            (ContextMenuClsid, ContextMenuProgId),
            (IconHandlerClsid, IconHandlerProgId),
            (InfoTipClsid, InfoTipProgId),
            (ThumbnailClsid, ThumbnailProgId)
        };

        // The native handler that declares each class id: the type mscoree activates, which is what
        // regasm records in the CLSID key's default value, InprocServer32\Class and the ProgID key.
        private static readonly (string Clsid, string NativeType)[] s_nativeTypes =
        {
            (ContextMenuClsid, "ApkShellext2.Shell.Native.ContextMenuHandler"),
            (IconHandlerClsid, "ApkShellext2.Shell.Native.IconHandlerNative"),
            (InfoTipClsid, "ApkShellext2.Shell.Native.InfoTipHandlerNative"),
            (ThumbnailClsid, "ApkShellext2.Shell.Native.ThumbnailHandlerNative")
        };

        private static string NativeTypeOf(string clsid)
        {
            foreach ((string registered, string nativeType) in s_nativeTypes)
            {
                if (string.Equals(registered, clsid, StringComparison.OrdinalIgnoreCase))
                {
                    return nativeType;
                }
            }

            Assert.Fail("no native handler is recorded for " + clsid);
            return null;
        }

        private string _sandboxRoot;

        private string _classesRoot
        {
            get { return _sandboxRoot + @"\Classes"; }
        }

        private string _mirrorRoot
        {
            get { return _classesRoot + @"\Wow6432Node"; }
        }

        private string _blockedRoot
        {
            get { return _sandboxRoot + @"\Blocked"; }
        }

        private string _approvedRoot
        {
            get { return _sandboxRoot + @"\Approved"; }
        }

        private static string ProductDllPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ApkShellext2.dll"); }
        }

        [SetUp]
        public void CreateSandbox()
        {
            _sandboxRoot = @"Software\ApkShellext2\RegistrationTests\" + Guid.NewGuid().ToString("N");
            Assert.That(File.Exists(ProductDllPath), Is.True, "the product assembly must sit next to the test assembly: " + ProductDllPath);
        }

        [TearDown]
        public void DeleteSandbox()
        {
            using (RegistryKey baseKey = OpenBaseKey())
            {
                if (baseKey.OpenSubKey(_sandboxRoot) != null)
                {
                    baseKey.DeleteSubKeyTree(_sandboxRoot);
                }
            }
        }

        private static RegistryKey OpenBaseKey()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        }

        private object SandboxOptions()
        {
            return RegistrationApi.Sandbox(_sandboxRoot);
        }

        private RegistrationRun Register(string scope = "User", object options = null)
        {
            return RegistrationApi.Register(scope, ProductDllPath, options ?? SandboxOptions());
        }

        private RegistrationRun RegisterWithBuild(int build, string scope = "User")
        {
            return RegistrationApi.Register(scope, ProductDllPath, RegistrationApi.WithBuild(SandboxOptions(), build));
        }

        private string Default(string relativePath, string root = null)
        {
            return ReadValue(root ?? _classesRoot, relativePath, string.Empty);
        }

        private string Value(string relativePath, string valueName, string root = null)
        {
            return ReadValue(root ?? _classesRoot, relativePath, valueName);
        }

        private string ReadValue(string classesRoot, string relativePath, string valueName)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.OpenSubKey(Combine(classesRoot, relativePath)))
            {
                return key == null ? null : key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }

        private bool KeyExists(string relativePath, string root = null)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.OpenSubKey(Combine(root ?? _classesRoot, relativePath)))
            {
                return key != null;
            }
        }

        private string[] SubKeyNames(string relativePath, string root = null)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.OpenSubKey(Combine(root ?? _classesRoot, relativePath)))
            {
                return key == null ? new string[0] : key.GetSubKeyNames();
            }
        }

        private string[] ValueNames(string relativePath, string root = null)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.OpenSubKey(Combine(root ?? _classesRoot, relativePath)))
            {
                return key == null ? new string[0] : key.GetValueNames();
            }
        }

        private static string Combine(string root, string relativePath)
        {
            return string.IsNullOrEmpty(relativePath) ? root : root + @"\" + relativePath;
        }

        private bool IsSubKey(string relativePath, string subKeyName)
        {
            return SubKeyNames(relativePath).Any(name => string.Equals(name, subKeyName, StringComparison.OrdinalIgnoreCase));
        }

        private void SeedDefault(string relativePath, string value)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.CreateSubKey(_classesRoot + @"\" + relativePath))
            {
                key.SetValue(string.Empty, value, RegistryValueKind.String);
            }
        }

        private void EnsureKey(string relativePath)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.CreateSubKey(_classesRoot + @"\" + relativePath))
            {
                Assert.That(key, Is.Not.Null);
            }
        }

        private void SeedValue(string relativePath, string valueName, string value)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.CreateSubKey(_classesRoot + @"\" + relativePath))
            {
                key.SetValue(valueName, value, RegistryValueKind.String);
            }
        }

        private void SeedAbsoluteValue(string absolutePath, string valueName, string value)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.CreateSubKey(absolutePath))
            {
                key.SetValue(valueName, value, RegistryValueKind.String);
            }
        }

        /// <summary>Every value in the classes tree as "relative\key\path :: name = value", sorted, for tree equality assertions.</summary>
        private string[] DumpValues(string root = null)
        {
            List<string> records = new List<string>();
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.OpenSubKey(root ?? _classesRoot))
            {
                if (key != null)
                {
                    Walk(key, root ?? _classesRoot, records);
                }
            }

            records.Sort(StringComparer.Ordinal);
            return records.ToArray();
        }

        private static void Walk(RegistryKey key, string prefix, List<string> records)
        {
            foreach (string name in key.GetValueNames())
            {
                string display = string.IsNullOrEmpty(name) ? "(default)" : name;
                object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                records.Add(prefix + "::" + display + "=" + value);
            }

            foreach (string sub in key.GetSubKeyNames())
            {
                using (RegistryKey child = key.OpenSubKey(sub))
                {
                    Walk(child, prefix + @"\" + sub, records);
                }
            }
        }

        [Test]
        public void RegisterCreatesEveryHandlerClsidInBothViews()
        {
            Register();

            foreach ((string clsid, string progId) in s_handlers)
            {
                string clsidKey = @"CLSID\" + clsid;
                string nativeType = NativeTypeOf(clsid);
                Assert.That(Default(clsidKey), Is.EqualTo(nativeType), "the CLSID key's default value is the native handler");
                Assert.That(
                    Default(clsidKey, _mirrorRoot),
                    Is.EqualTo(nativeType),
                    "without the Wow6432Node mirror a 32 bit process gets REGDB_E_CLASSNOTREG (0x80040154) from CoCreateInstance");
                Assert.That(Default(clsidKey + @"\ProgId"), Is.EqualTo(progId));
                Assert.That(SubKeyNames(clsidKey + @"\Implemented Categories"), Does.Contain(ManagedCategoryClsid));
            }
        }

        [Test]
        public void RegisterWritesTheRegasmInprocServer32Values()
        {
            Register();

            AssemblyName assembly = AssemblyName.GetAssemblyName(ProductDllPath);
            string inproc = @"CLSID\" + IconHandlerClsid + @"\InprocServer32";
            Assert.That(Default(inproc), Is.EqualTo("mscoree.dll"), "the managed server is activated through mscoree.dll");
            Assert.That(Value(inproc, "Class"), Is.EqualTo(NativeTypeOf(IconHandlerClsid)));
            Assert.That(Value(inproc, "Assembly"), Is.EqualTo(assembly.FullName));
            Assert.That(Value(inproc, "RuntimeVersion"), Is.EqualTo("v4.0.30319"));
            Assert.That(Value(inproc, "ThreadingModel"), Is.EqualTo("Both"), "the shipping handlers are Both; Apartment would break Explorer's reentrant calls");
            Assert.That(Value(inproc, "CodeBase"), Is.EqualTo(new Uri(ProductDllPath).AbsoluteUri));
            Assert.That(Value(inproc, "Assembly", _mirrorRoot), Is.EqualTo(assembly.FullName), "the mirror carries the same server registration");
        }

        [Test]
        public void RegisterWritesTheVersionSubKeyWithoutADefaultValue()
        {
            Register();

            string version = AssemblyName.GetAssemblyName(ProductDllPath).Version.ToString();
            string versionKey = @"CLSID\" + IconHandlerClsid + @"\InprocServer32\" + version;
            Assert.That(KeyExists(versionKey), Is.True, "regasm also writes the assembly version subkey");
            Assert.That(
                ValueNames(versionKey),
                Is.EquivalentTo(new[] { "Class", "Assembly", "RuntimeVersion", "CodeBase" }),
                "the version subkey carries exactly these four values - no default value, no ThreadingModel");
        }

        [Test]
        public void RegisterWritesTheProgIdKeysInBothDirections()
        {
            Register();

            foreach ((string clsid, string progId) in s_handlers)
            {
                string nativeType = NativeTypeOf(clsid);
                Assert.That(Default(progId), Is.EqualTo(nativeType), "regasm records the class name as the ProgID key's default value");
                Assert.That(Default(progId + @"\CLSID"), Is.EqualTo(clsid));
                Assert.That(Default(progId, _mirrorRoot), Is.EqualTo(nativeType), "the ProgID must exist in the 32 bit view too");
                Assert.That(Default(progId + @"\CLSID", _mirrorRoot), Is.EqualTo(clsid));
            }
        }

        [Test]
        public void RegisterWritesTheAssociationSlotsForEveryExtension()
        {
            Register();

            foreach (string extension in s_extensions)
            {
                string name = extension.Substring(1);
                string className = name + ".1";
                string shellEx = className + @"\ShellEx";

                Assert.That(Default(extension), Is.EqualTo(className), "an extension without a ProgID gets the installer's <name>.1 class");
                Assert.That(Default(className), Is.EqualTo(name + " Application"));
                Assert.That(Default(shellEx + @"\" + IconHandlerSlotName), Is.EqualTo(IconHandlerClsid), "all eight extensions get the icon handler");
                Assert.That(IsSubKey(shellEx, IconHandlerSlotName), Is.True, "a handler slot is a subkey, the shape the shell reads");
                Assert.That(Value(shellEx, IconHandlerSlotName), Is.Null, "the value shape of a slot is ignored by the shell");

                if (extension == ".aab")
                {
                    Assert.That(Value(shellEx, InfoTipSlotName), Is.Null, "install_user.bat skips InfoTip and ContextMenu for .aab");
                    Assert.That(Default(shellEx + @"\" + InfoTipSlotName), Is.Null);
                    Assert.That(Default(shellEx + @"\ContextMenuHandlers\ApkContextMenu"), Is.Null);
                }
                else
                {
                    Assert.That(Default(shellEx + @"\" + InfoTipSlotName), Is.EqualTo(InfoTipClsid));
                    Assert.That(IsSubKey(shellEx, InfoTipSlotName), Is.True);
                    Assert.That(Default(shellEx + @"\ContextMenuHandlers\ApkContextMenu"), Is.EqualTo(ContextMenuClsid));
                }

                if (extension == ".apk")
                {
                    Assert.That(Default(shellEx + @"\" + ThumbnailSlotName), Is.EqualTo(ThumbnailClsid), "only .apk gets the thumbnail provider");
                    Assert.That(IsSubKey(shellEx, ThumbnailSlotName), Is.True);
                }
                else
                {
                    Assert.That(Value(shellEx, ThumbnailSlotName), Is.Null);
                    Assert.That(Default(shellEx + @"\" + ThumbnailSlotName), Is.Null);
                }

                Assert.That(Default(className + @"\DefaultIcon"), Is.EqualTo("%1"), "the handler's own icon replaces the extension icon");
                Assert.That(Default(shellEx + @"\" + IconHandlerSlotName, _mirrorRoot), Is.EqualTo(IconHandlerClsid), "the association slots are mirrored for 32 bit hosts");
            }
        }

        [Test]
        public void RegisterKeepsAnExistingExtensionDefaultAndUsesItsClass()
        {
            SeedDefault(".apk", "myclass.apk");
            SeedDefault("myclass.apk", "myclass");

            Register();

            Assert.That(Default(".apk"), Is.EqualTo("myclass.apk"), "an extension that already has a ProgID keeps it");
            Assert.That(Default(@"myclass.apk\ShellEx\IconHandler"), Is.EqualTo(IconHandlerClsid));
            Assert.That(KeyExists("apk.1"), Is.False, "no invented class when the extension already points at one");
        }

        [Test]
        public void RegisterCreatesTheDefaultClassForAnExtensionWithoutOne()
        {
            EnsureKey(".apk");

            Register();

            Assert.That(Default(".apk"), Is.EqualTo("apk.1"));
            Assert.That(Default("apk.1"), Is.EqualTo("apk Application"));
        }

        [Test]
        public void RegisterBacksUpAnExistingDefaultIconOnlyOnce()
        {
            SeedDefault("apk.1", "apk Application");
            SeedDefault(@"apk.1\DefaultIcon", "old.ico");

            Register();

            Assert.That(Default(@"apk.1\DefaultIcon"), Is.EqualTo("%1"));
            Assert.That(Value(@"apk.1\DefaultIcon", DefaultIconBackupValueName), Is.EqualTo("old.ico"), "the previous icon is saved before it is replaced");

            Register();

            Assert.That(
                Value(@"apk.1\DefaultIcon", DefaultIconBackupValueName),
                Is.EqualTo("old.ico"),
                "a repeated run must not overwrite the backup with the placeholder it wrote itself");
        }

        [Test]
        public void RegisterDoesNotBackUpTheDefaultIconWhenItIsAlreadyThePlaceholder()
        {
            SeedDefault("apk.1", "apk Application");
            SeedDefault(@"apk.1\DefaultIcon", "%1");

            Register();

            Assert.That(Value(@"apk.1\DefaultIcon", DefaultIconBackupValueName), Is.Null, "backing up %1 would make Unregister restore a placeholder");
            Assert.That(Default(@"apk.1\DefaultIcon"), Is.EqualTo("%1"));
        }

        [Test]
        public void RegisterRemovesStaleVersionSubKeys()
        {
            SeedValue(@"CLSID\" + IconHandlerClsid + @"\InprocServer32\1.0.0.0", "Class", "Old.Namespace.OldHandler");

            Register();

            Assert.That(
                KeyExists(@"CLSID\" + IconHandlerClsid + @"\InprocServer32\1.0.0.0"),
                Is.False,
                "mscoree resolves the assembly from whichever version subkey it finds, so an upgrade must not leave the previous one behind");
        }

        /// <summary>
        /// An install of the first generation handlers left its class key behind. The class id it
        /// points at has no server any more, but the shell keeps offering the dead handler for as long
        /// as the class key resolves, so the install removes the class key, the orphaned class id and
        /// the <c>Approved</c> entry - in both class roots, because regasm wrote the 32 bit one too.
        /// </summary>
        [Test]
        public void RegisterRemovesTheClassKeyOfAnEarlierVersion()
        {
            const string legacyClass = "KKHomeProj.ApkShellExt.ApkShellExt";
            const string legacyClsid = "{1B0F3C21-0000-4C4E-9E64-6F4D0A6E1C11}";

            foreach (string root in new[] { _classesRoot, _mirrorRoot })
            {
                using (RegistryKey baseKey = OpenBaseKey())
                using (RegistryKey classKey = baseKey.CreateSubKey(Combine(root, legacyClass)))
                using (RegistryKey clsidValue = classKey.CreateSubKey("CLSID"))
                {
                    classKey.SetValue(string.Empty, legacyClass, RegistryValueKind.String);
                    clsidValue.SetValue(string.Empty, legacyClsid, RegistryValueKind.String);
                }

                using (RegistryKey baseKey = OpenBaseKey())
                using (RegistryKey deadServer = baseKey.CreateSubKey(Combine(root, @"CLSID\" + legacyClsid + @"\InprocServer32")))
                {
                    deadServer.SetValue(string.Empty, "mscoree.dll", RegistryValueKind.String);
                }
            }

            // Explorer lists an approved handler as a value; one early installer wrote a subkey here.
            SeedAbsoluteValue(_approvedRoot, legacyClsid, legacyClass);
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey approvedSubKey = baseKey.CreateSubKey(Combine(_approvedRoot, legacyClsid)))
            {
                approvedSubKey.SetValue("Note", legacyClass, RegistryValueKind.String);
            }

            Register();

            Assert.That(KeyExists(legacyClass), Is.False, "the class key of the earlier version must be gone");
            Assert.That(KeyExists(legacyClass, _mirrorRoot), Is.False, "and its 32 bit copy");
            Assert.That(KeyExists(@"CLSID\" + legacyClsid), Is.False, "the class id the dead key pointed at has no server");
            Assert.That(KeyExists(@"CLSID\" + legacyClsid, _mirrorRoot), Is.False);
            Assert.That(Value(string.Empty, legacyClsid, _approvedRoot), Is.Null, "an approved entry would keep the dead handler enabled");
            Assert.That(KeyExists(legacyClsid, _approvedRoot), Is.False, "the subkey shape is removed too");
        }

        [Test]
        public void RegisterReportsTheLegacyKeysItRemovedOnlyOnce()
        {
            const string legacyClass = "KKHomeProj.ApkShellExt.ApkShellExt";
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey classKey = baseKey.CreateSubKey(Combine(_classesRoot, legacyClass)))
            {
                classKey.SetValue(string.Empty, legacyClass, RegistryValueKind.String);
            }

            RegistrationRun first = Register();
            Assert.That(
                first.KeysWritten,
                Has.Some.EqualTo(Combine(_classesRoot, legacyClass)).IgnoreCase,
                "the install reports what it removed, so an installer can log it");

            RegistrationRun second = Register();
            Assert.That(second.KeysWritten, Is.SubsetOf(first.KeysWritten), "a second run finds nothing left to remove");
            Assert.That(second.KeysWritten, Has.None.Contains(legacyClass), "and does not report the cleanup again");
        }

        [Test]
        public void RegisterClearsTheBlockedList()
        {
            foreach ((string clsid, string _) in s_handlers)
            {
                SeedAbsoluteValue(_blockedRoot, clsid, "Blocked by Explorer");
            }

            Register();

            foreach ((string clsid, string _) in s_handlers)
            {
                Assert.That(Value(string.Empty, clsid, _blockedRoot), Is.Null, "a blocked CLSID is skipped by the shell no matter how well it is registered");
            }
        }

        [Test]
        public void RegisterIsIdempotent()
        {
            object options = SandboxOptions();
            RegistrationRun first = RegistrationApi.Register("User", ProductDllPath, options);
            string[] firstTree = DumpValues();

            RegistrationRun second = RegistrationApi.Register("User", ProductDllPath, SandboxOptions());

            Assert.That(DumpValues(), Is.EqualTo(firstTree), "a second run must produce the same registry tree");
            Assert.That(
                second.KeysWritten,
                Is.SubsetOf(first.KeysWritten),
                "the second run rewrites only what it wrote the first time - it invents no new keys");
        }

        [Test]
        public void UnregisterRemovesEveryValueItWrote()
        {
            Register();

            RegistrationApi.Unregister("User", SandboxOptions());

            Assert.That(DumpValues(), Is.Empty, "no value may survive Unregister inside the sandbox");
            foreach ((string clsid, string progId) in s_handlers)
            {
                Assert.That(KeyExists(@"CLSID\" + clsid), Is.False, "the CLSID key is removed");
                Assert.That(KeyExists(progId), Is.False, "the ProgID key is removed");
                Assert.That(KeyExists(@"CLSID\" + clsid, _mirrorRoot), Is.False, "the mirror is removed too");
            }

            foreach (string extension in s_extensions)
            {
                Assert.That(KeyExists(extension), Is.True, "the extension key itself belongs to the user, not to us");
                Assert.That(ValueNames(extension), Is.Empty, "but nothing of ours stays in it");
            }
        }

        /// <summary>
        /// The three named slots are ours, so uninstall removes the slot keys and both shapes of the
        /// old value, and leaves the extension key itself alone: an empty extension key belongs to
        /// the user.
        /// </summary>
        [Test]
        public void UnregisterRemovesTheSlotKeysAndTheOldValueShape()
        {
            Register();
            foreach (string extension in s_extensions)
            {
                string className = extension.Substring(1) + ".1";
                string shellEx = className + @"\ShellEx";
                bool wroteInfoTip = Default(shellEx + @"\" + InfoTipSlotName) != null;
                Assert.That(wroteInfoTip, Is.EqualTo(extension != ".aab"), "precondition: the info tip slot of " + extension);

                // The value shape an older release wrote: it must not survive the uninstall either.
                SeedValue(shellEx, IconHandlerSlotName, IconHandlerClsid);
                SeedValue(shellEx, InfoTipSlotName, InfoTipClsid);
                SeedValue(shellEx, ThumbnailSlotName, ThumbnailClsid);
            }

            RegistrationApi.Unregister("User", SandboxOptions());

            foreach (string extension in s_extensions)
            {
                string className = extension.Substring(1) + ".1";
                string shellEx = className + @"\ShellEx";
                Assert.That(KeyExists(shellEx), Is.False, "the ShellEx key of " + extension + " holds nothing of ours any more: values=" + string.Join(",", ValueNames(shellEx)) + " keys=" + string.Join(",", SubKeyNames(shellEx)));
                Assert.That(Default(className + @"\ShellEx\IconHandler"), Is.Null);
                Assert.That(Default(className + @"\ShellEx\IconHandler", _mirrorRoot), Is.Null, "the 32 bit view is cleared too: " + extension);
                Assert.That(Value(className + @"\ShellEx", IconHandlerSlotName), Is.Null, "the value shape of " + extension + " is cleared too");
            }
        }

        [Test]
        public void RegisterReplacesTheValueShapeOfAnEarlierRelease()
        {
            EnsureKey("apk.1");
            SeedValue(@"apk.1\ShellEx", IconHandlerSlotName, IconHandlerClsid);
            SeedValue(@"apk.1\ShellEx", InfoTipSlotName, InfoTipClsid);
            SeedValue(@"apk.1\ShellEx", ThumbnailSlotName, ThumbnailClsid);

            Register();

            string shellEx = @"apk.1\ShellEx";
            Assert.That(Default(shellEx + @"\" + IconHandlerSlotName), Is.EqualTo(IconHandlerClsid));
            Assert.That(Default(shellEx + @"\" + InfoTipSlotName), Is.EqualTo(InfoTipClsid));
            Assert.That(Default(shellEx + @"\" + ThumbnailSlotName), Is.EqualTo(ThumbnailClsid));
            Assert.That(Value(shellEx, IconHandlerSlotName), Is.Null, "the ignored value shape is cleaned up on install");
            Assert.That(Value(shellEx, InfoTipSlotName), Is.Null);
            Assert.That(Value(shellEx, ThumbnailSlotName), Is.Null);
        }

        [Test]
        public void RegisterKeepsASlotValueThatBelongsToAnotherHandler()
        {
            const string foreign = "{11111111-2222-3333-4444-555555555555}";
            EnsureKey("apk.1");
            SeedValue(@"apk.1\ShellEx", IconHandlerSlotName, foreign);

            Register();

            Assert.That(Value(@"apk.1\ShellEx", IconHandlerSlotName), Is.EqualTo(foreign), "a value that is not our CLSID is somebody else's registration");
            Assert.That(Default(@"apk.1\ShellEx\IconHandler"), Is.EqualTo(IconHandlerClsid), "our slot key is written next to it");
        }

        [Test]
        public void UnregisterKeepsASlotKeyThatBelongsToAnotherHandler()
        {
            const string foreign = "{11111111-2222-3333-4444-555555555555}";
            Register();
            SeedDefault(@"apk.1\ShellEx\IconHandler", foreign);

            RegistrationApi.Unregister("User", SandboxOptions());

            Assert.That(Default(@"apk.1\ShellEx\IconHandler"), Is.EqualTo(foreign), "a slot that no longer names our CLSID is not ours to delete");
            Assert.That(KeyExists(@"apk.1\ShellEx\IconHandler"), Is.True);
        }

        [Test]
        public void UnregisterRestoresTheBackedUpDefaultIcon()
        {
            SeedDefault("apk.1", "apk Application");
            SeedDefault(@"apk.1\DefaultIcon", "old.ico");
            Register();

            RegistrationApi.Unregister("User", SandboxOptions());

            Assert.That(Default(@"apk.1\DefaultIcon"), Is.EqualTo("old.ico"), "the icon that was there before must come back");
            Assert.That(Value(@"apk.1\DefaultIcon", DefaultIconBackupValueName), Is.Null, "the backup value is consumed by the restore");
        }

        [Test]
        public void Windows10BuildDoesNotWriteTheExplorerCommandHandler()
        {
            RegistrationRun run = RegisterWithBuild(Windows10Build);

            Assert.That(run.BuildNumber, Is.EqualTo(Windows10Build));
            Assert.That(run.ExplorerCommandRegistered, Is.False, "the Windows 11 command model only exists from build 22621");
            Assert.That(Value(@"apk.1\ShellEx", "ExplorerCommandHandler"), Is.Null);
        }

        [Test]
        public void Windows11BuildWritesTheExplorerCommandHandlerForEveryMenuClass()
        {
            RegistrationRun run = RegisterWithBuild(Windows11Build);

            Assert.That(run.BuildNumber, Is.EqualTo(Windows11Build));
            Assert.That(run.ExplorerCommandRegistered, Is.True);
            foreach (string extension in s_extensions)
            {
                string shellEx = extension.Substring(1) + ".1" + @"\ShellEx";
                if (extension == ".aab")
                {
                    Assert.That(Value(shellEx, "ExplorerCommandHandler"), Is.Null, ".aab has no context menu, so no ExplorerCommandHandler either");
                    continue;
                }

                Assert.That(Value(shellEx, "ExplorerCommandHandler"), Is.EqualTo(ExplorerCommandClsid));
                Assert.That(Value(shellEx, "ExplorerCommandHandler", _mirrorRoot), Is.EqualTo(ExplorerCommandClsid), "the 32 bit view needs it too");
            }
        }

        [Test]
        public void UnregisterRemovesTheExplorerCommandHandler()
        {
            object options = RegistrationApi.WithBuild(SandboxOptions(), Windows11Build);
            RegistrationApi.Register("User", ProductDllPath, options);
            Assert.That(Value(@"apk.1\ShellEx", "ExplorerCommandHandler"), Is.EqualTo(ExplorerCommandClsid), "precondition");

            RegistrationApi.Unregister("User", options);

            Assert.That(Value(@"apk.1\ShellEx", "ExplorerCommandHandler"), Is.Null);
        }

        [Test]
        public void UserScopeDoesNotWriteTheApprovedList()
        {
            Register();

            Assert.That(ValueNames(string.Empty, _approvedRoot), Is.Empty, "the per-user installer never touched Shell Extensions\\Approved");
        }

        [Test]
        public void MachineScopeWritesTheApprovedListAndStillMirrors()
        {
            RegistrationRun run = Register("Machine");

            Assert.That(run.Scope, Is.EqualTo("Machine"));
            foreach ((string clsid, string progId) in s_handlers)
            {
                Assert.That(Value(string.Empty, clsid, _approvedRoot), Is.EqualTo(progId), "a machine install whitelists the handlers");
            }

            Assert.That(Default(@"CLSID\" + IconHandlerClsid, _mirrorRoot), Is.EqualTo(NativeTypeOf(IconHandlerClsid)), "a machine install needs the 32 bit view as well");
        }
    }
}
