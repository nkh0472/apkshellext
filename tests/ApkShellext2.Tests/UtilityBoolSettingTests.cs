using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The boolean reader accepts every form a build may have written: the DWORD form a script or an
    /// older installer writes, and the text form Preferences writes. Before it existed, the reader
    /// compared the raw value with "True", so a DWORD 1 switched the feature off.
    /// </summary>
    [TestFixture]
    public sealed class UtilityBoolSettingTests
    {
        [TestCase("1")]
        [TestCase("True")]
        [TestCase(" true ")]
        [TestCase("TRUE")]
        [TestCase("yes")]
        [TestCase("On")]
        public void ATextValueThatMeansOnReadsTrue(string value)
        {
            using (new SettingsScope().Set("EnableThumbnail", value))
            {
                Assert.That(Utility.GetSettingBool("EnableThumbnail"), Is.True);
            }
        }

        [TestCase("0")]
        [TestCase("False")]
        [TestCase(" false ")]
        [TestCase("no")]
        [TestCase("OFF")]
        [TestCase("")]
        public void ATextValueThatMeansOffReadsFalse(string value)
        {
            using (new SettingsScope().Set("EnableThumbnail", value))
            {
                Assert.That(Utility.GetSettingBool("EnableThumbnail", true), Is.False);
            }
        }

        [Test]
        public void ADwordOneReadsTrue()
        {
            using (new SettingsScope().SetDword("EnableThumbnail", 1))
            {
                Assert.That(Utility.GetSettingBool("EnableThumbnail"), Is.True);
            }
        }

        [Test]
        public void ADwordZeroReadsFalse()
        {
            using (new SettingsScope().SetDword("EnableThumbnail", 0))
            {
                Assert.That(Utility.GetSettingBool("EnableThumbnail", true), Is.False);
            }
        }

        [Test]
        public void AnUnrecognisedValueKeepsTheCallersDefault()
        {
            using (new SettingsScope().Set("EnableThumbnail", "sometimes"))
            {
                Assert.That(Utility.GetSettingBool("EnableThumbnail", true), Is.True);
                Assert.That(Utility.GetSettingBool("EnableThumbnail", false), Is.False);
            }
        }
    }
}
