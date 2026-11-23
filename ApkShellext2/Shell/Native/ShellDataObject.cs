using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ApkShellext2.Interop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// Reads the file list the shell attached to <c>IShellExtInit.Initialize</c>. The selection
    /// arrives as a COM data object carrying <c>CF_HDROP</c>; this reads it through the managed
    /// data object view and always releases the storage medium, because the shell hands the
    /// object over only for the duration of the call.
    /// </summary>
    internal static class ShellDataObject {
        private const uint AllFiles = 0xFFFFFFFF;

        /// <summary>Absolute paths in the order the shell listed them; empty when there is no <c>CF_HDROP</c>.</summary>
        internal static string[] ReadFilePaths(IntPtr pDataObj) {
            if (pDataObj == IntPtr.Zero) return new string[0];
            var data = Marshal.GetObjectForIUnknown(pDataObj) as ComTypes.IDataObject;
            if (data == null) return new string[0];

            var format = new ComTypes.FORMATETC();
            format.cfFormat = (short)ClipboardFormat.CF_HDROP;
            format.dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT;
            format.lindex = -1;
            format.tymed = ComTypes.TYMED.TYMED_HGLOBAL;

            ComTypes.STGMEDIUM medium;
            try {
                data.GetData(ref format, out medium);
            } catch (COMException) {
                return new string[0];
            }
            try {
                return ReadDropFiles(medium.unionmember);
            } finally {
                ReleaseStgMedium(ref medium);
            }
        }

        private static string[] ReadDropFiles(IntPtr hDrop) {
            if (hDrop == IntPtr.Zero) return new string[0];
            uint count = DragQueryFileW(hDrop, AllFiles, IntPtr.Zero, 0);
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++) {
                uint length = DragQueryFileW(hDrop, i, IntPtr.Zero, 0);
                if (length == 0) continue;
                IntPtr buffer = Marshal.AllocHGlobal(((int)length + 1) * 2);
                try {
                    DragQueryFileW(hDrop, i, buffer, length + 1);
                    string path = Marshal.PtrToStringUni(buffer);
                    if (!string.IsNullOrEmpty(path)) paths.Add(path);
                } finally {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return paths.ToArray();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);

        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);
    }
}
