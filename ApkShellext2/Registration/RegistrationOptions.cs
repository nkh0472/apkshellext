using System;
using ApkShellext2.Interop;

namespace ApkShellext2.Registration {
    /// <summary>
    /// Knobs the registrar needs beside the scope: where to write, which build the Windows 11 gate
    /// sees, and at which build that gate opens. Production code passes <see cref="ForScope"/>; tests
    /// and the equivalence harness pass a sandbox target and/or explicit build numbers.
    /// </summary>
    internal sealed class RegistrationOptions {
        internal RegistrationOptions(RegistryTarget target) {
            if (target == null) {
                throw new ArgumentNullException(nameof(target));
            }
            Target = target;
            ExplorerCommandBuildThreshold = WindowsVersion.ExplorerCommandMinimumBuild;
        }

        /// <summary>Hive and class roots to write.</summary>
        internal RegistryTarget Target { get; }

        /// <summary>
        /// Build number the Windows 11 gate is evaluated with; <c>null</c> means "this machine"
        /// (<see cref="WindowsVersion.BuildNumber"/>). The gate must never come from
        /// <c>Environment.OSVersion</c>: without an application manifest that property reports 6.2 on
        /// Windows 10 and 11 alike.
        /// </summary>
        internal int? BuildNumber { get; set; }

        /// <summary>Build at which <c>ExplorerCommandHandler</c> is registered; 22H2 by default.</summary>
        internal int ExplorerCommandBuildThreshold { get; set; }

        /// <summary>The build number the gate really uses.</summary>
        internal int EffectiveBuildNumber {
            get { return BuildNumber ?? WindowsVersion.BuildNumber; }
        }

        /// <summary>True when this registration should write the Windows 11 command handler.</summary>
        internal bool ShouldRegisterExplorerCommand {
            get { return Win11CommandRegistration.ShouldRegister(EffectiveBuildNumber, ExplorerCommandBuildThreshold); }
        }

        internal static RegistrationOptions ForScope(RegistrationScope scope) {
            return new RegistrationOptions(RegistryTarget.ForScope(scope));
        }

        /// <summary>A sandbox tree under HKCU; see <see cref="RegistryTarget.Sandbox"/>.</summary>
        internal static RegistrationOptions Sandbox(string rootPath) {
            return new RegistrationOptions(RegistryTarget.Sandbox(rootPath));
        }
    }
}
