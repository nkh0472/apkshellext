using System;
using System.Collections.Generic;
using System.Text;

namespace ApkShellext2 {
    /// <summary>
    /// The rename-pattern boxes that accept <c>%variable%</c> tokens. Both boxes are
    /// expanded by <c>CommandRename.ReplaceVariables</c>, so today every variable belongs
    /// to both; the flag exists so a box that ever stops honouring a token can say so
    /// instead of silently offering it.
    /// </summary>
    [Flags]
    internal enum PatternVariablePages {
        None = 0,
        Rename = 1,
        InfoTip = 2,
        Both = Rename | InfoTip,
    }

    /// <summary>
    /// One <c>%variable%</c> a pattern box accepts: the token as the user types it, the
    /// one-line explanation shown next to it, and the boxes that expand it.
    /// </summary>
    internal sealed class PatternVariable {
        internal PatternVariable(string name, string token, string description, PatternVariablePages pages) {
            Name = name;
            Token = token;
            Description = description;
            Pages = pages;
        }

        /// <summary>Stable identifier, e.g. <c>AppName</c> (the token without the percent signs).</summary>
        internal string Name { get; private set; }

        /// <summary>The text the user types, e.g. <c>%AppName%</c>.</summary>
        internal string Token { get; private set; }

        /// <summary>One line for the tooltip and the copyable list, already localised.</summary>
        internal string Description { get; private set; }

        internal PatternVariablePages Pages { get; private set; }

        internal bool AppliesTo(PatternVariablePages page) {
            return (Pages & page) == page;
        }
    }

    /// <summary>
    /// A pattern box's text after a variable was typed into it, plus where the caret ends
    /// up (right behind the inserted token, which is where the user continues typing).
    /// </summary>
    internal readonly struct PatternInsertion {
        internal PatternInsertion(string text, int caretPosition) {
            Text = text;
            CaretPosition = caretPosition;
        }

        internal string Text { get; }

        internal int CaretPosition { get; }
    }

    /// <summary>
    /// The single source of the <c>%variable%</c> list shown under the rename-pattern and
    /// info-tip-pattern boxes. The Settings window builds both panels from it, and
    /// <c>Preferences</c> no longer needs the reader layer: the catalog only describes
    /// what can be typed, never what a package contains.
    ///
    /// The token strings are spelled out here instead of being read from
    /// <c>Properties.NonLocalizeResources</c> on purpose - those constants are the names
    /// <c>CommandRename.ReplaceVariables</c> compares against, so deriving the catalog from
    /// them would hide a changed constant instead of failing a test. RenameVariableCatalogTests
    /// cross-checks the two sets, so a new variable in ReplaceVariables without a catalog entry
    /// (or the other way round) fails the suite.
    /// </summary>
    internal static class RenameVariableCatalog {
        // Built per call, not cached: the descriptions come from the satellite resources,
        // which change when the user switches the UI language (the Settings window rebuilds
        // its panels after that, and they must get the new strings).
        private static PatternVariable[] BuildVariables() {
            return new PatternVariable[] {
                new PatternVariable("AppName", "%AppName%", Properties.Resources.strVarAppNameDescription, PatternVariablePages.Both),
                new PatternVariable("Package", "%Package%", Properties.Resources.strVarPackageDescription, PatternVariablePages.Both),
                new PatternVariable("Publisher", "%Publisher%", Properties.Resources.strVarPublisherDescription, PatternVariablePages.Both),
                new PatternVariable("Version", "%Version%", Properties.Resources.strVarVersionDescription, PatternVariablePages.Both),
                new PatternVariable("Revision", "%Revision%", Properties.Resources.strVarRevisionDescription, PatternVariablePages.Both),
                new PatternVariable("FileSize", "%FileSize%", Properties.Resources.strVarFileSizeDescription, PatternVariablePages.Both),
                new PatternVariable("OS", "%OS%", Properties.Resources.strVarOSDescription, PatternVariablePages.Both),
                new PatternVariable("LastModify", "%LastModify%", Properties.Resources.strVarLastModifyDescription, PatternVariablePages.Both),
                new PatternVariable("Debuggable", "%Debuggable%", Properties.Resources.strVarDebuggableDescription, PatternVariablePages.Both),
                new PatternVariable("Attribute", "%Attr(tag,attr)%", Properties.Resources.strVarAttributeDescription, PatternVariablePages.Both),
            };
        }

        /// <summary>Every variable, in the order the Settings window lists it.</summary>
        internal static IReadOnlyList<PatternVariable> Variables {
            get { return BuildVariables(); }
        }

        /// <summary>The variables a pattern box expands, in listing order.</summary>
        internal static IReadOnlyList<PatternVariable> For(PatternVariablePages page) {
            List<PatternVariable> matching = new List<PatternVariable>();
            foreach (PatternVariable variable in Variables) {
                if (variable.AppliesTo(page))
                    matching.Add(variable);
            }
            return matching;
        }

        /// <summary>
        /// The whole list as one copyable text block: one <c>%token% - description</c> line
        /// per variable a pattern box accepts.
        /// </summary>
        internal static string Describe(PatternVariablePages page) {
            StringBuilder text = new StringBuilder();
            foreach (PatternVariable variable in For(page)) {
                text.Append(variable.Token).Append(" - ").AppendLine(variable.Description);
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Types <paramref name="token"/> into a pattern box: it replaces the selected text
        /// when there is a selection and is inserted at the caret otherwise. Out-of-range
        /// offsets are clamped instead of throwing, because the selection of a box can lag
        /// behind a text change. Pure: no control is touched, so it is unit tested.
        /// </summary>
        internal static PatternInsertion Insert(string text, int selectionStart, int selectionLength, string token) {
            string source = text ?? "";
            int start = selectionStart;
            if (start < 0)
                start = 0;
            if (start > source.Length)
                start = source.Length;
            int length = selectionLength;
            if (length < 0)
                length = 0;
            if (start + length > source.Length)
                length = source.Length - start;
            string insert = token ?? "";
            string result = source.Substring(0, start) + insert + source.Substring(start + length);
            return new PatternInsertion(result, start + insert.Length);
        }
    }
}
