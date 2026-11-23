using System;
using System.Drawing;
using System.Globalization;

namespace ApkShellext2 {
    /// <summary>
    /// Contract every package reader exposes to the shell extension layer:
    /// identity/version metadata plus the icon and per-file flags.
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

        // use for other information, or file type specific info
        void setFlag(string flag, object value);
        object getFlag(string flag);
        Bitmap getIcon(Size size);
    }
}
