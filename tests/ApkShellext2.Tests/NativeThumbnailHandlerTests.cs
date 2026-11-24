using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ApkQuickReader;
using ApkShellext2.Shell.Native;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Drives the native thumbnail handler the way the shell does: IInitializeWithFile with the
    /// item path, then IThumbnailProvider for the size the view asks for. Every bitmap is compared
    /// pixel for pixel with the same package rendered through the shared pipeline, so a handler
    /// that resized twice, ignored the requested size or drew an overlay by accident fails.
    /// </summary>
    [TestFixture]
    public class NativeThumbnailHandlerTests
    {
        private const int S_OK = 0;
        private const int S_FALSE = 1;
        private const uint WtsatArgb = 2;

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [TearDown]
        public void RestoreThreadCulture()
        {
            ApkShellext2.Utility.Localize();
        }

        /// <summary>
        /// The colour channels of every pixel. A GDI+ bitmap handed to the shell travels as an
        /// HBITMAP, which has no alpha channel, so comparing alpha would compare the transport
        /// rather than the image; the handler states the alpha type separately.
        /// </summary>
        private static byte[] RgbPixels(Bitmap bitmap)
        {
            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] buffer = new byte[Math.Abs(data.Stride) * bitmap.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                byte[] rgb = new byte[buffer.Length / 4 * 3];
                for (int i = 0, j = 0; i < buffer.Length; i += 4, j += 3)
                {
                    rgb[j] = buffer[i];
                    rgb[j + 1] = buffer[i + 1];
                    rgb[j + 2] = buffer[i + 2];
                }

                return rgb;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>
        /// A bitmap handed to the shell travels as an HBITMAP, which has no alpha channel: GDI+
        /// composites it over a grey background on the way out. Running the expectation through
        /// the same transport compares the image, not the transport.
        /// </summary>
        private static Bitmap AsTheShellWouldSeeIt(Bitmap source)
        {
            IntPtr hbitmap = source.GetHbitmap();
            try
            {
                return Image.FromHbitmap(hbitmap);
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }

        /// <summary>The package icon at the requested size, rendered independently of the handler.</summary>
        private static Bitmap Expected(string path, int size)
        {
            using (Stream stream = File.OpenRead(path))
            using (AppPackageReader reader = new ApkReader(stream))
            {
                Bitmap icon = reader.getIcon(new Size(size, size));
                Assert.That(icon, Is.Not.Null, "the fixture package has no icon at " + size + "px");
                Bitmap resized = ApkShellext2.Utility.ResizeBitmap(icon, size);
                icon.Dispose();
                return resized;
            }
        }

        private static byte[] Fixture(string name)
        {
            return File.ReadAllBytes(RepoLayout.PathOf("tests/ApkShellext2.Tests/fixtures/" + name));
        }

        private static byte[] Png(int side)
        {
            using (Bitmap bitmap = new Bitmap(side, side))
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// The checked-in Spectroid slices carry only the manifest and the resource table, so the
        /// icon they reference does not exist. A thumbnail needs the drawable itself, therefore
        /// this package pairs the same real tables with real PNGs under the density names the
        /// table points at.
        /// </summary>
        private static string ThumbnailApkPath()
        {
            string path = Path.Combine(MenuFixtures.Directory, "with-icon.apk");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, ZipBuilder.Build(
                    new ZipBuilder.EntrySpec { Name = "resources.arsc", Data = Fixture("spectroid-resources.arsc") },
                    new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = Fixture("spectroid-manifest.bin") },
                    new ZipBuilder.EntrySpec { Name = "res/u3.png", Data = Png(160) },
                    new ZipBuilder.EntrySpec { Name = "res/SD.png", Data = Png(240) },
                    new ZipBuilder.EntrySpec { Name = "res/jy.png", Data = Png(320) },
                    new ZipBuilder.EntrySpec { Name = "res/D2.png", Data = Png(480) },
                    new ZipBuilder.EntrySpec { Name = "res/CG.png", Data = Png(640) }));
            }

            return path;
        }

        private static ThumbnailHandlerNative Initialized(string path)
        {
            ThumbnailHandlerNative handler = new ThumbnailHandlerNative();
            Assert.That(handler.Initialize(path, 0), Is.EqualTo(S_OK), "IInitializeWithFile failed for " + path);
            return handler;
        }

        [TestCase(32)]
        [TestCase(96)]
        [TestCase(256)]
        [TestCase(512)]
        public void GetThumbnailRendersTheRequestedSizeFromThePath(int size)
        {
            using (new SettingsScope().Set("EnableThumbnail", "True").Set("StretchThumbnail", "True").Set("ShowOverlayIcon", "False"))
            {
                string path = ThumbnailApkPath();
                ThumbnailHandlerNative handler = Initialized(path);
                IntPtr hbitmap;
                uint alpha;
                int hr = handler.GetThumbnail((uint)size, out hbitmap, out alpha);
                Assert.That(hr, Is.EqualTo(S_OK), "GetThumbnail failed at " + size + "px");
                try
                {
                    Assert.That(alpha, Is.EqualTo(WtsatArgb), "the bitmap is 32bpp ARGB");
                    Assert.That(hbitmap, Is.Not.EqualTo(IntPtr.Zero));
                    using (Bitmap actual = Image.FromHbitmap(hbitmap))
                    using (Bitmap expected = Expected(path, size))
                    using (Bitmap transported = AsTheShellWouldSeeIt(expected))
                    {
                        Assert.That(actual.Size, Is.EqualTo(new Size(size, size)), "the shell owns a bitmap of exactly the requested size");
                        Assert.That(RgbPixels(actual), Is.EqualTo(RgbPixels(transported)), "pixels differ from the shared thumbnail pipeline at " + size + "px");
                    }
                }
                finally
                {
                    DeleteObject(hbitmap);
                }
            }
        }

        [Test]
        public void GetThumbnailReturnsTheSamePixelsTwice()
        {
            using (new SettingsScope().Set("EnableThumbnail", "True").Set("ShowOverlayIcon", "False"))
            {
                ThumbnailHandlerNative handler = Initialized(ThumbnailApkPath());
                IntPtr first, second;
                uint alpha;
                Assert.That(handler.GetThumbnail(96, out first, out alpha), Is.EqualTo(S_OK));
                Assert.That(handler.GetThumbnail(96, out second, out alpha), Is.EqualTo(S_OK));
                try
                {
                    using (Bitmap a = Image.FromHbitmap(first))
                    using (Bitmap b = Image.FromHbitmap(second))
                    {
                        Assert.That(RgbPixels(a), Is.EqualTo(RgbPixels(b)), "two requests for one package must render the same pixels");
                    }
                }
                finally
                {
                    DeleteObject(first);
                    DeleteObject(second);
                }
            }
        }

        [Test]
        public void GetThumbnailRefusesASizeBeyondTheDecodeBudget()
        {
            using (new SettingsScope().Set("EnableThumbnail", "True"))
            {
                ThumbnailHandlerNative handler = Initialized(ThumbnailApkPath());
                IntPtr hbitmap;
                uint alpha;
                uint tooLarge = (uint)ApkShellext2.IconDecodeLimits.MaxIconSide + 1;
                Assert.That(handler.GetThumbnail(tooLarge, out hbitmap, out alpha), Is.EqualTo(S_FALSE));
                Assert.That(hbitmap, Is.EqualTo(IntPtr.Zero), "a refused request must not hand back a bitmap");
                Assert.That(alpha, Is.EqualTo(0u));
            }
        }

        [Test]
        public void GetThumbnailWithoutAnImageAnswersWithNoThumbnail()
        {
            using (new SettingsScope().Set("EnableThumbnail", "True"))
            {
                foreach (string path in new[] { MenuFixtures.UnknownPath(), MenuFixtures.HostileApkPath(), @"C:\apkshellext-tests\does-not-exist.apk" })
                {
                    ThumbnailHandlerNative handler = Initialized(path);
                    IntPtr hbitmap;
                    uint alpha;
                    Assert.That(handler.GetThumbnail(96, out hbitmap, out alpha), Is.EqualTo(S_FALSE), "no thumbnail expected for " + path);
                    Assert.That(hbitmap, Is.EqualTo(IntPtr.Zero), "no bitmap expected for " + path);
                    Assert.That(alpha, Is.EqualTo(0u), "no alpha type expected for " + path);
                }
            }
        }

        [Test]
        public void GetThumbnailRespectsTheDisabledSetting()
        {
            using (new SettingsScope().Set("EnableThumbnail", "False"))
            {
                ThumbnailHandlerNative handler = Initialized(ThumbnailApkPath());
                IntPtr hbitmap;
                uint alpha;
                Assert.That(handler.GetThumbnail(96, out hbitmap, out alpha), Is.EqualTo(S_FALSE),
                    "thumbnails are opt-in; disabled must not render anything");
                Assert.That(hbitmap, Is.EqualTo(IntPtr.Zero));
            }
        }

        [Test]
        public void GetThumbnailBeforeInitializeHasNoPath()
        {
            ThumbnailHandlerNative handler = new ThumbnailHandlerNative();
            IntPtr hbitmap;
            uint alpha;
            Assert.That(handler.GetThumbnail(96, out hbitmap, out alpha), Is.EqualTo(S_FALSE));
            Assert.That(hbitmap, Is.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void ThumbnailHandlerCarriesTheRegisteredIdentity()
        {
            string clsid = ShellGuidConstant("ThumbnailClsid");
            string progId = ShellGuidConstant("ThumbnailProgId");
            GuidAttribute guid = (GuidAttribute)Attribute.GetCustomAttribute(typeof(ThumbnailHandlerNative), typeof(GuidAttribute));
            ProgIdAttribute attribute = (ProgIdAttribute)Attribute.GetCustomAttribute(typeof(ThumbnailHandlerNative), typeof(ProgIdAttribute));
            Assert.That(guid, Is.Not.Null);
            Assert.That(attribute, Is.Not.Null);
            Assert.That(guid.Value, Is.EqualTo(clsid.Trim('{', '}')).IgnoreCase, "the CLSID must be the registered thumbnail handler");
            Assert.That(attribute.Value, Is.EqualTo(progId), "the ProgID must be the registered thumbnail handler");
        }

        private static string ShellGuidConstant(string name)
        {
            Type guids = ProductApi.Assembly.GetType("ApkShellext2.Registration.ShellGuids", true);
            FieldInfo field = guids.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "ShellGuids." + name + " was not found; update the test");
            return (string)field.GetRawConstantValue();
        }
    }
}
