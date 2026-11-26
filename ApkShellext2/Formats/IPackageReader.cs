using System;
using System.Drawing;
using System.Globalization;

namespace ApkShellext2 {
    /// <summary>
    /// Contract every package reader exposes to the shell extension layer:
    /// identity/version metadata, the platform facts the manifest declares,
    /// the icon and per-file flags.
    /// AppPackageReader is the base implementation; PackageReaderFactory is
    /// the only place that decides which concrete reader is used.
    /// </summary>
    public interface IPackageReader : IDisposable {
        string FileName { get; }
        // App name
        string AppName { get; }
        // Package name
        string PackageName { get; }
        // App version
        string Version { get; }
        // Sub version
        string Revision { get; }
        CultureInfo Culture { get; set; }
        // Publisher
        string Publisher { get; }
        Bitmap Icon { get; }
        string AppID { get; }
        AppPackageReader.AppType Type { get; }
        // android:minSdkVersion as the manifest writes it: "21", or a codename such
        // as "S". Empty when the attribute is absent or the container has no android
        // manifest. Kept as text on purpose - a codename carries information that an
        // integer conversion would throw away.
        string MinSdk { get; }
        // android:targetSdkVersion, same rules as MinSdk
        string TargetSdk { get; }
        // android:debuggable. Null when the attribute is not written at all, which is
        // not the same thing as an explicit false.
        bool? Debuggable { get; }
        // Packer/hardening tool named by the APK signing block; empty when the
        // container has no signing block or it names none of the known tools
        string Packer { get; }
        // The archive path the manifest's icon reference resolves to, e.g.
        // "res/mipmap-xxhdpi/ic_launcher.png": what the container says, not what it
        // holds. Empty when the container has no icon reference or the reference
        // cannot be resolved. Read-only, and the path is not guaranteed to exist in
        // the archive (a split bundle resolves it against the base apk only).
        string IconEntryPath { get; }

        // use for other information, or file type specific info
        void setFlag(string flag, object value);
        object getFlag(string flag);
        Bitmap getIcon(Size size);
    }
}
