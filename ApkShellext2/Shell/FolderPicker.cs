using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ApkShellext2.Shell {
    /// <summary>
    /// The modern shell folder picker (<c>IFileOpenDialog</c> with
    /// <c>FOS_PICKFOLDERS</c>). It runs on <see cref="StaUiHost"/>'s STA thread and returns
    /// the chosen folder, or null when the user cancels.
    /// </summary>
    internal static class FolderPicker {
        private const int Cancelled = unchecked((int)0x800704C7);

        internal static string Pick(string title) {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) {
                throw new InvalidOperationException("The folder picker must run on an STA thread.");
            }

            IFileDialog dialog = null;
            IShellItem result = null;
            try {
                dialog = (IFileDialog)new FileOpenDialogCoClass();
                FileOpenOptions options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FileOpenOptions.PickFolders | FileOpenOptions.ForceFileSystem);
                if (!string.IsNullOrEmpty(title)) {
                    dialog.SetTitle(title);
                }

                int hr = dialog.Show(IntPtr.Zero);
                if (hr == Cancelled) return null;
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);

                dialog.GetResult(out result);
                if (result == null) return null;

                IntPtr name;
                result.GetDisplayName(Sigdn.FileSystemPath, out name);
                try {
                    return Marshal.PtrToStringUni(name);
                } finally {
                    Marshal.FreeCoTaskMem(name);
                }
            } finally {
                if (result != null) Marshal.ReleaseComObject(result);
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        [Flags]
        private enum FileOpenOptions : uint {
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040
        }

        private enum Sigdn : uint {
            FileSystemPath = 0x80058000
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCoClass {
        }

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint fileTypes, IntPtr filterSpec);

            void SetFileTypeIndex(uint fileTypeIndex);

            void GetFileTypeIndex(out uint fileTypeIndex);

            void Advise(IntPtr events, out uint cookie);

            void Unadvise(uint cookie);

            void SetOptions(FileOpenOptions options);

            void GetOptions(out FileOpenOptions options);

            void SetDefaultFolder(IShellItem folder);

            void SetFolder(IShellItem folder);

            void GetFolder(out IShellItem folder);

            void GetCurrentSelection(out IShellItem selection);

            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

            void GetResult(out IShellItem item);

            void AddPlace(IShellItem item, int place);

            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

            void Close(int hr);

            void SetClientGuid(ref Guid guid);

            void ClearClientData();

            void SetFilter(IntPtr filter);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);

            void GetParent(out IShellItem parent);

            void GetDisplayName(Sigdn name, out IntPtr text);

            void GetAttributes(uint mask, out uint attributes);

            void Compare(IShellItem item, uint hint, out int order);
        }
    }
}
