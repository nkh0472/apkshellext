using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the hand-written COM interop against the layouts shell32 and explorer actually call.
    ///
    /// The declarations in ApkShellext2.Interop are deliberately flat single interfaces with
    /// every method marked [PreserveSig], because the CLR orders derived interface methods
    /// before base interface methods in a vtable: a declaration that mirrors the COM inheritance
    /// chain puts HandleMenuMsg2 two slots too early and explorer calls into the wrong method
    /// (plan §3.1). These tests fail as soon as a slot, an IID, a [PreserveSig] or a structure
    /// size drifts, before any handler is wired to the interface.
    /// </summary>
    [TestFixture]
    internal sealed class InteropLayoutTests
    {
        // The published IIDs from shlobj_core.h / shobjidl_core.h. Hard-coded on purpose: a
        // typo in a [Guid] attribute must fail the test, not silently agree with itself.
        private static readonly Dictionary<string, Guid> s_expectedIids = new Dictionary<string, Guid>
        {
            { "IContextMenuFlat", new Guid("000214e4-0000-0000-c000-000000000046") },
            { "IContextMenu2Flat", new Guid("000214f4-0000-0000-c000-000000000046") },
            { "IContextMenu3Flat", new Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719") },
            { "IShellExtInitFlat", new Guid("000214e8-0000-0000-c000-000000000046") },
            { "IQueryInfoFlat", new Guid("00021500-0000-0000-c000-000000000046") },
            { "IExtractIconWFlat", new Guid("000214fa-0000-0000-c000-000000000046") },
            { "IPersistFileFlat", new Guid("0000010b-0000-0000-c000-000000000046") },
            { "IThumbnailProviderFlat", new Guid("e357fccd-a995-4576-b01f-234630154e96") },
            { "IInitializeWithStreamFlat", new Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f") },
            { "IExplorerCommandFlat", new Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9") },
            { "IEnumExplorerCommandFlat", new Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8") },
            { "IShellFolderFlat", new Guid("000214e6-0000-0000-c000-000000000046") },
            { "IClassFactoryFlat", new Guid("00000001-0000-0000-c000-000000000046") }
        };

        // x86 / x64 sizes measured with Marshal.SizeOf in the shipping build (plan §4.2); the
        // structures use the natural Win32 alignment, so no [StructLayout(Pack = ...)].
        private static readonly Dictionary<string, int[]> s_expectedStructSizes = new Dictionary<string, int[]>
        {
            { "POINT", new[] { 8, 8 } },
            { "FORMATETC", new[] { 20, 32 } },
            { "STGMEDIUM", new[] { 12, 24 } },
            { "CMINVOKECOMMANDINFO", new[] { 40, 64 } },
            { "CMINVOKECOMMANDINFOEX", new[] { 64, 104 } },
            { "MENUITEMINFO", new[] { 48, 80 } },
            { "PROPERTYKEY", new[] { 20, 20 } }
        };

        [Test]
        public void InteropDeclarationsSatisfyTheVtableContract()
        {
            IReadOnlyList<string> violations = ComLayoutViolations();
            Assert.That(violations, Is.Empty, "COM layout violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void ContextMenuFamilyExposesTheShellSlotsInOrder()
        {
            Assert.That(MethodNames("IContextMenuFlat"), Is.EqualTo(new[] { "QueryContextMenu", "InvokeCommand", "GetCommandString" }));
            Assert.That(
                MethodNames("IContextMenu2Flat"),
                Is.EqualTo(new[] { "QueryContextMenu", "InvokeCommand", "GetCommandString", "HandleMenuMsg" }),
                "IContextMenu2Flat must declare IContextMenu's three slots itself: a flattened interface cannot inherit them.");
            Assert.That(
                MethodNames("IContextMenu3Flat"),
                Is.EqualTo(new[] { "QueryContextMenu", "InvokeCommand", "GetCommandString", "HandleMenuMsg", "HandleMenuMsg2" }),
                "IContextMenu3Flat slot 7 is HandleMenuMsg2; a shorter declaration shifts the vtable.");
        }

        [Test]
        public void ExplorerCommandInterfacesDeclareTheirShellSlots()
        {
            Assert.That(MethodNames("IExplorerCommandFlat"), Is.EqualTo(new[]
            {
                "GetTitle", "GetIcon", "GetToolTip", "GetCanonicalName", "GetState", "Invoke", "GetFlags", "EnumSubCommands"
            }));
            Assert.That(MethodNames("IEnumExplorerCommandFlat"), Is.EqualTo(new[] { "Next", "Skip", "Reset", "Clone" }));
            Assert.That(MethodNames("IShellFolderFlat"), Has.Length.EqualTo(10));
            Assert.That(MethodNames("IPersistFileFlat"), Is.EqualTo(new[]
            {
                "GetClassID", "IsDirty", "Load", "Save", "SaveCompleted", "GetCurFile"
            }));
        }

        [Test]
        public void EveryInteropSlotIsPreserveSig()
        {
            List<string> offenders = new List<string>();
            foreach (Type type in InteropInterfaces())
            {
                foreach (MethodInfo method in type.GetMethods())
                {
                    bool preserved = (method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0 ||
                                     method.GetCustomAttribute<PreserveSigAttribute>() != null;
                    if (!preserved)
                    {
                        offenders.Add(type.Name + "." + method.Name);
                    }
                }
            }

            Assert.That(
                offenders,
                Is.Empty,
                "these slots keep the CLR's default HRESULT mangling and would throw or return the wrong value: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void InteropInterfacesAreFlatSingleInheritance()
        {
            List<string> offenders = new List<string>();
            foreach (Type type in InteropInterfaces())
            {
                if (type.GetInterfaces().Length != 0)
                {
                    offenders.Add(type.Name + " -> " + string.Join(", ", type.GetInterfaces().Select(i => i.Name)));
                }

                InterfaceTypeAttribute kind = type.GetCustomAttribute<InterfaceTypeAttribute>();
                if (kind == null || kind.Value != ComInterfaceType.InterfaceIsIUnknown)
                {
                    offenders.Add(type.Name + " is not InterfaceIsIUnknown");
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("; ", offenders));
        }

        [Test]
        public void InteropInterfaceIidsMatchTheShellContracts()
        {
            List<string> offenders = new List<string>();
            foreach (KeyValuePair<string, Guid> expected in s_expectedIids)
            {
                Type type = InteropType(expected.Key);
                if (type.GUID != expected.Value)
                {
                    offenders.Add(expected.Key + ": declared " + type.GUID + ", expected " + expected.Value);
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("; ", offenders));
        }

        [Test]
        public void InteropStructSizesMatchTheWin32Abi()
        {
            int index = IntPtr.Size == 8 ? 1 : 0;
            List<string> offenders = new List<string>();
            foreach (KeyValuePair<string, int[]> expected in s_expectedStructSizes)
            {
                Type type = ProductApi.Assembly.GetType("ApkShellext2.Interop." + expected.Key, throwOnError: false);
                Assert.That(type, Is.Not.Null, "ApkShellext2.Interop." + expected.Key + " was not found.");
                int actual = Marshal.SizeOf(type);
                if (actual != expected.Value[index])
                {
                    offenders.Add(expected.Key + ": " + actual + " bytes, expected " + expected.Value[index]);
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("; ", offenders));
        }

        [Test]
        public void ExplorerCommandGateFollowsTheKernelBuildNumber()
        {
            Assert.That(WindowsVersionMethod("SupportsExplorerCommand", 22620), Is.False, "build 22620 predates the Windows 11 command model");
            Assert.That(WindowsVersionMethod("SupportsExplorerCommand", 22621), Is.True, "build 22621 is the first build with IExplorerCommand menus");
            Assert.That(WindowsVersionMethod("SupportsExplorerCommand", 26100), Is.True);
            Assert.That(WindowsVersionMethod("SupportsExplorerCommand", 19045), Is.False, "the Windows 10 build this machine really runs");
            Assert.That(WindowsVersionMethod("SupportsExplorerCommand", 0), Is.False, "an unreadable build number must not enable the Windows 11 path");
        }

        [Test]
        public void ThisMachineReadsItsBuildNumberFromRtlGetVersion()
        {
            Type versionType = InteropType("WindowsVersion");
            MethodInfo read = versionType.GetMethod("ReadBuildNumber", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(read, Is.Not.Null);
            int build = (int)read.Invoke(null, new object[0]);
            Assert.That(build, Is.GreaterThan(0), "RtlGetVersion must always return a real build number");
            Assert.That((int)read.Invoke(null, new object[0]), Is.EqualTo(build), "two reads of the kernel build number must agree");
        }

        [Test]
        public void HResultHelpersClassifyShellResults()
        {
            Type hresult = InteropType("HResult");
            Assert.That(Call(hresult, "Succeeded", 0), Is.True);
            Assert.That(Call(hresult, "Succeeded", 1), Is.True, "S_FALSE is a success code");
            Assert.That(Call(hresult, "Succeeded", unchecked((int)0x80004005)), Is.False, "E_FAIL");
            Assert.That(Call(hresult, "Failed", unchecked((int)0x80040154)), Is.True, "REGDB_E_CLASSNOTREG");
            Assert.That(Call(hresult, "Failed", 1), Is.False);
            Assert.That(Call(hresult, "MakeSuccess", 3), Is.EqualTo(3), "QueryContextMenu returns MAKE_HRESULT(SEVERITY_SUCCESS, 0, count)");
            Assert.That(Call(hresult, "MakeItemCount", 5), Is.EqualTo(5), "QueryContextMenu returns the inserted item count");
            Assert.That(
                Call(hresult, "MakeItemCount", 70000),
                Is.EqualTo(0xFFFF),
                "the item count is saturated to the 16 usable bits of an HRESULT and must never wrap");
        }

        [Test]
        public void ComObjectSlotConstantsMatchTheVtable()
        {
            Type comObject = InteropType("ComObject");
            Assert.That(Constant(comObject, "SlotQueryInterface"), Is.EqualTo(0));
            Assert.That(Constant(comObject, "SlotAddRef"), Is.EqualTo(1));
            Assert.That(Constant(comObject, "SlotRelease"), Is.EqualTo(2));
            Assert.That(Constant(comObject, "FirstMethodSlot"), Is.EqualTo(3), "the first interface method after QueryInterface/AddRef/Release");
        }

        private static IReadOnlyList<string> ComLayoutViolations()
        {
            Type layout = InteropType("ComLayout");
            MethodInfo verify = layout.GetMethod("Verify", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(verify, Is.Not.Null, "Interop.ComLayout.Verify was not found.");
            object result = verify.Invoke(null, new object[0]);
            return ((IEnumerable)result).Cast<string>().ToList().AsReadOnly();
        }

        private static IEnumerable<Type> InteropInterfaces()
        {
            return ProductApi.Assembly.GetTypes()
                .Where(t => t.Namespace == "ApkShellext2.Interop" && t.IsInterface && t.GetCustomAttribute<ComImportAttribute>() != null)
                .OrderBy(t => t.Name);
        }

        private static string[] MethodNames(string typeName)
        {
            return InteropType(typeName)
                .GetMethods()
                .OrderBy(m => m.MetadataToken)
                .Select(m => m.Name)
                .ToArray();
        }

        private static bool WindowsVersionMethod(string name, int build)
        {
            Type versionType = InteropType("WindowsVersion");
            MethodInfo method = versionType.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            Assert.That(method, Is.Not.Null, "WindowsVersion." + name + "(int) was not found.");
            return (bool)method.Invoke(null, new object[] { build });
        }

        private static object Call(Type type, string name, int argument)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, type.Name + "." + name + " was not found.");
            return method.Invoke(null, new object[] { argument });
        }

        private static object Constant(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, type.Name + "." + name + " was not found.");
            return field.GetRawConstantValue();
        }

        private static Type InteropType(string name)
        {
            Type type = ProductApi.Assembly.GetType("ApkShellext2.Interop." + name, throwOnError: false);
            Assert.That(type, Is.Not.Null, "type ApkShellext2.Interop." + name + " was not found.");
            return type;
        }
    }
}
