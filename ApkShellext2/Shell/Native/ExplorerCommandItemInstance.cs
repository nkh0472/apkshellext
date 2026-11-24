using System;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Shell.Commands;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// One subcommand of the modern context menu.
    /// </summary>
    /// <remarks>
    /// The shell asks each item for its own title, state and flags, so this class is only a
    /// projection of a <see cref="CommandItem"/>: the text, the order, the enabled state and the
    /// action all come from <see cref="CommandCatalog"/>, which is the same model the classic
    /// HMENU handler and the SharpShell adapter render. Nothing about the menu is decided here.
    /// </remarks>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ExplorerCommandItemInstance : IExplorerCommandNative {
        private const string ModuleName = "ExplorerCommandItem";

        private readonly CommandModel model;
        private readonly CommandItem item;

        internal ExplorerCommandItemInstance(CommandModel model, CommandItem item) {
            if (model == null) throw new ArgumentNullException("model");
            if (item == null) throw new ArgumentNullException("item");
            this.model = model;
            this.item = item;
        }

        internal CommandItem Item { get { return item; } }

        public int GetTitle(IntPtr psiItemArray, out IntPtr ppszName) {
            return ExplorerCommandText.Succeed(item.Text, out ppszName);
        }

        public int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon) {
            return ExplorerCommandText.Declined(out ppszIcon);
        }

        public int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip) {
            return ExplorerCommandText.Declined(out ppszInfotip);
        }

        public int GetCanonicalName(out Guid pguidCommandName) {
            pguidCommandName = Guid.Empty;
            return HResult.S_OK;
        }

        public int GetState(IntPtr psiItemArray, bool fOkToBeSlow, out uint pCmdState) {
            pCmdState = (uint)(item.Enabled ? EXPCMDSTATE.Enabled : EXPCMDSTATE.Disabled);
            return HResult.S_OK;
        }

        /// <summary>
        /// Runs the action through <see cref="CommandModel.Invoke"/>, i.e. through
        /// <see cref="CommandActions"/> - the same path a click on the classic menu takes, so the
        /// two menus cannot drift apart.
        /// </summary>
        /// <remarks>
        /// The selection the model was built from is the one acted on; the array handed back by
        /// the shell is ignored on purpose so the command can only do what the menu it was built
        /// for promised. No exception may cross the COM boundary: a throw here becomes an
        /// explorer.exe crash, not an error dialog.
        /// </remarks>
        public int Invoke(IntPtr psiItemArray, IntPtr pbc) {
            try {
                return model.Invoke(item) ? HResult.S_OK : HResult.E_FAIL;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The command \"" + item.Text + "\" could not be executed.", ex);
                return HResult.E_FAIL;
            }
        }

        public int GetFlags(out uint pFlags) {
            EXPCMDFLAGS flags;
            if (item.IsSeparator) flags = EXPCMDFLAGS.IsSeparator;
            else if (item.HasChildren) flags = EXPCMDFLAGS.HasSubCommands;
            else flags = EXPCMDFLAGS.Default;
            pFlags = (uint)flags;
            return HResult.S_OK;
        }

        public int EnumSubCommands(out IntPtr ppEnum) {
            if (!item.HasChildren) {
                ppEnum = IntPtr.Zero;
                return HResult.E_NOTIMPL;
            }
            try {
                ExplorerCommandEnumerator enumerator = new ExplorerCommandEnumerator(model, item.Children);
                ppEnum = Marshal.GetComInterfaceForObject(enumerator, typeof(IEnumExplorerCommandNative));
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The subcommands of \"" + item.Text + "\" could not be enumerated.", ex);
                ppEnum = IntPtr.Zero;
                return HResult.E_FAIL;
            }
        }
    }
}
