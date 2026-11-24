using System;
using System.Runtime.InteropServices;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Registration;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The info-tip handler: IQueryInfo returns the tooltip and IPersistFile carries the item path.
    ///
    /// There is one tip per package, so the QITIPF flags (single line, use name, use shortcut
    /// target) do not change the text - the pattern in the settings decides it. A tip is allocated
    /// with CoTaskMemAlloc by the marshaler because the shell frees it with CoTaskMemFree.
    /// </summary>
    [ComVisible(true)]
    [Guid("946435A5-FE96-416D-99DB-E94EE9FB46C8")]
    [ProgId(ShellGuids.InfoTipProgId)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class InfoTipHandlerNative : PersistFileNativeServer, IQueryInfoNative {
        private const string ModuleName = "InfoTipHandlerNative";

        public int GetInfoTip(uint dwFlags, out string ppwszTip) {
            try {
                if (string.IsNullOrEmpty(SelectedPath)) {
                    ppwszTip = null;
                    return HResult.S_FALSE;
                }
                ppwszTip = InfoTipRenderer.Render(SelectedPath, ModuleName);
                return string.IsNullOrEmpty(ppwszTip) ? HResult.S_FALSE : HResult.S_OK;
            } catch (Exception ex) {
                ppwszTip = null;
                ErrorLog.Error(ModuleName, "GetInfoTip failed", ex);
                return HResult.E_FAIL;
            }
        }

        public int GetInfoFlags(out uint pdwFlags) {
            // No QITIPF_* flag applies: the tip is a single short line and needs no slow query.
            pdwFlags = 0;
            return HResult.S_OK;
        }

        /// <summary>
        /// regasm runs this once for every class it registers. The batch entry point writes every
        /// handler in the assembly, so one run registers the whole product, not just this class.
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
