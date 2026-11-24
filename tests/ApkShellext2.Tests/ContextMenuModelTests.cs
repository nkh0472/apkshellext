using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ApkShellext2.Shell.Commands;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the data model behind the context menu: the items, their ids, verbs, flags and
    /// nesting for every package kind, the two shipped languages, and the rename pattern
    /// expansion. The native handler renders this model, so these tests pin the contract between
    /// the model and the menu the shell sees.
    /// </summary>
    [TestFixture]
    public class ContextMenuModelTests
    {
        private const int IdCmdFirst = 100;

        [TearDown]
        public void RestoreThreadCulture()
        {
            ApkShellext2.Utility.Localize();
        }

        private static SettingsScope Chinese()
        {
            return new SettingsScope()
                .Set("Language", "zh-CN")
                .Set("CheckForUpdates", "False")
                .Set("RenamePattern", "%AppName%_%Version%")
                .Set("ShowGooglePlay", "True")
                .Set("ShowAmazonStore", "True")
                .Set("ShowApkMirror", "False")
                .Set("ShowAppStoreWhenMultiSelected", "True")
                .Set("ShowNewVersion", "False")
                .Set("ShowMenuIcon", "True")
                .Set("ShowAppleStore", "True")
                .Set("ShowMSStore", "True");
        }

        private static string[] Table(CommandModel model)
        {
            return MenuTable.Expected(model, IdCmdFirst);
        }

        private static string[] Texts(CommandModel model)
        {
            return model.Items.Where(i => !i.IsSeparator).Select(i => i.Text).ToArray();
        }

        [Test]
        public void PlainApk_RendersTheChineseMenu()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                Assert.That(Table(model), Is.EqualTo(new[]
                {
                    "[0] id=100 fType=0x0 fState=0x0 submenu=1 bitmap=1 text=\"APK文件助手\"",
                    "  [0] id=101 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"更名为 Spectroid_1.1.2.apk\"",
                    "  [1] id=102 fType=0x0 fState=0x0 submenu=0 bitmap=0 text=\"导出信息 (CSV)…\"",
                    "  [2] id=103 fType=0x0 fState=0x0 submenu=1 bitmap=1 text=\"提取文件\"",
                    "    [0] id=104 fType=0x0 fState=0x0 submenu=0 bitmap=0 text=\"提取 androidmanifest.xml\"",
                    "    [1] id=105 fType=0x0 fState=0x0 submenu=0 bitmap=0 text=\"提取XML文件...\"",
                    "  [3] id=0 fType=0x800 fState=0x3 submenu=0 bitmap=0 text=\"\"",
                    "  [4] id=107 fType=0x0 fState=0x0 submenu=1 bitmap=1 text=\"搜索商店\"",
                    "    [0] id=108 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"在Google Play网页上查看Spectroid\"",
                    "    [1] id=109 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"在Amazon Appstore上查看Spectroid\"",
                    "  [5] id=0 fType=0x800 fState=0x3 submenu=0 bitmap=0 text=\"\"",
                    "  [6] id=111 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"选项和更新\""
                }));
            }
        }

        [Test]
        public void PlainApk_RendersTheEnglishMenu()
        {
            using (Chinese().Set("Language", "en-US"))
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                Assert.That(Table(model), Is.EqualTo(new[]
                {
                    "[0] id=100 fType=0x0 fState=0x0 submenu=1 bitmap=1 text=\"APK Shell Extension\"",
                    "  [0] id=101 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"Rename to Spectroid_1.1.2.apk\"",
                    "  [1] id=102 fType=0x0 fState=0x0 submenu=0 bitmap=0 text=\"Export info (CSV)…\"",
                    "  [2] id=103 fType=0x0 fState=0x0 submenu=1 bitmap=1 text=\"Dump\"",
                    "    [0] id=104 fType=0x0 fState=0x0 submenu=0 bitmap=0 text=\"Dump androidmanifest.xml\"",
                    "    [1] id=105 fType=0x0 fState=0x0 submenu=0 bitmap=0 text=\"Dump XML ...\"",
                    "  [3] id=0 fType=0x800 fState=0x3 submenu=0 bitmap=0 text=\"\"",
                    "  [4] id=107 fType=0x0 fState=0x0 submenu=1 bitmap=1 text=\"Search in Stores\"",
                    "    [0] id=108 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"Search Spectroid in GooglePlay\"",
                    "    [1] id=109 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"Search Spectroid in Amazon Appstore\"",
                    "  [5] id=0 fType=0x800 fState=0x3 submenu=0 bitmap=0 text=\"\"",
                    "  [6] id=111 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"Preferences\""
                }));
            }
        }

        [Test]
        public void HostileResourceTable_DoesNotChangeTheMenu()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.HostileApkPath() });

                Assert.That(model.Count, Is.EqualTo(12), "the hostile table does not remove any entry");
                Assert.That(model.Items[1].Enabled, Is.True);
                Assert.That(model.Items[1].Text, Does.StartWith("更名为 ").And.EndWith("_1.1.2.apk"));
            }
        }

        [Test]
        public void Ipa_OffersTheAppleStoreEntryAtTopLevel()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.IpaPath() });

                Assert.That(Table(model), Is.EqualTo(new[]
                {
                    "[0] id=100 fType=0x0 fState=0x0 submenu=1 bitmap=1 text=\"APK文件助手\"",
                    "  [0] id=101 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"更名为 My App_2.0.1.ipa\"",
                    "  [1] id=102 fType=0x0 fState=0x0 submenu=0 bitmap=0 text=\"导出信息 (CSV)…\"",
                    "  [2] id=0 fType=0x800 fState=0x3 submenu=0 bitmap=0 text=\"\"",
                    "  [3] id=104 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"在Appstore上查看 My App\"",
                    "  [4] id=0 fType=0x800 fState=0x3 submenu=0 bitmap=0 text=\"\"",
                    "  [5] id=106 fType=0x0 fState=0x0 submenu=0 bitmap=1 text=\"选项和更新\""
                }));
            }
        }

        [Test]
        public void Appx_OffersTheMicrosoftStoreEntry()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.AppxPath() });

                Assert.That(model.Items.Select(i => i.Action), Is.EqualTo(new[]
                {
                    CommandAction.None,
                    CommandAction.Rename,
                    CommandAction.Export,
                    CommandAction.None,
                    CommandAction.MicrosoftStore,
                    CommandAction.None,
                    CommandAction.Preferences
                }));
                Assert.That(model.Items[4].Text, Is.EqualTo("在Microsoft Store上查看Test App"));
                Assert.That(model.Items[1].Text, Is.EqualTo("更名为 Test App_1.2.3.4.appx"));
                Assert.That(model.Items[1].Children, Is.Empty);
            }
        }

        [Test]
        public void AppxBundle_UsesTheBundleExtensionInTheRename()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.AppxBundlePath() });

                Assert.That(model.Count, Is.EqualTo(7));
                Assert.That(model.Items[1].Text, Is.EqualTo("更名为 Test App_1.2.3.4.appxbundle"));
                Assert.That(model.Items[4].Action, Is.EqualTo(CommandAction.MicrosoftStore));
            }
        }

        [Test]
        public void Aab_HasNoDumpSubmenuAndNoStoreEntry()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.AabPath() });

                Assert.That(model.Count, Is.EqualTo(6),
                    "an .aab shows the rename, the export, two separators and the preferences entry");
                Assert.That(model.Items.Count(i => i.IsSeparator), Is.EqualTo(2));
                Assert.That(model.Items.Select(i => i.Action), Is.EqualTo(new[]
                {
                    CommandAction.None,
                    CommandAction.Rename,
                    CommandAction.Export,
                    CommandAction.None,
                    CommandAction.None,
                    CommandAction.Preferences
                }));
                Assert.That(model.Items[1].Text, Does.StartWith("更名为 ").And.EndWith(".aab"));
            }
        }

        [Test]
        public void SplitBundle_KeepsTheApkExtensionRulesOff()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.ApksPath() });

                Assert.That(model.Count, Is.EqualTo(6));
                Assert.That(model.Items[1].Text, Is.EqualTo("更名为 Spectroid_1.1.2.apks"));
            }
        }

        [Test]
        public void UnsupportedExtension_IsRejected()
        {
            using (Chinese())
            {
                Assert.That(() => CommandCatalog.Build(new[] { MenuFixtures.UnknownPath() }),
                    Throws.TypeOf<NotSupportedException>(),
                    "the menu cannot be built for a file no reader accepts");
            }
        }

        [Test]
        public void RenamePattern_ExpandsVariablesAndStripsInvalidCharacters()
        {
            using (Chinese().Set("RenamePattern", "%AppName%_%Version%:a|b"))
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                Assert.That(model.Items[1].Text, Is.EqualTo("更名为 Spectroid_1.1.2ab.apk"));
            }
        }

        [Test]
        public void MultiSelection_ShowsThePatternAndObeysTheStoreSetting()
        {
            using (Chinese().Set("ShowAppStoreWhenMultiSelected", "False"))
            {
                CommandModel model = CommandCatalog.Build(new[]
                {
                    MenuFixtures.PlainApkPath(),
                    MenuFixtures.HostileApkPath()
                });

                Assert.That(model.Items[1].Text, Is.EqualTo("更名为 %AppName%_%Version%"));
                Assert.That(model.Items.Where(i => i.Action == CommandAction.GooglePlay).Single().Enabled, Is.False);
                Assert.That(model.Items.Where(i => i.Action == CommandAction.AmazonAppStore).Single().Enabled, Is.False);
                Assert.That(model.Items.Where(i => i.Action == CommandAction.Preferences).Single().Enabled, Is.True);
            }
        }

        [Test]
        public void NewVersion_IsAppendedToTheRootText()
        {
            using (Chinese().Set("ShowNewVersion", "True").Set("LatestVersion", "9.9.9.9"))
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                Assert.That(model.Root.Text, Is.EqualTo("APK文件助手 (已有更新版本可用: 9.9.9.9)"));
            }
        }

        [Test]
        public void ShowMenuIconOff_SwitchesTheRootIconToTheLogo()
        {
            using (Chinese().Set("ShowMenuIcon", "False"))
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                Assert.That(model.Root.Icon, Is.EqualTo(CommandIcon.Logo));
            }
        }

        [Test]
        public void ShowApkMirror_AddsTheMirrorEntryToTheStoreSubmenu()
        {
            using (Chinese().Set("ShowApkMirror", "True"))
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                CommandItem store = model.Items.Single(i => i.Children.Any(c => c.Action == CommandAction.ApkMirror));
                Assert.That(store.Children.Select(c => c.Action), Is.EqualTo(new[]
                {
                    CommandAction.GooglePlay,
                    CommandAction.AmazonAppStore,
                    CommandAction.ApkMirror
                }));
            }
        }

        [Test]
        public void Verbs_ResolveToTheSameItemsAsTheIds()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                Assert.That(model.FindByIndex(CommandIds.First), Is.SameAs(model.Root));
                Assert.That(model.FindByIndex(1).Verb, Is.EqualTo(CommandVerb.Rename));
                Assert.That(model.FindByVerb(CommandVerb.Rename), Is.SameAs(model.FindByIndex(1)));
                Assert.That(model.FindByIndex(2).Verb, Is.EqualTo(CommandVerb.ExportCsv));
                Assert.That(model.FindByVerb(CommandVerb.ExportCsv), Is.SameAs(model.FindByIndex(2)));
                Assert.That(model.FindByVerb(CommandVerb.Preferences).Index, Is.EqualTo(11));
                Assert.That(model.FindByVerb("nope"), Is.Null);
                Assert.That(model.FindByIndex(CommandIds.Last + 1), Is.Null);
            }
        }

        [Test]
        public void Invoke_ReportsSuccessForEverythingButExecutesOnlyCommands()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                Assert.That(model.Invoke(null), Is.False);
                Assert.That(model.Invoke(model.Root), Is.True, "a submenu parent is a valid id that does nothing");
                Assert.That(model.Invoke(model.Items[6]), Is.True, "a separator is a valid id that does nothing");
                bool reached = false;
                Action<IReadOnlyList<string>> previous = CommandActions.PreferencesHandler;
                try
                {
                    CommandActions.PreferencesHandler = paths => { reached = paths.Count == 1; };
                    Assert.That(model.Invoke(model.FindByVerb(CommandVerb.Preferences)), Is.True);
                    Assert.That(reached, Is.True, "the preferences command routes through the injectable seam");
                }
                finally
                {
                    CommandActions.PreferencesHandler = previous;
                }
            }
        }
        [Test]
        public void Export_IsTheItemRightAfterRename()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                CommandItem export = model.FindByVerb(CommandVerb.ExportCsv);
                Assert.That(export, Is.Not.Null);
                Assert.That(export.Action, Is.EqualTo(CommandAction.Export));
                Assert.That(export.Index, Is.EqualTo(2));
                Assert.That(export.Text, Is.EqualTo("导出信息 (CSV)…"));
                Assert.That(export.Icon, Is.EqualTo(CommandIcon.None));
                Assert.That(export.Enabled, Is.True);
                Assert.That(model.Items[1].Action, Is.EqualTo(CommandAction.Rename));
            }
        }

        [TestCase("ipa")]
        [TestCase("appx")]
        [TestCase("appxbundle")]
        [TestCase("aab")]
        [TestCase("apks")]
        public void Export_IsOfferedForEverySupportedPackageKind(string kind)
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { SamplePath(kind) });

                Assert.That(model.FindByVerb(CommandVerb.ExportCsv).Action,
                    Is.EqualTo(CommandAction.Export));
            }
        }

        private static string SamplePath(string kind)
        {
            switch (kind)
            {
                case "ipa": return MenuFixtures.IpaPath();
                case "appx": return MenuFixtures.AppxPath();
                case "appxbundle": return MenuFixtures.AppxBundlePath();
                case "aab": return MenuFixtures.AabPath();
                case "apks": return MenuFixtures.ApksPath();
                default: throw new ArgumentOutOfRangeException("kind", kind, "unknown sample");
            }
        }

        [Test]
        public void Export_IsOfferedWhenOnlyOneFileOfAMixedSelectionIsSupported()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[]
                {
                    MenuFixtures.PlainApkPath(),
                    MenuFixtures.UnknownPath()
                });

                Assert.That(model.FindByVerb(CommandVerb.ExportCsv), Is.Not.Null,
                    "one readable package keeps the export available for the whole selection");
            }
        }

        [Test]
        public void Invoke_ExportsThroughTheInjectableSeam()
        {
            using (Chinese())
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                IReadOnlyList<string> reached = null;
                Action<IReadOnlyList<string>> previous = CommandActions.ExportHandler;
                try
                {
                    CommandActions.ExportHandler = paths => { reached = paths; };
                    Assert.That(model.Invoke(model.FindByVerb(CommandVerb.ExportCsv)), Is.True);
                    Assert.That(reached, Is.Not.Null);
                    Assert.That(reached.Count, Is.EqualTo(1));
                    Assert.That(reached[0], Is.EqualTo(MenuFixtures.PlainApkPath()));
                }
                finally
                {
                    CommandActions.ExportHandler = previous;
                }
            }
        }
    }
}
