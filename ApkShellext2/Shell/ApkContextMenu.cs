using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ApkQuickReader;
using ApkShellext2.Diagnostics;
using ApkShellext2.Shell.Commands;
using Microsoft.Win32;
using SharpShell.Attributes;
using SharpShell.Diagnostics;
using SharpShell.Extensions;
using SharpShell.ServerRegistration;
using SharpShell.SharpContextMenu;

namespace ApkShellext2 {
    /// <summary>
    /// The WinForms face of the shell context menu. It renders the model built by
    /// <see cref="CommandCatalog"/> and forwards every click back to the model, so the menu
    /// content and the command ids live in one place.
    ///
    /// The class declares no COM identity any more: the menu handler the shell activates is
    /// <see cref="ApkShellext2.Shell.Native.ContextMenuHandler"/>. This class stays until the
    /// SharpShell dependency is removed.
    /// </summary>
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apk")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ipa")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".appxbundle")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".appx")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apks")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".apkm")]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".xapk")]
    public class ApkContextMenu : SharpContextMenu {
        /// <summary>
        /// Determines whether this instance can a shell context show menu, given the specified selected file list.
        /// </summary>
        /// <returns>
        ///   <c>true</c> if this instance should show a shell context menu for the specified file list; otherwise, <c>false</c>.
        /// </returns>
        protected override bool CanShowMenu() {
            return true;
        }

        /// <summary>
        /// Creates the context menu. This can be a single menu item or a tree of them.
        /// </summary>
        /// <returns>The context menu for the shell context menu.</returns>
        protected override ContextMenuStrip CreateMenu() {
            CommandModel model = CommandCatalog.Build(SelectedItemPaths);

            var menu = new ContextMenuStrip();
            var mainMenu = new ToolStripMenuItem(model.Root.Text);

            // the icon edge length follows the item height, as every image in this menu does
            int size = mainMenu.Height + 1;
            mainMenu.Image = CommandIconResolver.Resolve(model.Root.Icon, size, model.SelectedPaths);
            AddChildren(mainMenu, model.Root, model, size);

            menu.Items.Add(mainMenu);

            Utility.CheckUpdate();
            return menu;
        }

        private static void AddChildren(ToolStripMenuItem parent, CommandItem node, CommandModel model, int size) {
            foreach (var child in node.Children) {
                if (child.IsSeparator) {
                    parent.DropDownItems.Add("-");
                    continue;
                }
                var item = new ToolStripMenuItem(child.Text) {
                    Enabled = child.Enabled
                };
                if (child.Icon != CommandIcon.None) {
                    item.Image = CommandIconResolver.Resolve(child.Icon, size, model.SelectedPaths);
                }
                AddChildren(item, child, model, size);
                var command = child;
                if (command.IsCommand) {
                    item.Click += (sender, args) => model.Invoke(command);
                }
                parent.DropDownItems.Add(item);
            }
        }

        /// <summary>
        /// Expands the <c>%variable%</c> tokens of a name pattern for a parsed package.
        /// </summary>
        public static string ReplaceVariables(string ori, IPackageReader reader) {
            return CommandRename.ReplaceVariables(ori, reader);
        }

        [CustomRegisterFunction]
        public static void postDoRegister(Type type, RegistrationType registrationType) {
            Console.WriteLine("Registering " + type.FullName);

            #region Clean up older versions registry
            try {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"\CLSID\" +
                    type.GUID.ToRegistryString() + @"\InprocServer32")) {
                    if (key != null && key.GetSubKeyNames().Count() != 0) {
                        Console.WriteLine("Found old version in registry, cleaning up ...");
                        foreach (var k in key.GetSubKeyNames()) {
                            if (k != type.Assembly.GetName().Version.ToString()) {
                                Registry.ClassesRoot.DeleteSubKeyTree(@"\CLSID\" +
                        type.GUID.ToRegistryString() + @"\InprocServer32\" + k);
                            }
                        }
                    }
                }
            } catch (Exception e) {
                Logging.Error("Cleaning up older version but see exception. "
                     + e.Message);
            }
            #endregion
        }

        protected override void Log(string message) {
            string s = (SelectedItemPaths.Count() > 0) ? Path.GetFileName(SelectedItemPaths.ElementAt(0)) : "";
            Utility.Log(this, s, message);
        }

        /// <summary>
        /// Routes failures to the default-on error log. The SharpShell base classes already
        /// catch the exceptions around their COM entry points, but they report them through
        /// LogError, which only wrote to a Trace listener that no release build has. This
        /// override is what makes those existing catch blocks visible in the field log.
        /// </summary>
        protected override void LogError(string message, Exception exception) {
            ErrorLog.Error(this.GetType().Name, message, exception);
        }
    }
}
