using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PlistCS;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Pins thread safety of the binary plist reader: readBinary creates a
    /// BinaryPlistReader per call, so concurrent reads cannot share working state.
    /// This test parses two different plists from 8 threads and demands that every
    /// result matches its single-threaded reference, which fails immediately if
    /// shared state is reintroduced.
    ///
    /// The fixtures are built with Plist.writeBinary single-threaded on purpose:
    /// the *writer* still keeps static state (by design, it is not hot), so building
    /// fixtures concurrently would test the wrong thing.
    /// </summary>
    [TestFixture]
    public class PlistConcurrencyTests
    {
        private const int Threads = 8;
        private const int Rounds = 50;

        [Test]
        [Timeout(120000)]
        public void ParsesTwoDifferentPlistsConcurrently_WithIdenticalResults()
        {
            byte[] first = Plist.writeBinary(Fixture("alpha", 1));
            byte[] second = Plist.writeBinary(Fixture("beta", 2));

            string expectedFirst = Describe(Plist.readPlist(first));
            string expectedSecond = Describe(Plist.readPlist(second));
            Assert.That(expectedFirst, Is.Not.EqualTo(expectedSecond), "the two fixtures must actually differ, otherwise a mix-up is invisible");
            Assert.That(expectedFirst, Does.Contain("alpha"));

            ConcurrentQueue<string> failures = new ConcurrentQueue<string>();
            using (ManualResetEventSlim start = new ManualResetEventSlim(false))
            {
                Task[] workers = new Task[Threads];
                for (int worker = 0; worker < Threads; worker++)
                {
                    int id = worker;
                    workers[worker] = Task.Run(() =>
                    {
                        start.Wait();
                        for (int round = 0; round < Rounds; round++)
                        {
                            bool takeFirst = ((id + round) & 1) == 0;
                            byte[] input = takeFirst ? first : second;
                            string expected = takeFirst ? expectedFirst : expectedSecond;
                            try
                            {
                                string actual = Describe(Plist.readPlist(input));
                                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                                {
                                    failures.Enqueue("thread " + id + " round " + round + " returned " + actual);
                                }
                            }
                            catch (Exception ex)
                            {
                                failures.Enqueue("thread " + id + " round " + round + " threw " + ex.GetType().Name + ": " + ex.Message);
                            }
                        }
                    });
                }

                start.Set();
                Assert.That(Task.WaitAll(workers, TimeSpan.FromSeconds(100)), Is.True,
                    "concurrent parsing did not finish in 100 s");
            }

            Assert.That(failures, Is.Empty,
                "concurrent reads must not see each other's state:\n" + string.Join("\n", failures));
        }

        [Test]
        public void ReadsTheSameFixtureRepeatedly_WithoutDrift()
        {
            byte[] plist = Plist.writeBinary(Fixture("gamma", 3));
            string reference = Describe(Plist.readPlist(plist));

            for (int i = 0; i < 200; i++)
            {
                Assert.That(Describe(Plist.readPlist(plist)), Is.EqualTo(reference), "parse " + i + " drifted");
            }
        }

        private static Dictionary<string, object> Fixture(string name, int seed)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "seed", seed },
                {
                    "items", new List<object>
                    {
                        "first",
                        seed * 3,
                        seed % 2 == 1,
                        new List<object> { "deep-" + name, seed + 100 },
                    }
                },
                { "enabled", seed % 2 == 1 },
            };
        }

        /// <summary>
        /// Stable, order-independent rendering of a parsed plist, so two results can
        /// be compared without depending on Dictionary enumeration order.
        /// </summary>
        private static string Describe(object value)
        {
            StringBuilder builder = new StringBuilder();
            Append(value, builder);
            return builder.ToString();
        }

        private static void Append(object value, StringBuilder builder)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            IDictionary<string, object> dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                List<string> keys = new List<string>(dictionary.Keys);
                keys.Sort(StringComparer.Ordinal);
                builder.Append('{');
                foreach (string key in keys)
                {
                    builder.Append(key).Append('=');
                    Append(dictionary[key], builder);
                    builder.Append(';');
                }
                builder.Append('}');
                return;
            }

            IList<object> list = value as IList<object>;
            if (list != null)
            {
                builder.Append('[');
                foreach (object item in list)
                {
                    Append(item, builder);
                    builder.Append(',');
                }
                builder.Append(']');
                return;
            }

            byte[] bytes = value as byte[];
            if (bytes != null)
            {
                builder.Append("bytes:").Append(bytes.Length);
                return;
            }

            if (value is DateTime)
            {
                builder.Append(((DateTime)value).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                return;
            }

            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            builder.Append('<').Append(value.GetType().Name).Append('>');
        }
    }
}
