using System;
using System.Runtime.InteropServices;
using ApkShellext2.Interop;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The item path the shell hands a handler before querying it, plus the parts of IPersistFile a
    /// read-only handler has to answer.
    ///
    /// Explorer always initialises with IPersistFile::Load and only then calls the handler; a
    /// handler that ignored Load would answer every query with a null path and keep drawing the
    /// generic icon. Save and GetCurFile are no-ops because the handler never writes anything back.
    /// </summary>
    public abstract class PersistFileNativeServer : IPersistFileNative {
        /// <summary>The path the shell is asking about, or null before Load.</summary>
        protected string SelectedPath { get; private set; }

        public int GetClassID(out Guid pClassID) {
            pClassID = GetType().GUID;
            return HResult.S_OK;
        }

        public int IsDirty() {
            return HResult.S_FALSE;
        }

        public int Load(string pszFileName, uint dwMode) {
            try {
                SelectedPath = pszFileName;
                return HResult.S_OK;
            } catch (Exception ex) {
                Diagnostics.ErrorLog.Error(GetType().Name, "Load failed", ex);
                return HResult.E_FAIL;
            }
        }

        public int Save(string pszFileName, bool fRemember) {
            return HResult.S_OK;
        }

        public int SaveCompleted(string pszFileName) {
            return HResult.S_OK;
        }

        public int GetCurFile(out IntPtr ppszFileName) {
            ppszFileName = IntPtr.Zero;
            return HResult.S_FALSE;
        }
    }
}
