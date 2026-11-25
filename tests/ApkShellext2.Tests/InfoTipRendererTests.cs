using System;
using System.IO;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The info-tip renderer walks the pattern line by line: a line whose facts are all
    /// missing is dropped whole, a retained line loses its trailing spaces, and only a tip
    /// that ends up with no text at all falls back to the package name.
    /// </summary>
    [TestFixture]
    public sealed class InfoTipRendererTests
    {
        private SettingsScope language;

        [SetUp]
        public void UseTheEnglishResources()
        {
            language = new SettingsScope().Set("Language", "en-US");
        }

        [TearDown]
        public void RestoreTheLanguage()
        {
            language.Dispose();
        }

        [Test]
        public void AFactThePackageLacksTakesItsWholeLineWithIt()
        {
            using (new SettingsScope().Set("ToolTipPattern", "%AppName%\r\nPacker: %Packer%\r\n%Package%"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()),
                    Is.EqualTo("My App\r\ncom.example.app"));
            }
        }

        [Test]
        public void APartialLineIsTrimmedButKept()
        {
            using (new SettingsScope().Set("ToolTipPattern", "Version: %Version% %Revision%"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.AppxPath()),
                    Is.EqualTo("Version: 1.2.3.4"));
            }
        }

        [Test]
        public void ALiteralLineIsAlwaysKept()
        {
            using (new SettingsScope().Set("ToolTipPattern", "%AppName%\r\nStatic"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()),
                    Is.EqualTo("My App\r\nStatic"));
            }
        }

        [Test]
        public void ATrailingSpaceIsRemovedFromARetainedLine()
        {
            using (new SettingsScope().Set("ToolTipPattern", "[%Version%] %NoSuchFact%"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()),
                    Is.EqualTo("[2.0.1]"));
            }
        }

        [Test]
        public void ConsecutiveBlankLinesCollapseToOne()
        {
            using (new SettingsScope().Set("ToolTipPattern", "%AppName%\r\n \r\n\t\r\n%Package%"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()),
                    Is.EqualTo("My App\r\n\r\ncom.example.app"));
            }
        }

        [Test]
        public void TrailingBlankLinesAreRemoved()
        {
            using (new SettingsScope().Set("ToolTipPattern", "%AppName%\r\n \r\n\t"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()),
                    Is.EqualTo("My App"));
            }
        }

        [Test]
        public void APatternOfOnlyEmptyFactsFallsBackToTheAppName()
        {
            using (new SettingsScope().Set("ToolTipPattern", "Min SDK: %MinSdk%\r\nPacker: %Packer%"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()),
                    Is.EqualTo("My App"));
            }
        }

        [Test]
        public void AnEmptyAppNameFallsBackToTheFailureText()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "empty-app-name-" + Guid.NewGuid().ToString("N") + ".appx");
            File.WriteAllBytes(path, AppxFixtures.Appx(appName: ""));
            try
            {
                using (new SettingsScope().Set("ToolTipPattern", "%AppName%"))
                {
                    Assert.That(ProductApi.RenderInfoTip(path),
                        Is.EqualTo("Fail to read file"));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void TheDefaultPatternShowsOnlyTheFactsAnIpaHas()
        {
            string path = MenuFixtures.IpaPath();
            using (new SettingsScope().Set("ToolTipPattern", ProductApi.NonLocalizeString("strInfoTipDefault")))
            {
                Assert.That(ProductApi.RenderInfoTip(path),
                    Is.EqualTo("My App\r\ncom.example.app\r\nVersion: 2.0.1 42\r\nSize: " +
                        ProductApi.FileSize(path)));
            }
        }

        [Test]
        public void TheDefaultPatternShowsTheSdkLevelsOfAnApk()
        {
            string path = MenuFixtures.PlainApkPath();
            using (new SettingsScope().Set("ToolTipPattern", ProductApi.NonLocalizeString("strInfoTipDefault")))
            {
                Assert.That(ProductApi.RenderInfoTip(path),
                    Is.EqualTo("Spectroid\r\norg.intoorbit.spectrum\r\nVersion: 1.1.2 31\r\nSize: " +
                        ProductApi.FileSize(path) + "\r\nMin SDK: 14\r\nTarget SDK: 33"));
            }
        }

        [Test]
        public void TheDefaultPatternListsTheSevenFacts()
        {
            Assert.That(
                ProductApi.NonLocalizeString("strInfoTipDefault").Replace("\r\n", "\n"),
                Is.EqualTo("%AppName%\n%Package%\nVersion: %Version% %Revision%\nSize: %FileSize%\n" +
                    "Min SDK: %MinSdk%\nTarget SDK: %TargetSdk%\nPacker: %Packer%"));
        }

        [Test]
        public void APackedApkShowsAllSevenLines()
        {
            byte[] packed = SigningBlockBuilder.Insert(MenuFixtures.PlainApk(),
                SigningBlockBuilder.Block(SigningBlockBuilder.Pair.Of(SigningBlockBuilder.Frosting)));
            string path = Path.Combine(Path.GetTempPath(),
                "packed-" + Guid.NewGuid().ToString("N") + ".apk");
            File.WriteAllBytes(path, packed);
            try
            {
                using (new SettingsScope().Set("ToolTipPattern",
                    ProductApi.NonLocalizeString("strInfoTipDefault")))
                {
                    Assert.That(ProductApi.RenderInfoTip(path),
                        Is.EqualTo("Spectroid\r\norg.intoorbit.spectrum\r\nVersion: 1.1.2 31\r\nSize: " +
                            ProductApi.FileSize(path) + "\r\nMin SDK: 14\r\nTarget SDK: 33\r\nPacker: Frosting"));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void CRLFAndLFPatternsProduceTheSameTip()
        {
            string path = MenuFixtures.IpaPath();
            string tip;
            using (new SettingsScope().Set("ToolTipPattern", "%AppName%\r\n%Package%"))
            {
                tip = ProductApi.RenderInfoTip(path);
            }

            using (new SettingsScope().Set("ToolTipPattern", "%AppName%\n%Package%"))
            {
                Assert.That(ProductApi.RenderInfoTip(path), Is.EqualTo(tip));
            }
        }

        [Test]
        public void AnEmptyPatternProducesAnEmptyTip()
        {
            using (new SettingsScope().Set("ToolTipPattern", ""))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()), Is.Empty);
            }
        }

        [Test]
        public void AWhitespacePatternProducesAnEmptyTip()
        {
            using (new SettingsScope().Set("ToolTipPattern", "  \r\n \t"))
            {
                Assert.That(ProductApi.RenderInfoTip(MenuFixtures.IpaPath()), Is.Empty);
            }
        }
    }
}
