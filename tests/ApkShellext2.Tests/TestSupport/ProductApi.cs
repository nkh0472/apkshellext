using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using WebPWrapper;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The one place that knows the product's internal/private member names.
    ///
    /// The stage 0 guards are deliberately not public API: WebPWrapper.ValidateDecodeSize is
    /// private and ZipSafety is internal. A friend-assembly declaration is not an option
    /// here because the product is strong-named with a key that is not in the repository
    /// (each checkout generates its own, and CI builds unsigned when the key secret is
    /// absent), so [InternalsVisibleTo] can carry no portable public key and would not
    /// compile (CS1726). Reflection keeps the product sources and therefore the shipped
    /// dll byte-for-byte unchanged, and a renamed guard fails one test with a clear
    /// message instead of silently losing coverage.
    /// </summary>
    internal static class ProductApi
    {
        private const BindingFlags StaticAnyVisibility = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static Assembly Assembly
        {
            get { return typeof(WebP).Assembly; }
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, StaticAnyVisibility);
            Assert.That(
                method,
                Is.Not.Null,
                "ApkShellext2." + type.Name + "." + name + " was not found. The guard was renamed or moved; " +
                "update TestSupport/ProductApi.cs together with the guard so the regression stays pinned.");
            return method;
        }

        private static Type RequireType(string typeName)
        {
            Type type = Assembly.GetType(typeName, throwOnError: false);
            Assert.That(type, Is.Not.Null, "type " + typeName + " was not found in " + Assembly.FullName);
            return type;
        }

        /// <summary>
        /// Calls WebPWrapper.WebP.ValidateDecodeSize and lets the guard's own exception
        /// type (InvalidDataException) escape unwrapped - reflection wraps it in
        /// TargetInvocationException, which would hide what the caller asserts on.
        /// </summary>
        public static void ValidateDecodeSize(int width, int height)
        {
            MethodInfo method = RequireMethod(typeof(WebP), "ValidateDecodeSize");
            try
            {
                method.Invoke(null, new object[] { width, height });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable: Throw() always rethrows
            }
        }

        /// <summary>
        /// Reads a guard constant (MaxWebPDecodeSide / MaxWebPDecodePixels) so the tests
        /// can pin the exact limits, not just the behaviour around them.
        /// </summary>
        public static object DecodeLimitConstant(string name)
        {
            FieldInfo field = typeof(WebP).GetField(name, StaticAnyVisibility);
            Assert.That(field, Is.Not.Null, "ApkShellext2.WebP." + name + " was not found; update TestSupport/ProductApi.cs.");
            return field.GetRawConstantValue();
        }

        /// <summary>
        /// Opens a zip with ApkQuickReader.ZipSafety.OpenZip, the entry point every
        /// reader uses: it applies the entry-count gate and the legacy entry-name
        /// encoding, so guards tested through it are the ones that actually ship.
        /// </summary>
        public static ZipArchive OpenZip(Stream stream)
        {
            Type zipSafety = RequireType("ApkQuickReader.ZipSafety");
            MethodInfo method = RequireMethod(zipSafety, "OpenZip");
            try
            {
                return (ZipArchive)method.Invoke(null, new object[] { stream });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable
            }
        }

        /// <summary>
        /// Calls ApkQuickReader.ZipSafety.ReadEntryBounded, the single sanctioned way the
        /// readers materialise an attacker-controlled zip entry.
        /// </summary>
        public static byte[] ReadEntryBounded(ZipArchive zip, ZipArchiveEntry entry, long hardCap, string what)
        {
            Type zipSafety = RequireType("ApkQuickReader.ZipSafety");
            MethodInfo method = RequireMethod(zipSafety, "ReadEntryBounded");
            try
            {
                return (byte[])method.Invoke(null, new object[] { zip, entry, hardCap, what });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable
            }
        }
    }
}
