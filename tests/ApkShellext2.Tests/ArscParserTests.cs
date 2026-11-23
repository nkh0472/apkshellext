using System;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the shared resource-table parser (ApkQuickReader.ArscParser) against real bytes
    /// cut out of Spectroid_org.intoorbit.spectrum_1.1.2(31).apk.
    ///
    /// The fixtures are not hand-written tables: they are byte ranges of resources.arsc /
    /// AndroidManifest.xml cut out of that apk, with a handful of documented fields patched,
    /// so the tests exercise the parser over the same byte shapes it meets in the wild. The
    /// generator behind them is a development-time tool and lives outside this repository
    /// (fixtures/README.md lists the offsets and patches it applies).
    ///
    /// What is pinned here matters twice over: the four cost caps are what keeps a hostile
    /// apk from turning a shell icon request into an out-of-memory or an endless walk, and the
    /// name lookup is what decides which drawable becomes the icon.
    /// </summary>
    [TestFixture]
    public class ArscParserTests
    {
        private const string FixtureDir = "tests/ApkShellext2.Tests/fixtures/";

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf(FixtureDir + name);
            Assert.That(path, Does.Exist, "fixture " + path + " is missing; regenerate it with X:\\apks-tn\\fixtures\\make-fixtures.ps1.");
            return System.IO.File.ReadAllBytes(path);
        }

        /// <summary>The parser the readers build: table bytes + the shared string-pool reader.</summary>
        private static object ParserOver(byte[] table)
        {
            return ProductApi.ArscOf(ProductApi.NewAxmlParser(table, resolveReferences: true));
        }

        private static void AssertEmpty(object resource)
        {
            Assert.That(ProductApi.ResourceCount(resource), Is.EqualTo(0u), "the guard must reject the whole resource, not part of it");
        }

        [Test]
        public void CapsAreTheStageZeroLimits()
        {
            // Loosening these is a security regression, not a tuning knob.
            Assert.That(ProductApi.ArscConstant("MaxResourceConfigSize"), Is.EqualTo(1024L));
            Assert.That(ProductApi.ArscConstant("MaxResourcePackages"), Is.EqualTo(256L));
            Assert.That(ProductApi.ArscConstant("MaxResourceEntries"), Is.EqualTo(0x10000L));
            Assert.That(ProductApi.ArscConstant("MaxResourceRefDepth"), Is.EqualTo(32L));
        }

        [Test]
        public void FindResourceId_ResolvesTheLauncherIcon()
        {
            Assert.That(ProductApi.ArscFindResourceId(ParserOver(Fixture("spectroid-resources.arsc")), "mipmap", "ic_launcher"),
                Is.EqualTo(0x7F080000u));
        }

        [Test]
        public void FindResourceId_ResolvesAStringEntry()
        {
            Assert.That(ProductApi.ArscFindResourceId(ParserOver(Fixture("spectroid-resources.arsc")), "string", "action_pause"),
                Is.EqualTo(0x7F090001u));
        }

        [Test]
        public void FindResourceId_ResolvesAColorEntry()
        {
            Assert.That(ProductApi.ArscFindResourceId(ParserOver(Fixture("spectroid-resources.arsc")), "color", "ic_launcher_background"),
                Is.EqualTo(0x7F030007u));
        }

        [Test]
        public void FindResourceId_ReturnsZeroForAnUnknownName()
        {
            // "no such key" must look like "not found", never like package 0 entry 0.
            Assert.That(ProductApi.ArscFindResourceId(ParserOver(Fixture("spectroid-resources.arsc")), "mipmap", "no_such_key"),
                Is.EqualTo(0u));
        }

        [Test]
        public void FindResourceId_TakesTheSmallestIdWhenTheNameIsDuplicated()
        {
            // The dup fixture appends a second copy of the package chunk with package id 0x02,
            // so (mipmap, ic_launcher) exists twice: 0x7F080000 first, 0x02080000 second.
            // The smallest id wins - even though the bigger one comes first,
            // which a "first hit" implementation would have returned.
            object parser = ParserOver(Fixture("spectroid-arsc-dup.bin"));
            Assert.That(ProductApi.ArscFindResourceId(parser, "mipmap", "ic_launcher"), Is.EqualTo(0x02080000u));
            Assert.That(ProductApi.ArscFindResourceId(parser, "string", "action_pause"), Is.EqualTo(0x02090001u));
        }

        [Test]
        public void QuickSearchResource_ReturnsOneValuePerConfig()
        {
            object resource = ProductApi.ArscQuickSearchResource(ParserOver(Fixture("spectroid-resources.arsc")), 0x7F080000);
            Assert.That(ProductApi.ResourceCount(resource), Is.EqualTo(6u), "mipmap/ic_launcher ships six density configs");
            Assert.That(ProductApi.ResourceValue(resource, 0), Is.EqualTo("res/u3.png"));
            Assert.That(ProductApi.ResourceValue(resource, 4), Is.EqualTo("res/CG.png"));
        }

        [Test]
        public void QuickSearchResource_FollowsAReferenceEntry()
        {
            // string/action_pause in this table is a TYPE_REFERENCE to another string id; the
            // reader must hand back the final text, not the reference number.
            object resource = ProductApi.ArscQuickSearchResource(ParserOver(Fixture("spectroid-resources.arsc")), 0x7F090026);
            Assert.That(ProductApi.ResourceValue(resource, 0), Is.EqualTo("logarithmic"));
        }

        [Test]
        public void ConfigSizeCap_RejectsAnOversizedConfig()
        {
            // colour chunk config_size = 8192 > MaxResourceConfigSize (1024).
            AssertEmpty(ProductApi.ArscQuickSearchResource(ParserOver(Fixture("spectroid-arsc-configsize.bin")), 0x7F030000));
        }

        [Test]
        public void ConfigSizeCap_RejectsAConfigThatUnderflows()
        {
            // menu chunk config_size = 2; the reader has already consumed two bytes, so
            // configBytes would go negative and ReadBytes would throw mid-table.
            AssertEmpty(ProductApi.ArscQuickSearchResource(ParserOver(Fixture("spectroid-arsc-configsize.bin")), 0x7F070000));
        }

        [Test]
        public void ConfigSizeCap_RejectsAConfigPastTheChunkEnd()
        {
            // drawable chunk config_size = 1024 but only 96 bytes remain in the chunk: the
            // config would read into the next chunk, so the entry must be dropped.
            AssertEmpty(ProductApi.ArscQuickSearchResource(ParserOver(Fixture("spectroid-arsc-configsize.bin")), 0x7F040000));
        }

        [Test]
        public void PackageCountCap_RejectsTooManyPackages()
        {
            // packageCount = 0x200 > MaxResourcePackages (256). Both entry points must stop,
            // otherwise a 4-byte patch would make the parser walk attacker-chosen chunks.
            object parser = ParserOver(Fixture("spectroid-arsc-packages.bin"));
            AssertEmpty(ProductApi.ArscQuickSearchResource(parser, 0x7F080000));
            Assert.That(ProductApi.ArscFindResourceId(parser, "mipmap", "ic_launcher"), Is.EqualTo(0u));
        }

        [Test]
        public void EntryCountCap_RejectsTooManyEntries()
        {
            // string chunk entryCount = 0x20000 > MaxResourceEntries (0x10000); the index
            // table is what would be walked, so the whole chunk must be skipped.
            AssertEmpty(ProductApi.ArscQuickSearchResource(ParserOver(Fixture("spectroid-arsc-entries.bin")), 0x7F090000));
        }

        [Test]
        public void ReferenceDepthCap_StopsAtTheThirtySecondHop()
        {
            // The chain fixture rewrites string entries 0x00..0x27 into a linear reference
            // chain ending at 0x28 ("Linear"). 0x7F090009 is 31 hops away and resolves;
            // 0x7F090008 is 32 hops away and must come back empty instead of walking on.
            object parser = ParserOver(Fixture("spectroid-arsc-chain.bin"));
            object near = ProductApi.ArscQuickSearchResource(parser, 0x7F090009);
            Assert.That(ProductApi.ResourceValue(near, 0), Is.EqualTo("Linear"));
            AssertEmpty(ProductApi.ArscQuickSearchResource(parser, 0x7F090008));
        }

        [Test]
        public void SelfReference_StopsInsteadOfRecursing()
        {
            // Entry 0x0000 points back at its own id. Without the guard the walk would recurse
            // until the depth cap (or the stack); with it the resource is simply empty - and
            // the call returns immediately.
            object parser = ParserOver(Fixture("spectroid-arsc-selfref.bin"));
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            object resource = ProductApi.ArscQuickSearchResource(parser, 0x7F090000);
            watch.Stop();
            AssertEmpty(resource);
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000), "a self reference must not keep the shell waiting");
        }
    }
}
