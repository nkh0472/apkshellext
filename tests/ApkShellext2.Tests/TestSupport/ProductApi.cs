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
            return (ZipArchive)InvokeZipSafety("OpenZip", stream);
        }

        /// <summary>
        /// Calls ApkQuickReader.ZipSafety.ReadEntryBounded, the single sanctioned way the
        /// readers materialise an attacker-controlled zip entry.
        /// </summary>
        public static byte[] ReadEntryBounded(ZipArchive zip, ZipArchiveEntry entry, long hardCap, string what)
        {
            return (byte[])InvokeZipSafety("ReadEntryBounded", zip, entry, hardCap, what);
        }

        /// <summary>The legacy fuzzy (case-insensitive, first hit) entry lookup.</summary>
        public static ZipArchiveEntry GetEntry(ZipArchive zip, string name)
        {
            return (ZipArchiveEntry)InvokeZipSafety("GetEntry", zip, name);
        }

        /// <summary>The ordinal-exact lookup with the legacy fuzzy fallback.</summary>
        public static ZipArchiveEntry GetEntryExact(ZipArchive zip, string name)
        {
            return (ZipArchiveEntry)InvokeZipSafety("GetEntryExact", zip, name);
        }

        public static bool Contains(ZipArchive zip, string name)
        {
            return (bool)InvokeZipSafety("Contains", zip, name);
        }

        /// <summary>True for file entries, false for directory entries (and null).</summary>
        public static bool IsFile(ZipArchiveEntry entry)
        {
            return (bool)InvokeZipSafety("IsFile", entry);
        }

        /// <summary>The declared uncompressed size, or -1 when the archive carries none.</summary>
        public static long DeclaredLength(ZipArchiveEntry entry)
        {
            return (long)InvokeZipSafety("DeclaredLength", entry);
        }

        /// <summary>The byte cap an entry is actually read under (hard cap vs ratio allowance).</summary>
        public static long EntryByteLimit(long compressedLength, long hardCap)
        {
            return (long)InvokeZipSafety("EntryByteLimit", compressedLength, hardCap);
        }

        /// <summary>
        /// Reads a ZipSafety const (MAX_ENTRIES, RATIO_LIMIT, …) so tests pin the limit
        /// that ships rather than restating the number.
        /// </summary>
        public static long ZipSafetyConstant(string name)
        {
            FieldInfo field = ZipSafetyType.GetField(name, StaticAnyVisibility);
            Assert.That(field, Is.Not.Null, "ApkQuickReader.ZipSafety." + name + " was not found; update TestSupport/ProductApi.cs.");
            return Convert.ToInt64(field.GetRawConstantValue());
        }

        /// <summary>
        /// Calls an SVG parser's public Parse(string) entry point (number list, unit,
        /// unit list, transform list, path data). The converters also carry an internal
        /// Parse(TextSlice) overload that the public one forwards to, so the method is
        /// selected by its exact string parameter rather than by name alone
        /// (GetMethod(name) would report an ambiguous match).
        /// </summary>
        public static object ParseSvg(string typeName, string value)
        {
            Type type = RequireType(typeName);
            MethodInfo method = null;
            foreach (MethodInfo candidate in type.GetMethods(StaticAnyVisibility))
            {
                if (candidate.Name != "Parse")
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    method = candidate;
                    break;
                }
            }

            Assert.That(
                method,
                Is.Not.Null,
                typeName + ".Parse(string) was not found. The parser was renamed or moved; " +
                "update TestSupport/ProductApi.cs together with the parser so the regression stays pinned.");

            try
            {
                return method.Invoke(null, new object[] { value });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable: Throw() always rethrows
            }
        }

        /// <summary>Reads a public instance property of a parsed SVG object (SvgUnit.Value, a segment's End, ...).</summary>
        public static object ReadSvgProperty(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(
                property,
                Is.Not.Null,
                instance.GetType().Name + "." + name + " was not found; update TestSupport/ProductApi.cs.");
            return property.GetValue(instance, null);
        }

        /// <summary>Calls a public parameterless string-returning method on a parsed SVG object (SvgTransform.WriteToString).</summary>
        public static string CallSvgMethod(object instance, string name)
        {
            MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(
                method,
                Is.Not.Null,
                instance.GetType().Name + "." + name + " was not found; update TestSupport/ProductApi.cs.");
            return (string)method.Invoke(instance, null);
        }

        /// <summary>
        /// Element count of a parsed SVG collection. SvgPathSegmentList implements only
        /// IList&lt;SvgPathSegment&gt;, not the non-generic IList, so the tests cannot cast.
        /// </summary>
        public static int SvgCount(object list)
        {
            return (int)ReadSvgProperty(list, "Count");
        }

        /// <summary>Indexer access to a parsed SVG collection (see <see cref="SvgCount"/>).</summary>
        public static object SvgItem(object list, int index)
        {
            PropertyInfo item = list.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(
                item,
                Is.Not.Null,
                list.GetType().Name + ".Item was not found; update TestSupport/ProductApi.cs.");
            return item.GetValue(list, new object[] { index });
        }

        private static Type ZipSafetyType
        {
            get { return RequireType("ApkQuickReader.ZipSafety"); }
        }

        private static object InvokeZipSafety(string name, params object[] arguments)
        {
            MethodInfo method = RequireMethod(ZipSafetyType, name);
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable: Throw() always rethrows
            }
        }
    }
}
