using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The completeness hint a package carries: the rules themselves, the reader that
    /// collects the structural evidence, and the containers that only forward it. The
    /// healthy cases use the real Spectroid bytes; every hostile case is a synthetic
    /// archive built with ZipBuilder/AxmlBuilder, so the tests never depend on a local
    /// package directory.
    /// </summary>
    [TestFixture]
    public class PackageVerdictTests
    {
        private const string FixtureDir = "tests/ApkShellext2.Tests/fixtures/";
        private const string HealthyIconEntry = "res/u3.png";

        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "apkshellext-verdict-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf(FixtureDir + name);
            Assert.That(path, Does.Exist, "fixture " + path + " is missing; regenerate it with tests\\ApkShellext2.Tests\\fixtures\\make-fixtures.ps1.");
            return File.ReadAllBytes(path);
        }

        private static byte[] Apk(byte[] manifest, byte[] resourceTable = null, params string[] extraEntries)
        {
            List<ZipBuilder.EntrySpec> entries = new List<ZipBuilder.EntrySpec>();
            entries.Add(new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = manifest });
            if (resourceTable != null) entries.Add(new ZipBuilder.EntrySpec { Name = "resources.arsc", Data = resourceTable });
            foreach (string name in extraEntries)
            {
                entries.Add(new ZipBuilder.EntrySpec { Name = name, Data = new byte[] { 0x89, 0x50, 0x4e, 0x47 } });
            }
            return ZipBuilder.Build(entries.ToArray());
        }

        private static byte[] Manifest(bool debuggable = false, string icon = null)
        {
            AxmlBuilder.Element root = AxmlBuilder.Root("manifest")
                .Attr("package", "com.example.synthetic")
                .Attr("versionName", "1.0")
                .Attr("versionCode", "1");
            AxmlBuilder.Element application = root.Add("application").AndroidAttr("label", "Synthetic");
            if (debuggable) application.AndroidBool("debuggable", true);
            if (icon != null) application.AndroidAttr("icon", icon);
            return AxmlBuilder.Build(root);
        }

        private static ApkQuickReader.ApkReader Open(byte[] apk)
        {
            return new ApkQuickReader.ApkReader(new MemoryStream(apk, 0, apk.Length, false, false));
        }

        [Test]
        public void AHealthyPackageIsInfoWithNoEvidence()
        {
            byte[] apk = Apk(Fixture("spectroid-manifest.bin"), Fixture("spectroid-resources.arsc"), HealthyIconEntry);
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.IconEntryPath, Is.EqualTo(HealthyIconEntry), "the fixture table maps the launcher icon to this entry");
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Info));
                Assert.That(reader.VerdictEvidence, Is.Empty);
            }
        }

        [Test]
        public void AManifestIconMissingFromTheArchiveIsTampered()
        {
            byte[] apk = Apk(Fixture("spectroid-manifest.bin"), Fixture("spectroid-resources.arsc"));
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.IconEntryPath, Is.EqualTo(HealthyIconEntry));
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Tampered));
                Assert.That(reader.VerdictEvidence, Is.EqualTo("the icon the manifest names is not in the package"));
            }
        }

        [Test]
        public void AResourceReferenceWithoutTheResourceTableIsTampered()
        {
            byte[] apk = Apk(Fixture("spectroid-manifest.bin"));
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Tampered));
                Assert.That(reader.VerdictEvidence, Is.EqualTo("resources.arsc is missing"));
            }
        }

        [TestCase(SigningBlockBuilder.Walle, "Walle")]
        [TestCase(SigningBlockBuilder.Frosting, "Frosting")]
        [TestCase(SigningBlockBuilder.PackerNg, "PackerNG")]
        [TestCase(SigningBlockBuilder.VasDollyV2, "VasDollyV2")]
        public void APackerNamedByTheSigningBlockIsSuspicious(uint blockId, string packer)
        {
            byte[] apk = SigningBlockBuilder.Insert(Apk(Manifest()), SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(blockId)));
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.Packer, Is.EqualTo(packer));
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Suspicious));
                Assert.That(reader.VerdictEvidence, Is.EqualTo("signed by " + packer));
            }
        }

        [Test]
        public void ADebuggableManifestIsSuspicious()
        {
            byte[] apk = Apk(Manifest(debuggable: true));
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Suspicious));
                Assert.That(reader.VerdictEvidence, Is.EqualTo("the manifest marks the package debuggable"));
            }
        }

        [Test]
        public void EvidenceKeepsTheTamperedReasonsFirstAndCapsAtThree()
        {
            byte[] apk = Apk(Manifest(debuggable: true, icon: "res/missing.png"), Fixture("spectroid-resources.arsc"));
            apk = SigningBlockBuilder.Insert(apk, SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Walle)));
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Tampered), "one tampered fact outweighs any number of suspicious facts");
                Assert.That(reader.VerdictEvidence, Is.EqualTo(
                    "the icon the manifest names is not in the package; " +
                    "signed by Walle; " +
                    "the manifest marks the package debuggable"),
                    "the tampered reasons come first and only three statements survive");
            }
        }

        [Test]
        public void ASplitBundleForwardsTheBaseVerdict()
        {
            byte[] baseApk = Apk(Manifest(debuggable: true));
            byte[] bundle = ZipBuilder.Build(new ZipBuilder.EntrySpec { Name = "base.apk", Data = baseApk });
            string path = Path.Combine(directory, "synthetic-bundle.apks");
            File.WriteAllBytes(path, bundle);
            using (ApkQuickReader.SplitApkBundleReader reader = new ApkQuickReader.SplitApkBundleReader(path))
            {
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Suspicious));
                Assert.That(reader.VerdictEvidence, Is.EqualTo("the manifest marks the package debuggable"));
            }
        }

        [Test]
        public void AMalformedManifestDoesNotThrowAndStaysInfo()
        {
            byte[] apk = Apk(new byte[] { 0x03, 0x00 });
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Info));
                Assert.That(reader.VerdictEvidence, Is.Empty);
            }
        }

        [Test]
        public void AnApkWithoutAnIconReferenceOrATableIsInfo()
        {
            byte[] apk = Apk(Manifest());
            using (ApkQuickReader.ApkReader reader = Open(apk))
            {
                Assert.That(reader.IconEntryPath, Is.Empty);
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Info));
                Assert.That(reader.VerdictEvidence, Is.Empty);
            }
        }

        [Test]
        public void TheBaseReaderHasTheNeutralVerdict()
        {
            using (AppPackageReader reader = new AppPackageReader())
            {
                Assert.That(reader.Verdict, Is.EqualTo(PackageVerdict.Info));
                Assert.That(reader.VerdictEvidence, Is.Empty);
            }
        }

        [Test]
        public void TheRulesKeepTheTamperedEvidenceFirstAndCapAtThree()
        {
            PackageVerdictEvidence evidence = new PackageVerdictEvidence
            {
                IconEntryMissing = true,
                Packer = "Walle",
                Debuggable = true
            };
            string text;
            Assert.That(PackageVerdictRules.Grade(evidence, out text), Is.EqualTo(PackageVerdict.Tampered));
            Assert.That(text, Is.EqualTo(
                "the icon the manifest names is not in the package; " +
                "signed by Walle; " +
                "the manifest marks the package debuggable"));
        }

        [Test]
        public void TheRulesOnlySayInfoForEmptyEvidence()
        {
            string text;
            Assert.That(PackageVerdictRules.Grade(new PackageVerdictEvidence(), out text), Is.EqualTo(PackageVerdict.Info));
            Assert.That(text, Is.Empty);
            Assert.That(PackageVerdictRules.Grade(null, out text), Is.EqualTo(PackageVerdict.Info));
            Assert.That(text, Is.Empty);
        }

        [TestCase(typeof(AabReader))]
        [TestCase(typeof(IpaReader))]
        [TestCase(typeof(AppxReader))]
        [TestCase(typeof(AppxBundleReader))]
        public void OnlyTheApkReadersGradeTheirInput(Type container)
        {
            // Aab/Ipa/Appx/Appxbundle have no apk manifest, so they must inherit the neutral
            // verdict instead of pretending to grade something they cannot see.
            Assert.That(container.GetProperty("Verdict").DeclaringType, Is.EqualTo(typeof(AppPackageReader)), container.Name);
            Assert.That(container.GetProperty("VerdictEvidence").DeclaringType, Is.EqualTo(typeof(AppPackageReader)), container.Name);
        }
    }
}
