using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// The executable form of the vtable contract: every flat COM declaration and every marshalled
    /// structure in this namespace is compared against the Win32 layout, and <see cref="Verify"/>
    /// returns the list of mismatches (empty means "the declarations are what the shell expects").
    ///
    /// This exists because a wrong slot or a wrong field offset does not fail at compile time - it
    /// corrupts the call at run time. In the vendored SharpShell the failure mode was an access
    /// violation in the shell process when Explorer called <c>HandleMenuMsg2</c>; with 64 bit
    /// <c>FORMATETC</c>/<c>STGMEDIUM</c> a wrong size makes <c>ReleaseStgMedium</c> free the wrong
    /// pointer. <c>InteropLayoutTests</c> asserts that this returns nothing, which turns both classes
    /// of defect into a test failure.
    /// </summary>
    internal static class ComLayout {
        private sealed class InterfaceContract {
            internal InterfaceContract(Type type, string iid, params string[] methods) {
                Type = type;
                Iid = iid;
                Methods = methods;
            }

            /// <summary>Declared type; its name doubles as the label in diagnostics.</summary>
            internal Type Type { get; }
            /// <summary>Expected IID as text (the <c>[Guid]</c> attribute must match).</summary>
            internal string Iid { get; }
            /// <summary>Method names in vtable order; index 0 is slot 3.</summary>
            internal string[] Methods { get; }
        }

        private sealed class FieldContract {
            internal string Name { get; set; }
            internal int OffsetX86 { get; set; }
            internal int OffsetX64 { get; set; }
        }

        private sealed class StructContract {
            internal StructContract(Type type, int sizeX86, int sizeX64, params FieldContract[] fields) {
                Type = type;
                SizeX86 = sizeX86;
                SizeX64 = sizeX64;
                Fields = fields;
            }

            internal Type Type { get; }
            internal int SizeX86 { get; }
            internal int SizeX64 { get; }
            internal FieldContract[] Fields { get; }
        }

        private static FieldContract Offset(string field, int x86, int x64) {
            return new FieldContract { Name = field, OffsetX86 = x86, OffsetX64 = x64 };
        }

        /// <summary>
        /// Vtable contract per declaration. The IID strings are the Win32 literals (the comment in
        /// each declaration names the SharpShell file it was copied from).
        /// </summary>
        private static readonly InterfaceContract[] InterfaceContracts = {
            new InterfaceContract(typeof(IContextMenuFlat), "000214e4-0000-0000-c000-000000000046",
                "QueryContextMenu", "InvokeCommand", "GetCommandString"),
            new InterfaceContract(typeof(IContextMenu2Flat), "000214f4-0000-0000-c000-000000000046",
                "QueryContextMenu", "InvokeCommand", "GetCommandString", "HandleMenuMsg"),
            new InterfaceContract(typeof(IContextMenu3Flat), "bcfce0a0-ec17-11d0-8d10-00a0c90f2719",
                "QueryContextMenu", "InvokeCommand", "GetCommandString", "HandleMenuMsg", "HandleMenuMsg2"),
            new InterfaceContract(typeof(IShellExtInitFlat), "000214e8-0000-0000-c000-000000000046",
                "Initialize"),
            new InterfaceContract(typeof(IQueryInfoFlat), "00021500-0000-0000-c000-000000000046",
                "GetInfoTip", "GetInfoFlags"),
            new InterfaceContract(typeof(IExtractIconWFlat), "000214fa-0000-0000-c000-000000000046",
                "GetIconLocation", "Extract"),
            new InterfaceContract(typeof(IPersistFileFlat), "0000010b-0000-0000-c000-000000000046",
                "GetClassID", "IsDirty", "Load", "Save", "SaveCompleted", "GetCurFile"),
            new InterfaceContract(typeof(IThumbnailProviderFlat), "e357fccd-a995-4576-b01f-234630154e96",
                "GetThumbnail"),
            new InterfaceContract(typeof(IInitializeWithStreamFlat), "b824b49d-22ac-4161-ac8a-9916e8fa3f7f",
                "Initialize"),
            new InterfaceContract(typeof(IShellFolderFlat), "000214e6-0000-0000-c000-000000000046",
                "ParseDisplayName", "EnumObjects", "BindToObject", "BindToStorage", "CompareIDs",
                "CreateViewObject", "GetAttributesOf", "GetUIObjectOf", "GetDisplayNameOf", "SetNameOf"),
            new InterfaceContract(typeof(IClassFactoryFlat), "00000001-0000-0000-c000-000000000046",
                "CreateInstance", "LockServer"),
            new InterfaceContract(typeof(IExplorerCommandFlat), "a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9",
                "GetTitle", "GetIcon", "GetToolTip", "GetCanonicalName", "GetState", "Invoke",
                "GetFlags", "EnumSubCommands"),
            new InterfaceContract(typeof(IEnumExplorerCommandFlat), "a88826f8-186f-4987-aade-ea0cef8fbfe8",
                "Next", "Skip", "Reset", "Clone")
        };

        /// <summary>
        /// Structure contract per marshalled struct: size and the offsets that a wrong alignment
        /// (for example an explicit <c>Pack = 4</c>) would move. Offsets are given for x86 and x64.
        /// </summary>
        private static readonly StructContract[] StructContracts = {
            new StructContract(typeof(POINT), 8, 8,
                Offset("X", 0, 0), Offset("Y", 4, 4)),
            new StructContract(typeof(FORMATETC), 20, 32,
                Offset("cfFormat", 0, 0), Offset("ptd", 4, 8), Offset("dwAspect", 8, 16),
                Offset("lindex", 12, 20), Offset("tymed", 16, 24)),
            new StructContract(typeof(STGMEDIUM), 12, 24,
                Offset("tymed", 0, 0), Offset("unionmember", 4, 8), Offset("pUnkForRelease", 8, 16)),
            new StructContract(typeof(CMINVOKECOMMANDINFO), 40, 64,
                Offset("cbSize", 0, 0), Offset("fMask", 4, 4), Offset("hwnd", 8, 8),
                Offset("lpVerb", 12, 16), Offset("nShow", 24, 40), Offset("hIcon", 32, 48),
                Offset("lpTitle", 36, 56)),
            new StructContract(typeof(CMINVOKECOMMANDINFOEX), 64, 104,
                Offset("cbSize", 0, 0), Offset("lpVerb", 12, 16), Offset("lpTitle", 36, 56),
                Offset("lpVerbW", 40, 64), Offset("lpTitleW", 52, 88), Offset("ptInvoke", 56, 96)),
            new StructContract(typeof(MENUITEMINFO), 48, 80,
                Offset("cbSize", 0, 0), Offset("wID", 16, 16), Offset("hSubMenu", 20, 24),
                Offset("dwItemData", 32, 48), Offset("dwTypeData", 36, 56), Offset("cch", 40, 64),
                Offset("hbmpItem", 44, 72)),
            new StructContract(typeof(PROPERTYKEY), 20, 20,
                Offset("fmtid", 0, 0), Offset("pid", 16, 16)),
            new StructContract(typeof(OSVERSIONINFOEXW), 284, 284,
                Offset("dwOSVersionInfoSize", 0, 0), Offset("dwBuildNumber", 12, 12),
                Offset("wServicePackMajor", 276, 276), Offset("wProductType", 282, 282))
        };

        /// <summary>
        /// All contract violations, or an empty list when every declaration matches Win32.
        /// </summary>
        internal static IReadOnlyList<string> Verify() {
            var violations = new List<string>();
            VerifyInterfaces(violations);
            VerifyStructures(violations);
            return violations;
        }

        private static void VerifyInterfaces(List<string> violations) {
            var covered = new HashSet<Type>();
            foreach (InterfaceContract contract in InterfaceContracts) {
                Type type = contract.Type;
                covered.Add(type);
                string name = type.Name;

                if (type.GetCustomAttribute<ComImportAttribute>() == null) {
                    violations.Add(name + ": missing [ComImport].");
                }

                GuidAttribute guid = type.GetCustomAttribute<GuidAttribute>();
                if (guid == null) {
                    violations.Add(name + ": missing [Guid]; expected " + contract.Iid + ".");
                } else if (!string.Equals(guid.Value, contract.Iid, StringComparison.OrdinalIgnoreCase)) {
                    violations.Add(name + ": [Guid(\"" + guid.Value + "\")] does not match the expected IID " + contract.Iid + ".");
                }

                InterfaceTypeAttribute interfaceType = type.GetCustomAttribute<InterfaceTypeAttribute>();
                if (interfaceType == null || interfaceType.Value != ComInterfaceType.InterfaceIsIUnknown) {
                    violations.Add(name + ": must be [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)].");
                }

                Type[] baseInterfaces = type.GetInterfaces();
                if (baseInterfaces.Length != 0) {
                    violations.Add(name + ": declares base interface(s) " +
                        string.Join(", ", baseInterfaces.Select(t => t.Name)) +
                        "; a flat declaration must inherit nothing, or the base methods shift every slot.");
                }

                //  Metadata tokens are assigned in declaration order, which is the order the CLR
                //  emits the vtable entries in - the only reliable reading order for a COM interface.
                MethodInfo[] methods = type
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .OrderBy(m => m.MetadataToken)
                    .ToArray();

                if (methods.Length != contract.Methods.Length) {
                    violations.Add(name + ": declares " + methods.Length + " method(s), expected " + contract.Methods.Length + ".");
                }

                int common = Math.Min(methods.Length, contract.Methods.Length);
                for (int i = 0; i < common; i++) {
                    int slot = ComObject.FirstMethodSlot + i;
                    if (!string.Equals(methods[i].Name, contract.Methods[i], StringComparison.Ordinal)) {
                        violations.Add(name + ": slot " + slot + " is " + methods[i].Name + ", expected " + contract.Methods[i] + ".");
                    }
                    if (!HasPreserveSig(methods[i])) {
                        violations.Add(name + "." + methods[i].Name + " (slot " + slot + "): missing [PreserveSig].");
                    }
                }
            }

            //  Anything declared later without a contract entry would be unchecked, so it fails here
            //  instead. This is the check that catches "new interop file, forgot the layout test".
            foreach (Type type in typeof(ComLayout).Assembly.GetTypes()) {
                if (type.Namespace != typeof(ComLayout).Namespace || !type.IsInterface) {
                    continue;
                }
                if (type.GetCustomAttribute<ComImportAttribute>() == null || covered.Contains(type)) {
                    continue;
                }
                violations.Add(type.Name + ": [ComImport] interface without a contract entry in ComLayout.");
            }
        }

        /// <summary>
        /// <c>[PreserveSig]</c> is a method *implementation* flag (MethodImplAttributes.PreserveSig),
        /// not a plain custom attribute; check both so a future compiler change cannot make the
        /// audit pass silently.
        /// </summary>
        private static bool HasPreserveSig(MethodInfo method) {
            return (method.GetMethodImplementationFlags() & MethodImplAttributes.PreserveSig) != 0
                || method.GetCustomAttribute<PreserveSigAttribute>() != null;
        }

        private static void VerifyStructures(List<string> violations) {
            bool is64Bit = IntPtr.Size == 8;
            string platform = is64Bit ? "x64" : "x86";

            foreach (StructContract contract in StructContracts) {
                Type type = contract.Type;
                int expectedSize = is64Bit ? contract.SizeX64 : contract.SizeX86;
                int actualSize = Marshal.SizeOf(type);
                if (actualSize != expectedSize) {
                    violations.Add(type.Name + ": size is " + actualSize + " on " + platform + ", expected " + expectedSize + ".");
                }

                StructLayoutAttribute layout = type.StructLayoutAttribute;
                if (layout != null && layout.Pack != 0 && layout.Pack != 8) {
                    //  Pack = 8 is what Sequential means when the attribute names no pack size, so
                    //  only an explicit non default pack is worth reporting. On x86 the natural pack
                    //  happens to be 4, which the offset checks below already accept.
                    violations.Add(type.Name + ": declares Pack = " + layout.Pack + "; the Win32 layout needs the natural alignment.");
                }

                foreach (FieldContract field in contract.Fields) {
                    int expectedOffset = is64Bit ? field.OffsetX64 : field.OffsetX86;
                    int actualOffset;
                    try {
                        actualOffset = Marshal.OffsetOf(type, field.Name).ToInt32();
                    } catch (ArgumentException) {
                        violations.Add(type.Name + ": field " + field.Name + " does not exist.");
                        continue;
                    }
                    if (actualOffset != expectedOffset) {
                        violations.Add(type.Name + "." + field.Name + ": offset is " + actualOffset + " on " +
                            platform + ", expected " + expectedOffset + ".");
                    }
                }
            }
        }
    }
}
