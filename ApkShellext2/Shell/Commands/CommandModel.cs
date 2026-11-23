using System;
using System.Collections.Generic;
using System.Linq;

namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// The context menu as data for one selection: the root item, its descendants and the
    /// pre-order walk that turns the tree into command ids. The model is UI-free and
    /// side-effect free apart from the action it is asked to invoke, so both the WinForms
    /// adapter and the native <c>IContextMenu</c> handler render exactly the same menu.
    /// </summary>
    public sealed class CommandModel {
        internal CommandModel(CommandItem root, IReadOnlyList<string> selectedPaths) {
            Root = root;
            SelectedPaths = selectedPaths ?? new string[0];
            var items = new List<CommandItem>();
            Flatten(root, items);
            Items = items;
        }

        /// <summary>The single top-level item that carries the menu body.</summary>
        public CommandItem Root { get; private set; }

        public IReadOnlyList<string> SelectedPaths { get; private set; }

        /// <summary>Every node in pre-order, index-aligned with the command ids.</summary>
        public IReadOnlyList<CommandItem> Items { get; private set; }

        /// <summary>Number of items (and therefore command ids) the menu occupies.</summary>
        public int Count {
            get { return Items.Count; }
        }

        public CommandItem FindByIndex(int index) {
            if (index < CommandIds.First || index > CommandIds.Last) return null;
            int offset = index - CommandIds.First;
            return offset < Items.Count ? Items[offset] : null;
        }

        public CommandItem FindByVerb(string verb) {
            if (string.IsNullOrEmpty(verb)) return null;
            return Items.FirstOrDefault(i => string.Equals(i.Verb, verb, StringComparison.Ordinal));
        }

        /// <summary>
        /// Runs the item's action on the model's selection. Unknown items return false; a
        /// separator, a submenu parent or a disabled item is a valid id that does nothing, so
        /// those return true without running an action (the shell treats an id it handed out
        /// as always acceptable).
        /// </summary>
        public bool Invoke(CommandItem item) {
            if (item == null) return false;
            if (item.IsCommand && item.Enabled) {
                CommandActions.Execute(item.Action, SelectedPaths);
            }
            return true;
        }

        private static void Flatten(CommandItem item, List<CommandItem> items) {
            item.Index = items.Count;
            items.Add(item);
            foreach (var child in item.Children) {
                Flatten(child, items);
            }
        }
    }
}
