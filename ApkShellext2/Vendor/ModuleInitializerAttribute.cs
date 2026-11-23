using System;

// .NET Framework 4.8 has no ModuleInitializerAttribute in mscorlib; Roslyn honours a
// user-declared attribute with this exact name and shape (the same trick as IsExternalInit).
//
// Do not delete: Diagnostics\ModuleInitializer.cs puts [ModuleInitializer] on
// ModuleBootstrap.Initialize, which is what installs the error-log hooks the moment
// ApkShellext2.dll is loaded into explorer.exe - an unhandled exception raised before the
// first ErrorLog access would otherwise leave no trace (the v3.4.x "explorer died and left
// no trace" case). Several module initializers per module are allowed.
//
// The declaration belongs here, on its own: a second declaration of the attribute anywhere
// in the assembly is CS0101.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
