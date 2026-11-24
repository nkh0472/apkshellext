using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Shell.Commands;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The cursor the shell walks over a command's subcommands.
    /// </summary>
    /// <remarks>
    /// Each <see cref="Next"/> call builds fresh
    /// <see cref="ExplorerCommandItemInstance"/> objects and hands out COM pointers to them via a
    /// callable wrapper, which starts the reference count at one: the shell owns those pointers
    /// and releases them, so they are never released here. The enumerator itself is owned by the
    /// shell the same way - whoever calls <c>EnumSubCommands</c> releases the pointer it got.
    /// </remarks>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ExplorerCommandEnumerator : IEnumExplorerCommandNative {
        private const string ModuleName = "ExplorerCommandEnumerator";

        private readonly CommandModel model;
        private readonly CommandItem[] items;
        private int position;

        internal ExplorerCommandEnumerator(CommandModel model, IEnumerable<CommandItem> items) {
            if (model == null) throw new ArgumentNullException("model");
            if (items == null) throw new ArgumentNullException("items");
            this.model = model;
            this.items = items as CommandItem[] ?? new List<CommandItem>(items).ToArray();
        }

        /// <summary>Copies the cursor of <paramref name="source"/> at its current position.</summary>
        private ExplorerCommandEnumerator(ExplorerCommandEnumerator source) {
            if (source == null) throw new ArgumentNullException("source");
            model = source.model;
            items = source.items;
            position = source.position;
        }

        internal int Position { get { return position; } }

        public int Next(uint celt, IntPtr pUICommand, out uint pulFetched) {
            pulFetched = 0;
            if (pUICommand == IntPtr.Zero) return HResult.E_POINTER;

            int available = items.Length - position;
            int count = available > 0 ? (int)Math.Min(celt, (uint)available) : 0;
            int written = 0;
            try {
                for (; written < count; written++) {
                    ExplorerCommandItemInstance command = new ExplorerCommandItemInstance(model, items[position + written]);
                    IntPtr current = Marshal.GetComInterfaceForObject(command, typeof(IExplorerCommandNative));
                    Marshal.WriteIntPtr(pUICommand, written * IntPtr.Size, current);
                }
            } catch (Exception ex) {
                // The pointers already handed out belong to the caller now; the ones written in
                // this loop do not, so release those before failing.
                for (int i = 0; i < written; i++) ComObject.Release(Marshal.ReadIntPtr(pUICommand, i * IntPtr.Size));
                ErrorLog.Error(ModuleName, "A subcommand could not be handed to the shell.", ex);
                return HResult.E_FAIL;
            }

            position += count;
            pulFetched = (uint)count;
            return count == 0 ? HResult.S_FALSE : HResult.S_OK;
        }

        public int Skip(uint celt) {
            long target = (long)position + celt;
            position = (int)Math.Min(target, items.Length);
            return HResult.S_OK;
        }

        public int Reset() {
            position = 0;
            return HResult.S_OK;
        }

        public int Clone(out IntPtr ppenum) {
            try {
                ExplorerCommandEnumerator copy = new ExplorerCommandEnumerator(this);
                ppenum = Marshal.GetComInterfaceForObject(copy, typeof(IEnumExplorerCommandNative));
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The enumerator could not be cloned.", ex);
                ppenum = IntPtr.Zero;
                return HResult.E_FAIL;
            }
        }
    }
}
