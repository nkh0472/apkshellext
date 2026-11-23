using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The icon source chain is the single funnel every package reader's getIcon goes
    /// through. Two contracts matter and are pinned here:
    ///
    /// 1. Ordering and failure: the first source that produces a bitmap wins, a source
    ///    that declines is skipped, and a source that throws (attacker bytes reach every
    ///    decoder) is logged and skipped instead of escaping into the shell as a COM
    ///    exception.
    /// 2. Byte discipline: every source reads through SafeArchive.ReadEntryBounded and
    ///    refuses a declared size over the icon limits *before* a decoder allocates.
    /// </summary>
    [TestFixture]
    public class IconSourceChainTests
    {
        private static readonly Size Icon64 = new Size(64, 64);

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>A real, decodable PNG (GDI+ writes a valid IHDR/IDAT/IEND set).</summary>
        private static byte[] Png(int width, int height)
        {
            using (Bitmap bitmap = new Bitmap(width, height))
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static void WriteBigEndian(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteChunkHeader(Stream stream, string type, uint length)
        {
            WriteBigEndian(stream, length);
            foreach (char c in type)
            {
                stream.WriteByte((byte)c);
            }
        }

        /// <summary>
        /// A PNG whose IHDR honestly declares a 5000x5000 bitmap but carries no pixel
        /// data. The CRC is irrelevant: the gate reads the header only, and a decoder
        /// must never be reached.
        /// </summary>
        private static byte[] PngWithDeclaredSize(uint width, uint height, bool cgbiFirst)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                if (cgbiFirst)
                {
                    // Apple's crushed PNG puts CgBI before IHDR.
                    WriteChunkHeader(stream, "CgBI", 4);
                    stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, 4);
                    stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, 4); // CRC
                }
                WriteChunkHeader(stream, "IHDR", 13);
                WriteBigEndian(stream, width);
                WriteBigEndian(stream, height);
                stream.Write(new byte[] { 8, 6, 0, 0, 0 }, 0, 5);
                stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, 4); // CRC
                return stream.ToArray();
            }
        }

        /// <summary>
        /// A WebP whose VP8X canvas declares 16384x16384. The vendored wrapper's
        /// ValidateDeclaredSize rejects it before libwebp is asked to decode.
        /// </summary>
        private static byte[] OversizedWebp()
        {
            byte[] bytes = new byte[40];
            // RIFF....WEBP VP8X
            bytes[0] = (byte)'R';
            bytes[1] = (byte)'I';
            bytes[2] = (byte)'F';
            bytes[3] = (byte)'F';
            bytes[8] = (byte)'W';
            bytes[9] = (byte)'E';
            bytes[10] = (byte)'B';
            bytes[11] = (byte)'P';
            bytes[12] = (byte)'V';
            bytes[13] = (byte)'P';
            bytes[14] = (byte)'8';
            bytes[15] = (byte)'X';
            // chunk size (LE) = 10
            bytes[16] = 10;
            // canvas is (width-1, height-1) little endian at payload+4 / payload+7
            uint payload = 20;
            uint w = 16384 - 1;
            uint h = 16384 - 1;
            bytes[payload + 4] = (byte)w;
            bytes[payload + 5] = (byte)(w >> 8);
            bytes[payload + 6] = (byte)(w >> 16);
            bytes[payload + 7] = (byte)h;
            bytes[payload + 8] = (byte)(h >> 8);
            bytes[payload + 9] = (byte)(h >> 16);
            return bytes;
        }

        private static ProductApi.ArchiveHandle ZipWithPng()
        {
            byte[] archive = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "res/drawable/ic_launcher.png", Data = Png(8, 8) });
            return ProductApi.OpenArchive(archive);
        }

        private static IconRequest ArchiveRequest(ProductApi.ArchiveHandle handle, string path, Size size)
        {
            return new IconRequest(size, handle.Internal)
            {
                Lookup = IconLookupKind.ArchivePath,
                Path = path
            };
        }

        /// <summary>A source whose result is scripted; records that it was asked.</summary>
        private sealed class ScriptedSource : IIconSource
        {
            private readonly Func<IconRequest, Bitmap> produce;

            public ScriptedSource(Func<IconRequest, Bitmap> produce)
            {
                this.produce = produce;
            }

            public int Calls { get; private set; }

            public bool TryGetIcon(IconRequest request, out Bitmap icon)
            {
                Calls++;
                icon = produce(request);
                return icon != null;
            }
        }

        /// <summary>A source that answers with a fixed boolean and hands out a bitmap anyway.</summary>
        private sealed class DecliningSource : IIconSource
        {
            public Bitmap Leaked;

            public bool TryGetIcon(IconRequest request, out Bitmap icon)
            {
                Leaked = new Bitmap(4, 4);
                icon = Leaked;
                return false;
            }
        }

        private sealed class ThrowingSource : IIconSource
        {
            public bool TryGetIcon(IconRequest request, out Bitmap icon)
            {
                icon = null;
                throw new InvalidOperationException("decoder blew up");
            }
        }

        private static Bitmap NewIcon()
        {
            return new Bitmap(32, 32);
        }

        // ------------------------------------------------------------------
        // Chain ordering, failure policy, logging
        // ------------------------------------------------------------------

        [Test]
        public void Chain_RunsSourcesInOrder_AndStopsAtFirstSuccess()
        {
            List<int> asked = new List<int>();
            ScriptedSource first = new ScriptedSource(r =>
            {
                asked.Add(1);
                return NewIcon();
            });
            ScriptedSource second = new ScriptedSource(r =>
            {
                asked.Add(2);
                return NewIcon();
            });
            IconSourceChain chain = new IconSourceChain().Add(first).Add(second);

            Bitmap icon;
            bool found = chain.TryGetIcon(new IconRequest(Icon64), out icon);

            Assert.That(found, Is.True);
            Assert.That(icon, Is.Not.Null);
            Assert.That(asked, Is.EqualTo(new[] { 1 }), "the chain must stop at the first success");
            Assert.That(second.Calls, Is.EqualTo(0));
            icon.Dispose();
        }

        [Test]
        public void Chain_SkipsDecliningSource_AndAsksTheNext()
        {
            List<int> asked = new List<int>();
            ScriptedSource first = new ScriptedSource(r =>
            {
                asked.Add(1);
                return null;
            });
            ScriptedSource second = new ScriptedSource(r =>
            {
                asked.Add(2);
                return NewIcon();
            });
            IconSourceChain chain = new IconSourceChain().Add(first).Add(second);

            Bitmap icon;
            Assert.That(chain.TryGetIcon(new IconRequest(Icon64), out icon), Is.True);
            Assert.That(asked, Is.EqualTo(new[] { 1, 2 }));
            icon.Dispose();
        }

        [Test]
        public void Chain_SwallowsSourceException_AndContinues()
        {
            ScriptedSource second = new ScriptedSource(r => NewIcon());
            IconSourceChain chain = new IconSourceChain().Add(new ThrowingSource()).Add(second);

            Bitmap icon;
            Assert.DoesNotThrow(() => chain.TryGetIcon(new IconRequest(Icon64), out icon));
            Assert.That(chain.TryGetIcon(new IconRequest(Icon64), out icon), Is.True);
            Assert.That(second.Calls, Is.EqualTo(2), "the healthy source must still be asked");
            icon.Dispose();
        }

        [Test]
        public void Chain_SingleThrowingSource_DoesNotBubble()
        {
            IconSourceChain chain = new IconSourceChain().Add(new ThrowingSource());

            Bitmap icon = null;
            bool found = true;
            Assert.DoesNotThrow(() => found = chain.TryGetIcon(new IconRequest(Icon64), out icon));
            Assert.That(found, Is.False);
            Assert.That(icon, Is.Null);
        }

        [Test]
        public void Chain_NoSourceSucceeds_ReturnsFalseWithNull()
        {
            IconSourceChain chain = new IconSourceChain()
                .Add(new ScriptedSource(r => null))
                .Add(new ScriptedSource(r => null));

            Bitmap icon;
            Assert.That(chain.TryGetIcon(new IconRequest(Icon64), out icon), Is.False);
            Assert.That(icon, Is.Null);
        }

        [Test]
        public void Chain_TreatsTrueWithNullBitmap_AsFailure()
        {
            IconSourceChain chain = new IconSourceChain().Add(new ScriptedSource(r => null));

            Bitmap icon;
            Assert.That(chain.TryGetIcon(new IconRequest(Icon64), out icon), Is.False);
        }

        [Test]
        public void Chain_DisposesBitmapASourceHandedOutWhileDeclining()
        {
            DecliningSource declining = new DecliningSource();
            IconSourceChain chain = new IconSourceChain()
                .Add(declining)
                .Add(new ScriptedSource(r => NewIcon()));

            Bitmap icon;
            Assert.That(chain.TryGetIcon(new IconRequest(Icon64), out icon), Is.True);

            // a disposed Bitmap throws ObjectDisposedException-ish (ArgumentException on
            // the GDI+ path) when its surface is touched
            Assert.Throws<ArgumentException>(() => { int unused = declining.Leaked.Width; });
            icon.Dispose();
        }

        [Test]
        public void Chain_AddNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new IconSourceChain().Add(null));
        }

        [Test]
        public void Chain_NullRequest_Throws()
        {
            Bitmap icon;
            Assert.Throws<ArgumentNullException>(() => new IconSourceChain().TryGetIcon(null, out icon));
        }

        [Test]
        public void Chain_CountReflectsRegistrations()
        {
            IconSourceChain chain = new IconSourceChain();
            Assert.That(chain.Count, Is.EqualTo(0));
            chain.Add(new ThrowingSource()).Add(new ThrowingSource());
            Assert.That(chain.Count, Is.EqualTo(2));
        }

        [Test]
        public void Chain_SetsItselfOnTheRequest_SoSourcesCanContinueTheWalk()
        {
            IconRequest seen = null;
            IconSourceChain chain = new IconSourceChain().Add(new ScriptedSource(r =>
            {
                seen = r;
                return null;
            }));

            Bitmap icon;
            chain.TryGetIcon(new IconRequest(Icon64), out icon);
            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.Chain, Is.SameAs(chain));
        }

        [Test]
        public void Chain_LogsSourceFailure_ThroughTheChainLog()
        {
            List<string> lines = new List<string>();
            IconSourceChain chain = new IconSourceChain(lines.Add).Add(new ThrowingSource());

            Bitmap icon;
            chain.TryGetIcon(new IconRequest(Icon64), out icon);

            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0], Does.Contain("ThrowingSource"));
            Assert.That(lines[0], Does.Contain("decoder blew up"));
        }

        [Test]
        public void Chain_LogsSourceFailure_ThroughTheRequestLog()
        {
            List<string> lines = new List<string>();
            IconRequest request = new IconRequest(Icon64) { Log = lines.Add };
            IconSourceChain chain = new IconSourceChain().Add(new ThrowingSource());

            Bitmap icon;
            chain.TryGetIcon(request, out icon);

            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0], Does.Contain("ThrowingSource"));
        }

        [Test]
        public void Chain_TryDecode_AsksTheSameChainForTheArchivePath()
        {
            IconRequest seen = null;
            IconSourceChain chain = new IconSourceChain().Add(new ScriptedSource(r =>
            {
                seen = r;
                return NewIcon();
            }));

            IconRequest request = new IconRequest(Icon64);
            Bitmap icon;
            Assert.That(chain.TryDecode(request, "res/mipmap/icon.png", out icon), Is.True);

            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.Lookup, Is.EqualTo(IconLookupKind.ArchivePath));
            Assert.That(seen.Path, Is.EqualTo("res/mipmap/icon.png"));
            Assert.That(seen.Chain, Is.SameAs(chain));
            // the original request is untouched (ForPath clones)
            Assert.That(request.Lookup, Is.EqualTo(IconLookupKind.None));
            Assert.That(request.Path, Is.Null);
            icon.Dispose();
        }

        // ------------------------------------------------------------------
        // ① arsc/axml reference resolution
        // ------------------------------------------------------------------

        private sealed class FakeManifestLookup : IManifestIconLookup
        {
            private readonly string path;

            public FakeManifestLookup(string path)
            {
                this.path = path;
            }

            public string Tag;
            public string Attr;
            public string LegacyTag;

            public string ResolveIconPath(string tag, string attribute, out uint referenceId, out int dataType)
            {
                Tag = tag;
                Attr = attribute;
                referenceId = 0x7F020001;
                dataType = 1; // TYPE_REFERENCE
                return path;
            }

            public string ResolveLegacyIconPath(string tag, string attribute)
            {
                LegacyTag = tag;
                return path;
            }
        }

        [Test]
        public void ManifestReference_ResolvesTheAttribute_ThenDecodesThePath()
        {
            using (ProductApi.ArchiveHandle handle = ZipWithPng())
            {
                FakeManifestLookup lookup = new FakeManifestLookup("res/drawable/ic_launcher.png");
                ManifestIconReferenceSource source = new ManifestIconReferenceSource();
                IconSourceChain chain = new IconSourceChain()
                    .Add(source)
                    .Add(new ArchiveImageIconSource());

                IconRequest request = new IconRequest(Icon64, handle.Internal)
                {
                    Reader = IconReaderKind.Apk,
                    Lookup = IconLookupKind.ReferenceAttribute,
                    Tag = "manifest/application",
                    Attr = "icon",
                    ManifestLookup = lookup
                };

                Bitmap icon;
                Assert.That(chain.TryGetIcon(request, out icon), Is.True);
                Assert.That(source.Attempted, Is.True);
                Assert.That(source.ResolvedPath, Is.EqualTo("res/drawable/ic_launcher.png"));
                Assert.That(source.ReferenceId, Is.EqualTo(0x7F020001u));
                Assert.That(source.DataType, Is.EqualTo(1));
                Assert.That(lookup.Tag, Is.EqualTo("manifest/application"));
                Assert.That(lookup.Attr, Is.EqualTo("icon"));
                Assert.That(icon.Size, Is.EqualTo(Icon64));
                icon.Dispose();
            }
        }

        [Test]
        public void ManifestReference_EmptyPath_ReturnsFalseWithoutDecoding()
        {
            using (ProductApi.ArchiveHandle handle = ZipWithPng())
            {
                ManifestIconReferenceSource source = new ManifestIconReferenceSource();
                IconSourceChain chain = new IconSourceChain().Add(source);

                IconRequest request = new IconRequest(Icon64, handle.Internal)
                {
                    Lookup = IconLookupKind.ReferenceAttribute,
                    Tag = "manifest/application",
                    Attr = "icon",
                    ManifestLookup = new FakeManifestLookup("")
                };

                Bitmap icon;
                Assert.That(chain.TryGetIcon(request, out icon), Is.False);
                Assert.That(source.Attempted, Is.True);
                Assert.That(source.ResolvedPath, Is.EqualTo(""));
            }
        }

        [Test]
        public void ManifestReference_OtherLookupKinds_AreIgnored()
        {
            ManifestIconReferenceSource source = new ManifestIconReferenceSource();
            IconRequest request = new IconRequest(Icon64) { Lookup = IconLookupKind.ArchivePath };

            Bitmap icon;
            Assert.That(source.TryGetIcon(request, out icon), Is.False);
            Assert.That(source.Attempted, Is.False);
        }

        // ------------------------------------------------------------------
        // ② archive path -> direct decode (png / webp) and ③ xml drawables
        // ------------------------------------------------------------------

        [Test]
        public void ArchiveImage_DecodesPngAtResPath()
        {
            using (ProductApi.ArchiveHandle handle = ZipWithPng())
            {
                ArchiveImageIconSource source = new ArchiveImageIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(
                    ArchiveRequest(handle, "res/drawable/ic_launcher.png", Icon64), out icon), Is.True);
                Assert.That(icon.Size, Is.EqualTo(Icon64));
                icon.Dispose();
            }
        }

        [Test]
        public void ArchiveImage_MissingEntry_ReturnsFalse()
        {
            using (ProductApi.ArchiveHandle handle = ZipWithPng())
            {
                ArchiveImageIconSource source = new ArchiveImageIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(
                    ArchiveRequest(handle, "res/drawable/nope.png", Icon64), out icon), Is.False);
            }
        }

        [Test]
        public void ArchiveImage_UnsupportedExtension_ReturnsFalse()
        {
            using (ProductApi.ArchiveHandle handle = ZipWithPng())
            {
                ArchiveImageIconSource source = new ArchiveImageIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(
                    ArchiveRequest(handle, "res/drawable/ic_launcher.txt", Icon64), out icon), Is.False);
            }
        }

        [Test]
        public void ArchiveImage_OversizedPngDeclaration_Throws()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "res/drawable/huge.png",
                Data = PngWithDeclaredSize(5000, 5000, cgbiFirst: false)
            });
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(archive))
            {
                ArchiveImageIconSource source = new ArchiveImageIconSource();
                IconRequest request = ArchiveRequest(handle, "res/drawable/huge.png", Icon64);

                InvalidDataException ex = Assert.Throws<InvalidDataException>(
                    () => { Bitmap unused; source.TryGetIcon(request, out unused); });
                Assert.That(ex.Message, Does.Contain("5000x5000"));
                Assert.That(ex.Message, Does.Contain("icon decode limits"));
            }
        }

        [Test]
        public void ArchiveImage_OversizedPngDeclaration_IsSwallowedByTheChain()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "res/drawable/huge.png",
                Data = PngWithDeclaredSize(5000, 5000, cgbiFirst: false)
            });
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(archive))
            {
                List<string> lines = new List<string>();
                IconSourceChain chain = new IconSourceChain(lines.Add).Add(new ArchiveImageIconSource());

                Bitmap icon;
                Assert.DoesNotThrow(() => chain.TryGetIcon(
                    ArchiveRequest(handle, "res/drawable/huge.png", Icon64), out icon));
                Assert.That(chain.TryGetIcon(
                    ArchiveRequest(handle, "res/drawable/huge.png", Icon64), out icon), Is.False);
                Assert.That(lines, Is.Not.Empty);
                Assert.That(lines[0], Does.Contain("icon decode limits"));
            }
        }

        [Test]
        public void ArchiveImage_OversizedWebpDeclaration_Throws()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "res/drawable/huge.webp",
                Data = OversizedWebp()
            });
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(archive))
            {
                ArchiveImageIconSource source = new ArchiveImageIconSource();
                IconRequest request = ArchiveRequest(handle, "res/drawable/huge.webp", Icon64);

                InvalidDataException ex = Assert.Throws<InvalidDataException>(
                    () => { Bitmap unused; source.TryGetIcon(request, out unused); });
                Assert.That(ex.Message, Does.Contain("16384x16384"));
                Assert.That(ex.Message, Does.Contain("decode limits"));
            }
        }

        private sealed class FakeRenderer : IVectorIconRenderer
        {
            public string Path;
            public Size Size;

            public Bitmap RenderXmlDrawable(IconRequest request, string path)
            {
                Path = path;
                Size = request.Size;
                return NewIcon();
            }
        }

        [Test]
        public void VectorDrawable_DefersToTheReadersRenderer()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "res/drawable/ic_vector.xml",
                Data = new byte[] { 1, 2, 3, 4 }
            });
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(archive))
            {
                FakeRenderer renderer = new FakeRenderer();
                VectorDrawableIconSource source = new VectorDrawableIconSource();
                IconRequest request = ArchiveRequest(handle, "res/drawable/ic_vector.xml", Icon64);
                request.Renderer = renderer;

                Bitmap icon;
                Assert.That(source.TryGetIcon(request, out icon), Is.True);
                Assert.That(renderer.Path, Is.EqualTo("res/drawable/ic_vector.xml"));
                Assert.That(renderer.Size, Is.EqualTo(Icon64));
                icon.Dispose();
            }
        }

        [Test]
        public void VectorDrawable_WithoutRenderer_ReturnsFalse()
        {
            using (ProductApi.ArchiveHandle handle = ZipWithPng())
            {
                VectorDrawableIconSource source = new VectorDrawableIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(
                    ArchiveRequest(handle, "res/drawable/ic.xml", Icon64), out icon), Is.False);
            }
        }

        [Test]
        public void VectorDrawable_OversizedRenderTarget_IsRejectedBeforeDrawing()
        {
            byte[] archive = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "res/drawable/ic.xml",
                Data = new byte[] { 1, 2, 3, 4 }
            });
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(archive))
            {
                FakeRenderer renderer = new FakeRenderer();
                VectorDrawableIconSource source = new VectorDrawableIconSource();
                IconRequest request = ArchiveRequest(handle, "res/drawable/ic.xml", new Size(5000, 5000));
                request.Renderer = renderer;

                InvalidDataException ex = Assert.Throws<InvalidDataException>(
                    () => { Bitmap unused; source.TryGetIcon(request, out unused); });
                Assert.That(ex.Message, Does.Contain("5000x5000"));
                Assert.That(renderer.Path, Is.Null, "the rasteriser must not be called at all");
            }
        }

        // ------------------------------------------------------------------
        // ④ iOS plist icon names
        // ------------------------------------------------------------------

        private const string IpaRoot = "Payload/Test.app/";

        private sealed class FakePlistLookup : IPlistIconLookup
        {
            private readonly string name;

            public FakePlistLookup(string name)
            {
                this.name = name;
            }

            public string[] Keys;

            public string FindIconName(string[] keys)
            {
                Keys = keys;
                return name;
            }
        }

        private static ProductApi.ArchiveHandle IpaArchive(params string[] entryNames)
        {
            List<ZipBuilder.EntrySpec> specs = new List<ZipBuilder.EntrySpec>();
            foreach (string name in entryNames)
            {
                specs.Add(new ZipBuilder.EntrySpec { Name = IpaRoot + name, Data = Png(8, 8) });
            }
            return ProductApi.OpenArchive(ZipBuilder.Build(specs.ToArray()));
        }

        private static IconRequest IpaRequest(ProductApi.ArchiveHandle handle, string name)
        {
            return new IconRequest(Icon64, handle.Internal)
            {
                Reader = IconReaderKind.Ipa,
                Lookup = IconLookupKind.PListKeys,
                RootPrefix = IpaRoot,
                Keys = new[] { "CFBundleIconFiles" },
                PlistLookup = new FakePlistLookup(name)
            };
        }

        [Test]
        public void Ipa_FindsTheAt3xVariantFirst()
        {
            using (ProductApi.ArchiveHandle handle = IpaArchive("Icon@3x.png", "Icon@2x.png", "Icon.png"))
            {
                IpaPlistIconSource source = new IpaPlistIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(IpaRequest(handle, "Icon"), out icon), Is.True);
                // the source returns the decoded file; IpaReader.getIcon resizes afterwards
                Assert.That(icon.Width, Is.EqualTo(8));
                icon.Dispose();
            }
        }

        [Test]
        public void Ipa_FallsBackToThePlainName()
        {
            using (ProductApi.ArchiveHandle handle = IpaArchive("Icon.png"))
            {
                IpaPlistIconSource source = new IpaPlistIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(IpaRequest(handle, "Icon"), out icon), Is.True);
                icon.Dispose();
            }
        }

        [Test]
        public void Ipa_UsesThePlistKeyChain()
        {
            using (ProductApi.ArchiveHandle handle = IpaArchive("Icon.png"))
            {
                IpaPlistIconSource source = new IpaPlistIconSource();
                FakePlistLookup lookup = new FakePlistLookup("Icon");
                IconRequest request = new IconRequest(Icon64, handle.Internal)
                {
                    Reader = IconReaderKind.Ipa,
                    Lookup = IconLookupKind.PListKeys,
                    RootPrefix = IpaRoot,
                    Keys = new[] { "CFBundleIcons", "CFBundlePrimaryIcon", "CFBundleIconFiles" },
                    PlistLookup = lookup
                };

                Bitmap icon;
                Assert.That(source.TryGetIcon(request, out icon), Is.True);
                Assert.That(lookup.Keys, Is.EqualTo(new[] { "CFBundleIcons", "CFBundlePrimaryIcon", "CFBundleIconFiles" }));
                icon.Dispose();
            }
        }

        [Test]
        public void Ipa_NameWithoutExtension_AndMissingEntries_ReturnsFalse()
        {
            using (ProductApi.ArchiveHandle handle = IpaArchive("Other.png"))
            {
                IpaPlistIconSource source = new IpaPlistIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(IpaRequest(handle, "Icon"), out icon), Is.False);
            }
        }

        [Test]
        public void Ipa_OnlyRunsForTheIpaReader()
        {
            using (ProductApi.ArchiveHandle handle = IpaArchive("Icon.png"))
            {
                IpaPlistIconSource source = new IpaPlistIconSource();
                IconRequest request = IpaRequest(handle, "Icon");
                request.Reader = IconReaderKind.Apk;

                Bitmap icon;
                Assert.That(source.TryGetIcon(request, out icon), Is.False);
            }
        }

        // ------------------------------------------------------------------
        // ⑤ Appx / Appxbundle manifest logo
        // ------------------------------------------------------------------

        private static ProductApi.ArchiveHandle AppxArchive(params ZipBuilder.EntrySpec[] specs)
        {
            return ProductApi.OpenArchive(ZipBuilder.Build(specs));
        }

        private static IconRequest LogoRequest(ProductApi.ArchiveHandle handle, string logoPath)
        {
            return new IconRequest(Icon64, handle.Internal)
            {
                Reader = IconReaderKind.Appx,
                Lookup = IconLookupKind.ManifestLogo,
                LogoPath = logoPath
            };
        }

        [Test]
        public void AppxLogo_PicksTheBiggestScaleVariant()
        {
            using (ProductApi.ArchiveHandle handle = AppxArchive(
                new ZipBuilder.EntrySpec { Name = "Assets/Logo.scale-100.png", Data = Png(8, 8) },
                new ZipBuilder.EntrySpec { Name = "Assets/Logo.scale-200.png", Data = Png(16, 16) }))
            {
                AppxManifestLogoIconSource source = new AppxManifestLogoIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(LogoRequest(handle, "Assets/Logo.png"), out icon), Is.True);
                Assert.That(icon.Width, Is.EqualTo(16), "scale-200 must win over scale-100");
                icon.Dispose();
            }
        }

        [Test]
        public void AppxLogo_ExactMatchWinsOverScaledVariants()
        {
            using (ProductApi.ArchiveHandle handle = AppxArchive(
                new ZipBuilder.EntrySpec { Name = "Assets/Logo.scale-400.png", Data = Png(32, 32) },
                new ZipBuilder.EntrySpec { Name = "Assets/Logo.png", Data = Png(24, 24) }))
            {
                AppxManifestLogoIconSource source = new AppxManifestLogoIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(LogoRequest(handle, "Assets/Logo.png"), out icon), Is.True);
                Assert.That(icon.Width, Is.EqualTo(24), "the unscaled exact entry must win");
                icon.Dispose();
            }
        }

        [Test]
        public void AppxLogo_NoVariant_ReturnsFalse()
        {
            using (ProductApi.ArchiveHandle handle = AppxArchive(
                new ZipBuilder.EntrySpec { Name = "Assets/Other.png", Data = Png(8, 8) }))
            {
                AppxManifestLogoIconSource source = new AppxManifestLogoIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(LogoRequest(handle, "Assets/Logo.png"), out icon), Is.False);
            }
        }

        [Test]
        public void AppxLogo_PathWithoutExtension_ReturnsFalse()
        {
            using (ProductApi.ArchiveHandle handle = AppxArchive(
                new ZipBuilder.EntrySpec { Name = "Assets/Logo.png", Data = Png(8, 8) }))
            {
                AppxManifestLogoIconSource source = new AppxManifestLogoIconSource();
                Bitmap icon;
                Assert.That(source.TryGetIcon(LogoRequest(handle, "Assets/Logo"), out icon), Is.False);
            }
        }

        // ------------------------------------------------------------------
        // split-bundle root icon
        // ------------------------------------------------------------------

        [Test]
        public void BundleRoot_PicksTheLargestRootIcon()
        {
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "icon.png", Data = Png(8, 8) },
                new ZipBuilder.EntrySpec { Name = "icon_big.png", Data = Png(16, 16) })))
            {
                BundleRootIconSource source = new BundleRootIconSource();
                IconRequest request = new IconRequest(Icon64, handle.Internal)
                {
                    Reader = IconReaderKind.SplitApk,
                    Lookup = IconLookupKind.BundleRootIcon
                };

                Bitmap icon;
                Assert.That(source.TryGetIcon(request, out icon), Is.True);
                Assert.That(icon.Width, Is.EqualTo(16));
                icon.Dispose();
            }
        }

        [Test]
        public void BundleRoot_IgnoresNestedIcons()
        {
            using (ProductApi.ArchiveHandle handle = ProductApi.OpenArchive(ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "res/icon.png", Data = Png(16, 16) })))
            {
                BundleRootIconSource source = new BundleRootIconSource();
                IconRequest request = new IconRequest(Icon64, handle.Internal)
                {
                    Lookup = IconLookupKind.BundleRootIcon
                };

                Bitmap icon;
                Assert.That(source.TryGetIcon(request, out icon), Is.False);
            }
        }

        // ------------------------------------------------------------------
        // PNG declared-size gate and the shared limits
        // ------------------------------------------------------------------

        [Test]
        public void PngHeader_ReadsTheIhdrSize()
        {
            int width;
            int height;
            Assert.That(IconDecodeLimits.TryReadPngSize(
                PngWithDeclaredSize(1024, 768, cgbiFirst: false), out width, out height), Is.True);
            Assert.That(width, Is.EqualTo(1024));
            Assert.That(height, Is.EqualTo(768));
        }

        [Test]
        public void PngHeader_FindsIhdrAfterTheCrushedPngCgbiChunk()
        {
            int width;
            int height;
            Assert.That(IconDecodeLimits.TryReadPngSize(
                PngWithDeclaredSize(512, 256, cgbiFirst: true), out width, out height), Is.True);
            Assert.That(width, Is.EqualTo(512));
            Assert.That(height, Is.EqualTo(256));
        }

        [Test]
        public void PngHeader_NotAPng_IsLeftToTheDecoder()
        {
            int width;
            int height;
            Assert.That(IconDecodeLimits.TryReadPngSize(
                System.Text.Encoding.ASCII.GetBytes("not a png at all"), out width, out height), Is.False);
            Assert.DoesNotThrow(() => IconDecodeLimits.ValidatePng(
                System.Text.Encoding.ASCII.GetBytes("not a png at all"), "test"));
        }

        [Test]
        public void ValidatePng_OversizedDeclaration_ThrowsNamingTheSize()
        {
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                IconDecodeLimits.ValidatePng(PngWithDeclaredSize(9000, 9000, false), "icon test.png"));
            Assert.That(ex.Message, Does.Contain("9000x9000"));
        }

        [TestCase(4096, 1)]
        [TestCase(1, 4096)]
        [TestCase(4096, 2048)]
        public void ValidateDeclaredSize_AcceptsWithinTheLimits(int width, int height)
        {
            Assert.DoesNotThrow(() => IconDecodeLimits.ValidateDeclaredSize(width, height, "test"));
        }

        [TestCase(4097, 1)]
        [TestCase(1, 4097)]
        [TestCase(4096, 2049)]
        [TestCase(16384, 16384)]
        public void ValidateDeclaredSize_RejectsOutsideTheLimits(int width, int height)
        {
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                IconDecodeLimits.ValidateDeclaredSize(width, height, "test"));
            Assert.That(ex.Message, Does.Contain(width + "x" + height));
            Assert.That(ex.Message, Does.Contain("icon decode limits"));
        }

        [Test]
        public void ValidateDeclaredSize_ZeroSizedRenderRequest_IsNotADeclaration()
        {
            Assert.DoesNotThrow(() => IconDecodeLimits.ValidateDeclaredSize(0, 0, "test"));
            Assert.DoesNotThrow(() => IconDecodeLimits.ValidateRenderTarget(Size.Empty, "test"));
        }

        [Test]
        public void IconDecodeLimits_MatchTheWebpGate()
        {
            Assert.That(IconDecodeLimits.MaxIconSide,
                Is.EqualTo((int)ProductApi.DecodeLimitConstant("MaxWebPDecodeSide")));
            Assert.That(IconDecodeLimits.MaxIconPixels,
                Is.EqualTo((long)ProductApi.DecodeLimitConstant("MaxWebPDecodePixels")));
        }
    }
}
