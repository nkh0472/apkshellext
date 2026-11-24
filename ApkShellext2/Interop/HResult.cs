using System;

namespace ApkShellext2.Interop {
    /// <summary>
    /// The HRESULT values used by the shell handlers, plus the two packing helpers the shell
    /// contract needs.
    ///
    /// Every COM method declared in <c>Interop\</c> is <c>[PreserveSig]</c>, so a failure is
    /// returned as a negative value instead of being translated into an exception by the CLR.
    /// Compare with <see cref="Succeeded"/> / <see cref="Failed"/> rather than testing for
    /// equality against <see cref="S_OK"/>.
    /// </summary>
    internal static class HResult {
        internal const int S_OK = 0;
        internal const int S_FALSE = 1;

        internal const int E_NOTIMPL = unchecked((int)0x80004001);
        internal const int E_NOINTERFACE = unchecked((int)0x80004002);
        internal const int E_POINTER = unchecked((int)0x80004003);
        internal const int E_ABORT = unchecked((int)0x80004004);
        internal const int E_FAIL = unchecked((int)0x80004005);
        internal const int E_UNEXPECTED = unchecked((int)0x8000FFFF);

        internal const int E_INVALIDARG = unchecked((int)0x80070057);
        internal const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
        internal const int E_ACCESSDENIED = unchecked((int)0x80070005);
        internal const int E_HANDLE = unchecked((int)0x80070006);
        internal const int E_FILENOTFOUND = unchecked((int)0x80070002);
        internal const int E_PATHNOTFOUND = unchecked((int)0x80070003);

        /// <summary>
        /// <c>HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE)</c>: the data the caller offered is larger
        /// than the limit this component accepts for it.
        /// </summary>
        internal const int E_FILE_TOO_LARGE = unchecked((int)0x800700DF);

        internal const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
        internal const int CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);
        internal const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);
        internal const int CO_E_NOTINITIALIZED = unchecked((int)0x800401F0);

        internal const int STG_E_FILENOTFOUND = unchecked((int)0x80030002);
        internal const int STG_E_ACCESSDENIED = unchecked((int)0x80030005);
        internal const int STG_E_INVALIDPARAMETER = unchecked((int)0x80030057);

        /// <summary>Severity bit of an HRESULT (bit 31); set means failure.</summary>
        private const int SeverityFailure = unchecked((int)0x80000000);

        internal static bool Succeeded(int hr) {
            return (hr & SeverityFailure) == 0;
        }

        internal static bool Failed(int hr) {
            return (hr & SeverityFailure) != 0;
        }

        /// <summary>
        /// <c>HRESULT_FROM_WIN32</c>: wraps a Win32 error into an HRESULT with FACILITY_WIN32.
        /// A zero error maps to <see cref="S_OK"/>, a negative value is already an HRESULT and
        /// is returned unchanged (same contract as the Windows macro).
        /// </summary>
        internal static int FromWin32(int error) {
            if (error == 0) {
                return S_OK;
            }
            if (error < 0) {
                return error;
            }
            return unchecked((int)(((uint)error & 0x0000FFFFu) | 0x80070000u));
        }

        /// <summary>
        /// <c>MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, code)</c>. This is what
        /// <c>IContextMenu.QueryContextMenu</c> returns: the number of items added to the menu,
        /// not an error code. The shell only looks at the low 16 bits, so the count is masked.
        /// </summary>
        internal static int MakeSuccess(int code) {
            return code & 0xFFFF;
        }

        /// <summary>
        /// Same packing as <see cref="MakeSuccess"/>, but saturates instead of wrapping when the
        /// menu builder overflows the 16 bit slot (the shell would otherwise see a wrong count,
        /// and a negative value would be read as a failure HRESULT).
        /// </summary>
        internal static int MakeItemCount(int itemCount) {
            if (itemCount <= 0) {
                return 0;
            }
            return itemCount > 0xFFFF ? 0xFFFF : itemCount;
        }
    }
}
