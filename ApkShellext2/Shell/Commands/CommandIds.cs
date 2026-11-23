namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// The command id range the menu occupies. Ids handed to the shell are
    /// <c>idCmdFirst + CommandItem.Index</c>: the shell owns the base, the model owns the
    /// offset. <see cref="MaxItems"/> bounds one menu so the handler can reject an
    /// overflowing range instead of silently wrapping the 16-bit offset.
    /// </summary>
    public static class CommandIds {
        /// <summary>Offset of the first command (the root item) relative to <c>idCmdFirst</c>.</summary>
        public const int First = 0;

        /// <summary>Largest number of items one menu may contain.</summary>
        public const int MaxItems = 0x100;

        /// <summary>Offset of the last command relative to <c>idCmdFirst</c>.</summary>
        public const int Last = First + MaxItems - 1;
    }
}
