using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// version.txt is the single source of the product version: the
    /// GenerateVersionAssemblyInfo target feeds it to WriteCodeFragment, and
    /// Properties/AssemblyInfo.cs deliberately declares no AssemblyVersion or
    /// FileVersion of its own. These tests pin that wiring - if the target is
    /// dropped, or a literal version creeps back into a source file, the build
    /// fails here instead of shipping a dll whose version contradicts version.txt.
    /// </summary>
    [TestFixture]
    public class VersionSingleSourceTests
    {
        [Test]
        public void TestProject_ReferencesTheProductAssembly()
        {
            Assert.That(ProductApi.Assembly.GetName().Name, Is.EqualTo("ApkShellext2"));
            Assert.That(RepoLayout.Root, Is.Not.Empty);
        }

        [Test]
        public void VersionFile_HoldsOneFourPartVersion()
        {
            string text = File.ReadAllText(RepoLayout.PathOf("version.txt"), Encoding.UTF8).Trim();
            Assert.That(text, Does.Match(@"^\d+\.\d+\.\d+\.\d+$"), "version.txt must hold one four-part version and nothing else");

            Version parsed;
            Assert.That(Version.TryParse(text, out parsed), Is.True, "version.txt is not a parsable System.Version");
        }

        [Test]
        public void AssemblyVersion_ComesFromVersionFile()
        {
            Version expected = ExpectedVersion();
            Version actual = ProductApi.Assembly.GetName().Version;
            Assert.That(actual, Is.EqualTo(expected),
                "AssemblyVersion is " + actual + " but version.txt says " + expected +
                "; the GenerateVersionAssemblyInfo target no longer drives the assembly version");
        }

        [Test]
        public void FileVersion_ComesFromVersionFile()
        {
            Version expected = ExpectedVersion();
            string location = ProductApi.Assembly.Location;
            Assert.That(File.Exists(location), Is.True, "the product assembly has no location on disk: " + location);

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(location);
            Assert.That(info.FileVersion, Is.EqualTo(expected.ToString()),
                "the dll's Win32 file version is " + info.FileVersion + " but version.txt says " + expected +
                " (Explorer's properties dialog reads this value)");
        }

        [Test]
        public void ProductVersion_ComesFromVersionFile()
        {
            // the versioned 7z name and the installer read ProductVersion too
            Version expected = ExpectedVersion();
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(ProductApi.Assembly.Location);
            Assert.That(info.ProductVersion, Is.EqualTo(expected.ToString()));
        }

        private static Version ExpectedVersion()
        {
            string text = File.ReadAllText(RepoLayout.PathOf("version.txt"), Encoding.UTF8).Trim();
            return Version.Parse(text);
        }
    }
}
