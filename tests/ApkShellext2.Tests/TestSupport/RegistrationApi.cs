using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The outcome of one ShellRegistrar.Register / Unregister call, read through reflection
    /// because RegistrationReport is internal.
    /// </summary>
    internal sealed class RegistrationRun
    {
        public string Scope { get; set; }

        public int BuildNumber { get; set; }

        public bool ExplorerCommandRegistered { get; set; }

        public IReadOnlyList<string> KeysWritten { get; set; }
    }

    /// <summary>
    /// The one place that knows ApkShellext2.Registration internal member names, in the spirit
    /// of ProductApi: the registrar is internal (the product is strong-named with a key that is
    /// not in the repository, so [InternalsVisibleTo] cannot compile), and a renamed member must
    /// fail one test with a clear message instead of silently losing coverage.
    ///
    /// The tests themselves never read a value back through the product's own RegistryWriter -
    /// they open the sandbox with Microsoft.Win32.Registry and compare against hard-coded
    /// GUIDs, key paths and value names, so a mistake in ShellGuids cannot make a test agree
    /// with itself.
    /// </summary>
    internal static class RegistrationApi
    {
        private const BindingFlags StaticAnyVisibility = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceAnyVisibility = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Assembly Product
        {
            get { return ProductApi.Assembly; }
        }

        /// <summary>
        /// RegistrationOptions.Sandbox(rootPath): a target whose classes roots, Blocked key and
        /// Approved key all live under HKCU\&lt;rootPath&gt;, so a test never writes where a real
        /// installation lives.
        /// </summary>
        public static object Sandbox(string rootPath)
        {
            Type optionsType = RequireType("ApkShellext2.Registration.RegistrationOptions");
            MethodInfo sandbox = optionsType.GetMethod("Sandbox", StaticAnyVisibility);
            Assert.That(sandbox, Is.Not.Null, "RegistrationOptions.Sandbox was not found; update TestSupport/RegistrationApi.cs.");
            return Invoke(sandbox, null, new object[] { rootPath });
        }

        /// <summary>Overrides the build number the Win11 gate sees (null in the product means "this machine").</summary>
        public static object WithBuild(object options, int buildNumber)
        {
            PropertyInfo property = RequireType("ApkShellext2.Registration.RegistrationOptions")
                .GetProperty("BuildNumber", InstanceAnyVisibility);
            Assert.That(property, Is.Not.Null, "RegistrationOptions.BuildNumber was not found; update TestSupport/RegistrationApi.cs.");
            property.SetValue(options, buildNumber, null);
            return options;
        }

        /// <summary>Overrides the IExplorerCommand build threshold (22621 in shipping code).</summary>
        public static object WithThreshold(object options, int threshold)
        {
            PropertyInfo property = RequireType("ApkShellext2.Registration.RegistrationOptions")
                .GetProperty("ExplorerCommandBuildThreshold", InstanceAnyVisibility);
            Assert.That(property, Is.Not.Null, "RegistrationOptions.ExplorerCommandBuildThreshold was not found; update TestSupport/RegistrationApi.cs.");
            property.SetValue(options, threshold, null);
            return options;
        }

        public static RegistrationRun Register(string scopeName, string dllPath, object options)
        {
            return Describe(InvokeRegistrar("Register", scopeName, dllPath, options));
        }

        public static RegistrationRun Unregister(string scopeName, object options)
        {
            MethodInfo method = RequireRegistrarMethod("Unregister");
            return Describe(Invoke(method, null, new object[] { ParseScope(scopeName), options }));
        }

        public static string[] AllClsids()
        {
            return ReadShellGuidsList("AllClsids");
        }

        public static string[] AllProgIds()
        {
            return ReadShellGuidsList("AllProgIds");
        }

        private static string[] ReadShellGuidsList(string methodName)
        {
            Type guidsType = RequireType("ApkShellext2.Registration.ShellGuids");
            MethodInfo method = guidsType.GetMethod(methodName, StaticAnyVisibility);
            Assert.That(method, Is.Not.Null, "ShellGuids." + methodName + " was not found; update TestSupport/RegistrationApi.cs.");
            object value = Invoke(method, null, new object[0]);
            List<string> result = new List<string>();
            foreach (object item in (IEnumerable)value)
            {
                result.Add((string)item);
            }

            return result.ToArray();
        }

        private static MethodInfo RequireRegistrarMethod(string name)
        {
            Type registrarType = RequireType("ApkShellext2.Registration.ShellRegistrar");
            MethodInfo method = registrarType.GetMethod(name, StaticAnyVisibility);
            Assert.That(
                method,
                Is.Not.Null,
                "ShellRegistrar." + name + " was not found. The registrar was renamed or moved; update TestSupport/RegistrationApi.cs.");
            return method;
        }

        private static object InvokeRegistrar(string name, string scopeName, string dllPath, object options)
        {
            MethodInfo method = RequireRegistrarMethod(name);
            return Invoke(method, null, new[] { ParseScope(scopeName), (object)dllPath, options });
        }

        private static object ParseScope(string scopeName)
        {
            Type scopeType = RequireType("ApkShellext2.Registration.RegistrationScope");
            Assert.That(Enum.IsDefined(scopeType, scopeName), Is.True, "RegistrationScope." + scopeName + " does not exist.");
            return Enum.Parse(scopeType, scopeName);
        }

        private static RegistrationRun Describe(object report)
        {
            Type reportType = report.GetType();
            RegistrationRun run = new RegistrationRun();
            run.Scope = ((object)reportType.GetProperty("Scope", InstanceAnyVisibility).GetValue(report, null)).ToString();
            run.BuildNumber = (int)reportType.GetProperty("BuildNumber", InstanceAnyVisibility).GetValue(report, null);
            run.ExplorerCommandRegistered = (bool)reportType.GetProperty("ExplorerCommandRegistered", InstanceAnyVisibility).GetValue(report, null);
            List<string> keys = new List<string>();
            object written = reportType.GetProperty("KeysWritten", InstanceAnyVisibility).GetValue(report, null);
            foreach (object key in (IEnumerable)written)
            {
                keys.Add((string)key);
            }

            run.KeysWritten = keys.AsReadOnly();
            return run;
        }

        private static Type RequireType(string typeName)
        {
            Type type = Product.GetType(typeName, throwOnError: false);
            Assert.That(type, Is.Not.Null, "type " + typeName + " was not found in " + Product.FullName);
            return type;
        }

        /// <summary>
        /// Reflection wraps whatever the registrar throws (UnauthorizedAccessException,
        /// ArgumentException, ...) in TargetInvocationException; unwrap so a test can assert on
        /// the real type.
        /// </summary>
        private static object Invoke(MethodInfo method, object instance, object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable: Throw() always rethrows
            }
        }
    }
}
