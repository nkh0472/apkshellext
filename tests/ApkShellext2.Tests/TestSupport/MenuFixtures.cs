using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ApkShellext2.Shell.Commands;
using Microsoft.Win32;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Inputs for the context-menu tests. Every package is synthesised from the checked-in
    /// fixtures (or from AppxFixtures/ZipBuilder) into a temporary directory, so the menu is
    /// asked about real files with real extensions - the same contract the shell uses.
    /// </summary>
    internal static class MenuFixtures
    {
        private const string FixtureDir = "tests/ApkShellext2.Tests/fixtures/";

        private static readonly Lazy<string> s_directory = new Lazy<string>(CreateDirectory);

        public static string Directory
        {
            get { return s_directory.Value; }
        }

        private static string CreateDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "apkshellext-menu-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        private static byte[] Fixture(string name)
        {
            string path = RepoLayout.PathOf(FixtureDir + name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("fixture " + path + " is missing.");
            }

            return File.ReadAllBytes(path);
        }

        private static string Write(string fileName, byte[] data)
        {
            string path = Path.Combine(Directory, fileName);
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, data);
            }

            return path;
        }

        /// <summary>An apk whose manifest and resource table are the real Spectroid bytes.</summary>
        public static byte[] PlainApk()
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "resources.arsc", Data = Fixture("spectroid-resources.arsc") },
                new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = Fixture("spectroid-manifest.bin") });
        }

        /// <summary>An apk whose string table entry count is hostile, so the icon cannot be read.</summary>
        public static byte[] HostileApk()
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "resources.arsc", Data = Fixture("spectroid-arsc-entries.bin") },
                new ZipBuilder.EntrySpec { Name = "AndroidManifest.xml", Data = Fixture("spectroid-manifest.bin") });
        }

        public static byte[] Ipa()
        {
            byte[] plist = BinaryPlistBuilder.Build(new List<byte[]>
            {
                BinaryPlistBuilder.DictionaryObject(1, 2, 3, 4, 5, 6, 7, 8),
                BinaryPlistBuilder.StringObject("CFBundleDisplayName"),
                BinaryPlistBuilder.StringObject("My App"),
                BinaryPlistBuilder.StringObject("CFBundleShortVersionString"),
                BinaryPlistBuilder.StringObject("2.0.1"),
                BinaryPlistBuilder.StringObject("CFBundleVersion"),
                BinaryPlistBuilder.StringObject("42"),
                BinaryPlistBuilder.StringObject("CFBundleIdentifier"),
                BinaryPlistBuilder.StringObject("com.example.app")
            });
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "Payload/MyApp.app/Info.plist", Data = plist });
        }

        /// <summary>An .aab: the bundle manifest sits at base/manifest/androidmanifest.xml.</summary>
        public static byte[] Aab()
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec
                {
                    Name = "base/manifest/androidmanifest.xml",
                    Data = Fixture("spectroid-manifest.bin")
                });
        }

        /// <summary>An .apks: a zip whose base.apk is a complete apk.</summary>
        public static byte[] Apks()
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec { Name = "base.apk", Data = PlainApk() });
        }

        public static string PlainApkPath()
        {
            return Write("plain.apk", PlainApk());
        }

        public static string HostileApkPath()
        {
            return Write("hostile.apk", HostileApk());
        }

        public static string IpaPath()
        {
            return Write("sample.ipa", Ipa());
        }

        public static string AppxPath()
        {
            return Write("sample.appx", AppxFixtures.Appx());
        }

        public static string AppxBundlePath()
        {
            return Write("sample.appxbundle", AppxFixtures.Bundle(AppxFixtures.Appx()));
        }

        public static string AabPath()
        {
            return Write("sample.aab", Aab());
        }

        public static string ApksPath()
        {
            return Write("sample.apks", Apks());
        }

        public static string UnknownPath()
        {
            return Write("sample.txt", Encoding.UTF8.GetBytes("not a package"));
        }
    }

    /// <summary>
    /// Saves the HKCU\Software\ApkShellext2 values a test touches and puts them back on Dispose,
    /// so a run never leaves the machine's language or store preferences changed.
    /// </summary>
    internal sealed class SettingsScope : IDisposable
    {
        private const string KeyPath = @"Software\ApkShellext2";

        private readonly List<string> names = new List<string>();
        private readonly List<object> values = new List<object>();
        private readonly List<RegistryValueKind> kinds = new List<RegistryValueKind>();

        public SettingsScope Set(string name, string value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (!names.Contains(name))
                {
                    names.Add(name);
                    values.Add(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                    kinds.Add(key.GetValue(name, null) == null
                        ? RegistryValueKind.Unknown
                        : key.GetValueKind(name));
                }

                key.SetValue(name, value, RegistryValueKind.String);
            }

            return this;
        }

        public void Dispose()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (values[i] == null)
                    {
                        key.DeleteValue(names[i], false);
                    }
                    else
                    {
                        key.SetValue(names[i], values[i], kinds[i] == RegistryValueKind.Unknown
                            ? RegistryValueKind.String
                            : kinds[i]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A CF_HDROP data object carrying the selected files, which is the only format the
    /// context menu handler reads from IShellExtInit.
    /// </summary>
    [ComVisible(true)]
    public sealed class FileDropDataObject : IDataObject
    {
        private const short CfHDrop = 15;
        private const int GmemMoveable = 0x0002;
        private const int GmemZeroinit = 0x0040;

        private readonly string[] paths;

        public FileDropDataObject(params string[] paths)
        {
            this.paths = paths ?? new string[0];
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            if (format.cfFormat != CfHDrop || (format.tymed & TYMED.TYMED_HGLOBAL) == 0)
            {
                throw new COMException("only CF_HDROP is served", unchecked((int)0x80040064));
            }

            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, pUnkForRelease = null };
            medium.unionmember = CreateDropFiles(paths);
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
        {
            throw new NotSupportedException();
        }

        public int QueryGetData(ref FORMATETC format)
        {
            return format.cfFormat == CfHDrop && (format.tymed & TYMED.TYMED_HGLOBAL) != 0
                ? 0
                : unchecked((int)0x80040064);
        }

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            return unchecked((int)0x80040003);
        }

        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
        {
            throw new NotSupportedException();
        }

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            throw new NotSupportedException();
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            throw new NotSupportedException();
        }

        public void DUnadvise(int connection)
        {
            throw new NotSupportedException();
        }

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            throw new NotSupportedException();
        }

        /// <summary>Builds the DROPFILES block: 20 byte header, fWide, then a double terminated list.</summary>
        public static IntPtr CreateDropFiles(string[] files)
        {
            byte[] list = Encoding.Unicode.GetBytes(string.Join("\0", files) + "\0\0");
            int size = 20 + list.Length;
            IntPtr handle = GlobalAlloc(GmemMoveable | GmemZeroinit, new UIntPtr((uint)size));
            if (handle == IntPtr.Zero)
            {
                throw new OutOfMemoryException("GlobalAlloc failed for the CF_HDROP payload.");
            }

            IntPtr block = GlobalLock(handle);
            try
            {
                Marshal.WriteInt32(block, 0, 20);
                Marshal.WriteInt32(block, 16, 1);
                Marshal.Copy(list, 0, new IntPtr(block.ToInt64() + 20), list.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            return handle;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(int flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr memory);
    }

    /// <summary>
    /// Reads a popup menu back as text lines, and renders the same lines from the command
    /// model, so the native menu can be compared item by item against the model.
    /// </summary>
    internal static class MenuTable
    {
        internal const uint MiimState = 0x00000001;
        internal const uint MiimId = 0x00000002;
        internal const uint MiimSubMenu = 0x00000004;
        internal const uint MiimString = 0x00000040;
        internal const uint MiimBitmap = 0x00000080;
        internal const uint MiimFType = 0x00000100;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MENUITEMINFO
        {
            public uint cbSize;
            public uint fMask;
            public uint fType;
            public uint fState;
            public uint wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public IntPtr dwItemData;
            public IntPtr dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;
        }

        public static string[] Dump(IntPtr hMenu)
        {
            List<string> lines = new List<string>();
            Append(lines, hMenu, 0);
            return lines.ToArray();
        }

        private static void Append(List<string> lines, IntPtr hMenu, int depth)
        {
            int count = GetMenuItemCount(hMenu);
            for (int i = 0; i < count; i++)
            {
                MENUITEMINFO info = new MENUITEMINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(MENUITEMINFO));
                info.fMask = MiimId | MiimString | MiimFType | MiimState | MiimSubMenu | MiimBitmap;
                info.cch = 512;
                info.dwTypeData = Marshal.AllocCoTaskMem((int)info.cch * 2);
                try
                {
                    if (!GetMenuItemInfoW(hMenu, (uint)i, true, ref info))
                    {
                        throw new InvalidOperationException("GetMenuItemInfoW failed for item " + i);
                    }

                    string text = info.dwTypeData == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(info.dwTypeData);
                    lines.Add(Line(depth, i, info.wID, info.fType, info.fState,
                        info.hSubMenu != IntPtr.Zero, info.hbmpItem != IntPtr.Zero, text ?? string.Empty));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(info.dwTypeData);
                }

                if (info.hSubMenu != IntPtr.Zero)
                {
                    Append(lines, info.hSubMenu, depth + 1);
                }
            }
        }

        /// <summary>
        /// Renders the menu the model describes. Separators carry no id and the menu manager
        /// reports them disabled, which is why their state is pinned to 0x3 here.
        /// </summary>
        public static string[] Expected(CommandModel model, int idCmdFirst)
        {
            List<string> lines = new List<string>();
            AppendModel(lines, model.Root, idCmdFirst, 0, 0);
            return lines.ToArray();
        }

        private static void AppendModel(List<string> lines, CommandItem item, int idCmdFirst, int depth, int position)
        {
            lines.Add(Line(depth, position,
                item.IsSeparator ? 0u : (uint)(idCmdFirst + item.Index),
                item.IsSeparator ? 0x800u : 0u,
                item.IsSeparator || !item.Enabled ? 0x3u : 0u,
                item.HasChildren,
                item.Icon != CommandIcon.None,
                item.IsSeparator ? string.Empty : item.Text));

            for (int i = 0; i < item.Children.Count; i++)
            {
                AppendModel(lines, item.Children[i], idCmdFirst, depth + 1, i);
            }
        }

        private static string Line(int depth, int position, uint id, uint fType, uint fState,
            bool submenu, bool bitmap, string text)
        {
            return new string(' ', depth * 2) + "[" + position + "] id=" + id +
                " fType=0x" + fType.ToString("X") + " fState=0x" + fState.ToString("X") +
                " submenu=" + (submenu ? "1" : "0") + " bitmap=" + (bitmap ? "1" : "0") +
                " text=\"" + text + "\"";
        }

        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        public static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMenuItemInfoW(IntPtr hMenu, uint item, bool byPosition, ref MENUITEMINFO info);
    }
}
