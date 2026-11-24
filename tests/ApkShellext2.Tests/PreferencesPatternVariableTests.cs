using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Drives the real Settings window on an STA thread. The pattern-variable panels are
    /// built in code instead of by the designer (their content is product data), so only
    /// opening the window proves that both pages carry them, that they are not clipped, and
    /// that clicking a token inserts it into the pattern box. The layout numbers are written
    /// to preferences-layout.txt in the test working directory for the construction log.
    /// </summary>
    [TestFixture]
    public sealed class PreferencesPatternVariableTests
    {
        private const BindingFlags InstanceAnyVisibility = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void BothPatternPagesListTheTokensAndInsertThem()
        {
            string report = RunOnSta(InspectPreferences);
            File.WriteAllText(
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "preferences-layout.txt"),
                report);
            TestContext.Out.Write(report);
        }

        private static string InspectPreferences()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("Preferences pattern-variable smoke (STA thread)");
            using (Preferences form = new Preferences())
            {
                form.CreateControl();
                // Load is not run (it polls the network and localizes the whole form), so reset the
                // pages the way Load does before a page is shown: all pages hidden, the right pane on.
                MethodInfo disablePenels = typeof(Preferences).GetMethod("DisablePenels", InstanceAnyVisibility);
                Assert.That(disablePenels, Is.Not.Null, "Preferences.DisablePenels was renamed");
                disablePenels.Invoke(form, null);
                RequireControl<Panel>(form, "pnlRight", "Preferences").Visible = true;
                form.PerformLayout();

                CheckPage(form, "pnlRenaming", "pnlRenamePatternVariables", "txtRenamePattern", "Rename", report);
                CheckPage(form, "pnlInfoTip", "pnlInfoTipPatternVariables", "txtToolTipPattern", "InfoTip", report);
            }
            return report.ToString();
        }

        private static void CheckPage(Preferences form, string pageName, string panelName,
            string patternBoxName, string pages, StringBuilder report)
        {
            TableLayoutPanel page = RequireControl<TableLayoutPanel>(form, pageName, pageName);
            MethodInfo disablePenels = typeof(Preferences).GetMethod("DisablePenels", InstanceAnyVisibility);
            Assert.That(disablePenels, Is.Not.Null, "Preferences.DisablePenels was renamed");
            disablePenels.Invoke(form, null);
            page.Visible = true;
            page.PerformLayout();
            form.PerformLayout();

            TableLayoutPanel panel = RequireControl<TableLayoutPanel>(form, panelName, pageName);
            Assert.That(panel.Parent, Is.SameAs(page), panelName + " must sit on " + pageName);
            Assert.That(panel.AutoSize, Is.True, panelName + " must grow with its content");

            TextBox patternBox = RequireControl<TextBox>(form, patternBoxName, panelName);
            IReadOnlyList<string> tokens = ProductApi.RenameVariableTokensFor(pages);

            FlowLayoutPanel strip = RequireControl<FlowLayoutPanel>(panel, panelName + "Links", panelName);
            List<LinkLabel> links = Descendants<LinkLabel>(strip).ToList();
            Assert.That(links, Has.Count.EqualTo(tokens.Count), panelName + " must list every catalog token once");
            CollectionAssert.AreEqual(tokens, links.Select(link => link.Text).ToList());

            ToolTip toolTip = (ToolTip)typeof(Preferences)
                .GetField("toolTip1", InstanceAnyVisibility).GetValue(form);
            IReadOnlyList<string> descriptions = ProductApi.RenameVariableDescriptions();
            for (int i = 0; i < links.Count; i++)
            {
                Assert.That(toolTip.GetToolTip(links[i]), Is.EqualTo(descriptions[i]), links[i].Text + " tooltip");
            }

            TextBox list = RequireControl<TextBox>(panel, panelName + "List", panelName);
            Assert.That(list.ReadOnly, Is.True, panelName + " list must be read-only");
            Assert.That(list.Multiline, Is.True);
            Assert.That(list.Text, Is.EqualTo(ProductApi.RenameVariableDescribe(pages)),
                panelName + " list must show the whole catalog");
            foreach (string token in tokens)
            {
                Assert.That(list.Text, Does.Contain(token), panelName + " list is missing " + token);
            }

            LinkLabel moreInfo = RequireControl<LinkLabel>(panel, panelName + "MoreInfo", panelName);
            Assert.That(moreInfo.Text, Is.Not.Empty, panelName + " more-info link must be labeled");

            report.AppendLine(pageName + ": page=" + Bound(page) + " preferred=" + page.PreferredSize +
                " form=" + Bound(form) + " formPreferred=" + form.PreferredSize);
            report.AppendLine("  " + panelName + ": " + Bound(panel) + " preferred=" + panel.PreferredSize);
            report.AppendLine("  strip: " + Bound(strip) + " preferred=" + strip.PreferredSize);
            report.AppendLine("  list: " + Bound(list) + " preferred=" + list.PreferredSize);
            report.AppendLine("  moreInfo: " + Bound(moreInfo));

            // the three rows must not overlap each other
            Assert.That(strip.Bottom, Is.LessThanOrEqualTo(list.Top),
                panelName + " strip overlaps the list (" + Bound(strip) + " / " + Bound(list) + ")");
            Assert.That(list.Bottom, Is.LessThanOrEqualTo(moreInfo.Top),
                panelName + " list overlaps the more-info link (" + Bound(list) + " / " + Bound(moreInfo) + ")");

            // no clipped content: the auto-sized page has to contain the whole panel, and the
            // grown form has to contain the page
            Assert.That(panel.Bottom, Is.LessThanOrEqualTo(page.ClientSize.Height),
                panelName + " is clipped by " + pageName + " (" + Bound(panel) + " in " + Bound(page) + ")");
            Assert.That(strip.Width, Is.LessThanOrEqualTo(page.ClientSize.Width), panelName + " strip is wider than the page");
            Assert.That(list.Width, Is.LessThanOrEqualTo(page.ClientSize.Width), panelName + " list is wider than the page");
            Assert.That(page.Width, Is.LessThanOrEqualTo(form.ClientSize.Width), pageName + " is wider than the form");
            Assert.That(patternBox.Width, Is.GreaterThan(0));

            // clicking a token inserts it at the caret and leaves the caret behind it
            string token1 = tokens[1];
            patternBox.Text = "";
            ClickLink(links[1]);
            Assert.That(patternBox.Text, Is.EqualTo(token1), panelName + " click did not insert " + token1);
            Assert.That(patternBox.SelectionStart, Is.EqualTo(token1.Length));
            Assert.That(patternBox.SelectionLength, Is.EqualTo(0));
            report.AppendLine("  after clicking " + token1 + ": text=" + patternBox.Text +
                " caret=" + patternBox.SelectionStart);
        }

        private static void ClickLink(LinkLabel link)
        {
            MethodInfo onLinkClicked = typeof(LinkLabel).GetMethod("OnLinkClicked", InstanceAnyVisibility);
            Assert.That(onLinkClicked, Is.Not.Null, "LinkLabel.OnLinkClicked was renamed");
            onLinkClicked.Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) });
        }

        private static string Bound(Control control)
        {
            return "(" + control.Left + "," + control.Top + "," + control.Width + "x" + control.Height + ")";
        }

        private static T RequireControl<T>(Control parent, string name, string describedBy) where T : Control
        {
            Control[] found = parent.Controls.Find(name, searchAllChildren: true);
            T control = found.Length == 0 ? null : found[0] as T;
            Assert.That(control, Is.Not.Null, "control " + name + " (" + typeof(T).Name + ") of " + describedBy +
                " was not found; the Settings layout was changed and this smoke test must follow it");
            return control;
        }

        private static IEnumerable<T> Descendants<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                T match = child as T;
                if (match != null)
                {
                    yield return match;
                }
                foreach (T nested in Descendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static T RunOnSta<T>(Func<T> action)
        {
            T result = default(T);
            Exception failure = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            Assert.That(thread.Join(TimeSpan.FromSeconds(60)), Is.True, "the STA thread did not finish");
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return result;
        }
    }
}
