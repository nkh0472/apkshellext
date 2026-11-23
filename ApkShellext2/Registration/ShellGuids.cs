using System;
using System.Collections.Generic;

namespace ApkShellext2.Registration {
    /// <summary>
    /// The one COM class a registration entry can point at.
    /// </summary>
    internal enum ShellHandlerKind {
        /// <summary>Context menu handler (<c>ShellEx\ContextMenuHandlers</c>) - classic verbs.</summary>
        ContextMenu,
        /// <summary>Icon handler (<c>ShellEx\IconHandler</c>).</summary>
        IconHandler,
        /// <summary>Info tip handler (<c>ShellEx\{00021500-...}</c>).</summary>
        InfoTip,
        /// <summary>Thumbnail provider (<c>ShellEx\{e357fccd-...}</c>), currently .apk only.</summary>
        Thumbnail
    }

    /// <summary>
    /// A registered COM server: the CLSID the shell activates and the ProgID/class name the shell
    /// stores under <c>CLSID\{guid}</c> and its version subkey.
    /// </summary>
    internal sealed class ShellHandler {
        internal ShellHandler(ShellHandlerKind kind, string clsid, string progId) {
            Kind = kind;
            Clsid = clsid;
            ProgId = progId;
        }

        internal ShellHandlerKind Kind { get; }

        /// <summary>CLSID in braces, exactly as the registry stores it and as install*.bat hard codes it.</summary>
        internal string Clsid { get; }

        /// <summary>
        /// ProgID, which is also the managed type full name that <c>mscoree.dll</c> has to activate
        /// (<c>InprocServer32\Class</c>). In the current registration these two strings are equal -
        /// regasm derived the ProgID from the class name because the handlers declare no
        /// <c>[ProgId]</c> attribute.
        /// </summary>
        internal string ProgId { get; }

        /// <summary>Managed type the CLR activates; same value as <see cref="ProgId"/>.</summary>
        internal string ClassName {
            get { return ProgId; }
        }
    }

    /// <summary>
    /// Every registry constant the shell registration needs, in one place: the four CLSIDs, the four
    /// ProgIDs, the eight file extensions, the ShellEx subkey and value names, and the two guard
    /// lists (<c>Approved</c> / <c>Blocked</c>).
    ///
    /// These values are duplicates of what the command line installers still hard code -
    /// <c>Resources\install.bat</c>, <c>Resources\install_user.bat</c>,
    /// <c>Resources\uninstall.bat</c> and <c>Resources\uninstall_user.bat</c> keep their own copies.
    /// When changing a value here, change it there too, and
    /// update the CLSID/GUID mapping in <c>ApkShellext2\Shell\*.cs</c> (the <c>[Guid]</c> attributes
    /// are the source of truth: the registrar only repeats them).
    /// </summary>
    internal static class ShellGuids {
        //  ------------------------------------------------------------------------------------
        //  Handler CLSIDs. Each one equals the [Guid] attribute of the handler class:
        //    Shell\ApkContextMenu.cs:27      dcb629fc-f86f-456f-8e24-98b9b2643a9b
        //    Shell\ApkIconHandler.cs:23      1F869CEE-4FDA-35D9-896F-43975A87D1F6
        //    Shell\ApkInfoTipHandler.cs:17   946435a5-fe96-416d-99db-e94ee9fb46c8
        //    Shell\ApkThumbnailHandler.cs:18 d747c5a7-2f66-4b7d-8301-8531838e4ed3
        //  These CLSIDs survive handler rewrites on purpose: an update of the installed extension
        //  must never force a re-registration of the file associations.
        //  ------------------------------------------------------------------------------------
        internal const string ContextMenuClsid = "{DCB629FC-F86F-456F-8E24-98B9B2643A9B}";
        internal const string IconHandlerClsid = "{1F869CEE-4FDA-35D9-896F-43975A87D1F6}";
        internal const string InfoTipClsid = "{946435A5-FE96-416D-99DB-E94EE9FB46C8}";
        internal const string ThumbnailClsid = "{D747C5A7-2F66-4B7D-8301-8531838E4ED3}";

        /// <summary>
        /// CLSID used for the Windows 11 <c>ExplorerCommandHandler</c> registration. It is a
        /// placeholder: on a Windows 11 host the shell tries to activate it, finds no registration,
        /// and keeps using the classic verbs.
        /// </summary>
        internal const string ExplorerCommandClsid = "{7C2F1A44-3D51-4E8B-9C0A-5B6D2E3F4A11}";

        /// <summary>
        /// CLSID of the native menu handler (<c>Shell\Native\ContextMenuHandler</c>). It is not part of
        /// the file associations: every extension still activates <see cref="ContextMenuClsid"/>, and
        /// this identifier exists so a harness can activate the native class on its own and compare its
        /// menu with the one the registered handler produces. Nothing in the registrar writes it.
        /// </summary>
        internal const string NativeMenuClsid = "{A8D3F2E1-6C4B-4D9A-B7F5-2E1D0C9B8A76}";

        internal const string ContextMenuProgId = "ApkShellext2.ApkContextMenu";
        internal const string IconHandlerProgId = "ApkShellext2.ApkIconHandler";
        internal const string InfoTipProgId = "ApkShellext2.ApkInfoTipHandler";
        internal const string ThumbnailProgId = "ApkShellext2.ApkThumbnailHandler";

        /// <summary>Registry key names and shell defined value names.</summary>
        internal const string ClsidKey = "CLSID";
        internal const string ProgIdKey = "ProgId";
        internal const string InprocServer32Key = "InprocServer32";
        internal const string ImplementedCategoriesKey = "Implemented Categories";
        internal const string ShellExKey = "ShellEx";
        internal const string ContextMenuHandlersKey = "ContextMenuHandlers";
        internal const string DefaultIconKey = "DefaultIcon";
        internal const string ExplorerCommandHandlerValue = "ExplorerCommandHandler";

        /// <summary>ShellEx value name for the icon handler.</summary>
        internal const string IconHandlerValueName = "IconHandler";

        /// <summary>ShellEx value name for the info tip handler (<c>IID_IQueryInfo</c>).</summary>
        internal const string InfoTipValueName = "{00021500-0000-0000-C000-000000000046}";

        /// <summary>ShellEx value name for the thumbnail provider (<c>IID_IThumbnailProvider</c>).</summary>
        internal const string ThumbnailValueName = "{E357FCCD-A995-4576-B01F-234630154E96}";

        /// <summary>Subkey of <c>ContextMenuHandlers</c> that names this extension's menu handler.</summary>
        internal const string ContextMenuHandlerName = "ApkContextMenu";

        /// <summary>The .NET managed category regasm writes; kept so the registered class looks like regasm's product.</summary>
        internal const string ManagedCategoryClsid = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}";

        /// <summary>In-proc server module: the CLR shim, not our DLL.</summary>
        internal const string InprocServerModule = "mscoree.dll";

        /// <summary>
        /// Threading model the four handlers have always been registered with. The shell creates the
        /// handler on its UI thread; the managed CCW handles the apartment transition, so this must
        /// not become "Apartment" without measuring the shell load path again.
        /// </summary>
        internal const string ThreadingModel = "Both";

        /// <summary>CLR version <c>mscoree.dll</c> loads: net48 assemblies are always v4.0.30319.</summary>
        internal const string RuntimeVersion = "v4.0.30319";

        /// <summary>Value names inside <c>InprocServer32</c> (and inside its version subkey, minus ThreadingModel).</summary>
        internal const string ValueThreadingModel = "ThreadingModel";
        internal const string ValueClass = "Class";
        internal const string ValueAssembly = "Assembly";
        internal const string ValueRuntimeVersion = "RuntimeVersion";
        internal const string ValueCodeBase = "CodeBase";

        /// <summary>Value written to <c>{class}\DefaultIcon</c>; the extension still decides the real icon.</summary>
        internal const string DefaultIconValue = "%1";

        /// <summary>
        /// Value name under which a pre-existing DefaultIcon is remembered. The spelling is historical -
        /// existing installations already carry it, so renaming the value would orphan their backup.
        /// Never overwrite an existing backup: it holds the icon of whatever application owned the
        /// extension before this shell extension was installed.
        /// </summary>
        internal const string DefaultIconBackupValueName = "SharpShell_Backup_DefaultIcon";

        /// <summary>Under <c>Software\Microsoft\Windows\CurrentVersion\Shell Extensions</c>.</summary>
        internal const string ApprovedKeyName = "Approved";
        internal const string BlockedKeyName = "Blocked";

        /// <summary>
        /// File extensions the extension advertises, in the order the installers list them.
        /// </summary>
        internal static readonly string[] Extensions = {
            ".apk", ".ipa", ".aab", ".appx", ".appxbundle", ".apks", ".apkm", ".xapk"
        };

        /// <summary>The four handlers, in the order regasm and the installers register them.</summary>
        internal static readonly ShellHandler[] Handlers = {
            new ShellHandler(ShellHandlerKind.ContextMenu, ContextMenuClsid, ContextMenuProgId),
            new ShellHandler(ShellHandlerKind.IconHandler, IconHandlerClsid, IconHandlerProgId),
            new ShellHandler(ShellHandlerKind.InfoTip, InfoTipClsid, InfoTipProgId),
            new ShellHandler(ShellHandlerKind.Thumbnail, ThumbnailClsid, ThumbnailProgId)
        };

        /// <summary>
        /// Which handler is attached to which extension. Mirrors the association loop in
        /// <c>Resources\install_user.bat</c> [3/5] and the <c>COMServerAssociation</c> attributes on
        /// the handler classes:
        /// <list type="bullet">
        /// <item>icon handler: all eight extensions;</item>
        /// <item>info tip and context menu: everything except <c>.aab</c> (an .aab is a bundle, not
        /// an installable package, so it gets no verbs and no tooltip);</item>
        /// <item>thumbnail: <c>.apk</c> only.</item>
        /// </list>
        /// </summary>
        internal static bool AppliesTo(ShellHandlerKind kind, string extension) {
            if (string.IsNullOrEmpty(extension)) {
                return false;
            }
            bool isAab = string.Equals(extension, ".aab", StringComparison.OrdinalIgnoreCase);
            switch (kind) {
                case ShellHandlerKind.IconHandler:
                    return true;
                case ShellHandlerKind.InfoTip:
                case ShellHandlerKind.ContextMenu:
                    return !isAab;
                case ShellHandlerKind.Thumbnail:
                    return string.Equals(extension, ".apk", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        /// <summary>
        /// The class name an extension without any owner gets: <c>.apk</c> becomes <c>apk.1</c> with a
        /// display name of <c>apk Application</c>, exactly like the installer's association loop.
        /// </summary>
        internal static string DefaultClassForExtension(string extension) {
            if (string.IsNullOrEmpty(extension) || extension[0] != '.') {
                throw new ArgumentException("An extension must start with a dot, got '" + extension + "'.", nameof(extension));
            }
            return extension.Substring(1) + ".1";
        }

        /// <summary>Display name written on a class this registrar had to invent (<c>apk Application</c>).</summary>
        internal static string DefaultClassDisplayName(string extension) {
            if (string.IsNullOrEmpty(extension) || extension[0] != '.') {
                throw new ArgumentException("An extension must start with a dot, got '" + extension + "'.", nameof(extension));
            }
            return extension.Substring(1) + " Application";
        }

        /// <summary>
        /// Locates a handler by kind. Returns null for a kind that is not registered (defensive: the
        /// caller then skips that slot instead of writing a null value).
        /// </summary>
        internal static ShellHandler Find(ShellHandlerKind kind) {
            foreach (ShellHandler handler in Handlers) {
                if (handler.Kind == kind) {
                    return handler;
                }
            }
            return null;
        }

        /// <summary>All four CLSIDs, for the Blocked list sweep and for tests.</summary>
        internal static IReadOnlyList<string> AllClsids() {
            return new[] { ContextMenuClsid, IconHandlerClsid, InfoTipClsid, ThumbnailClsid };
        }

        /// <summary>All four ProgIDs, for unregistration and for tests.</summary>
        internal static IReadOnlyList<string> AllProgIds() {
            return new[] { ContextMenuProgId, IconHandlerProgId, InfoTipProgId, ThumbnailProgId };
        }
    }
}
