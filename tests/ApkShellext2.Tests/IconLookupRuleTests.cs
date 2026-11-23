using System;
using System.IO;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the icon lookup chain (ArscParser.ResolveIconValue, reached from
    /// ApkReader.getImage) over real Spectroid bytes and patched fixtures (fixtures/README.md
    /// lists every patch).
    ///
    /// The chain exists because the manifest icon attribute is one of three different things
    /// depending on how the app was built: a file path ("res/CG.png"), a "@type/name"
    /// reference (which may also be an "android:" framework reference), or a typed resource id
    /// whose name may be ambiguous across packages. Getting this wrong shows up as a wrong
    /// icon, which the 87-sample pixel baseline would catch - but only after shell
    /// integration, so each step of the chain is pinned here as well.
    /// </summary>
    [TestFixture]
    public class IconLookupRuleTests
    {
        private const string FixtureDir = "tests/ApkShellext2.Tests/fixtures/";

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf(FixtureDir + name);
            Assert.That(path, Does.Exist, "fixture " + path + " is missing; regenerate it with X:\\apks-tn\\fixtures\\make-fixtures.ps1.");
            return File.ReadAllBytes(path);
        }

        private static object Parser()
        {
            return ProductApi.NewAxmlParser(Fixture("spectroid-resources.arsc"), resolveReferences: true);
        }

        [Test]
        public void ReferenceIdWinsOverTheValueText()
        {
            // The typed reference (0x7F080000 here) is authoritative. The value
            // text is only a fallback - in the wild it is often a stale or scrambled name.
            Assert.That(ProductApi.AxmlResolveIconValue(Parser(), "junk", 0x7F080000), Is.EqualTo("res/u3.png"));
        }

        [Test]
        public void ReferenceIdWinsOverAnAndroidPrefixedValue()
        {
            // A manifest attribute is either typed or text; when a typed reference exists it is
            // what the app actually points at, so the android: text cannot veto it. (A value
            // without a typed reference and with an android: prefix is refused below.)
            Assert.That(ProductApi.AxmlResolveIconValue(Parser(), "@android:drawable/ic_menu", 0x7F080000), Is.EqualTo("res/u3.png"));
        }

        [Test]
        public void NameLookupResolvesTheResourceFile()
        {
            // Name fallback with the smallest id: "@mipmap/ic_launcher" has no typed id in this
            // call, so the name is looked up and the smallest matching id wins.
            object parser = Parser();
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "@mipmap/ic_launcher", 0), Is.EqualTo("res/u3.png"));
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "mipmap/ic_launcher", 0), Is.EqualTo("res/u3.png"));
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "org.intoorbit.spectrum:mipmap/ic_launcher", 0), Is.EqualTo("res/u3.png"));
        }

        [Test]
        public void NameLookupTakesTheSmallestIdAcrossPackages()
        {
            // Smallest id over the dup fixture: the name now exists in package 0x7F (first) and
            // package 0x02 (second). The smaller id must win, so the icon still resolves.
            object parser = ProductApi.NewAxmlParser(Fixture("spectroid-arsc-dup.bin"), resolveReferences: true);
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "@mipmap/ic_launcher", 0), Is.EqualTo("res/u3.png"));
        }

        [Test]
        public void SelfReferenceIsRefusedInsteadOfRepeating()
        {
            // Self reference over the selfref fixture: the entry resolves to nothing, so the value
            // is handed back unchanged and the shell reports "no icon" instead of hanging.
            object parser = ProductApi.NewAxmlParser(Fixture("spectroid-arsc-selfref.bin"), resolveReferences: true);
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            string resolved = ProductApi.AxmlResolveIconValue(parser, "@string/action_hideMarker", 0x7F090000);
            watch.Stop();
            Assert.That(resolved, Is.EqualTo("@string/action_hideMarker"));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000), "a self reference must not keep the shell waiting");
        }

        [Test]
        public void AndroidPrefixIsRefused()
        {
            // A framework resource is not in the app's table. Refusing it leaves the
            // value as-is, which the icon handler then reports as an unresolved image, instead
            // of silently resolving an unrelated app resource with a colliding name.
            object parser = Parser();
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "@android:drawable/ic_menu", 0), Is.EqualTo("@android:drawable/ic_menu"));
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "android:drawable/ic_menu", 0), Is.EqualTo("android:drawable/ic_menu"));
        }

        [Test]
        public void ForeignPackagePrefixIsNotResolvedAgainstThisApp()
        {
            // A ":package:type/name" prefix naming another package would otherwise be looked up
            // in the current app's table under a name that never belonged to it.
            Assert.That(ProductApi.AxmlResolveIconValue(Parser(), "com.other:mipmap/ic_launcher", 0),
                Is.EqualTo("com.other:mipmap/ic_launcher"));
        }

        [Test]
        public void ResPrefixedValuesAreFilePaths()
        {
            // A value starting with "res/" is a zip entry path, not a resource name,
            // so it is returned untouched - even when a typed reference is also present.
            object parser = Parser();
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "res/CG.png", 0x7F080000), Is.EqualTo("res/CG.png"));
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "res/CG.png", 0), Is.EqualTo("res/CG.png"));
        }

        [Test]
        public void ObfuscatedImagePathsAreFilePaths()
        {
            // Some apps ship icon paths that are not under res/ ("r/w/x6.png" from the real
            // 87-sample corpus). They are existing zip entries, so they must not be run through
            // the resource-name parser.
            Assert.That(ProductApi.AxmlResolveIconValue(Parser(), "r/w/x6.png", 0), Is.EqualTo("r/w/x6.png"));
        }

        [Test]
        public void UnknownNamesAndEmptyValuesStayUnresolved()
        {
            object parser = Parser();
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "@mipmap/no_such", 0), Is.EqualTo("@mipmap/no_such"));
            Assert.That(ProductApi.AxmlResolveIconValue(parser, "", 0), Is.Empty);
        }
    }
}
