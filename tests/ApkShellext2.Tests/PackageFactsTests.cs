using System;
using System.Collections.Generic;
using System.IO;
using ApkQuickReader;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The read-only platform facts every reader now publishes: the sdk levels as the
    /// manifest spells them, the debuggable flag with its three states, and the packer
    /// name. The manifests here are compiled by AxmlBuilder so the cases the sample
    /// corpus does not contain (codename level, absent uses-sdk, explicit false, a
    /// tools: override, a corrupt attribute table) are exercised against the same walk
    /// a real apk goes through.
    /// </summary>
    [TestFixture]
    public class PackageFactsTests
    {
        private const string ManifestEntry = "AndroidManifest.xml";
        private const string ResourcesEntry = "resources.arsc";
        private const string ResourcesFixture = "spectroid-resources.arsc";
        private const string AabManifestEntry = "base/manifest/AndroidManifest.xml";

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf("tests/ApkShellext2.Tests/fixtures/" + name);
            Assert.That(path, Does.Exist, "fixture {0} is missing; regenerate with fixtures\\make-fixtures.ps1", name);
            return File.ReadAllBytes(path);
        }

        /// <summary>A manifest root with the identity attributes every apk carries.</summary>
        private static AxmlBuilder.Element Manifest()
        {
            return AxmlBuilder.Root("manifest")
                .Attr("package", "com.example.app")
                .Attr("versionName", "1.0")
                .Attr("versionCode", "1");
        }

        private static AxmlBuilder.Element WithSdk(AxmlBuilder.Element root, int minSdk, int targetSdk)
        {
            root.Add("uses-sdk")
                .AndroidInt("minSdkVersion", minSdk)
                .AndroidInt("targetSdkVersion", targetSdk);
            return root;
        }

        private static byte[] ApkBytes(AxmlBuilder.Element manifest)
        {
            return ApkBytes(AxmlBuilder.Build(manifest), Fixture(ResourcesFixture));
        }

        private static byte[] ApkBytes(byte[] manifest, byte[] resources)
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = ManifestEntry, Data = manifest },
                new ZipBuilder.EntrySpec { Name = ResourcesEntry, Data = resources });
        }

        private static ApkReader Apk(AxmlBuilder.Element manifest)
        {
            byte[] apk = ApkBytes(manifest);
            return new ApkReader(new MemoryStream(apk, 0, apk.Length, false, false));
        }

        private static string TempFile(string extension, byte[] bytes)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "apkshellext-facts-" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        [Test]
        public void MinSdkAndTargetSdkKeepTheNumbersAsWritten()
        {
            AxmlBuilder.Element root = WithSdk(Manifest(), 21, 33);
            root.Add("application").AndroidAttr("label", "Example");

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.MinSdk, Is.EqualTo("21"));
                Assert.That(reader.TargetSdk, Is.EqualTo("33"));
            }
        }

        [Test]
        public void ACodenameMinSdkIsKeptAsTheCodename()
        {
            // "S" and "21" must not be normalised: the report shows the text the manifest
            // carries, and converting a codename through a number would invent a level.
            AxmlBuilder.Element root = Manifest();
            root.Add("uses-sdk")
                .AndroidAttr("minSdkVersion", "S")
                .AndroidInt("targetSdkVersion", 31);

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.MinSdk, Is.EqualTo("S"));
                Assert.That(reader.TargetSdk, Is.EqualTo("31"));
            }
        }

        [Test]
        public void FactsAreEmptyWithoutAUsesSdkElement()
        {
            AxmlBuilder.Element root = Manifest();
            root.Add("application");

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.MinSdk, Is.Empty);
                Assert.That(reader.TargetSdk, Is.Empty);
            }
        }

        [Test]
        public void DebuggableIsTrueWhenTheManifestSaysSo()
        {
            AxmlBuilder.Element root = WithSdk(Manifest(), 21, 33);
            root.Add("application").AndroidBool("debuggable", true);

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.Debuggable, Is.True);
            }
        }

        [Test]
        public void DebuggableIsFalseWhenTheManifestSaysSo()
        {
            // An explicit false is not the same as an absent attribute: the tooltip says
            // "Release" either way, but only the explicit false is a declaration.
            AxmlBuilder.Element root = WithSdk(Manifest(), 21, 33);
            root.Add("application").AndroidBool("debuggable", false);

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.Debuggable, Is.False);
                Assert.That(reader.Debuggable.HasValue, Is.True);
            }
        }

        [Test]
        public void DebuggableIsNullWhenTheAttributeIsAbsent()
        {
            AxmlBuilder.Element root = WithSdk(Manifest(), 21, 33);
            root.Add("application").AndroidAttr("label", "Example");

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.Debuggable, Is.Null);
            }
        }

        [Test]
        public void ANamespacedOverrideIsNotTakenAsThePlatformValue()
        {
            // tools:targetSdkVersion is a build-time override that aapt leaves in the
            // manifest; only the android namespace names the platform level.
            AxmlBuilder.Element root = Manifest();
            AxmlBuilder.Element sdk = root.Add("uses-sdk");
            sdk.AndroidInt("minSdkVersion", 21);
            sdk.ToolsInt("targetSdkVersion", 33);
            root.Add("application");

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.MinSdk, Is.EqualTo("21"));
                Assert.That(reader.TargetSdk, Is.Empty);
            }
        }

        [Test]
        public void ABrokenAttributeTableLeavesTheFactsEmptyWithoutBreakingTheAppName()
        {
            // uses-sdk claims 64 attributes while its chunk holds one: the facts walk must
            // refuse to follow it, and the identity attributes must keep their own path.
            AxmlBuilder.Element root = Manifest();
            AxmlBuilder.Element sdk = root.Add("uses-sdk");
            sdk.AndroidInt("minSdkVersion", 21);
            sdk.DeclaredAttributeCount = 64;
            root.Add("application").AndroidAttr("label", "Example");

            using (ApkReader reader = Apk(root))
            {
                Assert.That(reader.MinSdk, Is.Empty);
                Assert.That(reader.TargetSdk, Is.Empty);
                Assert.That(reader.Debuggable, Is.Null);
                Assert.That(reader.AppName, Is.EqualTo("Example"));
                Assert.That(reader.PackageName, Is.EqualTo("com.example.app"));
                Assert.That(reader.Version, Is.EqualTo("1.0"));
            }
        }

        [Test]
        public void TheRealManifestGivesTheSdkLevels()
        {
            byte[] apk = ApkBytes(Fixture("spectroid-manifest.bin"), Fixture(ResourcesFixture));

            using (ApkReader reader = new ApkReader(new MemoryStream(apk, 0, apk.Length, false, false)))
            {
                Assert.That(reader.MinSdk, Is.EqualTo("14"));
                Assert.That(reader.TargetSdk, Is.EqualTo("33"));
                Assert.That(reader.Debuggable, Is.Null);
                Assert.That(reader.AppName, Is.EqualTo("Spectroid"));
            }
        }

        [Test]
        public void TheAabBaseManifestGivesTheSdkLevels()
        {
            AxmlBuilder.Element root = WithSdk(Manifest(), 24, 34);
            root.Add("application").AndroidBool("debuggable", true);

            byte[] aab = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = AabManifestEntry,
                Data = AxmlBuilder.Build(root)
            });
            string path = TempFile(".aab", aab);
            try
            {
                using (AabReader reader = new AabReader(path))
                {
                    Assert.That(reader.MinSdk, Is.EqualTo("24"));
                    Assert.That(reader.TargetSdk, Is.EqualTo("34"));
                    Assert.That(reader.Debuggable, Is.True);
                    Assert.That(reader.PackageName, Is.EqualTo("com.example.app"));
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        [Test]
        public void AnIpaReportsNoAndroidFacts()
        {
            byte[] plist = BinaryPlistBuilder.Build(new List<byte[]>
            {
                BinaryPlistBuilder.DictionaryObject(1, 2, 3, 4),
                BinaryPlistBuilder.StringObject("CFBundleDisplayName"),
                BinaryPlistBuilder.StringObject("My App"),
                BinaryPlistBuilder.StringObject("CFBundleIdentifier"),
                BinaryPlistBuilder.StringObject("com.example.app")
            });
            byte[] ipa = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "Payload/MyApp.app/Info.plist",
                Data = plist
            });

            using (IpaReader reader = new IpaReader(new MemoryStream(ipa, 0, ipa.Length, false, false)))
            {
                Assert.That(reader.MinSdk, Is.Empty);
                Assert.That(reader.TargetSdk, Is.Empty);
                Assert.That(reader.Debuggable, Is.Null);
                Assert.That(reader.Packer, Is.Empty);
            }
        }

        [Test]
        public void AnAppxReportsNoAndroidFacts()
        {
            byte[] package = AppxFixtures.Appx();

            using (AppxReader reader = new AppxReader(new MemoryStream(package, 0, package.Length, false, false)))
            {
                Assert.That(reader.MinSdk, Is.Empty);
                Assert.That(reader.TargetSdk, Is.Empty);
                Assert.That(reader.Debuggable, Is.Null);
                Assert.That(reader.Packer, Is.Empty);
            }
        }

        [Test]
        public void ASplitBundleReusesTheBaseApkFacts()
        {
            // The container holds no manifest of its own; the base apk does.
            AxmlBuilder.Element root = WithSdk(Manifest(), 23, 30);
            root.Add("application").AndroidBool("debuggable", true);
            byte[] baseApk = ApkBytes(root);

            byte[] bundle = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "base.apk", Data = baseApk },
                new ZipBuilder.EntrySpec { Name = "split_config.en.apk", Data = baseApk });
            string path = TempFile(".apks", bundle);
            try
            {
                using (SplitApkBundleReader reader = new SplitApkBundleReader(path))
                {
                    Assert.That(reader.MinSdk, Is.EqualTo("23"));
                    Assert.That(reader.TargetSdk, Is.EqualTo("30"));
                    Assert.That(reader.Debuggable, Is.True);
                }
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        [Test]
        public void TheBaseReaderReportsEmptyFacts()
        {
            // A reader that has no manifest at all must still answer the four members.
            using (AppPackageReader reader = new AppPackageReader())
            {
                Assert.That(reader.MinSdk, Is.Empty);
                Assert.That(reader.TargetSdk, Is.Empty);
                Assert.That(reader.Debuggable, Is.Null);
                Assert.That(reader.Packer, Is.Empty);
            }
        }
    }
}
