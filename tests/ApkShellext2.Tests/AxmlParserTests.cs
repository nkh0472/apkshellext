using System;
using System.IO;
using System.Xml;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the shared binary-XML parser (ApkQuickReader.AxmlParser) over real bytes from
    /// Spectroid_org.intoorbit.spectrum_1.1.2(31).apk: AndroidManifest.xml plus the matching
    /// resources.arsc, plus patched variants of both (fixtures/README.md lists every patch).
    ///
    /// The manifest is the file every shell property (app name, version, icon path) comes
    /// from, so the string-pool / resource-map bounds must reject rather than read a
    /// neighbour chunk, and the attribute walk must stop at the element chunk end.
    /// </summary>
    [TestFixture]
    public class AxmlParserTests
    {
        private const string FixtureDir = "tests/ApkShellext2.Tests/fixtures/";

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf(FixtureDir + name);
            Assert.That(path, Does.Exist, "fixture " + path + " is missing; regenerate it with X:\\apks-tn\\fixtures\\make-fixtures.ps1.");
            return File.ReadAllBytes(path);
        }

        private static byte[] Table
        {
            get { return Fixture("spectroid-resources.arsc"); }
        }

        private static byte[] Manifest
        {
            get { return Fixture("spectroid-manifest.bin"); }
        }

        /// <summary>An ApkReader-shaped parser: resource table present, references resolved.</summary>
        private static object ApkParser()
        {
            return ProductApi.NewAxmlParser(Table, resolveReferences: true);
        }

        /// <summary>An AabReader-shaped parser: no resources.arsc, references stay numeric.</summary>
        private static object AabParser()
        {
            return ProductApi.NewAxmlParser(null, resolveReferences: false);
        }

        [Test]
        public void StringPool_ReadsTheFirstString()
        {
            object parser = ApkParser();
            Assert.That(ProductApi.AxmlStringPool(parser, Manifest, 0), Is.EqualTo("theme"));
        }

        [Test]
        public void StringPool_RejectsAnIndexPastTheCount()
        {
            object parser = ApkParser();
            Assert.That(ProductApi.AxmlStringPool(parser, Manifest, 1000), Is.Empty);
        }

        [Test]
        public void ResourceMap_ReadsTheFrameworkAttributeName()
        {
            object parser = ApkParser();
            Assert.That(ProductApi.AxmlResourceMap(parser, Manifest, 0), Is.EqualTo("theme"));
        }

        [Test]
        public void ResourceMap_RejectsAnIndexPastTheChunk()
        {
            object parser = ApkParser();
            Assert.That(ProductApi.AxmlResourceMap(parser, Manifest, 1000), Is.Empty);
        }

        [Test]
        public void StringPoolOrResourceMap_FallsBackToTheHexPlaceholder()
        {
            // An id that is in neither the string pool nor the resource map must render as a
            // visible placeholder, not as an empty attribute value.
            object parser = ApkParser();
            Assert.That(ProductApi.AxmlStringPoolOrResourceMap(parser, Manifest, 500), Is.EqualTo("(0x000001F4)"));
        }

        [Test]
        public void StringPool_RejectsAnEntryLongerThanTheBuffer()
        {
            // The entry's u16 length is patched to 0x7FFF while only a few bytes remain, so a
            // decoder that trusts the length would read past the buffer.
            object parser = ApkParser();
            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => ProductApi.AxmlStringPool(parser, Fixture("spectroid-axml-pool-oob.bin"), 0));
            Assert.That(error.Message, Does.Contain("string pool entry out of bounds"));
        }

        [Test]
        public void ResourceMap_RejectsACorruptChunk()
        {
            // The resource map's first id is patched to a value with no R_attr name, so the
            // framework attribute name of that index reads as "no name".
            object parser = ApkParser();
            byte[] corrupt = Fixture("spectroid-axml-resmap-oob.bin");
            Assert.That(ProductApi.AxmlResourceMap(parser, corrupt, 0), Is.Empty);
            Assert.That(ProductApi.AxmlStringPoolOrResourceMap(parser, corrupt, 1000), Is.EqualTo("(0x000003E8)"));
        }

        [Test]
        public void AttributeWalk_RejectsAttributesPastTheChunkEnd()
        {
            // application claims 8 attributes of 20 B in a 116 B chunk: the walk leaves the
            // chunk at the fifth one. The first attribute still resolves, which proves the
            // guard triggers on position and not by refusing every patched manifest.
            object parser = ApkParser();
            byte[] patched = Fixture("spectroid-axml-attr-oob.bin");
            Assert.That(ProductApi.AxmlAttribute(parser, patched, "manifest/application", "icon"), Is.EqualTo("res/u3.png"));
            Exception error = Assert.Throws<Exception>(
                () => ProductApi.AxmlAttribute(parser, patched, "manifest/application", "theme"));
            Assert.That(error.Message, Is.EqualTo("Out of Chunk when processing tag application"));
        }

        [Test]
        public void ManifestAttributes_ResolveThroughTheResourceTable()
        {
            object parser = ApkParser();
            Assert.That(ProductApi.AxmlAttribute(parser, Manifest, "manifest/application", "icon"), Is.EqualTo("res/u3.png"));
            Assert.That(ProductApi.AxmlAttribute(parser, Manifest, "manifest", "versionName"), Is.EqualTo("1.1.2"));
            Assert.That(ProductApi.AxmlAttribute(parser, Manifest, "manifest/application", "label"), Is.EqualTo("Spectroid"));
        }

        [Test]
        public void ManifestAttributes_StayNumericWithoutAResourceTable()
        {
            // An .aab has no resources.arsc; the raw reference is rendered as a decimal number,
            // which is the pre-existing behaviour and the reason AxmlParser takes resolveReferences.
            object parser = AabParser();
            Assert.That(ProductApi.AxmlAttribute(parser, Manifest, "manifest/application", "icon"), Is.EqualTo("2131230720"));
            Assert.That(ProductApi.AxmlAttribute(parser, Manifest, "manifest", "versionName"), Is.EqualTo("1.1.2"));
        }

        [Test]
        public void ExtractCompressedXml_DumpsTheRealManifest()
        {
            XmlDocument document = ProductApi.AxmlExtract(ApkParser(), Manifest);
            Assert.That(document.DocumentElement.Name, Is.EqualTo("manifest"));
            Assert.That(document.DocumentElement.ChildNodes.Count, Is.EqualTo(3), "manifest, uses-sdk, uses-permission, application are the only top level elements");
            Assert.That(document.DocumentElement.ChildNodes[0].Name, Is.EqualTo("uses-sdk"));
            XmlElement application = (XmlElement)document.DocumentElement.ChildNodes[2];
            Assert.That(application.Name, Is.EqualTo("application"));
            Assert.That(application.GetAttribute("icon"), Is.EqualTo("res/u3.png"));
        }

        [Test]
        public void ExtractCompressedXml_KeepsReferencesNumericWithoutAResourceTable()
        {
            XmlDocument document = ProductApi.AxmlExtract(AabParser(), Manifest);
            XmlElement application = (XmlElement)document.DocumentElement.ChildNodes[2];
            Assert.That(application.GetAttribute("icon"), Is.EqualTo("2131230720"));
        }
    }
}
