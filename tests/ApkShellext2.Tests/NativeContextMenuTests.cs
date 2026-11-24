using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ApkShellext2.Shell.Commands;
using ApkShellext2.Shell.Native;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Drives the native handler the way the shell does: IShellExtInit with a CF_HDROP data
    /// object, then IContextMenu against a real popup menu. Every menu is compared item by
    /// item with the table the command model describes, so the two menu implementations
    /// cannot drift apart.
    /// </summary>
    [TestFixture]
    public class NativeContextMenuTests
    {
        private const int IdCmdFirst = 100;
        private const int IdCmdLast = IdCmdFirst + 0x7FFF;
        private const uint GcsVerbW = 4;
        private const uint CmicUnicode = 0x4000;
        private const uint MiimSubMenu = 0x00000004;
        private const int E_FAIL = unchecked((int)0x80004005);

        [TearDown]
        public void RestoreThreadCulture()
        {
            ApkShellext2.Utility.Localize();
        }

        private static SettingsScope Chinese()
        {
            return new SettingsScope()
                .Set("Language", "zh-CN")
                .Set("CheckForUpdates", "False")
                .Set("RenamePattern", "%AppName%_%Version%")
                .Set("ShowGooglePlay", "True")
                .Set("ShowAmazonStore", "True")
                .Set("ShowApkMirror", "False")
                .Set("ShowAppStoreWhenMultiSelected", "True")
                .Set("ShowNewVersion", "False")
                .Set("ShowMenuIcon", "True")
                .Set("ShowAppleStore", "True")
                .Set("ShowMSStore", "True");
        }

        private static string SamplePath(string kind)
        {
            switch (kind)
            {
                case "plain": return MenuFixtures.PlainApkPath();
                case "hostile": return MenuFixtures.HostileApkPath();
                case "ipa": return MenuFixtures.IpaPath();
                case "appx": return MenuFixtures.AppxPath();
                case "appxbundle": return MenuFixtures.AppxBundlePath();
                case "aab": return MenuFixtures.AabPath();
                case "apks": return MenuFixtures.ApksPath();
                case "unknown": return MenuFixtures.UnknownPath();
                default: throw new ArgumentOutOfRangeException("kind", kind, "unknown sample");
            }
        }

        private static ContextMenuHandler CreateHandler(params string[] paths)
        {
            ContextMenuHandler handler = new ContextMenuHandler();
            IntPtr unknown = IntPtr.Zero;
            if (paths.Length > 0)
            {
                FileDropDataObject drop = new FileDropDataObject(paths);
                unknown = Marshal.GetComInterfaceForObject(drop, typeof(System.Runtime.InteropServices.ComTypes.IDataObject));
            }

            try
            {
                Assert.That(handler.Initialize(IntPtr.Zero, unknown, IntPtr.Zero), Is.EqualTo(0),
                    "IShellExtInit.Initialize must accept the selection");
            }
            finally
            {
                if (unknown != IntPtr.Zero)
                {
                    Marshal.Release(unknown);
                }
            }

            return handler;
        }

        /// <summary>The shell always asks QueryContextMenu before it invokes or asks for a verb.</summary>
        private static void Prepare(ContextMenuHandler handler)
        {
            IntPtr menu;
            int hr;
            Query(handler, out hr, out menu);
            Assert.That(hr & 0x80000000, Is.EqualTo(0), "the menu must build before a command is used");
        }

        private static string[] Query(ContextMenuHandler handler, out int hr, out IntPtr menu,
            int idCmdFirst = IdCmdFirst, uint flags = 0)
        {
            menu = MenuTable.CreatePopupMenu();
            try
            {
                hr = handler.QueryContextMenu(menu, 0, idCmdFirst, idCmdFirst + 0x7FFF, flags);
                return MenuTable.Dump(menu);
            }
            finally
            {
                MenuTable.DestroyMenu(menu);
            }
        }

        [TestCase("plain")]
        [TestCase("hostile")]
        [TestCase("ipa")]
        [TestCase("appx")]
        [TestCase("appxbundle")]
        [TestCase("aab")]
        [TestCase("apks")]
        public void QueryContextMenu_MatchesTheCommandModel(string kind)
        {
            using (Chinese())
            {
                string path = SamplePath(kind);
                CommandModel model = CommandCatalog.Build(new[] { path });
                ContextMenuHandler handler = CreateHandler(path);

                IntPtr menu;
                int hr;
                string[] table = Query(handler, out hr, out menu);

                Assert.That(hr & 0xFFFF, Is.EqualTo(model.Count), "the shell gets the item count back");
                Assert.That(table, Is.EqualTo(MenuTable.Expected(model, IdCmdFirst)));
            }
        }

        [Test]
        public void QueryContextMenu_RejectsAnUnsupportedExtensionWithoutItems()
        {
            using (Chinese())
            {
                ContextMenuHandler handler = CreateHandler(MenuFixtures.UnknownPath());

                IntPtr menu;
                int hr;
                string[] table = Query(handler, out hr, out menu);

                Assert.That(hr, Is.EqualTo(E_FAIL));
                Assert.That(table, Is.Empty);
            }
        }

        [Test]
        public void AabAndUnsupportedDifferInItemCount()
        {
            using (Chinese())
            {
                ContextMenuHandler aab = CreateHandler(MenuFixtures.AabPath());
                IntPtr menu;
                int aabHr;
                string[] aabTable = Query(aab, out aabHr, out menu);

                ContextMenuHandler unknown = CreateHandler(MenuFixtures.UnknownPath());
                int unknownHr;
                string[] unknownTable = Query(unknown, out unknownHr, out menu);

                Assert.That(aabTable.Length, Is.EqualTo(11));
                Assert.That(aabHr & 0xFFFF, Is.EqualTo(11));
                Assert.That(unknownTable.Length, Is.EqualTo(0));
                Assert.That(unknownHr, Is.EqualTo(E_FAIL));
            }
        }

        [Test]
        public void EmptySelection_FailsWithoutTouchingTheMenu()
        {
            ContextMenuHandler handler = CreateHandler();

            IntPtr menu;
            int hr;
            string[] table = Query(handler, out hr, out menu);

            Assert.That(hr, Is.EqualTo(E_FAIL));
            Assert.That(table, Is.Empty);
        }

        [Test]
        public void DefaultOnly_ReturnsOkWithAnEmptyMenu()
        {
            ContextMenuHandler handler = CreateHandler(MenuFixtures.PlainApkPath());

            IntPtr menu;
            int hr;
            string[] table = Query(handler, out hr, out menu, IdCmdFirst, 1);

            Assert.That(hr, Is.EqualTo(0));
            Assert.That(table, Is.Empty);
        }

        [Test]
        public void CommandIdsFollowTheIdCmdFirstTheShellPasses()
        {
            using (Chinese())
            {
                ContextMenuHandler handler = CreateHandler(MenuFixtures.PlainApkPath());
                CommandModel model = CommandCatalog.Build(new[] { MenuFixtures.PlainApkPath() });

                IntPtr menu;
                int hr;
                string[] table = Query(handler, out hr, out menu, 500);

                Assert.That(table, Is.EqualTo(MenuTable.Expected(model, 500)));
                Assert.That(table[0], Does.Contain("id=500"));
            }
        }

        [Test]
        public void GetCommandString_ReturnsTheCanonicalVerbOfEachCommand()
        {
            using (Chinese())
            {
                ContextMenuHandler handler = CreateHandler(MenuFixtures.PlainApkPath());
                Prepare(handler);

                Assert.That(ReadCommandString(handler, IdCmdFirst + 1), Is.EqualTo(CommandVerb.Rename));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 2), Is.EqualTo(CommandVerb.ExportCsv));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 4), Is.EqualTo(CommandVerb.CopyFilePath));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 5), Is.EqualTo(CommandVerb.CopyAppName));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 6), Is.EqualTo(CommandVerb.CopyPackage));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 7), Is.EqualTo(CommandVerb.CopyVersion));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 9), Is.EqualTo(CommandVerb.DumpManifest));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 10), Is.EqualTo(CommandVerb.DumpXml));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 13), Is.EqualTo(CommandVerb.GooglePlay));
                Assert.That(ReadCommandString(handler, IdCmdFirst + 16), Is.EqualTo(CommandVerb.Preferences));
            }
        }

        [Test]
        public void GetCommandString_RefusesParentsSeparatorsAndForeignIds()
        {
            using (Chinese())
            {
                ContextMenuHandler handler = CreateHandler(MenuFixtures.PlainApkPath());
                Prepare(handler);

                Assert.That(RequestCommandString(handler, IdCmdFirst, GcsVerbW, 260), Is.EqualTo(E_FAIL));
                Assert.That(RequestCommandString(handler, IdCmdFirst + 3, GcsVerbW, 260), Is.EqualTo(E_FAIL),
                    "the copy submenu parent carries no verb");
                Assert.That(RequestCommandString(handler, IdCmdFirst + 11, GcsVerbW, 260), Is.EqualTo(E_FAIL));
                Assert.That(RequestCommandString(handler, IdCmdFirst + 200, GcsVerbW, 260), Is.EqualTo(E_FAIL));
                Assert.That(RequestCommandString(handler, IdCmdFirst + 1, 0, 260), Is.EqualTo(E_FAIL),
                    "an ANSI request is refused instead of filled with UTF-16");
                Assert.That(RequestCommandString(handler, IdCmdFirst + 1, GcsVerbW, 4), Is.EqualTo(E_FAIL),
                    "a buffer that cannot hold the verb and its terminator is refused");
            }
        }

        [Test]
        public void InvokeCommand_AcceptsTheIdOffsetAndRejectsWhatIsNotOnTheMenu()
        {
            using (Chinese())
            {
                ContextMenuHandler handler = CreateHandler(MenuFixtures.PlainApkPath());
                Prepare(handler);

                Assert.That(InvokeIndex(handler, 11), Is.EqualTo(0), "the separator is a no-op success");
                Assert.That(InvokeIndex(handler, 3), Is.EqualTo(0), "the copy submenu parent is a no-op success");
                Assert.That(InvokeIndex(handler, 200), Is.EqualTo(E_FAIL));
                Assert.That(InvokeIndex(handler, 0), Is.EqualTo(E_FAIL),
                    "a zero verb pointer means 'the default item', and this menu has no default item");
            }
        }

        [Test]
        public void InvokeCommand_ResolvesAVerbThroughTheModel()
        {
            string path = MenuFixtures.PlainApkPath();
            string dumped = path + ".androidmanifest.xml";
            if (File.Exists(dumped))
            {
                File.Delete(dumped);
            }

            using (Chinese())
            {
                ContextMenuHandler handler = CreateHandler(path);
                Prepare(handler);

                Assert.That(InvokeVerb(handler, CommandVerb.DumpManifest), Is.EqualTo(0));
                Assert.That(dumped, Does.Exist, "the verb runs the same action the WinForms adapter runs");
                Assert.That(InvokeVerb(handler, "ApkShellext2.NotACommand"), Is.EqualTo(E_FAIL));
            }
        }

        [Test]
        public void HandleMenuMsg2_ReportsZeroAndDoesNotThrow()
        {
            ContextMenuHandler handler = CreateHandler(MenuFixtures.PlainApkPath());
            IntPtr result = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(result, new IntPtr(1234));

                Assert.That(handler.HandleMenuMsg(0x0117, IntPtr.Zero, IntPtr.Zero), Is.EqualTo(0));
                Assert.That(handler.HandleMenuMsg2(0x0117, IntPtr.Zero, IntPtr.Zero, result), Is.EqualTo(0));
                Assert.That(Marshal.ReadIntPtr(result), Is.EqualTo(IntPtr.Zero));
            }
            finally
            {
                Marshal.FreeHGlobal(result);
            }
        }

        [Test]
        public void ReinitializingReplacesTheSelection()
        {
            using (Chinese())
            {
                ContextMenuHandler handler = CreateHandler(MenuFixtures.PlainApkPath());

                IntPtr menu;
                int firstHr;
                string[] first = Query(handler, out firstHr, out menu);

                FileDropDataObject drop = new FileDropDataObject(MenuFixtures.IpaPath());
                IntPtr unknown = Marshal.GetComInterfaceForObject(
                    drop, typeof(System.Runtime.InteropServices.ComTypes.IDataObject));
                try
                {
                    Assert.That(handler.Initialize(IntPtr.Zero, unknown, IntPtr.Zero), Is.EqualTo(0));
                }
                finally
                {
                    Marshal.Release(unknown);
                }

                int secondHr;
                string[] second = Query(handler, out secondHr, out menu);

                Assert.That(first.Length, Is.EqualTo(17));
                Assert.That(second.Length, Is.EqualTo(12));
                Assert.That(second.Where(l => l.Contains("Appstore")).Count(), Is.EqualTo(1));
            }
        }

        private static string ReadCommandString(ContextMenuHandler handler, int id)
        {
            IntPtr buffer = Marshal.AllocCoTaskMem(260 * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0, 0);
                int hr = handler.GetCommandString(id, GcsVerbW, 0, buffer, 260);
                Assert.That(hr, Is.EqualTo(0), "GetCommandString must succeed for id " + id);
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        private static int RequestCommandString(ContextMenuHandler handler, int id, uint flags, int capacity)
        {
            IntPtr buffer = Marshal.AllocCoTaskMem((capacity + 1) * 2);
            try
            {
                return handler.GetCommandString(id, flags, 0, buffer, capacity);
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        private static int InvokeIndex(ContextMenuHandler handler, int offset)
        {
            CMINVOKECOMMANDINFO info = new CMINVOKECOMMANDINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(CMINVOKECOMMANDINFO)),
                lpVerb = new IntPtr(offset),
                nShow = 1
            };
            IntPtr block = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CMINVOKECOMMANDINFO)));
            try
            {
                Marshal.StructureToPtr(info, block, false);
                return handler.InvokeCommand(block);
            }
            finally
            {
                Marshal.FreeHGlobal(block);
            }
        }

        private static int InvokeVerb(ContextMenuHandler handler, string verb)
        {
            IntPtr text = Marshal.StringToHGlobalUni(verb);
            CMINVOKECOMMANDINFOEX info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX)),
                fMask = CmicUnicode,
                lpVerb = text,
                lpVerbW = text,
                nShow = 1
            };
            IntPtr block = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX)));
            try
            {
                Marshal.StructureToPtr(info, block, false);
                return handler.InvokeCommand(block);
            }
            finally
            {
                Marshal.FreeHGlobal(block);
                Marshal.FreeHGlobal(text);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CMINVOKECOMMANDINFO
        {
            public uint cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public IntPtr lpParameters;
            public IntPtr lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr lpTitle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CMINVOKECOMMANDINFOEX
        {
            public uint cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public IntPtr lpParameters;
            public IntPtr lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr lpTitle;
            public IntPtr lpVerbW;
            public IntPtr lpParametersW;
            public IntPtr lpDirectoryW;
            public IntPtr lpTitleW;
            public int ptX;
            public int ptY;
        }
    }
}
