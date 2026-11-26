using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using ApkShellext2;
using ApkQuickReader;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the CSV contract of the export: the byte order mark, CRLF, the RFC 4180 quoting
    /// rules, the fixed column order and the status values. The reader is a fake so the tests
    /// do not depend on any package format; the file system rows still use real files.
    /// </summary>
    [TestFixture]
    public class PackageCsvWriterTests
    {
        private string directory;

        [SetUp]
        public void CreateDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "apkshellext-csv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private string File(string name, string content = "package")
        {
            string path = Path.Combine(directory, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        // real readers do the parsing; these two synthetic package files only need to be
        // readable so the factory dispatches on the extension
        private static byte[] ResourcesFixture()
        {
            string path = RepoLayout.PathOf("tests/ApkShellext2.Tests/fixtures/spectroid-resources.arsc");
            Assert.That(path, Does.Exist, "fixture is missing; regenerate with fixtures\\make-fixtures.ps1");
            return System.IO.File.ReadAllBytes(path);
        }

        private string SyntheticApk(string name, int minSdk, int targetSdk,
            bool? debuggable = null, params SigningBlockBuilder.Pair[] signingPairs)
        {
            AxmlBuilder.Element root = AxmlBuilder.Root("manifest")
                .Attr("package", "com.example.synthetic")
                .Attr("versionName", "1.0")
                .Attr("versionCode", "1");
            root.Add("uses-sdk")
                .AndroidInt("minSdkVersion", minSdk)
                .AndroidInt("targetSdkVersion", targetSdk);
            AxmlBuilder.Element application = root.Add("application").AndroidAttr("label", "Synthetic");
            if (debuggable.HasValue) application.AndroidBool("debuggable", debuggable.Value);

            byte[] apk = ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = AxmlBuilder.Build(root) },
                new ZipBuilder.EntrySpec { Name = "resources.arsc", Data = ResourcesFixture() });
            if (signingPairs.Length > 0)
                apk = SigningBlockBuilder.Insert(apk, SigningBlockBuilder.Block(signingPairs));
            string path = Path.Combine(directory, name);
            System.IO.File.WriteAllBytes(path, apk);
            return path;
        }

        private string SyntheticIpa(string name)
        {
            byte[] plist = BinaryPlistBuilder.Build(new List<byte[]>
            {
                BinaryPlistBuilder.DictionaryObject(1, 2, 3, 4),
                BinaryPlistBuilder.StringObject("CFBundleDisplayName"),
                BinaryPlistBuilder.StringObject("Synthetic App"),
                BinaryPlistBuilder.StringObject("CFBundleIdentifier"),
                BinaryPlistBuilder.StringObject("com.example.ios")
            });
            byte[] ipa = ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = "Payload/Example.app/Info.plist",
                Data = plist
            });
            string path = Path.Combine(directory, name);
            System.IO.File.WriteAllBytes(path, ipa);
            return path;
        }

        private static MemoryStream Write(
            IEnumerable<string> paths,
            Func<string, IPackageReader> openReader = null,
            CancellationToken cancellationToken = default(CancellationToken),
            Action<int, int> progress = null)
        {
            var output = new MemoryStream();
            PackageCsvWriter.Write(output, paths, openReader, cancellationToken, progress);
            output.Position = 0;
            return output;
        }

        private static byte[] Bytes(MemoryStream output)
        {
            return output.ToArray();
        }

        private static string Text(MemoryStream output)
        {
            byte[] bytes = output.ToArray();
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        private static string[] Lines(string text)
        {
            return text.Split(new[] { "\r\n" }, StringSplitOptions.None);
        }

        private static string[] Fields(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
            fields.Add(field.ToString());
            return fields.ToArray();
        }

        private sealed class FakeReader : IPackageReader
        {
            public string FileName { get; set; }
            public string AppName { get; set; }
            public string PackageName { get; set; }
            public string Version { get; set; }
            public string Revision { get; set; }
            public CultureInfo Culture { get; set; }
            public string Publisher { get; set; }
            public System.Drawing.Bitmap Icon { get { return null; } }
            public string AppID { get; set; }
            public AppPackageReader.AppType Type { get; set; }
            public bool Disposed { get; private set; }

            // the writer emits the platform facts in their own columns; a test that does not
            // care leaves them at the neutral values
            private string minSdk, targetSdk, packer;
            private bool? debuggable;

            public string MinSdk {
                get { ThrowIfRequested(); return minSdk; }
                set { minSdk = value; }
            }
            public string TargetSdk {
                get { ThrowIfRequested(); return targetSdk; }
                set { targetSdk = value; }
            }
            public bool? Debuggable {
                get { ThrowIfRequested(); return debuggable; }
                set { debuggable = value; }
            }
            public string Packer {
                get { ThrowIfRequested(); return packer; }
                set { packer = value; }
            }
            public string IconEntryPath { get { return ""; } }

            public bool ThrowOnFacts { get; set; }

            private void ThrowIfRequested() {
                if (ThrowOnFacts) throw new InvalidOperationException("facts\r\nfailed");
            }

            public FakeReader()
            {
                AppName = "Example";
                PackageName = "com.example.app";
                Version = "1.2.3";
                Revision = "45";
                Publisher = "Example Ltd";
                AppID = "app-id";
                Type = AppPackageReader.AppType.AndroidApp;
            }

            public void setFlag(string name, object value) { }
            public object getFlag(string name) { return null; }
            public System.Drawing.Bitmap getIcon(System.Drawing.Size size) { return null; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        [Test]
        public void OutputStartsWithTheUtf8ByteOrderMark()
        {
            byte[] bytes = Bytes(Write(new string[0]));

            Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(bytes[0], Is.EqualTo(0xEF));
            Assert.That(bytes[1], Is.EqualTo(0xBB));
            Assert.That(bytes[2], Is.EqualTo(0xBF));
        }

        [Test]
        public void HeaderIsTheDocumentedColumnOrder()
        {
            string header = Lines(Text(Write(new string[0])))[0];

            Assert.That(PackageCsvWriter.Columns, Is.EqualTo(new[] {
                "File", "Folder", "AppName", "Package", "Version", "Revision",
                "Publisher", "AppID", "Type", "MinSdk", "TargetSdk", "Debuggable", "Packer",
                "SizeBytes", "Sha256", "Xxh3", "Modified", "Status"
            }));
            Assert.That(Fields(header), Is.EqualTo(PackageCsvWriter.Columns));
        }

        [Test]
        public void EveryRecordEndsWithCrLfAndNothingElseUsesLoneNewlines()
        {
            string path = File("Plain.apk");
            string text = Text(Write(new[] { path }, p => new FakeReader()));

            Assert.That(text, Does.EndWith("\r\n"));
            Assert.That(text.Replace("\r\n", ""), Does.Not.Contain("\n"));
            Assert.That(Lines(text).Length, Is.EqualTo(3), "header, one row and the empty tail");
        }

        [Test]
        public void AnOkRowCarriesTheReaderValues()
        {
            string path = File("Plain.apk");
            var reader = new FakeReader {
                FileName = path,
                AppName = "Example App",
                PackageName = "com.example.app",
                Version = "1.2.3",
                Revision = "45",
                Publisher = "Example Ltd",
                AppID = "ai-123",
                Type = AppPackageReader.AppType.AndroidApp
            };

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => reader)))[1]);

            Assert.That(row[0], Is.EqualTo("Plain.apk"));
            Assert.That(row[1], Is.EqualTo(Path.GetFullPath(directory)));
            Assert.That(row[2], Is.EqualTo("Example App"));
            Assert.That(row[3], Is.EqualTo("com.example.app"));
            Assert.That(row[4], Is.EqualTo("1.2.3"));
            Assert.That(row[5], Is.EqualTo("45"));
            Assert.That(row[6], Is.EqualTo("Example Ltd"));
            Assert.That(row[7], Is.EqualTo("ai-123"));
            Assert.That(row[8], Is.EqualTo("AndroidApp"));
            Assert.That(row[9], Is.Empty, "a neutral reader reports no minSdk");
            Assert.That(row[10], Is.Empty);
            Assert.That(row[11], Is.Empty, "a null Debuggable renders blank");
            Assert.That(row[12], Is.Empty);
            FileHash expectedHash = FileHasher.Compute(path);
            Assert.That(row[14], Is.EqualTo(expectedHash.Sha256));
            Assert.That(row[15], Is.EqualTo(expectedHash.Xxh3));
            Assert.That(row[17], Is.EqualTo("ok"));
        }

        [Test]
        public void AHealthyApkWritesAllEighteenColumns()
        {
            string path = File("Healthy.apk");
            var reader = new FakeReader {
                FileName = path,
                AppName = "Healthy App",
                PackageName = "com.example.healthy",
                Version = "1.0",
                Revision = "1",
                Publisher = "Healthy Ltd",
                AppID = "ai-healthy",
                Type = AppPackageReader.AppType.AndroidApp,
                MinSdk = "21",
                TargetSdk = "34",
                Debuggable = true,
                Packer = "Walle"
            };

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => reader)))[1]);

            Assert.That(row.Length, Is.EqualTo(PackageCsvWriter.Columns.Length));
            Assert.That(row[8], Is.EqualTo("AndroidApp"));
            Assert.That(row[9], Is.EqualTo("21"));
            Assert.That(row[10], Is.EqualTo("34"));
            Assert.That(row[11], Is.EqualTo("true"));
            Assert.That(row[12], Is.EqualTo("Walle"));
            Assert.That(row[17], Is.EqualTo("ok"));
        }

        [Test]
        public void ASyntheticApkIsReadForItsPlatformFacts()
        {
            string path = SyntheticApk("Facts.apk", 26, 34, true,
                SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Walle));

            string[] row = Fields(Lines(Text(Write(new[] { path })))[1]);

            Assert.That(row.Length, Is.EqualTo(PackageCsvWriter.Columns.Length));
            Assert.That(row[8], Is.EqualTo("AndroidApp"));
            Assert.That(row[9], Is.EqualTo("26"));
            Assert.That(row[10], Is.EqualTo("34"));
            Assert.That(row[11], Is.EqualTo("true"));
            Assert.That(row[12], Is.EqualTo("Walle"), "the packer is named from the signing block");
            Assert.That(row[17], Is.EqualTo("ok"));
        }

        [Test]
        public void AnApkWithoutAPackerOrDebuggableLeavesThoseColumnsEmpty()
        {
            string path = SyntheticApk("Plain.apk", 23, 30);

            string[] row = Fields(Lines(Text(Write(new[] { path })))[1]);

            Assert.That(row[9], Is.EqualTo("23"));
            Assert.That(row[10], Is.EqualTo("30"));
            Assert.That(row[11], Is.Empty, "no debuggable attribute renders blank");
            Assert.That(row[12], Is.Empty, "no signing block pair renders blank");
            Assert.That(row[17], Is.EqualTo("ok"));
        }

        [Test]
        public void ADebuggableFalseIsWrittenAsTheTextFalse()
        {
            string path = SyntheticApk("Release.apk", 24, 33, false);

            string[] row = Fields(Lines(Text(Write(new[] { path })))[1]);

            Assert.That(row[11], Is.EqualTo("false"));
            Assert.That(row[12], Is.Empty);
            Assert.That(row[17], Is.EqualTo("ok"));
        }

        [Test]
        public void ANonAndroidPackageLeavesThePlatformColumnsEmptyAndStaysOk()
        {
            string path = SyntheticIpa("Store.ipa");

            string[] row = Fields(Lines(Text(Write(new[] { path })))[1]);

            Assert.That(row.Length, Is.EqualTo(PackageCsvWriter.Columns.Length));
            Assert.That(row[8], Is.EqualTo("iOSApp"));
            Assert.That(row[9], Is.Empty);
            Assert.That(row[10], Is.Empty);
            Assert.That(row[11], Is.Empty);
            Assert.That(row[12], Is.Empty);
            Assert.That(row[17], Is.EqualTo("ok"));
        }

        [Test]
        public void AMixedSelectionKeepsTheColumnCountOnEveryRow()
        {
            string apk = SyntheticApk("Mixed.apk", 21, 33);
            string unsupported = File("notes.txt");

            string[] lines = Lines(Text(Write(new[] { apk, unsupported })));

            Assert.That(lines.Length, Is.EqualTo(4), "header, two rows and the empty tail");
            string[] apkRow = Fields(lines[1]);
            string[] unsupportedRow = Fields(lines[2]);
            Assert.That(apkRow.Length, Is.EqualTo(PackageCsvWriter.Columns.Length));
            Assert.That(unsupportedRow.Length, Is.EqualTo(PackageCsvWriter.Columns.Length));
            Assert.That(apkRow[9], Is.EqualTo("21"));
            Assert.That(unsupportedRow[8], Is.Empty);
            Assert.That(unsupportedRow[9], Is.Empty);
            Assert.That(unsupportedRow[17], Is.EqualTo("unsupported type"));
        }

        [Test]
        public void AFactsFailureBlanksTheFourColumnsAndRecordsOneError()
        {
            string path = File("Broken.apk");
            var reader = new FakeReader { AppName = "Broken App", ThrowOnFacts = true };

            PackageCsvResult result;
            string[] row;
            using (var output = new MemoryStream())
            {
                result = PackageCsvWriter.Write(output, new[] { path }, p => reader, default(CancellationToken), null);
                output.Position = 0;
                row = Fields(Lines(Text(output))[1]);
            }

            Assert.That(row.Length, Is.EqualTo(PackageCsvWriter.Columns.Length));
            Assert.That(row[2], Is.EqualTo("Broken App"), "the identity columns survive the failure");
            Assert.That(row[9], Is.Empty);
            Assert.That(row[10], Is.Empty);
            Assert.That(row[11], Is.Empty);
            Assert.That(row[12], Is.Empty);
            Assert.That(row[17], Is.EqualTo("error: facts failed"),
                "the newline in the message is collapsed so the record stays on one line");
            Assert.That(result.Errors, Is.EqualTo(1));
        }

        [Test]
        public void SizeComesFromFileInfoAndModifiedIsIsoWithAnOffset()
        {
            string path = File("Sized.apk", "0123456789");
            var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Local);
            System.IO.File.SetLastWriteTime(path, when);

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => new FakeReader())))[1]);

            Assert.That(row[13], Is.EqualTo("10"));
            Assert.That(row[16], Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$"));
            DateTimeOffset parsed = DateTimeOffset.Parse(row[16], CultureInfo.InvariantCulture);
            Assert.That(parsed.LocalDateTime, Is.EqualTo(when));
        }

        [Test]
        public void TypeUsesTheReaderAppTypeName()
        {
            string path = File("Store.ipa");
            var reader = new FakeReader { Type = AppPackageReader.AppType.iOSApp };

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => reader)))[1]);

            Assert.That(row[8], Is.EqualTo("iOSApp"));
            Assert.That(row[17], Is.EqualTo("ok"));
        }

        [Test]
        public void UnsupportedExtensionIsReportedAndTheFactoryIsNeverCalled()
        {
            string path = File("notes.txt");
            bool called = false;

            var result = new PackageCsvResult();
            using (var output = new MemoryStream())
            {
                result = PackageCsvWriter.Write(output, new[] { path }, p => {
                    called = true;
                    return new FakeReader();
                }, default(CancellationToken), null);
                output.Position = 0;
                string[] row = Fields(Lines(Text(output))[1]);
                Assert.That(row[8], Is.Empty);
                Assert.That(row[9], Is.Empty, "an unsupported file has no platform facts");
                Assert.That(row[10], Is.Empty);
                Assert.That(row[11], Is.Empty);
                Assert.That(row[12], Is.Empty);
                Assert.That(row[17], Is.EqualTo("unsupported type"));
            }

            Assert.That(called, Is.False);
            Assert.That(result.Rows, Is.EqualTo(1));
            Assert.That(result.Errors, Is.EqualTo(1));
        }

        [Test]
        public void AMissingFileIsUnreadableWithoutAskingAReader()
        {
            string path = Path.Combine(directory, "Gone.apk");
            bool called = false;

            PackageCsvResult result;
            string[] row;
            using (var output = new MemoryStream())
            {
                result = PackageCsvWriter.Write(output, new[] { path }, p => {
                    called = true;
                    return new FakeReader();
                }, default(CancellationToken), null);
                output.Position = 0;
                row = Fields(Lines(Text(output))[1]);
            }

            Assert.That(called, Is.False);
            Assert.That(row[8], Is.EqualTo("AndroidApp"), "the extension still names the type");
            Assert.That(row[14], Is.Empty, "a file that cannot be read has no hash");
            Assert.That(row[15], Is.Empty, "a file that cannot be read has no hash");
            Assert.That(row[17], Is.EqualTo("unreadable"));
            Assert.That(result.Errors, Is.EqualTo(1));
        }

        [Test]
        public void AReaderThatThrowsTurnsTheRowIntoUnreadable()
        {
            string path = File("Broken.apk");

            PackageCsvResult result;
            string[] row;
            using (var output = new MemoryStream())
            {
                result = PackageCsvWriter.Write(output, new[] { path },
                    p => { throw new InvalidDataException("bad zip"); },
                    default(CancellationToken), null);
                output.Position = 0;
                row = Fields(Lines(Text(output))[1]);
            }

            Assert.That(row[17], Is.EqualTo("unreadable"));
            Assert.That(result.Rows, Is.EqualTo(1));
            Assert.That(result.Errors, Is.EqualTo(1));
            Assert.That(result.Cancelled, Is.False);
        }

        [Test]
        public void ACancelledExportWritesOneCancelledRowAndStops()
        {
            string first = File("First.apk");
            string second = File("Second.apk");
            bool called = false;

            PackageCsvResult result;
            string[] lines;
            using (var output = new MemoryStream())
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                result = PackageCsvWriter.Write(output, new[] { first, second },
                    p => {
                        called = true;
                        return new FakeReader();
                    },
                    cancellation.Token, null);
                output.Position = 0;
                lines = Lines(Text(output));
            }

            Assert.That(called, Is.False);
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Rows, Is.EqualTo(1));
            Assert.That(lines.Length, Is.EqualTo(3));
            Assert.That(Fields(lines[1])[17], Is.EqualTo("cancelled"));
        }

        [Test]
        public void CancellingAfterTheFirstRowKeepsTheFirstRowAndMarksTheSecond()
        {
            string first = File("First.apk");
            string second = File("Second.apk");
            var progress = new List<string>();
            CancellationTokenSource cancellation = new CancellationTokenSource();

            PackageCsvResult result;
            string[] lines;
            using (var output = new MemoryStream())
            {
                result = PackageCsvWriter.Write(output, new[] { first, second },
                    p => new FakeReader(), cancellation.Token,
                    (done, total) => {
                        progress.Add(done + "/" + total);
                        if (done == 1) cancellation.Cancel();
                    });
                output.Position = 0;
                lines = Lines(Text(output));
            }

            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Rows, Is.EqualTo(2));
            Assert.That(Fields(lines[1])[17], Is.EqualTo("ok"));
            Assert.That(Fields(lines[2])[17], Is.EqualTo("cancelled"));
            Assert.That(progress, Is.EqualTo(new[] { "1/2", "2/2" }));
        }

        [Test]
        public void AnEmptySelectionWritesOnlyTheHeader()
        {
            PackageCsvResult result;
            string text;
            using (var output = new MemoryStream())
            {
                result = PackageCsvWriter.Write(output, new string[0]);
                output.Position = 0;
                text = Text(output);
            }

            Assert.That(result.Rows, Is.EqualTo(0));
            Assert.That(result.Errors, Is.EqualTo(0));
            Assert.That(text, Is.EqualTo(string.Join(",", PackageCsvWriter.Columns) + "\r\n"));
        }

        [Test]
        public void QuotesAreDoubledAndTheFieldIsQuoted()
        {
            string path = File("Quote.apk");
            var reader = new FakeReader { AppName = "A \"quoted\" name" };

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => reader)))[1]);

            Assert.That(row[2], Is.EqualTo("A \"quoted\" name"));
            Assert.That(Text(Write(new[] { path }, p => reader)), Does.Contain("\"A \"\"quoted\"\" name\""));
        }

        [Test]
        public void CommaForcesQuoting()
        {
            string path = File("Comma.apk");
            var reader = new FakeReader { Publisher = "Smith, John" };

            string text = Text(Write(new[] { path }, p => reader));

            Assert.That(Fields(Lines(text)[1])[6], Is.EqualTo("Smith, John"));
            Assert.That(text, Does.Contain("\"Smith, John\""));
        }

        [Test]
        public void NewlineInsideAValueIsQuotedInsteadOfStartingARecord()
        {
            string path = File("Newline.apk");
            var reader = new FakeReader { Version = "1.0\r\nbeta" };

            string text = Text(Write(new[] { path }, p => reader));

            Assert.That(text, Does.Contain("\"1.0\r\nbeta\""));
            Assert.That(Fields(Lines(text)[1])[4], Is.EqualTo("1.0"));
        }

        [Test]
        public void PathsWithSpacesAndChineseCharactersKeepTheirNameAndFolder()
        {
            string folder = Path.Combine(directory, "中文 目录");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "应用 名称.apk");
            System.IO.File.WriteAllText(path, "package");

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => new FakeReader())))[1]);

            Assert.That(row[0], Is.EqualTo("应用 名称.apk"));
            Assert.That(row[1], Is.EqualTo(Path.GetFullPath(folder)));
        }

        [Test]
        public void ADotSegmentInThePathStillYieldsTheFullFolder()
        {
            string path = Path.Combine(directory, ".", "Plain.apk");
            System.IO.File.WriteAllText(path, "package");

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => new FakeReader())))[1]);

            Assert.That(row[0], Is.EqualTo("Plain.apk"));
            Assert.That(row[1], Is.EqualTo(Path.GetFullPath(directory)));
        }

        [Test]
        public void ADotDotSegmentIsNormalisedToTheRealFolder()
        {
            string sub = Path.Combine(directory, "sub");
            Directory.CreateDirectory(sub);
            string path = Path.Combine(sub, "..", "Plain.apk");
            System.IO.File.WriteAllText(path, "package");

            string[] row = Fields(Lines(Text(Write(new[] { path }, p => new FakeReader())))[1]);

            Assert.That(row[0], Is.EqualTo("Plain.apk"));
            Assert.That(row[1], Is.EqualTo(Path.GetFullPath(directory)));
        }

        [Test]
        public void EveryReaderIsDisposedAfterItsRow()
        {
            string path = File("Dispose.apk");
            var reader = new FakeReader();

            Write(new[] { path }, p => reader);

            Assert.That(reader.Disposed, Is.True);
        }

        [Test]
        public void ProgressReportsTheRunningCountAndTheSelectionSize()
        {
            string first = File("First.apk");
            string second = File("Second.apk");
            var seen = new List<string>();

            Write(new[] { first, second }, p => new FakeReader(), default(CancellationToken),
                (done, total) => seen.Add(done + "/" + total));

            Assert.That(seen, Is.EqualTo(new[] { "1/2", "2/2" }));
        }

        [Test]
        public void AMixedSelectionKeepsOneRowPerPathInOrder()
        {
            string ok = File("Ok.apk");
            string unsupported = File("notes.txt");
            string missing = Path.Combine(directory, "Missing.apk");

            string[] lines = Lines(Text(Write(new[] { ok, unsupported, missing }, p => new FakeReader())));

            Assert.That(lines.Length, Is.EqualTo(5), "header plus three rows plus the empty tail");
            Assert.That(Fields(lines[1])[0], Is.EqualTo("Ok.apk"));
            Assert.That(Fields(lines[1])[17], Is.EqualTo("ok"));
            Assert.That(Fields(lines[2])[0], Is.EqualTo("notes.txt"));
            Assert.That(Fields(lines[2])[17], Is.EqualTo("unsupported type"));
            Assert.That(Fields(lines[2]).Length, Is.EqualTo(PackageCsvWriter.Columns.Length),
                "an unsupported row is padded to the same width as every other row");
            Assert.That(Fields(lines[3])[0], Is.EqualTo("Missing.apk"));
            Assert.That(Fields(lines[3])[17], Is.EqualTo("unreadable"));
        }

        [Test]
        public void ErrorsCountOnlyTheRowsThatAreNeitherOkNorCancelled()
        {
            string ok = File("Ok.apk");
            string unsupported = File("notes.txt");

            PackageCsvResult result;
            using (var output = new MemoryStream())
            {
                result = PackageCsvWriter.Write(output, new[] { ok, unsupported },
                    p => new FakeReader(), default(CancellationToken), null);
            }

            Assert.That(result.Rows, Is.EqualTo(2));
            Assert.That(result.Errors, Is.EqualTo(1));
        }

        [Test]
        public void LeadingAndTrailingSpacesAreQuotedSoReadersDoNotTrimThem()
        {
            string path = File("Padded.apk");
            var reader = new FakeReader { AppName = " Padded Name " };

            string text = Text(Write(new[] { path }, p => reader));

            Assert.That(text, Does.Contain("\" Padded Name \""));
            Assert.That(Fields(Lines(text)[1])[2], Is.EqualTo(" Padded Name "));
        }

        [Test]
        public void TheDefaultFileNameIsApkInfoCsv()
        {
            Assert.That(PackageCsvWriter.DefaultFileName, Is.EqualTo("apk-info.csv"));
        }
    }
}


