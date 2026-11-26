using System;
using System.Reflection;
using Microsoft.Win32;
using NUnit.Framework;
using ApkShellext2;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Every feature switch goes through <see cref="Utility.GetSettingBool"/>. These cases pin the
    /// forms a value can arrive in: the text an installer writes, the DWORD a hand edit or an
    /// older build leaves behind, and the values this code does not recognise.
    /// </summary>
    [TestFixture]
    public class SettingsBoolTests
    {
        private const string SettingsKey = @"Software\ApkShellext2";
        private const string ValueName = "SupportAdaptiveIcon";

        private static readonly MethodInfo Parse = typeof(Utility).GetMethod(
            "ParseBoolean", BindingFlags.NonPublic | BindingFlags.Static);

        private static bool ParseBoolean(string text, bool fallback)
        {
            Assert.That(Parse, Is.Not.Null, "Utility.ParseBoolean is the single place that reads the text form");
            return (bool)Parse.Invoke(null, new object[] { text, fallback });
        }

        [TestCase("True", true)]
        [TestCase("true", true)]
        [TestCase("TRUE", true)]
        [TestCase(" tRuE ", true)]
        [TestCase("1", true)]
        [TestCase("yes", true)]
        [TestCase("YES", true)]
        [TestCase("on", true)]
        [TestCase("On", true)]
        [TestCase("False", false)]
        [TestCase("false", false)]
        [TestCase("0", false)]
        [TestCase("no", false)]
        [TestCase("off", false)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        public void TextFormsReadAsTheirWriterMeantThem(string text, bool expected)
        {
            Assert.That(ParseBoolean(text, !expected), Is.EqualTo(expected));
        }

        [Test]
        public void AnUnrecognisedTextKeepsTheFallback()
        {
            Assert.That(ParseBoolean("maybe", true), Is.True);
            Assert.That(ParseBoolean("maybe", false), Is.False);
        }

        [Test]
        public void TheDwordFormSwitchesTheFeatureTheSameWay()
        {
            using (RegistryKey settings = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                Assert.That(settings, Is.Not.Null, "the settings key must be writable for this case");
                object original = settings.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                try
                {
                    settings.SetValue(ValueName, 1, RegistryValueKind.DWord);
                    Assert.That(Utility.GetSettingBool(ValueName, false), Is.True,
                        "a DWORD 1 means on, not an unrecognised value that falls back to off");

                    settings.SetValue(ValueName, 0, RegistryValueKind.DWord);
                    Assert.That(Utility.GetSettingBool(ValueName, true), Is.False,
                        "a DWORD 0 means off even when the fallback is on");

                    settings.SetValue(ValueName, "True", RegistryValueKind.String);
                    Assert.That(Utility.GetSettingBool(ValueName, false), Is.True);
                }
                finally
                {
                    if (original == null)
                        settings.DeleteValue(ValueName, false);
                    else
                        settings.SetValue(ValueName, original, RegistryValueKind.String);
                }
            }
        }

        [Test]
        public void AMissingValueUsesTheFallback()
        {
            using (RegistryKey settings = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                Assert.That(settings, Is.Not.Null);
                object original = settings.GetValue(ValueName, null);
                try
                {
                    settings.DeleteValue(ValueName, false);
                    Assert.That(Utility.GetSettingBool(ValueName, true), Is.True);
                    Assert.That(Utility.GetSettingBool(ValueName, false), Is.False);
                }
                finally
                {
                    if (original != null)
                        settings.SetValue(ValueName, original, RegistryValueKind.String);
                }
            }
        }
    }
}
