using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The Settings window offers the rename-pattern variables from RenameVariableCatalog.
    /// These tests pin the catalog against CommandRename.ReplaceVariables: the command
    /// compares a typed token with the var* constants of Properties.NonLocalizeResources, so
    /// the catalog must list exactly those tokens. A variable added to the command without a
    /// catalog entry (or the other way round) fails here instead of silently disappearing
    /// from the window.
    /// </summary>
    [TestFixture]
    public sealed class RenameVariableCatalogTests
    {
        [Test]
        public void TheCatalogListsExactlyTheTokensReplaceVariablesCompares()
        {
            IReadOnlyDictionary<string, string> constants = ProductApi.NonLocalizeVariableConstants();
            // varAppid names the store links (CommandActions), ReplaceVariables never
            // compares against it, so %AppID% would be a token that expands to nothing
            Assert.That(constants.ContainsKey("varAppid"), Is.True, "varAppid moved; update ProductApi.NonLocalizeVariableConstants");
            // varAttribute is a regex matched against the token, not a literal token
            Assert.That(constants.ContainsKey("varAttribute"), Is.True, "varAttribute moved; update the catalog cross-check");
            Regex attributePattern = new Regex(constants["varAttribute"]);

            List<string> expected = new List<string>();
            foreach (KeyValuePair<string, string> constant in constants)
            {
                if (constant.Key == "varAppid" || constant.Key == "varAttribute")
                {
                    continue;
                }
                expected.Add("%" + constant.Value + "%");
            }

            List<string> actual = ProductApi.RenameVariableTokens().ToList();
            List<string> literals = actual.Where(token => !attributePattern.IsMatch(token.Trim('%'))).ToList();
            CollectionAssert.AreEquivalent(expected, literals, "the catalog and ReplaceVariables disagree on the literal tokens");
            Assert.That(actual, Has.No.Member("%AppID%"), "AppID is not a pattern token: ReplaceVariables never expands it");
            CollectionAssert.AllItemsAreUnique(actual);

            // the one Attribute entry must be a template the command's regex accepts
            List<string> attributeTokens = actual.Where(token => attributePattern.IsMatch(token.Trim('%'))).ToList();
            Assert.That(attributeTokens, Has.Count.EqualTo(1), "the catalog must offer exactly one %Attr(..)% template");
            Assert.That(attributeTokens[0], Is.EqualTo("%Attr(tag,attr)%"));
        }

        [Test]
        public void EveryTokenHasItsOwnOneLineDescription()
        {
            IReadOnlyList<string> tokens = ProductApi.RenameVariableTokens();
            IReadOnlyList<string> descriptions = ProductApi.RenameVariableDescriptions();
            Assert.That(descriptions, Has.Count.EqualTo(tokens.Count));
            for (int i = 0; i < tokens.Count; i++)
            {
                Assert.That(descriptions[i], Is.Not.Null.And.Not.Empty, tokens[i] + " has no description");
                Assert.That(descriptions[i], Does.Not.Contain("\n"), tokens[i] + " description is not one line");
                Assert.That(descriptions[i], Is.Not.EqualTo(tokens[i]));
            }
        }

        [Test]
        public void TheSdkAndPackerVariablesAreOfferedOnBothPages()
        {
            IReadOnlyList<string> tokens = ProductApi.RenameVariableTokens();
            CollectionAssert.Contains(tokens, "%MinSdk%");
            CollectionAssert.Contains(tokens, "%TargetSdk%");
            CollectionAssert.Contains(tokens, "%Packer%");

            IReadOnlyList<string> both = ProductApi.RenameVariableTokensFor("Both");
            CollectionAssert.Contains(both, "%MinSdk%");
            CollectionAssert.Contains(both, "%TargetSdk%");
            CollectionAssert.Contains(both, "%Packer%");
        }

        [Test]
        public void TheSdkAndPackerVariablesAreDescribedInTheUiLanguage()
        {
            Assert.That(Description("%MinSdk%", "en-US"),
                Is.EqualTo("Minimum Android SDK version of the package"));
            Assert.That(Description("%TargetSdk%", "en-US"),
                Is.EqualTo("Target Android SDK version of the package"));
            Assert.That(Description("%Packer%", "en-US"),
                Is.EqualTo("Tool that signed the package, e.g. Walle"));

            Assert.That(Description("%MinSdk%", "zh-CN"),
                Is.EqualTo("包的最低 Android SDK 版本"));
            Assert.That(Description("%TargetSdk%", "zh-CN"),
                Is.EqualTo("包的目标 Android SDK 版本"));
            Assert.That(Description("%Packer%", "zh-CN"),
                Is.EqualTo("对包签名的工具，例如 Walle"));
        }

        /// <summary>
        /// The catalog reads its descriptions from Properties.Resources, so the text follows the
        /// thread UI culture - which is whatever culture the machine happens to run. Each lookup
        /// therefore pins the culture it means and puts the thread back afterwards.
        /// </summary>
        private static string Description(string token, string culture)
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            CultureInfo previousUi = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

                IReadOnlyList<string> tokens = ProductApi.RenameVariableTokens();
                IReadOnlyList<string> descriptions = ProductApi.RenameVariableDescriptions();
                return descriptions[tokens.ToList().IndexOf(token)];
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
                Thread.CurrentThread.CurrentUICulture = previousUi;
            }
        }

        [Test]
        public void BothPatternBoxesOfferTheSameVariablesInTheSameOrder()
        {
            IReadOnlyList<string> all = ProductApi.RenameVariableTokens();
            Assert.That(ProductApi.RenameVariableTokensFor("Rename"), Is.EqualTo(all));
            Assert.That(ProductApi.RenameVariableTokensFor("InfoTip"), Is.EqualTo(all));
        }

        [TestCase("Rename")]
        [TestCase("InfoTip")]
        public void TheCopyableListHasOneTokenPerLine(string page)
        {
            IReadOnlyList<string> tokens = ProductApi.RenameVariableTokensFor(page);
            string text = ProductApi.RenameVariableDescribe(page);
            string[] lines = text.Split('\n');
            Assert.That(lines, Has.Length.EqualTo(tokens.Count));
            for (int i = 0; i < tokens.Count; i++)
            {
                string line = lines[i].TrimEnd('\r');
                Assert.That(line, Does.StartWith(tokens[i] + " - "), "line " + i + " of the copyable list");
            }
            Assert.That(text, Does.Not.EndWith("\n"));
            Assert.That(text, Does.Not.EndWith("\r"));
        }

        [TestCase("abc", 3, 0, "%OS%", "abc%OS%", 7)]
        [TestCase("ab", 1, 0, "%OS%", "a%OS%b", 5)]
        [TestCase("a%Xx%b", 1, 4, "%OS%", "a%OS%b", 5)]
        [TestCase("%Old%", 0, 5, "%Environment%", "%Environment%", 13)]
        [TestCase("", 0, 0, "%Version%", "%Version%", 9)]
        [TestCase("abc", 99, 5, "%OS%", "abc%OS%", 7)]
        public void TypingATokenReplacesTheSelectionOrInsertsAtTheCaret(
            string text, int selectionStart, int selectionLength, string token, string expectedText, int expectedCaret)
        {
            object insertion = ProductApi.RenameVariableInsert(text, selectionStart, selectionLength, token);
            Assert.That(ProductApi.PatternInsertionText(insertion), Is.EqualTo(expectedText));
            Assert.That(ProductApi.PatternInsertionCaret(insertion), Is.EqualTo(expectedCaret));
        }

        [Test]
        public void ANullTokenAndNegativeOffsetsAreTolerated()
        {
            object insertion = ProductApi.RenameVariableInsert(null, -5, -1, null);
            Assert.That(ProductApi.PatternInsertionText(insertion), Is.EqualTo(""));
            Assert.That(ProductApi.PatternInsertionCaret(insertion), Is.EqualTo(0));
        }
    }
}
