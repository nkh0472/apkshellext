using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ApkShellext2.Shell.Commands;
using ApkShellext2.Shell.Native;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// The modern command served through <c>IExplorerCommand</c>: what it reports for a selection,
    /// what it enumerates, and what an invocation does. The wording and the visibility rules come
    /// from <see cref="CommandCatalog"/>, so these tests compare against the same model the classic
    /// context menu renders instead of restating the text.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    internal sealed class ExplorerCommandTests
    {
        private const int S_OK = 0;
        private const int S_FALSE = 1;
        private const int E_NOTIMPL = unchecked((int)0x80004001);
        private const int E_POINTER = unchecked((int)0x80004003);
        private const int E_FAIL = unchecked((int)0x80004005);

        private const uint StateEnabled = 0;
        private const uint StateDisabled = 1;
        private const uint StateHidden = 2;

        private const uint FlagsDefault = 0;
        private const uint FlagsHasSubCommands = 1;
        private const uint FlagsIsSeparator = 8;

        /// <summary>Two paths are enough to stay away from the file system: a single selection
        /// makes the menu model open the package to name the rename target.</summary>
        private static readonly string[] TwoPaths = { @"C:\fake\first.apk", @"C:\fake\second.apk" };

        private static readonly Type ErrorLogType = ProductApi.Assembly.GetType("ApkShellext2.Diagnostics.ErrorLog", true);
        private static readonly FieldInfo LogPathField = ErrorLogType.GetField("_logPath", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo RotatedPathField = ErrorLogType.GetField("_rotatedPath", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo SizeField = ErrorLogType.GetField("_size", BindingFlags.Static | BindingFlags.NonPublic);

        private string logDirectory;
        private string originalLogPath;
        private string originalRotatedPath;
        private Action<IReadOnlyList<string>> originalPreferences;
        private Action<IReadOnlyList<string>> originalExport;

        [SetUp]
        public void RedirectTheLogAndForgetTheActionHooks()
        {
            originalLogPath = (string)LogPathField.GetValue(null);
            originalRotatedPath = (string)RotatedPathField.GetValue(null);
            logDirectory = Path.Combine(Path.GetTempPath(), "apkshellext-command-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDirectory);
            LogPathField.SetValue(null, Path.Combine(logDirectory, "apkshellext-error.log"));
            RotatedPathField.SetValue(null, Path.Combine(logDirectory, "apkshellext-error.1.log"));
            SizeField.SetValue(null, -1L);
            originalPreferences = CommandActions.PreferencesHandler;
            originalExport = CommandActions.ExportHandler;
        }

        [TearDown]
        public void RestoreTheLogAndTheActionHooks()
        {
            CommandActions.PreferencesHandler = originalPreferences;
            CommandActions.ExportHandler = originalExport;
            LogPathField.SetValue(null, originalLogPath);
            RotatedPathField.SetValue(null, originalRotatedPath);
            SizeField.SetValue(null, -1L);
            try { Directory.Delete(logDirectory, true); } catch { }
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

        // ---- title, state and flags -------------------------------------------------------

        [Test]
        public void TheTitleIsTheSameTextTheClassicMenuShows()
        {
            using (Chinese()) {
                IExplorerCommandNative command = NewCommand(TwoPaths);
                IntPtr text;
                Assert.That(command.GetTitle(IntPtr.Zero, out text), Is.EqualTo(S_OK));
                try {
                    Assert.That(Marshal.PtrToStringUni(text), Is.EqualTo(CommandCatalog.Build(TwoPaths).Root.Text));
                } finally {
                    Marshal.FreeCoTaskMem(text);
                }
            }
        }

        [Test]
        public void ASupportedSelectionEnablesTheCommand()
        {
            Assert.That(StateOf(@"C:\fake\plain.apk"), Is.EqualTo(StateEnabled), "a file the extension handles must show the menu");
        }

        [Test]
        public void AnUnsupportedSelectionHidesTheCommand()
        {
            Assert.That(StateOf(@"C:\fake\notes.txt"), Is.EqualTo(StateHidden), "the verb must not appear on a file we cannot read");
        }

        [Test]
        public void OneSupportedFileIsEnoughToEnableTheCommand()
        {
            Assert.That(StateOf(@"C:\fake\notes.txt", @"C:\fake\plain.apk"), Is.EqualTo(StateEnabled));
        }

        [Test]
        public void NoSelectionHidesTheCommand()
        {
            Assert.That(StateOf(new string[0]), Is.EqualTo(StateHidden));
        }

        [Test]
        public void TheRootCommandDeclaresSubCommands()
        {
            uint flags;
            Assert.That(NewCommand(TwoPaths).GetFlags(out flags), Is.EqualTo(S_OK));
            Assert.That(flags, Is.EqualTo(FlagsHasSubCommands), "the verb is a menu of commands, not a command");
        }

        [Test]
        public void OptionalSlotsAreDeclinedWithoutLeakingPointers()
        {
            IExplorerCommandNative command = NewCommand(TwoPaths);
            IntPtr icon = new IntPtr(1);
            Assert.That(command.GetIcon(IntPtr.Zero, out icon), Is.EqualTo(E_NOTIMPL));
            Assert.That(icon, Is.EqualTo(IntPtr.Zero), "a declined out parameter must not look like an allocated string");

            IntPtr tooltip = new IntPtr(1);
            Assert.That(command.GetToolTip(IntPtr.Zero, out tooltip), Is.EqualTo(E_NOTIMPL));
            Assert.That(tooltip, Is.EqualTo(IntPtr.Zero), "a declined annotation must not look like an allocated string");

            Guid canonical;
            Assert.That(command.GetCanonicalName(out canonical), Is.EqualTo(S_OK));
            Assert.That(canonical, Is.EqualTo(Guid.Empty), "the shell only needs a name when it caches the command");
        }

        [Test]
        public void TheRootCommandDoesNotExecuteAnything()
        {
            Assert.That(NewCommand(TwoPaths).Invoke(IntPtr.Zero, IntPtr.Zero), Is.EqualTo(S_OK),
                        "activation of the root only opens the submenu");
        }

        // ---- enumeration -----------------------------------------------------------------

        [Test]
        public void TheEnumeratorYieldsTheTopLevelMenuItemsInOrder()
        {
            using (Chinese()) {
                string[] expected = CommandCatalog.Build(TwoPaths).Root.Children.Select(item => item.Text).ToArray();

                IExplorerCommandNative command = NewCommand(TwoPaths);
                IntPtr enumerator;
                Assert.That(command.EnumSubCommands(out enumerator), Is.EqualTo(S_OK));
                try {
                    Assert.That(ReadTitles(enumerator, expected.Length), Is.EqualTo(expected));
                } finally {
                    Marshal.Release(enumerator);
                }
            }
        }

        [Test]
        public void TheEnumeratorReportsTheEndOfTheList()
        {
            using (Chinese()) {
                IEnumExplorerCommandNative enumerator = NewEnumerator(CommandCatalog.Build(TwoPaths));
                IntPtr buffer = Marshal.AllocCoTaskMem(16 * IntPtr.Size);
                try {
                    uint fetched;
                    Assert.That(enumerator.Next(16, buffer, out fetched), Is.EqualTo(S_OK));
                    Assert.That(enumerator.Next(1, buffer, out fetched), Is.EqualTo(S_FALSE), "the shell stops on S_FALSE");
                    Assert.That(fetched, Is.EqualTo(0u));
                } finally {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }
        }

        [Test]
        public void SkipAdvancesTheCursor()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                IEnumExplorerCommandNative enumerator = NewEnumerator(model);
                Assert.That(enumerator.Skip(2), Is.EqualTo(S_OK));

                Assert.That(NextTitle(enumerator), Is.EqualTo(model.Root.Children[2].Text));
            }
        }

        [Test]
        public void SkipPastTheEndYieldsNoMoreItems()
        {
            using (Chinese()) {
                IEnumExplorerCommandNative enumerator = NewEnumerator(CommandCatalog.Build(TwoPaths));
                Assert.That(enumerator.Skip(99), Is.EqualTo(S_OK));

                IntPtr buffer = Marshal.AllocCoTaskMem(IntPtr.Size);
                try {
                    uint fetched;
                    Assert.That(enumerator.Next(1, buffer, out fetched), Is.EqualTo(S_FALSE));
                    Assert.That(fetched, Is.EqualTo(0u));
                } finally {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }
        }

        [Test]
        public void ResetRewindsTheCursor()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                IEnumExplorerCommandNative enumerator = NewEnumerator(model);
                enumerator.Skip(3);
                Assert.That(enumerator.Reset(), Is.EqualTo(S_OK));

                Assert.That(NextTitle(enumerator), Is.EqualTo(model.Root.Children[0].Text));
            }
        }

        [Test]
        public void CloneCarriesTheCursorAndThenMovesOnItsOwn()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                IEnumExplorerCommandNative enumerator = NewEnumerator(model);
                enumerator.Skip(1);

                IntPtr clone;
                Assert.That(enumerator.Clone(out clone), Is.EqualTo(S_OK));
                try {
                    IntPtr buffer = Marshal.AllocCoTaskMem(4 * IntPtr.Size);
                    try {
                        uint fetched;
                        Assert.That(enumerator.Next(2, buffer, out fetched), Is.EqualTo(S_OK));
                        for (int i = 0; i < fetched; i++) {
                            Marshal.Release(Marshal.ReadIntPtr(buffer, i * IntPtr.Size));
                        }
                    } finally {
                        Marshal.FreeCoTaskMem(buffer);
                    }

                    //  The original is now three items in; a clone made before that would not be a
                    //  clone. What a clone must guarantee is that moving one does not move the other.
                    Assert.That(ReadTitles(clone, 1)[0], Is.EqualTo(model.Root.Children[1].Text));
                } finally {
                    Marshal.Release(clone);
                }
            }
        }

        [Test]
        public void NextRejectsANullArray()
        {
            using (Chinese()) {
                IEnumExplorerCommandNative enumerator = NewEnumerator(CommandCatalog.Build(TwoPaths));
                uint fetched;
                Assert.That(enumerator.Next(1, IntPtr.Zero, out fetched), Is.EqualTo(E_POINTER));
                Assert.That(fetched, Is.EqualTo(0u));
            }
        }

        [Test]
        public void AnItemWithoutChildrenDeclinesToEnumerate()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem leaf = model.Root.Children.First(item => !item.HasChildren && !item.IsSeparator);
                IntPtr enumerator = new IntPtr(1);
                Assert.That(NewItem(model, leaf).EnumSubCommands(out enumerator), Is.EqualTo(E_NOTIMPL));
                Assert.That(enumerator, Is.EqualTo(IntPtr.Zero));
            }
        }

        // ---- items -----------------------------------------------------------------------

        [Test]
        public void ItemTitlesComeFromTheMenuModel()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem rename = model.Root.Children[0];
                IntPtr text;
                Assert.That(NewItem(model, rename).GetTitle(IntPtr.Zero, out text), Is.EqualTo(S_OK));
                try {
                    Assert.That(Marshal.PtrToStringUni(text), Is.EqualTo(rename.Text));
                } finally {
                    Marshal.FreeCoTaskMem(text);
                }
            }
        }

        [Test]
        public void SeparatorsDeclareThemselves()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem separator = model.Root.Children.First(item => item.IsSeparator);
                uint flags;
                Assert.That(NewItem(model, separator).GetFlags(out flags), Is.EqualTo(S_OK));
                Assert.That(flags, Is.EqualTo(FlagsIsSeparator));
            }
        }

        [Test]
        public void SubMenuParentsDeclareTheirChildren()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem parent = model.Root.Children.First(item => item.HasChildren);
                uint flags;
                Assert.That(NewItem(model, parent).GetFlags(out flags), Is.EqualTo(S_OK));
                Assert.That(flags, Is.EqualTo(FlagsHasSubCommands));
            }
        }

        [Test]
        public void LeavesDeclareNoSpecialFlags()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem leaf = model.Root.Children.First(item => !item.HasChildren && !item.IsSeparator);
                uint flags;
                Assert.That(NewItem(model, leaf).GetFlags(out flags), Is.EqualTo(S_OK));
                Assert.That(flags, Is.EqualTo(FlagsDefault));
            }
        }

        [Test]
        public void ADisabledItemReportsDisabledAndDoesNothing()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem disabled = DisabledItemFor(model);
                IExplorerCommandNative item = NewItem(model, disabled);

                uint state;
                Assert.That(item.GetState(IntPtr.Zero, false, out state), Is.EqualTo(S_OK));
                Assert.That(state, Is.EqualTo(StateDisabled), "a greyed entry, not a hidden one: the shell decides by state");

                bool ran = false;
                CommandActions.PreferencesHandler = paths => ran = true;
                Assert.That(item.Invoke(IntPtr.Zero, IntPtr.Zero), Is.EqualTo(S_OK), "an ignored click is not a failure");
                Assert.That(ran, Is.False);
            }
        }

        [Test]
        public void InvokingThePreferencesItemRunsThePreferencesAction()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem preferences = model.Items.First(item => item.Action == CommandAction.Preferences);
                List<IReadOnlyList<string>> seen = new List<IReadOnlyList<string>>();
                CommandActions.PreferencesHandler = paths => seen.Add(paths);

                Assert.That(NewItem(model, preferences).Invoke(IntPtr.Zero, IntPtr.Zero), Is.EqualTo(S_OK));

                Assert.That(seen, Has.Count.EqualTo(1));
                Assert.That(seen[0], Is.EqualTo(model.SelectedPaths), "the action must see the whole selection");
            }
        }

        [Test]
        public void InvokingTheExportItemRunsTheExportAction()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem export = model.Items.First(item => item.Action == CommandAction.Export);
                int calls = 0;
                CommandActions.ExportHandler = paths => calls++;

                Assert.That(NewItem(model, export).Invoke(IntPtr.Zero, IntPtr.Zero), Is.EqualTo(S_OK));

                Assert.That(calls, Is.EqualTo(1));
            }
        }

        [Test]
        public void InvokingASeparatorRunsNothing()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                CommandItem separator = model.Root.Children.First(item => item.IsSeparator);
                bool ran = false;
                CommandActions.PreferencesHandler = paths => ran = true;

                Assert.That(NewItem(model, separator).Invoke(IntPtr.Zero, IntPtr.Zero), Is.EqualTo(S_OK));

                Assert.That(ran, Is.False);
            }
        }

        [Test]
        public void AFailingActionIsLoggedAndReported()
        {
            using (Chinese()) {
                CommandModel model = CommandCatalog.Build(TwoPaths);
                //  The extraction action opens the package before it guards anything, so a path that
                //  does not exist makes the call throw where no other action would: exactly the case
                //  the item has to absorb instead of letting it cross the COM boundary.
                CommandItem manifest = model.Items.First(item => item.Action == CommandAction.DumpManifest);

                Assert.That(NewItem(model, manifest).Invoke(IntPtr.Zero, IntPtr.Zero), Is.EqualTo(E_FAIL),
                            "an exception must never cross the COM boundary");

                string log = File.ReadAllText(Path.Combine(logDirectory, "apkshellext-error.log"));
                Assert.That(log, Does.Contain("ExplorerCommandItem"), "the log names the class that failed");
                Assert.That(log, Does.Contain("could not be executed"), "the log says what was being run");
            }
        }

        // ---- the selection reader ---------------------------------------------------------

        [Test]
        public void TheSelectionReaderReadsARealShellItemArray()
        {
            string path = MenuFixtures.PlainApkPath();
            IntPtr item = CreateShellItem(path);
            try {
                IntPtr array = CreateShellItemArray(item);
                try {
                    Type selection = NativeType("ApkShellext2.Shell.Native.ShellItemSelection");
                    MethodInfo read = selection.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                    IReadOnlyList<string> paths = (IReadOnlyList<string>)read.Invoke(null, new object[] { array });

                    Assert.That(paths, Has.Count.EqualTo(1));
                    Assert.That(paths[0], Is.EqualTo(path).IgnoreCase, "the file system path is what the menu model needs");
                } finally {
                    Marshal.Release(array);
                }
            } finally {
                Marshal.Release(item);
            }
        }

        [Test]
        public void TheCommandReadsTheSelectionFromAShellItemArray()
        {
            string path = MenuFixtures.PlainApkPath();
            IntPtr item = CreateShellItem(path);
            try {
                IntPtr array = CreateShellItemArray(item);
                try {
                    IExplorerCommandNative command = NewCommand(new string[0]);
                    uint state;
                    Assert.That(command.GetState(array, true, out state), Is.EqualTo(S_OK));
                    Assert.That(state, Is.EqualTo(StateEnabled), "the array the shell passes is the only selection source");
                } finally {
                    Marshal.Release(array);
                }
            } finally {
                Marshal.Release(item);
            }
        }

        // ---- helpers -----------------------------------------------------------------------

        private static uint StateOf(params string[] paths)
        {
            uint state;
            Assert.That(NewCommand(paths).GetState(IntPtr.Zero, false, out state), Is.EqualTo(S_OK));
            return state;
        }

        private static Type NativeType(string name)
        {
            Type type = ProductApi.Assembly.GetType(name, false);
            Assert.That(type, Is.Not.Null, "missing type " + name);
            return type;
        }

        private static IExplorerCommandNative NewCommand(params string[] paths)
        {
            Type type = NativeType("ApkShellext2.Shell.Native.ExplorerCommandInstance");
            ConstructorInfo ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                                                       new[] { typeof(IReadOnlyList<string>) }, null);
            Assert.That(ctor, Is.Not.Null, "the test seam constructor must exist");
            return (IExplorerCommandNative)ctor.Invoke(new object[] { paths });
        }

        private static IExplorerCommandNative NewItem(CommandModel model, CommandItem item)
        {
            Type type = NativeType("ApkShellext2.Shell.Native.ExplorerCommandItemInstance");
            ConstructorInfo ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                                                       new[] { typeof(CommandModel), typeof(CommandItem) }, null);
            Assert.That(ctor, Is.Not.Null);
            return (IExplorerCommandNative)ctor.Invoke(new object[] { model, item });
        }

        private static IEnumExplorerCommandNative NewEnumerator(CommandModel model)
        {
            Type type = NativeType("ApkShellext2.Shell.Native.ExplorerCommandEnumerator");
            ConstructorInfo ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                                                       new[] { typeof(CommandModel), typeof(IEnumerable<CommandItem>) }, null);
            Assert.That(ctor, Is.Not.Null);
            return (IEnumExplorerCommandNative)ctor.Invoke(new object[] { model, model.Root.Children });
        }

        /// <summary>
        /// A model item with the disabled state an unreadable selection produces, made without a
        /// file system: the state mapping is what is under test, not the rename target scan.
        /// </summary>
        private static CommandItem DisabledItemFor(CommandModel model)
        {
            Type itemType = typeof(CommandItem);
            ConstructorInfo ctor = itemType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                                                           new[] { typeof(string) }, null);
            CommandItem disabled = (CommandItem)ctor.Invoke(new object[] { "disabled entry" });
            MethodInfo setter = itemType.GetProperty("Enabled").GetSetMethod(true);
            Assert.That(setter, Is.Not.Null, "the disabled state must stay reachable");
            setter.Invoke(disabled, new object[] { false });
            return disabled;
        }

        private static string NextTitle(IEnumExplorerCommandNative enumerator)
        {
            IntPtr buffer = Marshal.AllocCoTaskMem(IntPtr.Size);
            try {
                uint fetched;
                Assert.That(enumerator.Next(1, buffer, out fetched), Is.EqualTo(S_OK));
                Assert.That(fetched, Is.EqualTo(1u));
                IntPtr item = Marshal.ReadIntPtr(buffer);
                try {
                    return TitleOf(item);
                } finally {
                    Marshal.Release(item);
                }
            } finally {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        /// <summary>Reads up to <paramref name="count"/> titles straight off the COM pointers.</summary>
        private static string[] ReadTitles(IntPtr enumerator, int count)
        {
            IntPtr buffer = Marshal.AllocCoTaskMem(count * IntPtr.Size);
            try {
                NextThunk next = (NextThunk)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(enumerator), 3 * IntPtr.Size), typeof(NextThunk));
                uint fetched;
                Assert.That(next(enumerator, (uint)count, buffer, out fetched), Is.EqualTo(S_OK));
                string[] titles = new string[fetched];
                for (int i = 0; i < fetched; i++) {
                    IntPtr item = Marshal.ReadIntPtr(buffer, i * IntPtr.Size);
                    try {
                        titles[i] = TitleOf(item);
                    } finally {
                        Marshal.Release(item);
                    }
                }
                return titles;
            } finally {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        private static string TitleOf(IntPtr item)
        {
            GetTitleThunk title = (GetTitleThunk)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(item), 3 * IntPtr.Size), typeof(GetTitleThunk));
            IntPtr text;
            Assert.That(title(item, IntPtr.Zero, out text), Is.EqualTo(S_OK));
            try {
                return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(text);
            } finally {
                if (text != IntPtr.Zero) {
                    Marshal.FreeCoTaskMem(text);
                }
            }
        }

        private static IntPtr CreateShellItem(string path)
        {
            Guid iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            IntPtr item;
            Assert.That(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item), Is.EqualTo(S_OK), "shell item for " + path);
            return item;
        }

        private static IntPtr CreateShellItemArray(IntPtr item)
        {
            Guid iid = new Guid("b63ea76d-1f85-456f-a19c-48159efa858b");
            IntPtr array;
            Assert.That(SHCreateShellItemArrayFromShellItem(item, ref iid, out array), Is.EqualTo(S_OK), "item array");
            return array;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetTitleThunk(IntPtr self, IntPtr psiItemArray, out IntPtr title);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NextThunk(IntPtr self, uint celt, IntPtr items, out uint fetched);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHCreateItemFromParsingName", PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IntPtr item);

        [DllImport("shell32.dll", EntryPoint = "SHCreateShellItemArrayFromShellItem", PreserveSig = true)]
        private static extern int SHCreateShellItemArrayFromShellItem(IntPtr item, ref Guid riid, out IntPtr array);
    }
}
