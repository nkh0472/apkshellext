using System;
using System.Collections.Generic;

namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// One node of the context menu: a leaf command, a submenu parent or a separator.
    /// The node is immutable once the catalog has built it; the UI layers only read it.
    /// </summary>
    public sealed class CommandItem {
        private static readonly CommandItem[] NoChildren = new CommandItem[0];

        internal CommandItem(string text) {
            Text = text ?? string.Empty;
            Verb = string.Empty;
            HelpText = string.Empty;
            Action = CommandAction.None;
            Icon = CommandIcon.None;
            Enabled = true;
            Children = NoChildren;
        }

        /// <summary>Offset relative to <c>idCmdFirst</c>; assigned by the pre-order walk.</summary>
        public int Index { get; internal set; }

        /// <summary>The text shown to the user (already localized and formatted).</summary>
        public string Text { get; internal set; }

        /// <summary>Canonical verb; empty for separators and submenu parents.</summary>
        public string Verb { get; internal set; }

        /// <summary>Help text the shell shows in the status bar; empty when there is none.</summary>
        public string HelpText { get; internal set; }

        public CommandAction Action { get; internal set; }

        public CommandIcon Icon { get; internal set; }

        public bool Enabled { get; internal set; }

        public bool IsSeparator { get; internal set; }

        public IReadOnlyList<CommandItem> Children { get; internal set; }

        public bool HasChildren {
            get { return Children.Count > 0; }
        }

        /// <summary>True when the node can be invoked by the user (a leaf command).</summary>
        public bool IsCommand {
            get { return !IsSeparator && Action != CommandAction.None; }
        }

        public override string ToString() {
            return IsSeparator ? "<separator>" : string.Format("{0} (#{1})", Text, Index);
        }
    }
}
