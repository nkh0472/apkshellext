using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Registration;
using ApkShellext2.Shell.Commands;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The native context menu handler. It answers <c>IShellExtInit</c> and the three
    /// <c>IContextMenu</c> versions the shell may ask for, and it builds the menu entirely out of
    /// window menu handles - no WinForms object is created on this path.
    ///
    /// The items come from <see cref="CommandCatalog"/>, the same model the WinForms handler walks,
    /// so both renderings show the same texts, the same order, the same enable states and the same
    /// pre-order command ids. Command ids are <c>idCmdFirst + item.Index</c>; separators consume an
    /// index like any other item but are inserted without an id.
    ///
    /// Every entry point catches its own exceptions: an escaping exception would unwind through the
    /// shell's COM call and tear the Explorer thread down, so each one logs with
    /// <see cref="ErrorLog"/> and returns a failure the shell understands instead. Each entry also
    /// releases the submenus of the previous call, because the shell may ask for a new menu for a
    /// different selection while it still holds the object.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [Guid("A8D3F2E1-6C4B-4D9A-B7F5-2E1D0C9B8A76")]
    public sealed class ContextMenuHandler : IShellExtInitNative, IContextMenuNative, IContextMenu2Native, IContextMenu3Native {
        private const string ModuleName = "ContextMenuHandler";
        private static readonly int InvokeInfoSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFO));
        private static readonly int InvokeInfoExSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX));

        private readonly List<IntPtr> subMenus = new List<IntPtr>();
        private string[] selectedPaths = new string[0];
        private CommandModel model;
        private int idCmdFirst = CommandIds.First;

        //  ------------------------------------------------------------------------------------
        //  IShellExtInit
        //  ------------------------------------------------------------------------------------

        public int Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgID) {
            try {
                ReleaseMenus();
                model = null;
                selectedPaths = ShellDataObject.ReadFilePaths(pDataObj);
                return HResult.S_OK;
            } catch (Exception ex) {
                selectedPaths = new string[0];
                model = null;
                ErrorLog.Error(ModuleName, "An exception occured reading the selected items.", ex);
                return HResult.E_FAIL;
            }
        }

        //  ------------------------------------------------------------------------------------
        //  IContextMenu
        //  ------------------------------------------------------------------------------------

        public int QueryContextMenu(IntPtr hMenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags) {
            try {
                ReleaseMenus();
                model = null;

                if (selectedPaths.Length == 0) {
                    ErrorLog.Warn(ModuleName, "Query Context Menu skipped: no selected items.");
                    return HResult.E_FAIL;
                }
                if (((CMF)uFlags & CMF.DefaultOnly) == CMF.DefaultOnly) {
                    return HResult.S_OK;
                }
                if (hMenu == IntPtr.Zero) {
                    ErrorLog.Warn(ModuleName, "Query Context Menu skipped: no menu handle.");
                    return HResult.E_FAIL;
                }

                CommandModel built = CommandCatalog.Build(selectedPaths);
                int size = NativeMenuIcons.ImageSize;

                IntPtr rootMenu = NativeMethods.CreatePopupMenu();
                if (rootMenu == IntPtr.Zero) {
                    ErrorLog.Warn(ModuleName, "CreatePopupMenu failed for the menu body.");
                    return HResult.E_FAIL;
                }
                subMenus.Add(rootMenu);
                InsertChildren(rootMenu, built.Root, size, idCmdFirst);

                if (!InsertItem(hMenu, indexMenu, built.Root, idCmdFirst, size, rootMenu)) {
                    ErrorLog.Warn(ModuleName, "InsertMenuItem failed for the menu body.");
                    return HResult.E_FAIL;
                }

                this.idCmdFirst = idCmdFirst;
                model = built;
                Utility.CheckUpdate();
                return HResult.MakeItemCount(built.Count);
            } catch (Exception ex) {
                model = null;
                ErrorLog.Error(ModuleName, "An exception occured building the context menu.", ex);
                return HResult.E_FAIL;
            }
        }

        public int InvokeCommand(IntPtr pici) {
            try {
                if (pici == IntPtr.Zero) return HResult.E_INVALIDARG;

                var info = (CMINVOKECOMMANDINFO)Marshal.PtrToStructure(pici, typeof(CMINVOKECOMMANDINFO));
                bool extended = (info.fMask & CMIC.UNICODE) != 0 && info.cbSize >= (uint)InvokeInfoExSize && info.cbSize >= (uint)InvokeInfoSize;
                var extendedInfo = new CMINVOKECOMMANDINFOEX();
                if (extended) {
                    extendedInfo = (CMINVOKECOMMANDINFOEX)Marshal.PtrToStructure(pici, typeof(CMINVOKECOMMANDINFOEX));
                }

                CommandModel current = model;
                if (current == null) {
                    ErrorLog.Warn(ModuleName, "Invoke skipped: no menu was built for this selection.");
                    return HResult.E_FAIL;
                }

                IntPtr verbPointer = extended ? extendedInfo.lpVerbW : info.lpVerb;
                if (verbPointer == IntPtr.Zero) {
                    ErrorLog.Warn(ModuleName, "Invoke skipped: no verb and no command id.");
                    return HResult.E_FAIL;
                }

                long raw = verbPointer.ToInt64();
                CommandItem item;
                if ((raw >> 16) == 0) {
                    //  An integer verb is the command's offset from idCmdFirst, not the item id
                    //  the menu stored. Callers that do hand over the absolute id are accepted too.
                    int offset = (int)(raw & 0xFFFF);
                    item = current.FindByIndex(offset) ?? current.FindByIndex(offset - idCmdFirst);
                } else {
                    string verb = extended ? Marshal.PtrToStringUni(verbPointer) : Marshal.PtrToStringAnsi(verbPointer);
                    item = current.FindByVerb(verb);
                }
                if (item == null) {
                    ErrorLog.Warn(ModuleName, "Invoke skipped: the command is not part of this menu.");
                    return HResult.E_FAIL;
                }
                return current.Invoke(item) ? HResult.S_OK : HResult.E_FAIL;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "An exception occured invoking a context menu command.", ex);
                return HResult.E_FAIL;
            }
        }

        public int GetCommandString(int idCmd, uint uFlags, int reserved, IntPtr pszName, int cchMax) {
            try {
                CommandModel current = model;
                if (current == null) return HResult.E_FAIL;

                //  The shell sends the command id the menu reported (idCmdFirst + offset); a caller
                //  that sends the bare offset instead is accepted as well.
                CommandItem item = current.FindByIndex(idCmd - idCmdFirst) ?? current.FindByIndex(idCmd);
                if (item == null || item.IsSeparator || item.HasChildren) return HResult.E_FAIL;

                //  GCS_VERBW is 4 and GCS_HELPTEXTW is 5: the wide flag is bit 2, the help flag bit 0.
                //  The ANSI spellings would need an ANSI copy of the text, which the shell does not ask
                //  for on a Unicode build, so those requests are refused rather than filled with UTF-16.
                if ((uFlags & (uint)GCS.Unicode) == 0) return HResult.E_FAIL;
                string value = (uFlags & (uint)GCS.HelpTextA) != 0 ? item.HelpText : item.Verb;
                return WriteCommandString(value, pszName, cchMax);
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "An exception occured reading a command string.", ex);
                return HResult.E_FAIL;
            }
        }

        //  ------------------------------------------------------------------------------------
        //  IContextMenu2 / IContextMenu3
        //  ------------------------------------------------------------------------------------

        public int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam) {
            try {
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "An exception occured handling a menu message.", ex);
                return HResult.E_NOTIMPL;
            }
        }

        public int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr plResult) {
            try {
                //  The shell passes the address of the window procedure's result. An owner draw
                //  handler may write to it; this menu is not owner drawn, so it only clears it.
                if (plResult != IntPtr.Zero) Marshal.WriteIntPtr(plResult, IntPtr.Zero);
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "An exception occured handling a menu message.", ex);
                return HResult.E_NOTIMPL;
            }
        }

        //  ------------------------------------------------------------------------------------
        //  Menu construction
        //  ------------------------------------------------------------------------------------

        private void InsertChildren(IntPtr hMenu, CommandItem parent, int size, int firstId) {
            uint position = 0;
            foreach (var child in parent.Children) {
                if (child.IsSeparator) {
                    MENUITEMINFO separator = MENUITEMINFO.Create(MIIM.Type);
                    separator.fType = (uint)MFT.Separator;
                    if (!NativeMethods.InsertMenuItemW(hMenu, position, true, ref separator)) {
                        ErrorLog.Warn(ModuleName, "InsertMenuItem failed for a separator.");
                    }
                    position++;
                    continue;
                }

                IntPtr childMenu = IntPtr.Zero;
                if (child.HasChildren) {
                    childMenu = NativeMethods.CreatePopupMenu();
                    if (childMenu == IntPtr.Zero) {
                        ErrorLog.Warn(ModuleName, "CreatePopupMenu failed for \"" + child.Text + "\".");
                        continue;
                    }
                    subMenus.Add(childMenu);
                }

                if (!InsertItem(hMenu, position, child, firstId, size, childMenu)) {
                    ErrorLog.Warn(ModuleName, "InsertMenuItem failed for \"" + child.Text + "\".");
                }
                position++;

                if (childMenu != IntPtr.Zero) {
                    InsertChildren(childMenu, child, size, firstId);
                }
            }
        }

        private bool InsertItem(IntPtr hMenu, uint position, CommandItem item, int firstId, int size, IntPtr subMenu) {
            uint mask = (uint)(MIIM.ID | MIIM.FType | MIIM.State | MIIM.String);
            if (subMenu != IntPtr.Zero) mask |= (uint)MIIM.SubMenu;

            MENUITEMINFO info = MENUITEMINFO.Create((MIIM)mask);
            info.wID = (uint)(firstId + item.Index);
            info.fType = (uint)MFT.String;
            info.fState = item.Enabled ? (uint)MFS.Enabled : (uint)(MFS.Disabled | MFS.Grayed);
            if (subMenu != IntPtr.Zero) info.hSubMenu = subMenu;

            IntPtr bitmap = NativeMenuIcons.Get(item.Icon, size, selectedPaths);
            if (bitmap != IntPtr.Zero) {
                info.fMask |= (uint)MIIM.Bitmap;
                info.hbmpItem = bitmap;
            }

            IntPtr text = Marshal.StringToCoTaskMemUni(item.Text ?? string.Empty);
            try {
                info.dwTypeData = text;
                return NativeMethods.InsertMenuItemW(hMenu, position, true, ref info);
            } finally {
                Marshal.FreeCoTaskMem(text);
            }
        }

        private static int WriteCommandString(string value, IntPtr pszName, int cchMax) {
            if (pszName == IntPtr.Zero || cchMax <= 0) return HResult.E_FAIL;
            if (value == null) value = string.Empty;
            char[] chars = value.ToCharArray();
            if (chars.Length + 1 > cchMax) return HResult.E_FAIL;
            Marshal.Copy(chars, 0, pszName, chars.Length);
            Marshal.WriteInt16(pszName, chars.Length * 2, 0);
            return HResult.S_OK;
        }

        //  ------------------------------------------------------------------------------------
        //  Lifetime
        //  ------------------------------------------------------------------------------------

        /// <summary>
        /// Destroys the submenus this instance created. The shell owns <c>hMenu</c> and destroys it
        /// itself; only the nested popups handed to it through <c>hSubMenu</c> are ours to free.
        /// </summary>
        private void ReleaseMenus() {
            foreach (IntPtr menu in subMenus) {
                NativeMethods.DestroyMenu(menu);
            }
            subMenus.Clear();
        }

        ~ContextMenuHandler() {
            try {
                ReleaseMenus();
            } catch (Exception) {
                //  A finalizer must not throw; there is nothing left to report to.
            }
        }

        /// <summary>
        /// regasm runs this once for every class it registers. The batch entry point writes the
        /// handlers the product owns - this class is reachable only through the class id a caller
        /// chooses for it, so registering it here would write a class id nothing points at.
        /// </summary>
        [ComRegisterFunction]
        public static void Register(Type serverType) {
            ShellRegistrar.RegisterFromComRegistration(serverType);
        }

        [ComUnregisterFunction]
        public static void Unregister(Type serverType) {
            ShellRegistrar.UnregisterFromComUnregistration(serverType);
        }
    }
}
