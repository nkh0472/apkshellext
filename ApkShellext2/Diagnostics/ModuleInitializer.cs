using System.Runtime.CompilerServices;

namespace ApkShellext2.Diagnostics {
    /// <summary>
    /// Installs the error-log hooks the moment ApkShellext2.dll is loaded into a host process.
    ///
    /// ErrorLog's static constructor calls EnsureHooks() as well, but a static constructor only
    /// runs on the first access to the class - i.e. after something already reported an error.
    /// An unhandled exception (or an unobserved task exception) raised before that first error is
    /// exactly the v3.4.x "explorer died and left no trace" case: nothing would ever have logged
    /// it. A module initializer runs on assembly load, so the hooks are in place before any of our
    /// extension code runs.
    ///
    /// The [ModuleInitializer] attribute itself is the polyfill declared in
    /// FacadeResolver.cs:8-12 (net48 has no System.Runtime.CompilerServices.ModuleInitializerAttribute
    /// in mscorlib, and Roslyn honours a user-declared one with that exact name). Using that
    /// attribute means a second declaration here would be CS0101 - this file only adds the entry
    /// point. Several module initializers per module are allowed; this one is kept separate from
    /// FacadeResolver.Init() so the assembly-resolve hook and the diagnostics hooks stay
    /// independent.
    ///
    /// Keep this trivial: the CLR runs it while holding the module lock, so it must not block and
    /// must not throw. EnsureHooks() only subscribes two handlers behind an Interlocked guard.
    /// </summary>
    internal static class ModuleBootstrap {
        [ModuleInitializer]
        internal static void Initialize() {
            ErrorLog.EnsureHooks();
        }
    }
}
