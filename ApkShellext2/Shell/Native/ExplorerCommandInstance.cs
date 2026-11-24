using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Registration;
using ApkShellext2.Shell.Commands;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The modern context menu verb: the class the shell activates through
    /// <c>shell\&lt;verb&gt;\ExplorerCommandHandler</c>.
    /// </summary>
    /// <remarks>
    /// This is the root command only. It carries the label and the visibility of the whole menu
    /// and returns the individual commands from <see cref="EnumSubCommands"/>, which is how the
    /// Windows 11 menu shows one top level entry with a list of actions underneath.
    ///
    /// The menu itself is still <see cref="CommandCatalog"/>: the titles, their order, the enabled
    /// state and the action of every entry come from there, exactly as in the classic handler.
    ///
    /// No [ComRegisterFunction] here on purpose. The registrar writes the command CLSID only when
    /// the OS build supports modern verbs, while regasm would register it on every machine - so
    /// this class stays a passive server and <see cref="Registration.ShellRegistrar"/> decides.
    /// </remarks>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    //  Same value as ShellGuids.ExplorerCommandClsid, written without braces because the
    //  attribute requires the plain form; a test pins the two together.
    [Guid("0B6C1F2A-7D34-4E5B-9A18-3C2D4E5F6071")]
    public sealed class ExplorerCommandInstance : IExplorerCommandNative {
        private const string ModuleName = "ExplorerCommand";

        //  The shell only hands the selection to GetTitle/GetState/Invoke; EnumSubCommands and
        //  GetFlags take no array. The selection described by the last call is therefore kept so
        //  that the enumerator the shell asks for next describes the same files.
        private IReadOnlyList<string> selection;
        private CommandModel menu;

        /// <summary>The instance the shell creates through the command CLSID.</summary>
        public ExplorerCommandInstance() {
        }

        /// <summary>
        /// A command that already knows its selection, so the menu can be examined without a shell
        /// item array. COM activation keeps using the parameterless constructor above.
        /// </summary>
        internal ExplorerCommandInstance(IReadOnlyList<string> selectedPaths) {
            if (selectedPaths == null) throw new ArgumentNullException("selectedPaths");
            selection = selectedPaths;
        }

        public int GetTitle(IntPtr psiItemArray, out IntPtr ppszName) {
            try {
                Remember(psiItemArray);
                return ExplorerCommandText.Succeed(CommandCatalog.RootTitle(), out ppszName);
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The command title could not be built.", ex);
                ppszName = IntPtr.Zero;
                return HResult.E_FAIL;
            }
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

        /// <summary>
        /// Shows the verb for a selection that contains at least one package the extension
        /// understands and hides it otherwise, so the menu does not appear on unrelated files.
        /// </summary>
        public int GetState(IntPtr psiItemArray, bool fOkToBeSlow, out uint pCmdState) {
            pCmdState = (uint)EXPCMDSTATE.Hidden;
            try {
                IReadOnlyList<string> paths = Remember(psiItemArray);
                if (CommandCatalog.IsSupportedSelection(paths)) pCmdState = (uint)EXPCMDSTATE.Enabled;
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The command state could not be determined.", ex);
                return HResult.E_FAIL;
            }
        }

        /// <summary>
        /// The verb has no action of its own: everything it offers is a subcommand, and the shell
        /// runs those through <see cref="ExplorerCommandItemInstance.Invoke"/>.
        /// </summary>
        public int Invoke(IntPtr psiItemArray, IntPtr pbc) {
            return HResult.S_OK;
        }

        public int GetFlags(out uint pFlags) {
            pFlags = (uint)EXPCMDFLAGS.HasSubCommands;
            return HResult.S_OK;
        }

        public int EnumSubCommands(out IntPtr ppEnum) {
            ppEnum = IntPtr.Zero;
            try {
                CommandModel current = Menu();
                ExplorerCommandEnumerator enumerator = new ExplorerCommandEnumerator(current, current.Root.Children);
                ppEnum = Marshal.GetComInterfaceForObject(enumerator, typeof(IEnumExplorerCommandNative));
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The subcommands could not be enumerated.", ex);
                return HResult.E_FAIL;
            }
        }

        /// <summary>
        /// Reads the selection of this call and forgets the menu built for the previous one: one
        /// instance describes one context menu, and a rebuilt menu is the only way the shell can
        /// see a changed folder or a changed set of files.
        /// </summary>
        /// <remarks>
        /// A missing array keeps the selection already known: the shell always passes one for a
        /// real selection, and the calls that pass none (a title asked for without items) should
        /// not make the menu describe nothing.
        /// </remarks>
        private IReadOnlyList<string> Remember(IntPtr psiItemArray) {
            if (psiItemArray != IntPtr.Zero) selection = ShellItemSelection.Read(psiItemArray);
            menu = null;
            return selection ?? (selection = new string[0]);
        }

        private CommandModel Menu() {
            IReadOnlyList<string> paths = selection ?? (selection = new string[0]);
            return menu ?? (menu = CommandCatalog.Build(paths));
        }
    }
}
