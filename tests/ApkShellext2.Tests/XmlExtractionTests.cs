using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using ApkShellext2.Shell.Commands;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The two XML extraction actions write beside the package today; these tests pin the new
    /// contract: the user picks a folder, every selected package writes
    /// &lt;rename prefix&gt;.&lt;entry&gt;.xml into it, and a cancelled dialog writes nothing.
    /// </summary>
    [TestFixture]
    public sealed class XmlExtractionTests
    {
        private string work;
        private string output;
        private Func<string> previousFolder;
        private Func<string, string, string> previousPrompt;

        [SetUp]
        public void SetUp()
        {
            work = Path.Combine(Path.GetTempPath(), "apkshellext-xml-" + Guid.NewGuid().ToString("N"));
            output = Path.Combine(work, "out");
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(output);

            previousFolder = CommandActions.FolderHandler;
            previousPrompt = CommandActions.PromptHandler;
        }

        [TearDown]
        public void TearDown()
        {
            CommandActions.FolderHandler = previousFolder;
            CommandActions.PromptHandler = previousPrompt;
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch (IOException)
            {
                // a leftover temp folder is not a test failure
            }
        }

        [Test]
        public void DumpManifest_WritesEachSelectionUnderItsRenamePrefix()
        {
            string alpha = WriteApk("alpha.apk", "Alpha", "1.0", "com.example.alpha");
            string beta = WriteApk("beta.apk", "Beta", "2.0", "com.example.beta");
            CommandActions.FolderHandler = () => output;
            CommandActions.PromptHandler = (prompt, title) => throw new AssertionException("the manifest dump must not prompt");

            using (new SettingsScope().Set("RenamePattern", "%AppName%_%Version%")) {
                CommandActions.Execute(CommandAction.DumpManifest, new[] { alpha, beta });
            }

            AssertFile("Alpha_1.0.androidmanifest.xml", "manifest");
            AssertFile("Beta_2.0.androidmanifest.xml", "manifest");
            Assert.That(Directory.GetFiles(output).Length, Is.EqualTo(2));
        }

        [Test]
        public void DumpXml_ReplacesSlashesInTheEntryName()
        {
            byte[] drawable = AxmlBuilder.Build(AxmlBuilder.Root("shape"));
            string alpha = WriteApk("alpha.apk", "Alpha", "1.0", "com.example.alpha",
                new ZipBuilder.EntrySpec { Name = "res/drawable/icon.xml", Data = drawable });
            CommandActions.FolderHandler = () => output;
            CommandActions.PromptHandler = (prompt, title) => "res/drawable/icon.xml";

            using (new SettingsScope().Set("RenamePattern", "%AppName%_%Version%")) {
                CommandActions.Execute(CommandAction.DumpXml, new[] { alpha });
            }

            AssertFile("Alpha_1.0.res_drawable_icon.xml", "shape");
        }

        [Test]
        public void DumpXml_DoesNotAppendASecondXmlExtension()
        {
            string alpha = WriteApk("alpha.apk", "Alpha", "1.0", "com.example.alpha");
            CommandActions.FolderHandler = () => output;
            CommandActions.PromptHandler = (prompt, title) => "AndroidManifest.xml";

            using (new SettingsScope().Set("RenamePattern", "%AppName%_%Version%")) {
                CommandActions.Execute(CommandAction.DumpXml, new[] { alpha });
            }

            AssertFile("Alpha_1.0.AndroidManifest.xml", "manifest");
        }

        [Test]
        public void DumpXml_AddsASuffixWhenTheTargetNameIsTaken()
        {
            byte[] drawable = AxmlBuilder.Build(AxmlBuilder.Root("shape"));
            string alpha = WriteApk("alpha.apk", "Alpha", "1.0", "com.example.alpha",
                new ZipBuilder.EntrySpec { Name = "res/drawable/icon.xml", Data = drawable });
            File.WriteAllText(Path.Combine(output, "Alpha_1.0.res_drawable_icon.xml"), "already here");
            CommandActions.FolderHandler = () => output;
            CommandActions.PromptHandler = (prompt, title) => "res/drawable/icon.xml";

            using (new SettingsScope().Set("RenamePattern", "%AppName%_%Version%")) {
                CommandActions.Execute(CommandAction.DumpXml, new[] { alpha });
                CommandActions.Execute(CommandAction.DumpXml, new[] { alpha });
            }

            AssertFile("Alpha_1.0.res_drawable_icon-2.xml", "shape");
            AssertFile("Alpha_1.0.res_drawable_icon-3.xml", "shape");
        }

        [Test]
        public void CancelledFolderPickerWritesNothingAndDoesNotPrompt()
        {
            string alpha = WriteApk("alpha.apk", "Alpha", "1.0", "com.example.alpha");
            bool prompted = false;
            CommandActions.FolderHandler = () => null;
            CommandActions.PromptHandler = (prompt, title) =>
            {
                prompted = true;
                return "AndroidManifest.xml";
            };

            using (new SettingsScope().Set("RenamePattern", "%AppName%_%Version%")) {
                CommandActions.Execute(CommandAction.DumpXml, new[] { alpha });
            }

            Assert.That(prompted, Is.False);
            Assert.That(Directory.GetFileSystemEntries(output), Is.Empty);
        }

        [Test]
        public void CancelledEntryPromptWritesNothing()
        {
            string alpha = WriteApk("alpha.apk", "Alpha", "1.0", "com.example.alpha");
            CommandActions.FolderHandler = () => output;
            CommandActions.PromptHandler = (prompt, title) => "";

            using (new SettingsScope().Set("RenamePattern", "%AppName%_%Version%")) {
                CommandActions.Execute(CommandAction.DumpXml, new[] { alpha });
            }

            Assert.That(Directory.GetFileSystemEntries(output), Is.Empty);
        }

        private string WriteApk(string fileName, string label, string version, string package,
            params ZipBuilder.EntrySpec[] extra)
        {
            AxmlBuilder.Element manifest = AxmlBuilder.Root("manifest")
                .Attr("package", package)
                .AndroidAttr("versionName", version)
                .AndroidInt("versionCode", 1);
            manifest.Add("application").AndroidAttr("label", label);

            var entries = new List<ZipBuilder.EntrySpec>
            {
                new ZipBuilder.EntrySpec
                {
                    Name = "resources.arsc",
                    Data = File.ReadAllBytes(RepoLayout.PathOf("tests/ApkShellext2.Tests/fixtures/spectroid-resources.arsc"))
                },
                new ZipBuilder.EntrySpec
                {
                    Name = "AndroidManifest.xml",
                    Data = AxmlBuilder.Build(manifest)
                }
            };
            entries.AddRange(extra);

            string path = Path.Combine(work, fileName);
            File.WriteAllBytes(path, ZipBuilder.Build(entries.ToArray()));
            return path;
        }

        private void AssertFile(string fileName, string rootElement)
        {
            string path = Path.Combine(output, fileName);
            Assert.That(File.Exists(path), Is.True, "missing " + fileName + " in " + output);
            XmlDocument doc = new XmlDocument();
            doc.Load(path);
            Assert.That(doc.DocumentElement.Name, Is.EqualTo(rootElement));
        }
    }
}
