namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// Canonical verb strings. <c>GetCommandString(GCS_VERBW)</c> returns the verb of the
    /// item whose id the shell asks about, and <c>InvokeCommand</c> accepts the same strings,
    /// so a caller can drive the menu either by id or by verb. Verbs are stable across
    /// processes and builds; they never appear in the menu text.
    /// </summary>
    public static class CommandVerb {
        public const string Rename = "ApkShellext2.Rename";
        public const string ExportCsv = "ApkShellext2.ExportCsv";
        public const string DumpManifest = "ApkShellext2.DumpManifest";
        public const string DumpXml = "ApkShellext2.DumpXml";
        public const string GooglePlay = "ApkShellext2.GoToGooglePlay";
        public const string AmazonAppStore = "ApkShellext2.GoToAmazonAppStore";
        public const string ApkMirror = "ApkShellext2.GoToApkMirror";
        public const string AppleAppStore = "ApkShellext2.GoToAppleAppStore";
        public const string MicrosoftStore = "ApkShellext2.GoToMicrosoftStore";
        public const string Preferences = "ApkShellext2.Preferences";
        public const string CopyFilePath = "ApkShellext2.CopyFilePath";
        public const string CopyAppName = "ApkShellext2.CopyAppName";
        public const string CopyPackage = "ApkShellext2.CopyPackageName";
        public const string CopyVersion = "ApkShellext2.CopyVersion";
        public const string OpenVirusTotal = "ApkShellext2.OpenVirusTotal";

        /// <summary>
        /// The verb for an action, or <see cref="string.Empty"/> when the action is not a
        /// leaf command (separators, submenu parents).
        /// </summary>
        public static string For(CommandAction action) {
            switch (action) {
                case CommandAction.Rename: return Rename;
                case CommandAction.Export: return ExportCsv;
                case CommandAction.DumpManifest: return DumpManifest;
                case CommandAction.DumpXml: return DumpXml;
                case CommandAction.GooglePlay: return GooglePlay;
                case CommandAction.AmazonAppStore: return AmazonAppStore;
                case CommandAction.ApkMirror: return ApkMirror;
                case CommandAction.AppleAppStore: return AppleAppStore;
                case CommandAction.MicrosoftStore: return MicrosoftStore;
                case CommandAction.Preferences: return Preferences;
                case CommandAction.CopyFilePath: return CopyFilePath;
                case CommandAction.CopyAppName: return CopyAppName;
                case CommandAction.CopyPackage: return CopyPackage;
                case CommandAction.CopyVersion: return CopyVersion;
                case CommandAction.OpenVirusTotal: return OpenVirusTotal;
                default: return string.Empty;
            }
        }
    }
}
