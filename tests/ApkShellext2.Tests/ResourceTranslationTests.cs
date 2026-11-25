using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Properties\Resources.resx is the neutral set every UI string comes from, and
    /// Properties\Resources.&lt;culture&gt;.resx is the satellite ResourceManager falls
    /// back to for a non-English UI. A satellite that lags behind the neutral file does
    /// not fail the build - the missing entries simply render in English inside an
    /// otherwise translated menu - so these tests pin the key sets together: a new
    /// neutral string, or a renamed one, breaks here until every satellite carries it.
    /// </summary>
    [TestFixture]
    public class ResourceTranslationTests
    {
        private const string PropertiesFolder = "ApkShellext2/Properties/";
        private const string NeutralFileName = "Resources.resx";

        [Test]
        public void EverySatellite_MatchesTheNeutralKeySet()
        {
            Dictionary<string, string> neutral = ReadResx(NeutralFileName);
            List<string> missing = new List<string>();
            List<string> orphaned = new List<string>();

            foreach (string fileName in SatelliteFileNames())
            {
                Dictionary<string, string> satellite = ReadResx(fileName);

                List<string> absent = new List<string>();
                foreach (string key in neutral.Keys)
                {
                    if (!satellite.ContainsKey(key))
                    {
                        absent.Add(key);
                    }
                }
                if (absent.Count > 0)
                {
                    missing.Add(fileName + ": " + string.Join(", ", absent.ToArray()));
                }

                List<string> extra = new List<string>();
                foreach (string key in satellite.Keys)
                {
                    if (!neutral.ContainsKey(key))
                    {
                        extra.Add(key);
                    }
                }
                if (extra.Count > 0)
                {
                    orphaned.Add(fileName + ": " + string.Join(", ", extra.ToArray()));
                }
            }

            Assert.That(missing, Is.Empty,
                "these satellites do not translate every key of " + NeutralFileName +
                " (those entries fall back to English):" + Environment.NewLine +
                string.Join(Environment.NewLine, missing.ToArray()));
            Assert.That(orphaned, Is.Empty,
                "these satellites define keys " + NeutralFileName + " does not have " +
                "(a key was probably renamed on one side only):" + Environment.NewLine +
                string.Join(Environment.NewLine, orphaned.ToArray()));
        }

        [Test]
        public void EveryEntry_HasANonEmptyValue()
        {
            List<string> fileNames = new List<string> { NeutralFileName };
            fileNames.AddRange(SatelliteFileNames());

            List<string> empty = new List<string>();
            foreach (string fileName in fileNames)
            {
                foreach (KeyValuePair<string, string> entry in ReadResx(fileName))
                {
                    if (entry.Value.Trim().Length == 0)
                    {
                        empty.Add(fileName + ": " + entry.Key);
                    }
                }
            }

            Assert.That(empty, Is.Empty,
                "these entries would render a blank menu item or label:" + Environment.NewLine +
                string.Join(Environment.NewLine, empty.ToArray()));
        }

        private static List<string> SatelliteFileNames()
        {
            List<string> names = new List<string>();
            foreach (string file in Directory.GetFiles(RepoLayout.PathOf(PropertiesFolder), "Resources.*.resx"))
            {
                names.Add(Path.GetFileName(file));
            }
            names.Sort(StringComparer.Ordinal);
            Assert.That(names, Is.Not.Empty, "no Resources.<culture>.resx satellite found under " +
                RepoLayout.PathOf(PropertiesFolder));
            return names;
        }

        /// <summary>
        /// Reads one resx into name -> value. A checked-in resx is the source of truth
        /// here, not the compiled resources, so the file is parsed directly: the test
        /// must fail on the checkout the developer is about to commit.
        /// </summary>
        private static Dictionary<string, string> ReadResx(string fileName)
        {
            string path = RepoLayout.PathOf(PropertiesFolder + fileName);
            Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlNodeList nodes = document.SelectNodes("/root/data");
                foreach (XmlNode node in nodes)
                {
                    XmlAttribute name = node.Attributes["name"];
                    XmlNode value = node.SelectSingleNode("value");
                    if (name == null || value == null)
                    {
                        continue;
                    }
                    entries[name.Value] = value.InnerText;
                }
            }
            catch (XmlException e)
            {
                Assert.Fail("cannot parse " + path + ": " + e.Message);
            }
            catch (IOException e)
            {
                Assert.Fail("cannot read " + path + ": " + e.Message);
            }

            Assert.That(entries.Count, Is.GreaterThan(0), path + " holds no <data> entries");
            return entries;
        }
    }
}
