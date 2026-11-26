using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using ApkQuickReader;
using ApkShellext2.Shell.Commands;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The info-tip pattern expands the SDK and packer facts a reader exposes. The expected
    /// strings are written out here rather than derived from the producer constants, so a token
    /// that stops expanding (or a token compared against the wrong property) fails the suite.
    /// </summary>
    [TestFixture]
    public sealed class TipPatternVariableTests
    {
        [Test]
        public void MinSdkExpandsToTheManifestValue()
        {
            Assert.That(CommandRename.ReplaceVariables("%MinSdk%", Reader()), Is.EqualTo("34"));
        }

        [Test]
        public void TargetSdkExpandsToTheManifestValue()
        {
            Assert.That(CommandRename.ReplaceVariables("%TargetSdk%", Reader()), Is.EqualTo("35"));
        }

        [Test]
        public void PackerExpandsToTheSigningBlockValue()
        {
            Assert.That(CommandRename.ReplaceVariables("%Packer%", Reader()), Is.EqualTo("Frosting"));
        }

        [Test]
        public void EmptyMinSdkExpandsToAnEmptyString()
        {
            PatternReader reader = Reader();
            reader.MinSdk = "";
            Assert.That(CommandRename.ReplaceVariables("[%MinSdk%]", reader), Is.EqualTo("[]"));
        }

        [Test]
        public void EmptyTargetSdkExpandsToAnEmptyString()
        {
            PatternReader reader = Reader();
            reader.TargetSdk = "";
            Assert.That(CommandRename.ReplaceVariables("[%TargetSdk%]", reader), Is.EqualTo("[]"));
        }

        [Test]
        public void EmptyPackerExpandsToAnEmptyString()
        {
            PatternReader reader = Reader();
            reader.Packer = "";
            Assert.That(CommandRename.ReplaceVariables("[%Packer%]", reader), Is.EqualTo("[]"));
        }

        [Test]
        public void TheThreeTokensExpandInOnePattern()
        {
            Assert.That(CommandRename.ReplaceVariables("%AppName% %MinSdk%/%TargetSdk% %Packer%", Reader()),
                Is.EqualTo("Spectroid 34/35 Frosting"));
        }

        [Test]
        public void DebuggableTrueStillSaysDebug()
        {
            PatternReader reader = Reader();
            reader.Debuggable = true;
            Assert.That(CommandRename.ReplaceVariables("%Debuggable%", reader), Is.EqualTo("Debug"));
        }

        [Test]
        public void DebuggableFalseStillSaysRelease()
        {
            PatternReader reader = Reader();
            reader.Debuggable = false;
            Assert.That(CommandRename.ReplaceVariables("%Debuggable%", reader), Is.EqualTo("Release"));
        }

        [Test]
        public void DebuggableStaysEmptyForNonAndroid()
        {
            PatternReader reader = Reader();
            reader.Type = AppPackageReader.AppType.iOSApp;
            Assert.That(CommandRename.ReplaceVariables("[%Debuggable%]", reader), Is.EqualTo("[]"));
        }

        [Test]
        public void AnUnknownTokenStillExpandsToAnEmptyString()
        {
            Assert.That(CommandRename.ReplaceVariables("[%NoSuchFact%]", Reader()), Is.EqualTo("[]"));
        }

        [Test]
        public void TheRenamePathSharesTheTokenExpansion()
        {
            using (new SettingsScope().Set("RenamePattern", "%MinSdk%_%TargetSdk%_%Packer%_%AppName%"))
            {
                string path = MenuFixtures.PlainApkPath();
                string renamed = CommandRename.GetNewFileName(path, new[] { path });
                // the rename pattern and the info-tip pattern go through the same expansion,
                // so the SDK and packer tokens keep their values in both
                Assert.That(Path.GetFileName(renamed), Is.EqualTo("14_33__Spectroid.apk"));
            }
        }

        private static PatternReader Reader()
        {
            return new PatternReader
            {
                FileName = MenuFixtures.PlainApkPath(),
                AppName = "Spectroid",
                PackageName = "org.intoorbit.spectrum",
                Version = "1.1.2",
                Revision = "31",
                Publisher = "",
                MinSdk = "34",
                TargetSdk = "35",
                Debuggable = null,
                Packer = "Frosting",
                Type = AppPackageReader.AppType.AndroidApp,
            };
        }

        /// <summary>
        /// A reader whose every fact is controlled by the test, including the SDK and packer
        /// values that a real package may not declare.
        /// </summary>
        internal sealed class PatternReader : IPackageReader
        {
            public string FileName { get; set; }
            public string AppName { get; set; }
            public string PackageName { get; set; }
            public string Version { get; set; }
            public string Revision { get; set; }
            public CultureInfo Culture { get; set; }
            public string Publisher { get; set; }
            public string AppID { get; set; }
            public Bitmap Icon { get { return null; } }
            public AppPackageReader.AppType Type { get; set; }
            public string MinSdk { get; set; }
            public string TargetSdk { get; set; }
            public bool? Debuggable { get; set; }
            public string Packer { get; set; }

        public string IconEntryPath { get { return ""; } }
        public PackageVerdict Verdict { get { return PackageVerdict.Info; } }
        public string VerdictEvidence { get { return ""; } }

            public PatternReader()
            {
                FileName = "";
                AppName = "";
                PackageName = "";
                Version = "";
                Revision = "";
                Publisher = "";
                MinSdk = "";
                TargetSdk = "";
                Packer = "";
            }

            public void setFlag(string flag, object value) { }

            public object getFlag(string flag) { return null; }

            public Bitmap getIcon(Size size) { return null; }

            public void Dispose() { }
        }
    }
}
