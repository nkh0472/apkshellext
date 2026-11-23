using System;
using System.Collections;
using System.Collections.Generic;
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
    /// private and SafeArchive is internal. A friend-assembly declaration is not an option
    /// here because the product is strong-named with a key that is not in the repository
    /// (each checkout generates its own, and CI builds unsigned when the key secret is
    /// absent), so [InternalsVisibleTo] can carry no portable public key and would not
    /// compile (CS1726). Reflection keeps the guards non-public, and a renamed guard fails
    /// one test with a clear message instead of silently losing coverage.
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
        /// Opens an in-memory archive with ApkQuickReader.SafeArchive.Open, the entry point
        /// every reader uses: it applies the entry-count gate and the legacy entry-name
        /// encoding, so guards tested through it are the ones that actually ship.
        /// </summary>
        public static ArchiveHandle OpenArchive(byte[] bytes)
        {
            return new ArchiveHandle(InvokeSafeArchiveStatic(
                "Open", new MemoryStream(bytes, 0, bytes.Length, false, false)));
        }

        public static ArchiveHandle OpenArchive(Stream stream)
        {
            return new ArchiveHandle(InvokeSafeArchiveStatic("Open", stream));
        }

        /// <summary>
        /// Opens with an explicit cumulative read budget instead of the shipped
        /// MaxTotalReadBytes, so the budget path can be driven with a few kilobytes.
        /// Production readers never call this overload: they go through Open, which
        /// passes the 1 GiB constant.
        /// </summary>
        public static ArchiveHandle OpenArchiveWithBudget(byte[] bytes, long maxTotalReadBytes)
        {
            return OpenArchiveWithBudget(
                new MemoryStream(bytes, 0, bytes.Length, false, false), maxTotalReadBytes);
        }

        public static ArchiveHandle OpenArchiveWithBudget(Stream stream, long maxTotalReadBytes)
        {
            ConstructorInfo ctor = SafeArchiveType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Stream), typeof(long) },
                null);
            Assert.That(ctor, Is.Not.Null,
                "ApkQuickReader.SafeArchive(Stream, long) was not found; update TestSupport/ProductApi.cs.");
            try
            {
                return new ArchiveHandle(ctor.Invoke(new object[] { stream, maxTotalReadBytes }));
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable: Throw() always rethrows
            }
        }

        /// <summary>
        /// A disposable handle around one internal SafeArchive instance. Every member goes
        /// through the reflection helpers above, so a renamed guard fails with a clear
        /// message instead of a compile error, and the product surface stays internal.
        /// </summary>
        public sealed class ArchiveHandle : IDisposable
        {
            private readonly object handle;

            internal ArchiveHandle(object handle)
            {
                this.handle = handle;
            }

            public int DeclaredEntryCount
            {
                get { return (int)GetInstanceProperty(handle, "DeclaredEntryCount"); }
            }

            public string[] EntryNames
            {
                get
                {
                    List<string> names = new List<string>();
                    foreach (object entry in (IEnumerable)GetInstanceProperty(handle, "Entries"))
                    {
                        names.Add(EntryFullName(entry));
                    }
                    return names.ToArray();
                }
            }

            /// <summary>The legacy fuzzy (case-insensitive, first hit) entry lookup.</summary>
            public object GetEntry(string name)
            {
                return InvokeArchive(handle, "GetEntry", name);
            }

            /// <summary>The ordinal-exact lookup with the legacy fuzzy fallback.</summary>
            public object GetEntryExact(string name)
            {
                return InvokeArchive(handle, "GetEntryExact", name);
            }

            public bool Contains(string name)
            {
                return (bool)InvokeArchive(handle, "Contains", name);
            }

            /// <summary>True for file entries, false for directory entries (and null).</summary>
            public bool IsFile(object entry)
            {
                return (bool)InvokeArchive(handle, "IsFile", entry);
            }

            /// <summary>The declared uncompressed size, or -1 when the archive carries none.</summary>
            public long DeclaredLength(object entry)
            {
                return (long)InvokeArchive(handle, "DeclaredLength", entry);
            }

            /// <summary>
            /// Calls SafeArchive.ReadEntryBounded, the single sanctioned way the readers
            /// materialise an attacker-controlled zip entry.
            /// </summary>
            public byte[] ReadEntryBounded(object entry, long hardCap, string what)
            {
                return (byte[])InvokeArchive(handle, "ReadEntryBounded", entry, hardCap, what);
            }

            public string EntryFullName(object entry)
            {
                return (string)GetInstanceProperty(entry, "FullName");
            }

            public long EntryLength(object entry)
            {
                return (long)GetInstanceProperty(entry, "Length");
            }

            public long EntryCompressedLength(object entry)
            {
                return (long)GetInstanceProperty(entry, "CompressedLength");
            }

            /// <summary>
            /// The raw internal SafeArchive instance, for callers that must hand the archive to
            /// another product type taking it as an object (IconRequest.Container): it is the
            /// only instance implementing ApkQuickReader.IArchive, which stays internal.
            /// </summary>
            public object Internal
            {
                get { return handle; }
            }

            public void Dispose()
            {
                CloseArchive(handle);
            }
        }

        /// <summary>The byte cap an entry is actually read under (hard cap vs ratio allowance).</summary>
        public static long EntryByteLimit(long compressedLength, long hardCap)
        {
            return (long)InvokeSafeArchiveStatic("EntryByteLimit", compressedLength, hardCap);
        }

        /// <summary>
        /// Reads a SafeArchive const (MAX_ENTRIES, RATIO_LIMIT, …) so tests pin the limit
        /// that ships rather than restating the number.
        /// </summary>
        public static long SafeArchiveConstant(string name)
        {
            FieldInfo field = SafeArchiveType.GetField(name, StaticAnyVisibility);
            Assert.That(field, Is.Not.Null, "ApkQuickReader.SafeArchive." + name + " was not found; update TestSupport/ProductApi.cs.");
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

        private static Type SafeArchiveType
        {
            get { return RequireType("ApkQuickReader.SafeArchive"); }
        }

        private static object InvokeSafeArchiveStatic(string name, params object[] arguments)
        {
            MethodInfo method = RequireMethod(SafeArchiveType, name);
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

        private static void CloseArchive(object archive)
        {
            if (archive != null)
            {
                ((IDisposable)archive).Dispose();
            }
        }

        /// <summary>
        /// Calls an instance method on a SafeArchive handle and lets the guard's own
        /// exception escape unwrapped (reflection would hide it in TargetInvocationException).
        /// </summary>
        private static object InvokeArchive(object archive, string name, params object[] arguments)
        {
            MethodInfo method = archive.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(
                method,
                Is.Not.Null,
                archive.GetType().Name + "." + name + " was not found; update TestSupport/ProductApi.cs.");
            try
            {
                return method.Invoke(archive, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable: Throw() always rethrows
            }
        }

        // ------------------------------------------------------------------
        // Shared binary format parsers (stage 3 T-N).
        //
        // AxmlParser and ArscParser are internal, and they are the only place where the
        // stage 0 resource-table caps and the B4 icon lookup rules live. The fixtures
        // under tests/ApkShellext2.Tests/fixtures are real byte ranges cut out of a real
        // apk (see X:\apks-tn\fixtures\make-fixtures.ps1 for the offsets); the tests
        // drive the shipped parsers over those bytes.
        // ------------------------------------------------------------------

        private static Type AxmlParserType
        {
            get { return RequireType("ApkQuickReader.AxmlParser"); }
        }

        private static Type ArscParserType
        {
            get { return RequireType("ApkQuickReader.ArscParser"); }
        }

        private static Type ApkResourceType
        {
            get { return RequireType("ApkQuickReader.ApkResource"); }
        }

        /// <summary>
        /// Creates the parser the way ApkReader.createImage does: the resource table may be
        /// null (an .aab carries no resources.arsc, so referenced attributes stay numeric).
        /// densityRequested/ignoreXmlDensity are fixed to the shipping icon defaults here so
        /// a test asserts on the same config filter the shell uses.
        /// </summary>
        public static object NewAxmlParser(byte[] table, bool resolveReferences)
        {
            ConstructorInfo ctor = AxmlParserType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(byte[]), typeof(bool), typeof(bool), typeof(bool), typeof(Action<string>) },
                null);
            Assert.That(
                ctor,
                Is.Not.Null,
                "ApkQuickReader.AxmlParser(byte[], bool, bool, bool, Action<string>) was not found; " +
                "update TestSupport/ProductApi.cs together with the parser so the regression stays pinned.");
            try
            {
                return ctor.Invoke(new object[] { table, resolveReferences, false, false, (Action<string>)(_ => { }) });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable: Throw() always rethrows
            }
        }

        /// <summary>The ArscParser an AxmlParser resolves references through.</summary>
        public static object ArscOf(object axmlParser)
        {
            FieldInfo field = AxmlParserType.GetField("arsc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "ApkQuickReader.AxmlParser.arsc was not found; update TestSupport/ProductApi.cs.");
            return field.GetValue(axmlParser);
        }

        /// <summary>QuickSearchCompressedXml(xml, xpath, attribute, preferLocale): the rendered attribute value.</summary>
        public static string AxmlAttribute(object parser, byte[] xml, string xpath, string attribute)
        {
            return (string)InvokeInstance(parser, "QuickSearchCompressedXml", xml, xpath, attribute, false);
        }

        public static string AxmlStringPool(object parser, byte[] bytes, uint id)
        {
            return (string)InvokeInstance(parser, "QuickSearchStringPool", bytes, id);
        }

        public static string AxmlResourceMap(object parser, byte[] bytes, uint id)
        {
            return (string)InvokeInstance(parser, "QuickSearchCompressedXMlResMap", bytes, id);
        }

        public static string AxmlStringPoolOrResourceMap(object parser, byte[] bytes, uint id)
        {
            return (string)InvokeInstance(parser, "QuickSearchCompressedXmlStringPoolAndResMap", bytes, id);
        }

        public static System.Xml.XmlDocument AxmlExtract(object parser, byte[] xml)
        {
            return (System.Xml.XmlDocument)InvokeInstance(parser, "ExtractCompressedXml", xml);
        }

        /// <summary>ResolveIconValue(value, referenceId): the B4 icon lookup chain (rules 1, 3, 4, 5).</summary>
        public static string AxmlResolveIconValue(object parser, string value, uint referenceId)
        {
            return (string)InvokeInstance(parser, "ResolveIconValue", value, referenceId);
        }

        /// <summary>FindResourceId(typeName, entryName): name lookup across packages, smallest id wins.</summary>
        public static uint ArscFindResourceId(object arsc, string typeName, string entryName)
        {
            return (uint)InvokeInstance(arsc, "FindResourceId", typeName, entryName);
        }

        /// <summary>QuickSearchResource(id): the resolved configs/values, following references.</summary>
        public static object ArscQuickSearchResource(object arsc, uint id)
        {
            return InvokeInstance(arsc, "QuickSearchResource", id);
        }

        public static uint ResourceCount(object resource)
        {
            return (uint)GetInstanceProperty(resource, "Count");
        }

        public static string ResourceValue(object resource, int index)
        {
            // ApkResource exposes "values" as a public *field*, not a property; List<object>
            // exposes its indexer as "Item".
            FieldInfo valuesField = resource.GetType().GetField("values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(valuesField, Is.Not.Null, resource.GetType().Name + ".values was not found; update TestSupport/ProductApi.cs.");
            object values = valuesField.GetValue(resource);
            PropertyInfo item = values.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(item, Is.Not.Null, values.GetType().Name + ".Item was not found; update TestSupport/ProductApi.cs.");
            return (string)item.GetValue(values, new object[] { index });
        }

        /// <summary>Reads one of the stage 0 caps (MaxResourceConfigSize, MaxResourcePackages, ...).</summary>
        public static long ArscConstant(string name)
        {
            FieldInfo field = ArscParserType.GetField(name, StaticAnyVisibility);
            Assert.That(field, Is.Not.Null, "ApkQuickReader.ArscParser." + name + " was not found; update TestSupport/ProductApi.cs.");
            return Convert.ToInt64(field.GetRawConstantValue());
        }

        private static PropertyInfo RequireInstanceProperty(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(
                property,
                Is.Not.Null,
                instance.GetType().Name + "." + name + " was not found; update TestSupport/ProductApi.cs.");
            return property;
        }

        private static object GetInstanceProperty(object instance, string name)
        {
            return RequireInstanceProperty(instance, name).GetValue(instance, null);
        }

        private static object InvokeInstance(object instance, string name, params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(
                method,
                Is.Not.Null,
                instance.GetType().Name + "." + name + " was not found. The parser was renamed or moved; " +
                "update TestSupport/ProductApi.cs together with the parser so the regression stays pinned.");
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
