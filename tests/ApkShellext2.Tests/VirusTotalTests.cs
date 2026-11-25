using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

using ApkShellext2.Shell.Commands;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins the VirusTotal entry: the menu offers it for a supported selection without
    /// touching the files, and a click hashes the selection off the menu thread and opens
    /// one report address per package.
    /// </summary>
    [TestFixture]
    public class VirusTotalTests
    {
        private Action<string> previousHandler;

        [SetUp]
        public void SaveHandler()
        {
            previousHandler = CommandActions.OpenUrlHandler;
        }

        [TearDown]
        public void RestoreHandler()
        {
            CommandActions.OpenUrlHandler = previousHandler;
        }

        [Test]
        public void TheStoreSubmenuOffersTheVirusTotalEntry()
        {
            using (new SettingsScope()
                .Set("Language", "zh-CN")
                .Set("ShowNewVersion", "False")
                .Set("CheckForUpdates", "False")
                .Set("RenamePattern", "%AppName%_%Version%")
                .Set("ShowGooglePlay", "True")
                .Set("ShowAmazonStore", "True")
                .Set("ShowApkMirror", "False"))
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                CommandItem item = model.FindByVerb(CommandVerb.OpenVirusTotal);
                Assert.That(item, Is.Not.Null);
                Assert.That(item.Text, Is.EqualTo("在 VirusTotal 查看哈希"));
                Assert.That(item.Action, Is.EqualTo(CommandAction.OpenVirusTotal));
                Assert.That(item.Enabled, Is.True);
                Assert.That(item.Icon, Is.EqualTo(CommandIcon.None));
            }
        }

        [Test]
        public void AnIpaSelectionDoesNotOfferTheVirusTotalEntry()
        {
            using (new SettingsScope()
                .Set("Language", "zh-CN")
                .Set("ShowNewVersion", "False")
                .Set("CheckForUpdates", "False"))
            {
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.IpaPath() });

                Assert.That(model.FindByVerb(CommandVerb.OpenVirusTotal), Is.Null);
            }
        }

        [Test]
        public void BuildingTheMenuDoesNotHashOrOpenAnything()
        {
            var opened = new ConcurrentQueue<string>();
            CommandActions.OpenUrlHandler = opened.Enqueue;

            CommandModel model;
            using (new SettingsScope()
                .Set("Language", "zh-CN")
                .Set("ShowNewVersion", "False")
                .Set("CheckForUpdates", "False"))
            {
                model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });
            }

            Assert.That(model.FindByVerb(CommandVerb.OpenVirusTotal), Is.Not.Null);
            Thread.Sleep(200);
            Assert.That(opened, Is.Empty);
        }

        [Test]
        public void ExecutingOpensTheSha256AddressForThePackage()
        {
            string path = MenuFixtures.PlainApkPath();
            string sha256 = FileHasher.Compute(path).Sha256;
            var opened = new ConcurrentQueue<string>();
            CommandActions.OpenUrlHandler = opened.Enqueue;

            CommandActions.Execute(CommandAction.OpenVirusTotal, new[] { path });

            Assert.That(SpinWait.SpinUntil(() => opened.Count == 1, 10000), Is.True,
                "the background hash did not finish in time");
            string url;
            Assert.That(opened.TryDequeue(out url), Is.True);
            Assert.That(url, Is.EqualTo("https://www.virustotal.com/gui/file/" + sha256));
        }

        [Test]
        public void ExecutingSkipsFilesThatAreNotPackages()
        {
            string path = MenuFixtures.PlainApkPath();
            var opened = new ConcurrentQueue<string>();
            CommandActions.OpenUrlHandler = opened.Enqueue;

            CommandActions.Execute(CommandAction.OpenVirusTotal,
                new[] { path, MenuFixtures.UnknownPath() });

            Assert.That(SpinWait.SpinUntil(() => opened.Count == 1, 10000), Is.True);
            Thread.Sleep(200);
            Assert.That(opened.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExecutingStopsAfterTenPackages()
        {
            string folder = Path.Combine(Path.GetTempPath(), "apkshellext-vt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                byte[] apk = File.ReadAllBytes(MenuFixtures.PlainApkPath());
                string[] paths = new string[12];
                for (int i = 0; i < paths.Length; i++)
                {
                    paths[i] = Path.Combine(folder, "sample" + i + ".apk");
                    File.WriteAllBytes(paths[i], apk);
                }

                var opened = new ConcurrentQueue<string>();
                CommandActions.OpenUrlHandler = opened.Enqueue;

                CommandActions.Execute(CommandAction.OpenVirusTotal, paths);

                Assert.That(SpinWait.SpinUntil(() => opened.Count == 10, 20000), Is.True,
                    "only the first ten packages should be checked");
                Thread.Sleep(200);
                Assert.That(opened.Count, Is.EqualTo(10));
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }
    }
}

