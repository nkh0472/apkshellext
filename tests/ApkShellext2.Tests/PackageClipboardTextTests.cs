using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the clipboard text rules: which value each copy command writes, how a selection
    /// becomes one CRLF-separated block, when a value counts as missing, and how a selection
    /// that contains a file no reader accepts behaves. The menu decides visibility from the
    /// same <see cref="PackageClipboardTextSet.Has"/> answers.
    /// </summary>
    [TestFixture]
    public class PackageClipboardTextTests
    {
        private static StubReader Reader(string appName, string packageName, string version, string revision)
        {
            return new StubReader
            {
                AppName = appName,
                PackageName = packageName,
                Version = version,
                Revision = revision
            };
        }

        private static PackageClipboardTextSet JoinOne(string path, StubReader reader)
        {
            return PackageClipboardText.Join(new[] { PackageClipboardText.ReadEntry(path, reader) });
        }

        [Test]
        public void FilePath_IsTheAbsolutePath()
        {
            var entry = PackageClipboardText.ReadEntry(Path.Combine("folder", "pkg.apk"), null);

            Assert.That(entry.FilePath, Is.EqualTo(Path.GetFullPath(Path.Combine("folder", "pkg.apk"))));
        }

        [Test]
        public void FilePaths_JoinInSelectionOrderWithCrLf()
        {
            var set = PackageClipboardText.Join(new[]
            {
                PackageClipboardText.ReadEntry("a.apk", null),
                PackageClipboardText.ReadEntry("b.apk", null)
            });

            Assert.That(set.FilePath, Is.EqualTo(Path.GetFullPath("a.apk") + "\r\n" + Path.GetFullPath("b.apk")));
            Assert.That(set.FilePath.EndsWith("\r\n"), Is.False, "no trailing line break");
        }

        [Test]
        public void AppName_ComesFromTheReader()
        {
            var set = JoinOne("pkg.apk", Reader("Spectroid", "org.intoorbit.spectrum", "1.1.2", "31"));

            Assert.That(set.AppName, Is.EqualTo("Spectroid"));
        }

        [Test]
        public void PackageName_ComesFromTheReader()
        {
            var set = JoinOne("pkg.apk", Reader("Spectroid", "org.intoorbit.spectrum", "1.1.2", "31"));

            Assert.That(set.PackageName, Is.EqualTo("org.intoorbit.spectrum"));
        }

        [Test]
        public void Version_AppendsTheRevisionInParentheses()
        {
            var set = JoinOne("pkg.apk", Reader("Spectroid", "pkg", "1.1.2", "31"));

            Assert.That(set.Version, Is.EqualTo("1.1.2 (31)"));
        }

        [Test]
        public void Version_OmitsAnEmptyRevision()
        {
            var set = JoinOne("pkg.apk", Reader("Spectroid", "pkg", "1.1.2", ""));

            Assert.That(set.Version, Is.EqualTo("1.1.2"));
        }

        [Test]
        public void Version_IsEmptyWithoutAVersionNumber()
        {
            var set = JoinOne("pkg.apk", Reader("Spectroid", "pkg", "", "31"));

            Assert.That(set.Version, Is.Empty);
            Assert.That(set.Has(PackageClipboardText.Field.Version), Is.False);
        }

        [Test]
        public void UnreadableFile_KeepsOnlyThePath()
        {
            var set = JoinOne("pkg.apk", null);

            Assert.That(set.FilePath, Is.Not.Empty);
            Assert.That(set.Has(PackageClipboardText.Field.AppName), Is.False);
            Assert.That(set.Has(PackageClipboardText.Field.PackageName), Is.False);
            Assert.That(set.Has(PackageClipboardText.Field.Version), Is.False);
        }

        [Test]
        public void Join_SkipsValuesTheSelectionLacks()
        {
            var set = PackageClipboardText.Join(new[]
            {
                PackageClipboardText.ReadEntry("a.apk", Reader("Spectroid", "", "", "")),
                PackageClipboardText.ReadEntry("b.apk", Reader("", "", "", ""))
            });

            Assert.That(set.AppName, Is.EqualTo("Spectroid"));
            Assert.That(set.PackageName, Is.Empty);
            Assert.That(set.Has(PackageClipboardText.Field.PackageName), Is.False);
        }

        [Test]
        public void Join_KeepsOneLinePerValueAndNoTrailingBreak()
        {
            var set = PackageClipboardText.Join(new[]
            {
                PackageClipboardText.ReadEntry("a.apk", Reader("One", "pkg.one", "", "")),
                PackageClipboardText.ReadEntry("b.apk", Reader("Two", "pkg.two", "", ""))
            });

            Assert.That(set.AppName, Is.EqualTo("One\r\nTwo"));
            Assert.That(set.AppName.EndsWith("\r\n"), Is.False);
            Assert.That(set.AppName, Does.Not.Match("(?<!\r)\n"), "every break is a CRLF");
        }

        [Test]
        public void EmptySelection_HasNoValues()
        {
            var set = PackageClipboardText.Join(new PackageClipboardEntry[0]);

            foreach (PackageClipboardText.Field field in PackageClipboardText.Fields)
            {
                Assert.That(set.Has(field), Is.False);
                Assert.That(set.Get(field), Is.Empty);
            }
        }

        [Test]
        public void EmptyPath_ProducesNoLine()
        {
            var set = JoinOne("", null);

            Assert.That(set.FilePath, Is.Empty);
            Assert.That(set.Has(PackageClipboardText.Field.FilePath), Is.False);
        }

        [Test]
        public void Read_OpensAndDisposesOneReaderPerPath()
        {
            var first = Reader("One", "pkg.one", "", "");
            var second = Reader("Two", "pkg.two", "", "");
            var readers = new Queue<StubReader>(new[] { first, second });

            string text = PackageClipboardText.Read(
                PackageClipboardText.Field.AppName, new[] { "a.apk", "b.apk" }, path => readers.Dequeue());

            Assert.That(text, Is.EqualTo("One\r\nTwo"));
            Assert.That(first.Disposed, Is.True);
            Assert.That(second.Disposed, Is.True);
        }

        [Test]
        public void Read_FallsBackToThePathWhenTheFactoryRejectsTheFile()
        {
            Func<string, IPackageReader> factory = path =>
            {
                throw new NotSupportedException("unsupported type");
            };

            Assert.That(PackageClipboardText.Read(PackageClipboardText.Field.AppName, new[] { "a.txt" }, factory),
                Is.Empty);
            Assert.That(PackageClipboardText.Read(PackageClipboardText.Field.FilePath, new[] { "a.txt" }, factory),
                Is.EqualTo(Path.GetFullPath("a.txt")));
        }

        [Test]
        public void Read_DoesNotEscapeAReaderFailure()
        {
            var broken = new StubReader { ThrowOnAppName = true };

            string text = PackageClipboardText.Read(
                PackageClipboardText.Field.AppName, new[] { "a.apk" }, path => broken);

            Assert.That(text, Is.Empty);
            Assert.That(broken.Disposed, Is.True);
        }

        [Test]
        public void Read_RejectsANullSelection()
        {
            Assert.That(() => PackageClipboardText.Read(PackageClipboardText.Field.AppName, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConcurrentReads_ReturnTheSameText()
        {
            string[] paths = { "a.apk", "b.apk", "c.apk" };
            string[] results = new string[64];

            Parallel.For(0, results.Length, i =>
            {
                results[i] = PackageClipboardText.Read(
                    PackageClipboardText.Field.PackageName,
                    paths,
                    path => new StubReader { PackageName = Path.GetFileNameWithoutExtension(path) });
            });

            foreach (string result in results)
            {
                Assert.That(result, Is.EqualTo("a\r\nb\r\nc"));
            }
        }
    }

    /// <summary>
    /// A reader whose metadata comes from the test. <see cref="ThrowOnAppName"/> simulates a
    /// reader that parses the file lazily and fails on the first property the copy command asks
    /// about, which is the failure the clipboard command has to swallow.
    /// </summary>
    internal sealed class StubReader : IPackageReader
    {
        public string FileName { get; set; }
        public bool ThrowOnAppName { get; set; }
        public bool Disposed { get; private set; }

        private string appName = "";

        public string AppName
        {
            get
            {
                if (ThrowOnAppName) throw new InvalidOperationException("the package could not be parsed");
                return appName;
            }
            set { appName = value ?? ""; }
        }

        public string PackageName { get; set; }
        public string Version { get; set; }
        public string Revision { get; set; }
        public CultureInfo Culture { get; set; }
        public string Publisher { get; set; }
        public string AppID { get; set; }
        public Bitmap Icon { get { return null; } }
        public AppPackageReader.AppType Type { get { return AppPackageReader.AppType.AndroidApp; } }

        public StubReader()
        {
            PackageName = "";
            Version = "";
            Revision = "";
        }

        public void setFlag(string flag, object value) { }

        public object getFlag(string flag) { return null; }

        public Bitmap getIcon(Size size) { return null; }

        public void Dispose() { Disposed = true; }
    }
}
