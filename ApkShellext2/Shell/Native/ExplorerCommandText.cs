using System;
using System.Runtime.InteropServices;
using ApkShellext2.Interop;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The text out-parameters of <c>IExplorerCommand</c>. A returned string is a CoTaskMem
    /// allocation the shell frees, so it is copied once and never cached - the same instance is
    /// asked for its title in more than one language context and the shell owns nothing else here.
    /// </summary>
    internal static class ExplorerCommandText {
        /// <summary>Hands the text to the caller and reports success.</summary>
        internal static int Succeed(string text, out IntPtr result) {
            result = string.IsNullOrEmpty(text) ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(text);
            return HResult.S_OK;
        }

        /// <summary>
        /// Declines an optional text (tooltip, icon) without an error: E_NOTIMPL is the answer the
        /// shell treats as "this command has none", while a null pointer with S_OK is a valid but
        /// ambiguous empty string.
        /// </summary>
        internal static int Declined(out IntPtr result) {
            result = IntPtr.Zero;
            return HResult.E_NOTIMPL;
        }
    }
}
