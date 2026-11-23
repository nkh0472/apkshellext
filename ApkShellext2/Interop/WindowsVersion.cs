using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// The Windows build number from <c>RtlGetVersion</c>, plus the thresholds stage 4 gates on.
    ///
    /// Why not <c>Environment.OSVersion</c>: on Windows 8.1 and later that API reports
    /// <c>6.2.9200</c> to any process whose executable has no compatibility manifest, which is
    /// exactly the case for <c>regasm.exe</c>, the installers and the test host. The version gate
    /// therefore reads the real build from ntdll.
    /// </summary>
    internal static class WindowsVersion {
        /// <summary>
        /// Windows 11 22H2 (build 22621) is where Explorer prefers <c>IExplorerCommand</c> for the
        /// new context menu over the classic <c>ShellEx\ContextMenuHandlers</c> registration.
        /// </summary>
        internal const int ExplorerCommandMinimumBuild = 22621;

        //  -1 = not measured yet, 0 = measurement failed (treat as "older than Windows 11").
        private static int cachedBuild = -1;

        /// <summary>
        /// Host build number, or 0 when <c>RtlGetVersion</c> failed. A 0 build never passes a
        /// Windows 11 gate, so a failed probe degrades to the classic registration only.
        /// </summary>
        internal static int BuildNumber {
            get {
                int measured = cachedBuild;
                if (measured >= 0) {
                    return measured;
                }
                measured = ReadBuildNumber();
                cachedBuild = measured;
                return measured;
            }
        }

        /// <summary>
        /// Reads the build number straight from <c>RtlGetVersion</c>. Exposed separately from
        /// <see cref="BuildNumber"/> so a test can call it without warming the cache.
        /// </summary>
        internal static int ReadBuildNumber() {
            var versionInfo = new OSVERSIONINFOEXW();
            versionInfo.dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(OSVERSIONINFOEXW));
            int status = NativeMethods.RtlGetVersion(ref versionInfo);
            if (HResult.Failed(status)) {
                return 0;
            }
            return (int)versionInfo.dwBuildNumber;
        }

        /// <summary>Pure predicate, parameterised so the gate can be tested on any host.</summary>
        internal static bool SupportsExplorerCommand(int buildNumber) {
            return buildNumber >= ExplorerCommandMinimumBuild;
        }

        /// <summary>Gate for this host.</summary>
        internal static bool SupportsExplorerCommand() {
            return SupportsExplorerCommand(BuildNumber);
        }
    }
}
