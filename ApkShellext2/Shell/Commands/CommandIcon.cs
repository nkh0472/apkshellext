namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// Identifies the image a menu item shows. The token is resolved to a bitmap (or an
    /// <c>HBITMAP</c>) by the UI layer, so the model itself carries no GDI resources.
    /// </summary>
    public enum CommandIcon {
        None = 0,
        Rename,
        DumpOutput,
        Logo,
        GooglePlay,
        AmazonStore,
        ApkMirror,
        AppleStore,
        MicrosoftStore,
        /// <summary>The root item's composed package icons (up to three, stacked).</summary>
        PackageThumbnails
    }
}
