using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// ApkReader end to end over a complete in-memory apk: the manifest and the resource
    /// table are the real Spectroid bytes (plus the documented stage 0 patches), packed
    /// with ZipBuilder. The parser-level caps are pinned by ArscParserTests; these tests
    /// prove the same hostile tables cannot take the shell's entry point down.
    /// </summary>
    [TestFixture]
    public class ApkReaderTests
    {
        private const string FixtureDir = "tests/ApkShellext2.Tests/fixtures/";

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf(FixtureDir + name);
            Assert.That(path, Does.Exist, "fixture " + path + " is missing; regenerate it with X:\\apks-tn\\fixtures\\make-fixtures.ps1.");
            return File.ReadAllBytes(path);
        }

        private static byte[] Apk(string resourceTable, string manifest = "spectroid-manifest.bin")
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "resources.arsc", Data = Fixture(resourceTable) },
                new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = Fixture(manifest) });
        }

        private static ApkQuickReader.ApkReader Open(string resourceTable, string manifest = "spectroid-manifest.bin")
        {
            return new ApkQuickReader.ApkReader(new MemoryStream(Apk(resourceTable, manifest)));
        }

        private static void AssertReturnsQuickly(string what, Action action, int maxMilliseconds = 2000)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Assert.That(() => action(), Throws.Nothing);
            watch.Stop();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(maxMilliseconds),
                what + " must return instead of walking an attacker-chosen table");
        }

        [Test]
        public void ReadsTheManifestFieldsOfARealApk()
        {
            ApkQuickReader.ApkReader reader = Open("spectroid-resources.arsc");
            Assert.That(reader.PackageName, Is.EqualTo("org.intoorbit.spectrum"));
            Assert.That(reader.AppName, Is.Not.Null.And.Not.Empty);
            Assert.That(reader.Version, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EntryCountCap_IsEnforcedThroughTheFullReader()
        {
            // string chunk entryCount = 0x20000: the label lookup must come back empty.
            ApkQuickReader.ApkReader reader = Open("spectroid-arsc-entries.bin");
            string name = null;
            AssertReturnsQuickly("the label lookup", () => name = reader.AppName);
            // The string chunk is rejected, so the reference stays unresolved and the
            // manifest walk falls back to the visible hex placeholder, not table text.
            Assert.That(name, Does.StartWith("(0x"));
            Assert.That(name, Is.Not.EqualTo("Spectroid"));
        }

        [Test]
        public void PackageCountCap_IsEnforcedThroughTheFullReader()
        {
            // packageCount = 0x200: no package can be trusted, so no label resolves.
            ApkQuickReader.ApkReader reader = Open("spectroid-arsc-packages.bin");
            string name = null;
            AssertReturnsQuickly("the label lookup", () => name = reader.AppName);
            // Same contract as the entry cap: the lookup is rejected and the unresolved
            // reference surfaces as the hex placeholder, never as attacker table text.
            Assert.That(name, Does.StartWith("(0x"));
            Assert.That(name, Is.Not.EqualTo("Spectroid"));
        }

        [Test]
        public void ConfigSizeCap_DoesNotBreakTheIconPath()
        {
            // The colour/menu/drawable configs are oversized or truncated; the icon chain
            // must answer (null or a bitmap) instead of throwing through the shell entry.
            ApkQuickReader.ApkReader reader = Open("spectroid-arsc-configsize.bin");
            AssertReturnsQuickly("the icon lookup", () => { System.Drawing.Bitmap icon = reader.Icon; });
        }

        [Test]
        [Timeout(5000)]
        public void ReferenceChainCap_ReturnsWithoutWalkingTheWholeChain()
        {
            // The chain fixture rewrites 0x28 string entries into one linear reference
            // chain: without the depth cap the label lookup would walk all of them.
            ApkQuickReader.ApkReader reader = Open("spectroid-arsc-chain.bin");
            AssertReturnsQuickly("the label lookup", () => { string name = reader.AppName; });
        }

        [Test]
        [Timeout(5000)]
        public void SelfReference_DoesNotRecurseThroughTheReader()
        {
            ApkQuickReader.ApkReader reader = Open("spectroid-arsc-selfref.bin");
            AssertReturnsQuickly("the label lookup", () => { string name = reader.AppName; });
        }
    }
}
