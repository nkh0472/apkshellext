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
    /// The modern context menu's registration: the command server class that serves the verb and
    /// the verb key that makes Explorer look for it. The gate is a kernel build number, so both
    /// sides are exercised here on a machine that cannot run the Windows 11 menu at all.
    /// </summary>
    [TestFixture]
    internal sealed class ModernMenuRegistrationTests
    {
        private const string CommandClsid = "{0B6C1F2A-7D34-4E5B-9A18-3C2D4E5F6071}";
        private const string CommandClassName = "ApkShellext2.Shell.Native.ExplorerCommandInstance";
        private const string ManagedCategoryClsid = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}";
        private const string ForeignClsid = "{DEADBEEF-1111-2222-3333-444455556666}";
        private const int Windows10Build = 19045;
        private const int Windows11Build = 22621;

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

        private static string ProductDllPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ApkShellext2.dll"); }
        }

        private static string ProductVersion
        {
            get { return AssemblyName.GetAssemblyName(ProductDllPath).Version.ToString(); }
        }

        [SetUp]
        public void CreateSandbox()
        {
            _sandboxRoot = @"Software\ApkShellext2\ModernMenuTests\" + Guid.NewGuid().ToString("N");
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

        // ---- the gate ----------------------------------------------------------------------

        [Test]
        public void ShouldRegisterIsFalseForAnUnknownOrOlderBuild()
        {
            Assert.That(ShouldRegister(0, Windows11Build), Is.False, "a build number that could not be read must not enable the verb");
            Assert.That(ShouldRegister(Windows10Build, Windows11Build), Is.False);
            Assert.That(ShouldRegister(22620, Windows11Build), Is.False);
        }

        [Test]
        public void ShouldRegisterIsTrueFromTheMinimumBuild()
        {
            Assert.That(ShouldRegister(Windows11Build, Windows11Build), Is.True);
            Assert.That(ShouldRegister(26100, Windows11Build), Is.True);
        }

        [Test]
        public void TheThresholdIsInjectable()
        {
            Assert.That(ShouldRegister(Windows11Build, 26100), Is.False);
            Assert.That(ShouldRegister(26100, 26100), Is.True);
        }

        // ---- the command server class ------------------------------------------------------

        [Test]
        public void Windows11RegistersTheCommandServerClass()
        {
            RegisterWithBuild(Windows11Build);

            string clsidKey = @"CLSID\" + CommandClsid;
            string inprocKey = clsidKey + @"\InprocServer32";

            Assert.That(Default(clsidKey), Is.EqualTo(CommandClassName), "the class id must name the type mscoree activates");
            Assert.That(Default(inprocKey), Is.EqualTo("mscoree.dll"));
            Assert.That(Value(inprocKey, "ThreadingModel"), Is.EqualTo("Both"));
            Assert.That(Value(inprocKey, "Class"), Is.EqualTo(CommandClassName));
            Assert.That(Value(inprocKey, "Assembly"), Is.EqualTo(AssemblyName.GetAssemblyName(ProductDllPath).FullName));
            Assert.That(Value(inprocKey, "RuntimeVersion"), Is.EqualTo("v4.0.30319"));
            Assert.That(Value(inprocKey, "CodeBase"), Does.EndWith("ApkShellext2.dll"));
            Assert.That(SubKeyNames(clsidKey + @"\Implemented Categories"), Does.Contain(ManagedCategoryClsid));
            Assert.That(ValueNames(inprocKey + @"\" + ProductVersion), Is.EquivalentTo(
                new[] { "Class", "Assembly", "RuntimeVersion", "CodeBase" }));
            Assert.That(Default(clsidKey, _mirrorRoot), Is.EqualTo(CommandClassName),
                        "the shell can be a 32 bit process even on a machine that only has the 64 bit view written");
        }

        [Test]
        public void TheCommandServerClassDeclaresNoProgId()
        {
            RegisterWithBuild(Windows11Build);

            Assert.That(Value(@"CLSID\" + CommandClsid, "ProgId"), Is.Null,
                        "a verb names its class id; a ProgID would be a second, stale name for the same server");
            Assert.That(KeyExists(CommandClassName), Is.False, "only the four document handlers have ProgID keys");
        }

        [Test]
        public void Windows10DoesNotRegisterTheCommandServerClass()
        {
            RegisterWithBuild(Windows10Build);

            Assert.That(KeyExists(@"CLSID\" + CommandClsid), Is.False);
            Assert.That(KeyExists(@"CLSID\" + CommandClsid, _mirrorRoot), Is.False);
        }

        [Test]
        public void UnregisterRemovesTheCommandServerClass()
        {
            RegisterWithBuild(Windows11Build);
            Assert.That(KeyExists(@"CLSID\" + CommandClsid), Is.True, "precondition");

            RegistrationApi.Unregister("User", RegistrationApi.WithBuild(SandboxOptions(), Windows11Build));

            Assert.That(KeyExists(@"CLSID\" + CommandClsid), Is.False);
            Assert.That(KeyExists(@"CLSID\" + CommandClsid, _mirrorRoot), Is.False);
        }

        [Test]
        public void UnregisterRemovesAServerClassItNeverGated()
        {
            //  regasm writes a class block for every ComVisible class of the assembly, so a machine
            //  that was upgraded from an older build can hold this block without ever having the
            //  verb. Unregister must clean it up anyway.
            EnsureKey(@"CLSID\" + CommandClsid);
            EnsureKey(@"CLSID\" + CommandClsid, _mirrorRoot);

            RegistrationApi.Unregister("User", RegistrationApi.WithBuild(SandboxOptions(), Windows10Build));

            Assert.That(KeyExists(@"CLSID\" + CommandClsid), Is.False);
            Assert.That(KeyExists(@"CLSID\" + CommandClsid, _mirrorRoot), Is.False);
        }

        [Test]
        public void TheBlockedListIsClearedForTheCommandToo()
        {
            SeedAbsoluteValue(_blockedRoot, CommandClsid, "blocked by another shell extension");

            RegisterWithBuild(Windows11Build);

            Assert.That(ReadValue(_blockedRoot, CommandClsid), Is.Null,
                        "a blocked class id is a menu that silently never appears");
        }

        // ---- the verb ----------------------------------------------------------------------

        [Test]
        public void RegisterKeepsAForeignShellExCommandValue()
        {
            SeedValue(@"apk.1\ShellEx", "ExplorerCommandHandler", ForeignClsid);

            RegisterWithBuild(Windows11Build);

            Assert.That(Value(@"apk.1\ShellEx", "ExplorerCommandHandler"), Is.EqualTo(ForeignClsid),
                        "an earlier release of this product wrote that value; another product's value is not ours to delete");
        }

        [Test]
        public void RemoveKeepsAForeignVerbKey()
        {
            //  An uninstall on a machine where another product took over the verb must not take its
            //  command with it: the class id written in the key decides whose verb it is.
            SeedValue(@"apk.1\shell\ApkShellext2", "ExplorerCommandHandler", ForeignClsid);

            RegistrationApi.Unregister("User", RegistrationApi.WithBuild(SandboxOptions(), Windows11Build));

            Assert.That(Value(@"apk.1\shell\ApkShellext2", "ExplorerCommandHandler"), Is.EqualTo(ForeignClsid),
                        "the verb key belongs to whoever owns the class id written in it");
        }

        [Test]
        public void RemoveKeepsAForeignMultiSelectModel()
        {
            RegistrationApi.Register("User", ProductDllPath, RegistrationApi.WithBuild(SandboxOptions(), Windows11Build));
            SeedValue(@"apk.1\shell\ApkShellext2", "MultiSelectModel", "Single");

            RegistrationApi.Unregister("User", RegistrationApi.WithBuild(SandboxOptions(), Windows11Build));

            Assert.That(Value(@"apk.1\shell\ApkShellext2", "ExplorerCommandHandler"), Is.Null, "our class id is removed");
            Assert.That(Value(@"apk.1\shell\ApkShellext2", "MultiSelectModel"), Is.EqualTo("Single"),
                        "the value was changed by someone else, so it is no longer ours");
        }

        [Test]
        public void TheVerbKeyIsReportedInKeysWritten()
        {
            RegistrationRun run = RegisterWithBuild(Windows11Build);

            Assert.That(run.ExplorerCommandRegistered, Is.True);
            Assert.That(run.KeysWritten, Contains.Item(_classesRoot + @"\apk.1\shell\ApkShellext2"),
                        "a caller that logs what was written must see the verb too");
        }

        // ---- reflection into the internal gate ---------------------------------------------

        private static bool ShouldRegister(int buildNumber, int threshold)
        {
            Type type = ProductApi.Assembly.GetType("ApkShellext2.Registration.Win11CommandRegistration", true);
            MethodInfo method = type.GetMethod("ShouldRegister", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Win11CommandRegistration.ShouldRegister was renamed; update this test and TestSupport/RegistrationApi.cs.");
            return (bool)method.Invoke(null, new object[] { buildNumber, threshold });
        }

        // ---- sandbox helpers ---------------------------------------------------------------

        private object SandboxOptions()
        {
            return RegistrationApi.Sandbox(_sandboxRoot);
        }

        private RegistrationRun RegisterWithBuild(int build)
        {
            return RegistrationApi.Register("User", ProductDllPath, RegistrationApi.WithBuild(SandboxOptions(), build));
        }

        private static RegistryKey OpenBaseKey()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        }

        private string Default(string relativePath, string root = null)
        {
            return ReadValue(Combine(root ?? _classesRoot, relativePath), string.Empty);
        }

        private string Value(string relativePath, string valueName, string root = null)
        {
            return ReadValue(Combine(root ?? _classesRoot, relativePath), valueName);
        }

        private static string ReadValue(string absolutePath, string valueName)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.OpenSubKey(absolutePath))
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

        private void EnsureKey(string relativePath, string root = null)
        {
            using (RegistryKey baseKey = OpenBaseKey())
            using (RegistryKey key = baseKey.CreateSubKey(Combine(root ?? _classesRoot, relativePath)))
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
    }
}
