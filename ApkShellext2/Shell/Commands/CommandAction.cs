namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// The operation a menu item performs. The enumeration is deliberately UI-free: the
    /// WinForms adapter and the native <c>IContextMenu</c> handler both translate it into a
    /// click/handler dispatch and never learn which shell technology drew the menu.
    /// </summary>
    public enum CommandAction {
        /// <summary>No operation; separators and submenu parents carry this value.</summary>
        None = 0,
        Rename,
        DumpManifest,
        DumpXml,
        GooglePlay,
        AmazonAppStore,
        ApkMirror,
        AppleAppStore,
        MicrosoftStore,
        Preferences
    }
}
