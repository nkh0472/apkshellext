using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using ApkShellext2.Shell.Native;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Drives the native icon handler the way the shell does: IPersistFile::Load first, then
    /// IExtractIconW, and reads the HICON sizes back so a handler that ignored the requested size
    /// cannot pass. The info-tip tests compare every tip with the text the SharpShell handler
    /// produces for the same path, so the two implementations cannot drift apart while both are
    /// registered.
    /// </summary>
    [TestFixture]
    public class NativeIconAndInfoTipHandlerTests
    {
        private const int S_OK = 0;
        private const int S_FALSE = 1;
        private const uint GilNotFilename = 0x00000008;
        private const uint GilDontCache = 0x00000010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [TearDown]
        public void RestoreThreadCulture()
        {
            ApkShellext2.Utility.Localize();
        }

        private static uint Sizes(int large, int small)
        {
            return (uint)large | ((uint)small << 16);
        }

        private static Size SizeOf(IntPtr hicon)
        {
            using (Icon icon = Icon.FromHandle(hicon))
            {
                return icon.Size;
            }
        }

        private static void Extract(IconHandlerNative handler, uint nIconSize, out IntPtr large, out IntPtr small)
        {
            handler.Extract(null, 0, out large, out small, nIconSize);
        }

        private static IconHandlerNative LoadedIconHandler(string path)
        {
            IconHandlerNative handler = new IconHandlerNative();
            Assert.That(handler.Load(path, 0), Is.EqualTo(S_OK), "IPersistFile::Load failed for " + path);
            return handler;
        }

        [Test]
        public void LoadRecordsThePathSoExtractCanProduceBothSizes()
        {
            IconHandlerNative handler = LoadedIconHandler(MenuFixtures.PlainApkPath());
            IntPtr large, small;
            Extract(handler, Sizes(64, 16), out large, out small);
            try
            {
                Assert.That(large, Is.Not.EqualTo(IntPtr.Zero), "no large HICON after Load");
                Assert.That(small, Is.Not.EqualTo(IntPtr.Zero), "no small HICON after Load");
                Assert.That(SizeOf(large), Is.EqualTo(new Size(64, 64)), "large handle does not follow the requested size");
                Assert.That(SizeOf(small), Is.EqualTo(new Size(16, 16)), "small handle does not follow the requested size");
            }
            finally
            {
                DestroyIcon(large);
                DestroyIcon(small);
            }
        }

        [Test]
        public void ExtractBeforeLoadKeepsTheGenericIcon()
        {
            IconHandlerNative handler = new IconHandlerNative();
            IntPtr large, small;
            int hr = handler.Extract(null, 0, out large, out small, Sizes(64, 16));
            Assert.That(hr, Is.EqualTo(S_OK), "a handler without a path must still succeed with no handles");
            Assert.That(large, Is.EqualTo(IntPtr.Zero));
            Assert.That(small, Is.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void ExtractIgnoresAPathThatDoesNotExist()
        {
            IconHandlerNative handler = LoadedIconHandler(@"C:\apkshellext-tests\does-not-exist.apk");
            IntPtr large, small;
            int hr = handler.Extract(null, 0, out large, out small, Sizes(64, 16));
            Assert.That(hr, Is.EqualTo(S_OK), "a missing file asks for the generic icon, not an error");
            Assert.That(large, Is.EqualTo(IntPtr.Zero));
            Assert.That(small, Is.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void ExtractStillDrawsThePlatformIconForAPackageItCannotRead()
        {
            IconHandlerNative handler = LoadedIconHandler(MenuFixtures.HostileApkPath());
            IntPtr large, small;
            Extract(handler, Sizes(64, 16), out large, out small);
            try
            {
                Assert.That(large, Is.Not.EqualTo(IntPtr.Zero), "an unreadable .apk must fall back to the Android icon");
                Assert.That(SizeOf(large), Is.EqualTo(new Size(64, 64)));
            }
            finally
            {
                DestroyIcon(large);
                DestroyIcon(small);
            }
        }

        [Test]
        public void GetIconLocationAsksForExtractAndDisablesTheCache()
        {
            IconHandlerNative handler = LoadedIconHandler(MenuFixtures.PlainApkPath());
            const string sentinel = "sentinel";
            IntPtr buffer = Marshal.StringToHGlobalUni(sentinel);
            try
            {
                int index;
                uint flags;
                int hr = handler.GetIconLocation(0, buffer, 260, out index, out flags);
                Assert.That(hr, Is.EqualTo(S_OK));
                Assert.That(index, Is.EqualTo(0), "the icon is not a file, so there is no index");
                Assert.That(flags & GilNotFilename, Is.EqualTo(GilNotFilename), "the shell must call Extract");
                Assert.That(flags & GilDontCache, Is.EqualTo(GilDontCache), "a package icon must not be cached by the shell");
                Assert.That(Marshal.PtrToStringUni(buffer), Is.EqualTo(sentinel),
                    "a GIL_NOTFILENAME handler must not write into the shell's filename buffer");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// The patched icon handler answers for the CLSID and ProgID the registrar writes; the
        /// attribute values are bare literals because GuidAttribute rejects the braced constants
        /// in ShellGuids, so this is the test that catches a typo in either one.
        /// </summary>
        [Test]
        public void IconHandlerCarriesTheRegisteredIdentity()
        {
            AssertIdentity(typeof(IconHandlerNative), "IconHandlerClsid", "IconHandlerProgId");
        }

        [Test]
        public void InfoTipHandlerCarriesTheRegisteredIdentity()
        {
            AssertIdentity(typeof(InfoTipHandlerNative), "InfoTipClsid", "InfoTipProgId");
        }

        private static void AssertIdentity(Type handler, string clsidConstant, string progIdConstant)
        {
            string expectedClsid = ShellGuidConstant(clsidConstant);
            string expectedProgId = ShellGuidConstant(progIdConstant);
            GuidAttribute guid = (GuidAttribute)Attribute.GetCustomAttribute(handler, typeof(GuidAttribute));
            ProgIdAttribute progId = (ProgIdAttribute)Attribute.GetCustomAttribute(handler, typeof(ProgIdAttribute));
            Assert.That(guid, Is.Not.Null, handler.Name + " has no [Guid]");
            Assert.That(progId, Is.Not.Null, handler.Name + " has no [ProgId]");
            Assert.That(guid.Value, Is.EqualTo(expectedClsid.Trim('{', '}')).IgnoreCase,
                handler.Name + " CLSID does not match ShellGuids." + clsidConstant);
            Assert.That(progId.Value, Is.EqualTo(expectedProgId),
                handler.Name + " ProgID does not match ShellGuids." + progIdConstant);
        }

        private static string ShellGuidConstant(string name)
        {
            Type guids = ProductApi.Assembly.GetType("ApkShellext2.Registration.ShellGuids", true);
            FieldInfo field = guids.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "ShellGuids." + name + " was not found; update the test");
            return (string)field.GetRawConstantValue();
        }

        [Test]
        public void TipTextIsIdenticalToTheSharpShellHandler()
        {
            using (new SettingsScope().Set("Language", "en-US"))
            {
                AssertSameTip("a plain apk", MenuFixtures.PlainApkPath());
                AssertSameTip("an apk that cannot be read", MenuFixtures.HostileApkPath());
                AssertSameTip("an ipa", MenuFixtures.IpaPath());
                AssertSameTip("an aab", MenuFixtures.AabPath());
                AssertSameTip("an unknown extension", MenuFixtures.UnknownPath());
                AssertSameTip("a file that does not exist", @"C:\apkshellext-tests\does-not-exist.apk");
            }
        }

        private static void AssertSameTip(string label, string path)
        {
            string expected = SharpShellTip(path);
            InfoTipHandlerNative handler = new InfoTipHandlerNative();
            Assert.That(handler.Load(path, 0), Is.EqualTo(S_OK));
            string actual;
            int hr = handler.GetInfoTip(0, out actual);
            Assert.That(hr, Is.EqualTo(S_OK), "GetInfoTip failed for " + label);
            Assert.That(actual, Is.EqualTo(expected), "the tip text differs from the SharpShell handler for " + label);
        }

        /// <summary>
        /// Calls the still-registered SharpShell handler the way its COM server does, through
        /// reflection: SelectedItemPath is the only state it keeps and GetInfo is protected.
        /// The path is written through the auto-property's backing field because the setter is
        /// private and reflection on an inherited private setter is unreliable on .NET Framework.
        /// </summary>
        private static string SharpShellTip(string path)
        {
            Type type = ProductApi.Assembly.GetType("ApkShellext2.ApkInfoTipHandler", true);
            object handler = Activator.CreateInstance(type);
            FieldInfo selectedPath = null;
            for (Type t = type; t != null && selectedPath == null; t = t.BaseType)
            {
                selectedPath = t.GetField("<SelectedItemPath>k__BackingField",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            Assert.That(selectedPath, Is.Not.Null, "the SharpShell SelectedItemPath backing field was not found");
            selectedPath.SetValue(handler, path);
            MethodInfo getInfo = type.GetMethod("GetInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(getInfo, Is.Not.Null, "ApkInfoTipHandler.GetInfo was not found");
            ParameterInfo[] parameters = getInfo.GetParameters();
            object infoType = Enum.Parse(parameters[0].ParameterType, "InfoTip");
            return (string)getInfo.Invoke(handler, new object[] { infoType, false });
        }

        [Test]
        public void GetInfoTipWithoutAPathReturnsNoTip()
        {
            InfoTipHandlerNative handler = new InfoTipHandlerNative();
            string tip;
            Assert.That(handler.GetInfoTip(0, out tip), Is.EqualTo(S_FALSE), "no path means no tip, not an error");
            Assert.That(tip, Is.Null);

            Assert.That(handler.Load(string.Empty, 0), Is.EqualTo(S_OK));
            Assert.That(handler.GetInfoTip(0, out tip), Is.EqualTo(S_FALSE));
            Assert.That(tip, Is.Null);
        }

        [Test]
        public void GetInfoFlagsReportsNoSpecialFlags()
        {
            uint flags;
            Assert.That(new InfoTipHandlerNative().GetInfoFlags(out flags), Is.EqualTo(S_OK));
            Assert.That(flags, Is.EqualTo(0u));
        }
    }
}
