using System;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The two-letter language/country pair in a resource configuration is arbitrary
    /// bytes: a rebuilt or hand-edited table can carry anything, including the
    /// high-bit junk that is known to surface in the wild ("0xAD", "es-¤$"). Those
    /// must never become a CultureInfo, because the name ends up in Explorer's UI.
    /// The guard is a byte-level one - only ASCII letters can name a culture.
    /// </summary>
    [TestFixture]
    public class LocaleGuardTests
    {
        private static MethodInfo GetCultureMethod()
        {
            Type type = ProductApi.Assembly.GetType("ApkQuickReader.ArscParser", false);
            Assert.That(type, Is.Not.Null, "ApkQuickReader.ArscParser was not found");
            MethodInfo method = type.GetMethod("GetCulture",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, "ArscParser.GetCulture(byte[]) was not found");
            return method;
        }

        private static CultureInfo GetCulture(params byte[] code)
        {
            try
            {
                return (CultureInfo)GetCultureMethod().Invoke(null, new object[] { code });
            }
            catch (TargetInvocationException error)
            {
                Assert.Fail("GetCulture threw instead of answering null: " + error.InnerException);
                return null;
            }
        }

        [Test]
        public void LanguageOnlyBytesAnswerThatCulture()
        {
            Assert.That(GetCulture(0x65, 0x6E, 0x00, 0x00).Name, Is.EqualTo("en"));
            Assert.That(GetCulture(0x64, 0x65, 0x00, 0x00).Name, Is.EqualTo("de"));
        }

        [Test]
        public void LanguageAndCountryBytesAnswerTheRegionalCulture()
        {
            Assert.That(GetCulture(0x7A, 0x68, 0x43, 0x4E).Name, Is.EqualTo("zh-CN"));
            Assert.That(GetCulture(0x70, 0x74, 0x42, 0x52).Name, Is.EqualTo("pt-BR"));
        }

        [Test]
        public void LocaleLessConfigAnswersNull()
        {
            Assert.That(GetCulture(0x00, 0x00, 0x00, 0x00), Is.Null);
        }

        [Test]
        public void HighBitJunkInTheLanguageAnswersNull()
        {
            // The 0xAD byte reported from a repacked table, plus punctuation.
            Assert.That(GetCulture(0xAD, 0xAD, 0x00, 0x00), Is.Null);
            Assert.That(GetCulture(0xAD, 0x24, 0x00, 0x00), Is.Null);
        }

        [Test]
        public void GarbageCountryAnswersNull()
        {
            // "es" plus the same junk in the country field: the language alone would
            // resolve, but the pair the table declared is not a culture.
            Assert.That(GetCulture(0x65, 0x73, 0xA4, 0x24), Is.Null);
            Assert.That(GetCulture(0x65, 0x73, 0x24, 0x24), Is.Null);
        }

        [Test]
        public void ShortCulturesTakeTheLanguageOnlyFallback()
        {
            // A three-letter language ("fil", "und") cannot fit the two-byte field, so
            // the guard rejects it rather than inventing a code.
            Assert.That(GetCulture(0x66, 0x69, 0x00, 0x00).Name, Is.EqualTo("fi"));
        }

        [Test]
        public void EveryAcceptedCultureIsAsciiAndResolvable()
        {
            for (int a = 0; a < 256; a += 17)
            {
                CultureInfo culture = GetCulture((byte)a, (byte)(a ^ 0x5A), 0x00, 0x00);
                if (culture == null) continue;
                foreach (char c in culture.Name)
                    Assert.That(c < 128, Is.True, "culture name contains a non-ASCII character: " + culture.Name);
                Assert.DoesNotThrow(() => CultureInfo.GetCultureInfo(culture.Name));
            }
        }
    }
}
