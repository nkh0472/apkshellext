using System;
using System.Reflection;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the extension to reader mapping that moved out of AppPackageReader
    /// into PackageReaderFactory. The table covers every supported extension
    /// in mixed case, the split-container aliases that map onto the base apk
    /// reader, and the NotSupportedException text for unknown suffixes.
    ///
    /// Create() is deliberately not exercised with a real or non-existent path
    /// here: a Create() call enters the parse path (zip/plist/appx work over
    /// real bytes) and a missing file throws whatever the concrete reader
    /// throws, so it would test the parsers rather than the dispatch table.
    /// Create() dispatches on GetAppType and then constructs the reader, so
    /// covering GetAppType covers the mapping half of the factory.
    /// </summary>
    [TestFixture]
    public class PackageReaderFactoryTests
    {
        [TestCase("a.APK", AppPackageReader.AppType.AndroidApp)]
        [TestCase("b.Apk", AppPackageReader.AppType.AndroidApp)]
        [TestCase("c.apks", AppPackageReader.AppType.AndroidApp)]
        [TestCase("d.apkm", AppPackageReader.AppType.AndroidApp)]
        [TestCase("e.xapk", AppPackageReader.AppType.AndroidApp)]
        [TestCase("f.aab", AppPackageReader.AppType.AndroidAab)]
        [TestCase("g.ipa", AppPackageReader.AppType.iOSApp)]
        [TestCase("h.appx", AppPackageReader.AppType.WindowsPhoneApp)]
        [TestCase("i.appxbundle", AppPackageReader.AppType.WindowsPhoneAppBundle)]
        [TestCase(@"C:\some dir\j.APPXBUNDLE", AppPackageReader.AppType.WindowsPhoneAppBundle)]
        public void GetAppType_MapsKnownExtensions(string fileName, AppPackageReader.AppType expected)
        {
            Assert.That(PackageReaderFactory.GetAppType(fileName), Is.EqualTo(expected));
        }

        [TestCase(".zip", ".zip type is not supported.")]
        [TestCase("PORTABLE", " type is not supported.")]
        [TestCase("路径带空格.TXT", ".txt type is not supported.")]
        public void GetAppType_RejectsUnknownExtension(string fileName, string expectedMessage)
        {
            NotSupportedException ex = Assert.Throws<NotSupportedException>(
                () => PackageReaderFactory.GetAppType(fileName));
            Assert.That(ex.Message, Is.EqualTo(expectedMessage));
        }

        [Test]
        public void AppPackageReader_ImplementsIPackageReader()
        {
            Assert.That(typeof(IPackageReader).IsAssignableFrom(typeof(AppPackageReader)), Is.True);
            Assert.That(typeof(IPackageReader).IsAssignableFrom(typeof(ApkQuickReader.ApkReader)), Is.True);
        }

        [Test]
        public void AppPackageReader_NoLongerOwnsStaticDispatch()
        {
            // the static entry points live on PackageReaderFactory; keeping a
            // stale copy on the base class would let call sites drift back
            Assert.That(typeof(AppPackageReader).GetMethod("Read",
                BindingFlags.Public | BindingFlags.Static), Is.Null);
            Assert.That(typeof(AppPackageReader).GetMethod("getAppType",
                BindingFlags.Public | BindingFlags.Static), Is.Null);
        }
    }
}
