using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ApkShellext2.Shell.Commands;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The COM shape of the modern command classes: the shell finds them by IID, calls them by
    /// slot, and only reaches the code behind a public interface. The layout is therefore pinned
    /// here the same way the classic handlers are pinned in NativeHandlerLayoutTests: the flat
    /// server declarations must match the shell's IIDs and slot order, and they must agree with the
    /// ComImport declarations the callers use.
    /// </summary>
    [TestFixture]
    internal sealed class ExplorerCommandLayoutTests
    {
        private const int FirstMethodSlot = 3;

        private static readonly Dictionary<string, Guid> s_shellIids = new Dictionary<string, Guid> {
            { "IExplorerCommandNative", new Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9") },
            { "IEnumExplorerCommandNative", new Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8") },
        };

        private static readonly Dictionary<string, string[]> s_shellSlots = new Dictionary<string, string[]> {
            { "IExplorerCommandNative", new[] {
                "GetTitle", "GetIcon", "GetToolTip", "GetCanonicalName", "GetState", "Invoke", "GetFlags", "EnumSubCommands" } },
            { "IEnumExplorerCommandNative", new[] { "Next", "Skip", "Reset", "Clone" } },
        };

        [Test]
        public void ExplorerCommandInterfaceIidsMatchTheShellContracts()
        {
            List<string> offenders = new List<string>();
            foreach (KeyValuePair<string, Guid> pair in s_shellIids) {
                if (ServerType(pair.Key).GUID != pair.Value) {
                    offenders.Add(pair.Key + " is " + ServerType(pair.Key).GUID);
                }
            }

            Assert.That(offenders, Is.Empty, "the shell only finds these interfaces by their own IID");
        }

        [Test]
        public void ExplorerCommandInterfacesArePublicComVisibleAndPreserveSig()
        {
            List<string> offenders = new List<string>();
            foreach (string name in s_shellIids.Keys) {
                Type type = ServerType(name);
                if (!type.IsPublic) {
                    offenders.Add(type.FullName + " is not public, so the CCW would not expose it");
                }
                ComVisibleAttribute visible = type.GetCustomAttribute<ComVisibleAttribute>();
                if (visible == null || !visible.Value) {
                    offenders.Add(type.FullName + " is not ComVisible");
                }
                InterfaceTypeAttribute kind = type.GetCustomAttribute<InterfaceTypeAttribute>();
                if (kind == null || kind.Value != ComInterfaceType.InterfaceIsIUnknown) {
                    offenders.Add(type.FullName + " is not an IUnknown based interface");
                }
                if (type.GetInterfaces().Length != 0) {
                    offenders.Add(type.FullName + " inherits another interface, which would move its slots");
                }
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                    if (!IsPreserveSig(method)) {
                        offenders.Add(type.FullName + "." + method.Name + " lets the runtime translate the HRESULT");
                    }
                }
            }

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void ExplorerCommandSlotsFollowTheShellOrder()
        {
            foreach (KeyValuePair<string, string[]> pair in s_shellSlots) {
                Assert.That(SlotNames(ServerType(pair.Key)), Is.EqualTo(pair.Value), pair.Key + " must keep the shell's slot order");
            }
        }

        [Test]
        public void ExplorerCommandSlotsAgreeWithTheInteropDeclarations()
        {
            foreach (KeyValuePair<string, string[]> pair in s_shellSlots) {
                //  The ComImport counterpart lives next to the other caller side declarations and
                //  keeps the historical "Flat" name without the Native suffix.
                string interopName = "ApkShellext2.Interop." + pair.Key.Replace("Native", string.Empty) + "Flat";
                Assert.That(SlotNames(NativeType(interopName)), Is.EqualTo(SlotNames(ServerType(pair.Key))),
                            interopName + " is what callers and the layout tests see, so it must describe the same vtable");
            }
        }

        [Test]
        public void TheTitleSlotIsTheFirstMethodOnTheVtable()
        {
            string[] selected = { "C:\\fake\\first.apk", "C:\\fake\\second.apk" };
            object command = NewCommand(selected);
            IntPtr iface = Marshal.GetComInterfaceForObject(command, typeof(ApkShellext2.Shell.Native.IExplorerCommandNative));
            try {
                IntPtr slot = Marshal.ReadIntPtr(Marshal.ReadIntPtr(iface), FirstMethodSlot * IntPtr.Size);
                GetTitleThunk title = (GetTitleThunk)Marshal.GetDelegateForFunctionPointer(slot, typeof(GetTitleThunk));
                IntPtr text;
                Assert.That(title(iface, IntPtr.Zero, out text), Is.EqualTo(0), "GetTitle is slot 3");
                try {
                    Assert.That(Marshal.PtrToStringUni(text), Is.EqualTo(CommandCatalog.Build(selected).Root.Text),
                                "the text comes from the menu model, not from a second copy of the wording");
                } finally {
                    Marshal.FreeCoTaskMem(text);
                }
            } finally {
                Marshal.Release(iface);
            }
        }

        [Test]
        public void TheCommandClassesAreComVisibleAndHaveNoClassInterface()
        {
            foreach (string name in new[] {
                "ApkShellext2.Shell.Native.ExplorerCommandInstance",
                "ApkShellext2.Shell.Native.ExplorerCommandItemInstance",
                "ApkShellext2.Shell.Native.ExplorerCommandEnumerator" }) {
                Type type = NativeType(name);
                Assert.That(type.IsPublic, Is.True, name + " must be public for COM to activate it");
                ComVisibleAttribute visible = type.GetCustomAttribute<ComVisibleAttribute>();
                Assert.That(visible == null ? false : visible.Value, Is.True, name + " must be ComVisible");
                ClassInterfaceAttribute kind = type.GetCustomAttribute<ClassInterfaceAttribute>();
                Assert.That(kind == null ? ClassInterfaceType.None : kind.Value, Is.EqualTo(ClassInterfaceType.None),
                            name + " must not grow a dispatch interface");
            }
        }

        [Test]
        public void TheCommandClassCarriesTheRegisteredClsidAndName()
        {
            Type command = NativeType("ApkShellext2.Shell.Native.ExplorerCommandInstance");
            Type registrar = NativeType("ApkShellext2.Registration.ShellGuids");
            const BindingFlags constants = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            string clsid = (string)registrar.GetField("ExplorerCommandClsid", constants).GetValue(null);
            string className = (string)registrar.GetField("ExplorerCommandClassName", constants).GetValue(null);

            Assert.That(command.GUID, Is.EqualTo(new Guid(clsid.Trim('{', '}'))),
                        "the verb key points at this CLSID, so the class attribute and the constant must agree");
            Assert.That(command.FullName, Is.EqualTo(className),
                        "the server block records this name, and the runtime resolves the class by it");
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetTitleThunk(IntPtr self, IntPtr psiItemArray, out IntPtr title);

        private static Type NativeType(string name)
        {
            Type type = ProductApi.Assembly.GetType(name, false);
            Assert.That(type, Is.Not.Null, "missing type " + name);
            return type;
        }

        /// <summary>The public flat declaration the CCW exposes.</summary>
        private static Type ServerType(string simpleName)
        {
            return NativeType("ApkShellext2.Shell.Native." + simpleName);
        }

        private static string[] SlotNames(Type type)
        {
            return type.GetMethods()
                       .OrderBy(method => method.MetadataToken)
                       .Select(method => method.Name)
                       .ToArray();
        }

        private static bool IsPreserveSig(MethodInfo method)
        {
            if ((method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) == MethodImplAttributes.PreserveSig) {
                return true;
            }
            return method.GetCustomAttribute<PreserveSigAttribute>() != null;
        }

        private static object NewCommand(params string[] selectedPaths)
        {
            Type type = NativeType("ApkShellext2.Shell.Native.ExplorerCommandInstance");
            ConstructorInfo ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                                                       new[] { typeof(IReadOnlyList<string>) }, null);
            Assert.That(ctor, Is.Not.Null, "the test seam constructor must exist");
            return ctor.Invoke(new object[] { selectedPaths });
        }
    }
}
