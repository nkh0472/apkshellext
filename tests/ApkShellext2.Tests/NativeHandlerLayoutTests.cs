using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ApkShellext2.Shell.Native;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the native handler interfaces in ApkShellext2.Shell.Native against the shell contracts
    /// they implement, and against the vtable the CLR really hands the shell.
    ///
    /// Three things can drift silently here: an IID that no longer matches the shell's interface,
    /// a declaration order that moves a method to the wrong slot, and a missing [PreserveSig] that
    /// turns an HRESULT into an exception thrown across the COM boundary. All three fail in the
    /// shell - a wrong slot in explorer is an access violation, not an error code - so they are
    /// pinned before any handler is attached to the interfaces.
    /// </summary>
    [TestFixture]
    internal sealed class NativeHandlerLayoutTests
    {
        // The published IIDs (shlobj_core.h, shobjidl_core.h, thumbcache.h, propidl.h). Hard-coded
        // on purpose: a typo in the [Guid] attribute must fail here, not agree with itself.
        private static readonly Dictionary<string, Guid> s_shellIids = new Dictionary<string, Guid>
        {
            { "IExtractIconWNative", new Guid("000214fa-0000-0000-c000-000000000046") },
            { "IPersistFileNative", new Guid("0000010b-0000-0000-c000-000000000046") },
            { "IQueryInfoNative", new Guid("00021500-0000-0000-c000-000000000046") },
            { "IThumbnailProviderNative", new Guid("e357fccd-a995-4576-b01f-234630154e96") },
            { "IInitializeWithStreamNative", new Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f") },
            { "IInitializeWithFileNative", new Guid("b7d14566-0509-4cce-a71f-0a554233bd9b") }
        };

        // The slots after IUnknown, in the order the headers declare them.
        private static readonly Dictionary<string, string[]> s_shellSlots = new Dictionary<string, string[]>
        {
            { "IExtractIconWNative", new[] { "GetIconLocation", "Extract" } },
            { "IPersistFileNative", new[] { "GetClassID", "IsDirty", "Load", "Save", "SaveCompleted", "GetCurFile" } },
            { "IQueryInfoNative", new[] { "GetInfoTip", "GetInfoFlags" } },
            { "IThumbnailProviderNative", new[] { "GetThumbnail" } },
            { "IInitializeWithStreamNative", new[] { "Initialize" } },
            { "IInitializeWithFileNative", new[] { "Initialize" } }
        };

        private const int FirstMethodSlot = 3; // after QueryInterface, AddRef and Release

        [Test]
        public void NativeHandlerInterfaceIidsMatchTheShellContracts()
        {
            List<string> offenders = new List<string>();
            foreach (KeyValuePair<string, Guid> expected in s_shellIids)
            {
                Type type = NativeInterface(expected.Key);
                if (type.GUID != expected.Value)
                {
                    offenders.Add(expected.Key + ": declared " + type.GUID + ", expected " + expected.Value);
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("; ", offenders));
        }

        [Test]
        public void NativeHandlerInterfacesArePublicComVisibleAndPreserveSig()
        {
            List<string> offenders = new List<string>();
            foreach (string name in s_shellIids.Keys)
            {
                Type type = NativeInterface(name);
                if (!type.IsPublic)
                {
                    // Only public interfaces reach the COM callable wrapper: an internal declaration
                    // compiles, is implemented, and is never seen by the shell.
                    offenders.Add(name + " is not public");
                }

                ComVisibleAttribute visible = type.GetCustomAttribute<ComVisibleAttribute>();
                if (visible == null || !visible.Value)
                {
                    offenders.Add(name + " is not [ComVisible(true)]");
                }

                InterfaceTypeAttribute kind = type.GetCustomAttribute<InterfaceTypeAttribute>();
                if (kind == null || kind.Value != ComInterfaceType.InterfaceIsIUnknown)
                {
                    offenders.Add(name + " is not InterfaceIsIUnknown");
                }

                Type[] inherited = type.GetInterfaces();
                if (inherited.Length != 0)
                {
                    // A base interface shifts every declared method up the vtable.
                    offenders.Add(name + " inherits " + string.Join(", ", inherited.Select(i => i.Name)));
                }

                foreach (MethodInfo method in type.GetMethods())
                {
                    bool preserved = (method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0 ||
                                     method.GetCustomAttribute<PreserveSigAttribute>() != null;
                    if (!preserved)
                    {
                        offenders.Add(name + "." + method.Name + " is not [PreserveSig]");
                    }
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("; ", offenders));
        }

        [Test]
        public void NativeHandlerSlotsFollowTheShellOrder()
        {
            List<string> offenders = new List<string>();
            foreach (KeyValuePair<string, string[]> expected in s_shellSlots)
            {
                string[] actual = SlotNames(expected.Key);
                if (!actual.SequenceEqual(expected.Value))
                {
                    offenders.Add(expected.Key + ": " + string.Join(", ", actual) +
                                  " (expected " + string.Join(", ", expected.Value) + ")");
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("; ", offenders));
        }

        [Test]
        public void NativeHandlerSlotsAgreeWithTheInteropDeclarations()
        {
            // ApkShellext2.Interop declares the same interfaces for the layout tests. Both copies
            // exist because only these are public (and therefore reach COM) while those are the
            // ones the interop probes activate. Agreement means neither copy has been edited alone.
            Dictionary<string, string> pairs = new Dictionary<string, string>
            {
                { "IExtractIconWNative", "IExtractIconWFlat" },
                { "IPersistFileNative", "IPersistFileFlat" },
                { "IQueryInfoNative", "IQueryInfoFlat" },
                { "IThumbnailProviderNative", "IThumbnailProviderFlat" },
                { "IInitializeWithStreamNative", "IInitializeWithStreamFlat" }
            };

            List<string> offenders = new List<string>();
            foreach (KeyValuePair<string, string> pair in pairs)
            {
                string[] native = SlotNames(pair.Key);
                string[] interop = SlotNames(pair.Value, "ApkShellext2.Interop.");
                if (!native.SequenceEqual(interop))
                {
                    offenders.Add(pair.Key + ": " + string.Join(", ", native) +
                                  " vs " + pair.Value + ": " + string.Join(", ", interop));
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("; ", offenders));
        }

        [Test]
        public void TheFirstDeclaredMethodLandsInSlotThree()
        {
            // A real vtable read: the CLR builds the callable wrapper from the declaration, so this
            // proves the shell would reach the declared method at the declared slot. Slots past the
            // first are pinned by declaration order instead of by probe calls - a call through a
            // slot whose declaration had drifted would marshal the wrong argument types and take
            // the test host down, which is a worse failure signal than a failed assertion.
            var icon = new ExtractIconProbe();
            IntPtr iface = Marshal.GetComInterfaceForObject(icon, typeof(IExtractIconWNative));
            try
            {
                var call = (GetIconLocationThunk)Marshal.GetDelegateForFunctionPointer(
                    SlotAddress(iface, FirstMethodSlot), typeof(GetIconLocationThunk));
                int hr = call(iface, 0u, IntPtr.Zero, 0, out int index, out uint flags);
                Assert.That(icon.Calls, Is.EqualTo("GetIconLocation"));
                Assert.That(hr, Is.EqualTo(0), "the probe answers S_OK");
            }
            finally
            {
                Marshal.Release(iface);
            }

            var persist = new PersistFileProbe();
            iface = Marshal.GetComInterfaceForObject(persist, typeof(IPersistFileNative));
            try
            {
                var call = (GetClassIdThunk)Marshal.GetDelegateForFunctionPointer(
                    SlotAddress(iface, FirstMethodSlot), typeof(GetClassIdThunk));
                int hr = call(iface, out Guid clsid);
                Assert.That(persist.Calls, Is.EqualTo("GetClassID"));
                Assert.That(hr, Is.EqualTo(0));
                Assert.That(clsid, Is.EqualTo(Guid.Empty));
            }
            finally
            {
                Marshal.Release(iface);
            }

            var thumbnail = new ThumbnailProbe();
            iface = Marshal.GetComInterfaceForObject(thumbnail, typeof(IThumbnailProviderNative));
            try
            {
                var call = (GetThumbnailThunk)Marshal.GetDelegateForFunctionPointer(
                    SlotAddress(iface, FirstMethodSlot), typeof(GetThumbnailThunk));
                int hr = call(iface, 96u, out IntPtr bitmap, out uint alpha);
                Assert.That(thumbnail.Calls, Is.EqualTo("GetThumbnail"));
                Assert.That(hr, Is.EqualTo(0));
                Assert.That(bitmap, Is.EqualTo(IntPtr.Zero));
                Assert.That(alpha, Is.EqualTo(0u));
            }
            finally
            {
                Marshal.Release(iface);
            }

            var query = new QueryInfoProbe();
            iface = Marshal.GetComInterfaceForObject(query, typeof(IQueryInfoNative));
            try
            {
                var call = (GetInfoTipThunk)Marshal.GetDelegateForFunctionPointer(
                    SlotAddress(iface, FirstMethodSlot), typeof(GetInfoTipThunk));
                int hr = call(iface, 0u, out string tip);
                Assert.That(query.Calls, Is.EqualTo("GetInfoTip"));
                Assert.That(hr, Is.EqualTo(0));
                Assert.That(tip, Is.Null);
            }
            finally
            {
                Marshal.Release(iface);
            }

            var initializable = new InitializeWithFileProbe();
            iface = Marshal.GetComInterfaceForObject(initializable, typeof(IInitializeWithFileNative));
            try
            {
                var call = (InitializeThunk)Marshal.GetDelegateForFunctionPointer(
                    SlotAddress(iface, FirstMethodSlot), typeof(InitializeThunk));
                // a null path: the slot is being identified, not the argument validated
                int hr = call(iface, null, 0u);
                Assert.That(initializable.Calls, Is.EqualTo("Initialize"));
                Assert.That(hr, Is.EqualTo(0));
            }
            finally
            {
                Marshal.Release(iface);
            }

            var streamInitializable = new InitializeWithStreamProbe();
            iface = Marshal.GetComInterfaceForObject(streamInitializable, typeof(IInitializeWithStreamNative));
            try
            {
                var call = (InitializeStreamThunk)Marshal.GetDelegateForFunctionPointer(
                    SlotAddress(iface, FirstMethodSlot), typeof(InitializeStreamThunk));
                // a null stream: the slot is being identified, not the stream read
                int hr = call(iface, IntPtr.Zero, 0u);
                Assert.That(streamInitializable.Calls, Is.EqualTo("Initialize"));
                Assert.That(hr, Is.EqualTo(0));
            }
            finally
            {
                Marshal.Release(iface);
            }
        }

        private static IntPtr SlotAddress(IntPtr iface, int slot)
        {
            IntPtr vtable = Marshal.ReadIntPtr(iface);
            Assert.That(vtable, Is.Not.EqualTo(IntPtr.Zero), "the object exposes no vtable");
            return Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        }

        private static string[] SlotNames(string typeName, string prefix = "ApkShellext2.Shell.Native.")
        {
            return NativeType(prefix + typeName)
                .GetMethods()
                .OrderBy(m => m.MetadataToken)
                .Select(m => m.Name)
                .ToArray();
        }

        private static Type NativeInterface(string name)
        {
            Type type = NativeType("ApkShellext2.Shell.Native." + name);
            Assert.That(type.IsInterface, Is.True, name + " is not an interface");
            return type;
        }

        private static Type NativeType(string fullName)
        {
            Type type = ProductApi.Assembly.GetType(fullName, throwOnError: false);
            Assert.That(type, Is.Not.Null, "type " + fullName + " was not found.");
            return type;
        }

        // The thunks below declare the interface pointer as the first parameter, because a COM slot
        // call has no other way to name the object: the address taken from the vtable is a plain
        // function, not a delegate bound to an instance.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIconLocationThunk(IntPtr self, uint uFlags, IntPtr szIconFile, int cchMax,
            out int piIndex, out uint pwFlags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetClassIdThunk(IntPtr self, out Guid clsid);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetThumbnailThunk(IntPtr self, uint cx, out IntPtr bitmap, out uint alpha);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInfoTipThunk(IntPtr self, uint flags, [MarshalAs(UnmanagedType.LPWStr)] out string tip);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InitializeThunk(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string path, uint mode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InitializeStreamThunk(IntPtr self, IntPtr pstream, uint mode);
    }

    // Minimal COM visible implementations used only to read the vtable the CLR builds. Each records
    // the method the shell's slot actually reached, so a shifted vtable shows up as the wrong name
    // rather than as a crash.
    [ComVisible(true)]
    public sealed class ExtractIconProbe : IExtractIconWNative
    {
        public string Calls { get; private set; }

        public int GetIconLocation(uint uFlags, IntPtr szIconFile, int cchMax, out int piIndex, out uint pwFlags)
        {
            Calls = "GetIconLocation";
            piIndex = 0;
            pwFlags = 0;
            return 0;
        }

        public int Extract(string pszFile, uint nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize)
        {
            Calls = "Extract";
            phiconLarge = IntPtr.Zero;
            phiconSmall = IntPtr.Zero;
            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class PersistFileProbe : IPersistFileNative
    {
        public string Calls { get; private set; }

        public int GetClassID(out Guid pClassID)
        {
            Calls = "GetClassID";
            pClassID = Guid.Empty;
            return 0;
        }

        public int IsDirty()
        {
            Calls = "IsDirty";
            return 0;
        }

        public int Load(string pszFileName, uint dwMode)
        {
            Calls = "Load";
            return 0;
        }

        public int Save(string pszFileName, bool fRemember)
        {
            Calls = "Save";
            return 0;
        }

        public int SaveCompleted(string pszFileName)
        {
            Calls = "SaveCompleted";
            return 0;
        }

        public int GetCurFile(out IntPtr ppszFileName)
        {
            Calls = "GetCurFile";
            ppszFileName = IntPtr.Zero;
            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class QueryInfoProbe : IQueryInfoNative
    {
        public string Calls { get; private set; }

        public int GetInfoTip(uint dwFlags, out string ppwszTip)
        {
            Calls = "GetInfoTip";
            ppwszTip = null;
            return 0;
        }

        public int GetInfoFlags(out uint pdwFlags)
        {
            Calls = "GetInfoFlags";
            pdwFlags = 0;
            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class ThumbnailProbe : IThumbnailProviderNative
    {
        public string Calls { get; private set; }

        public int GetThumbnail(uint cx, out IntPtr phbmp, out uint pdwAlpha)
        {
            Calls = "GetThumbnail";
            phbmp = IntPtr.Zero;
            pdwAlpha = 0;
            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class InitializeWithFileProbe : IInitializeWithFileNative
    {
        public string Calls { get; private set; }

        public int Initialize(string pszFilePath, uint grfMode)
        {
            Calls = "Initialize";
            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class InitializeWithStreamProbe : IInitializeWithStreamNative
    {
        public string Calls { get; private set; }

        public int Initialize(IStream pstream, uint grfMode)
        {
            Calls = "Initialize";
            return 0;
        }
    }
}
