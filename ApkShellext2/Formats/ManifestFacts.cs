namespace ApkQuickReader
{
    /// <summary>
    /// The platform facts a manifest declares about the build: the two sdk levels and
    /// the debuggable flag. The values are the text the manifest wrote, so a codename
    /// minSdkVersion ("S") survives instead of collapsing into a number, and an absent
    /// attribute stays distinguishable from one that says false.
    /// </summary>
    internal sealed class ManifestFacts
    {
        internal string MinSdk = "";
        internal string TargetSdk = "";
        internal bool? Debuggable;
    }
}
