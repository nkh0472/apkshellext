using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// Turns the selection the shell hands a command into the file paths the menu model works on.
    /// </summary>
    internal interface ISelectionReader {
        IReadOnlyList<string> Read(IntPtr psiItemArray);
    }

    /// <summary>
    /// Reads <c>IShellItemArray</c> through its vtable.
    /// </summary>
    /// <remarks>
    /// The array is read slot by slot instead of being cast to a [ComImport] interface: the
    /// pointer belongs to the shell (it is only valid for the duration of the call) and a runtime
    /// callable wrapper would take a reference the shell still owns, then release it when the
    /// wrapper is collected. See the remarks on ComObject for the crash that produced.
    ///
    /// The item pointers returned by GetItemAt are a different case - those are ours, so each one
    /// is released after its display name is copied.
    /// </remarks>
    internal sealed class ShellItemArraySelectionReader : ISelectionReader {
        private const string ModuleName = "ShellItemSelection";

        //  IShellItemArray: 3 BindToHandler, 4 GetPropertyStore, 5 GetPropertyDescriptionList,
        //  6 SetProperty, 7 GetCount, 8 GetItemAt, 9 EnumItems.
        private const int GetCountSlot = ComObject.FirstMethodSlot + 4;
        private const int GetItemAtSlot = ComObject.FirstMethodSlot + 5;

        //  IShellItem: 3 BindToHandler, 4 GetParent, 5 GetDisplayName, 6 GetAttributes, 7 Compare.
        private const int GetDisplayNameSlot = ComObject.FirstMethodSlot + 2;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCountDelegate(IntPtr self, out uint pcItems);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetItemAtDelegate(IntPtr self, uint dwIndex, out IntPtr ppsi);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDisplayNameDelegate(IntPtr self, SIGDN sigdnName, out IntPtr ppszName);

        public IReadOnlyList<string> Read(IntPtr psiItemArray) {
            List<string> paths = new List<string>();
            if (psiItemArray == IntPtr.Zero) return paths;
            try {
                GetCountDelegate getCount = ComObject.GetDelegate<GetCountDelegate>(psiItemArray, GetCountSlot);
                GetItemAtDelegate getItemAt = ComObject.GetDelegate<GetItemAtDelegate>(psiItemArray, GetItemAtSlot);

                uint count;
                int result = getCount(psiItemArray, out count);
                if (HResult.Failed(result)) {
                    ErrorLog.Warn(ModuleName, "The selected items could not be counted: " + Describe(result) + ".");
                    return paths;
                }

                for (uint index = 0; index < count; index++) {
                    IntPtr item;
                    if (HResult.Failed(getItemAt(psiItemArray, index, out item)) || item == IntPtr.Zero) continue;
                    try {
                        string path = ReadFileSystemPath(item);
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    } finally {
                        ComObject.Release(item);
                    }
                }
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The selected items could not be read.", ex);
            }
            return paths;
        }

        /// <summary>
        /// The file system path of one item, or null when the item has none (a search result or a
        /// virtual folder answers SIGDN_FILESYSPATH with a failure).
        /// </summary>
        private static string ReadFileSystemPath(IntPtr item) {
            GetDisplayNameDelegate getDisplayName = ComObject.GetDelegate<GetDisplayNameDelegate>(item, GetDisplayNameSlot);
            IntPtr name;
            if (HResult.Failed(getDisplayName(item, SIGDN.FileSysPath, out name)) || name == IntPtr.Zero) return null;
            try {
                return Marshal.PtrToStringUni(name);
            } finally {
                Marshal.FreeCoTaskMem(name);
            }
        }

        private static string Describe(int hresult) {
            return "0x" + ((uint)hresult).ToString("X8", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The single entry point the command classes use to learn which files the user selected.
    /// </summary>
    internal static class ShellItemSelection {
        private const string ModuleName = "ShellItemSelection";

        private static readonly ISelectionReader s_reader = new ShellItemArraySelectionReader();

        internal static ISelectionReader Reader { get { return s_reader; } }

        /// <summary>The selected paths, empty when the shell passed nothing usable.</summary>
        internal static IReadOnlyList<string> Read(IntPtr psiItemArray) {
            if (psiItemArray == IntPtr.Zero) return new string[0];
            try {
                IReadOnlyList<string> paths = s_reader.Read(psiItemArray);
                return paths ?? new string[0];
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "The selected items could not be read.", ex);
                return new string[0];
            }
        }
    }
}
