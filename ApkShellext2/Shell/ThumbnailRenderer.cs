using System;
using System.Drawing;
using System.IO;
using ApkQuickReader;
using ApkShellext2.Diagnostics;

namespace ApkShellext2 {
    /// <summary>
    /// The thumbnail pipeline shared by the SharpShell handler, which is initialised with a stream
    /// from IInitializeWithStream, and the native handler, which is initialised with a path from
    /// IInitializeWithFile.
    ///
    /// Thumbnails are opt-in: EnableThumbnail has to be explicitly True, because rendering one
    /// opens and decodes the package for every item a view shows.
    /// </summary>
    internal static class ThumbnailRenderer {
        /// <summary>
        /// Returns the thumbnail for the package in <paramref name="stream"/> at
        /// <paramref name="width"/> pixels, or null when thumbnails are disabled or the package
        /// cannot be read. <paramref name="module"/> names the handler in the failure log, and
        /// <paramref name="log"/> receives the handler's own progress messages when it has any.
        /// </summary>
        internal static Bitmap Render(Stream stream, uint width, string module, Action<string> log = null) {
            if (Utility.GetSetting("EnableThumbnail") != "True") return null;

            Bitmap m_icon = null;
            try {
                int outputSize = (int)width;
                DbgLog.W("THUMB enter w=" + width + " stream=" + (stream == null ? "null" :
                    stream.Length + "B seek=" + stream.CanSeek + " pos=" + stream.Position));
                string appName = "?";
                using (AppPackageReader reader = new ApkReader(stream)) {
                    if (log != null) log("Reading stream from " + reader.AppName);
                    appName = reader.AppName;
                    DbgLog.W("THUMB reader ok app=" + reader.AppName);
                    // Render at the requested size: the Icon property is fixed at 64px and
                    // looked blurry when upscaled in the preview pane.
                    m_icon = reader.getIcon(new Size(outputSize, outputSize));
                    DbgLog.W("THUMB getIcon -> " + DbgLog.Sample(m_icon));
                }

                if (m_icon == null)
                    throw new Exception("Cannot find Icon from Stream, draw default");
                if (m_icon.Height < outputSize && Utility.GetSetting("StretchThumbnail", "True") != "True")
                    outputSize = m_icon.Height;

                if (log != null) log("Got icon, resizing...");
                Bitmap composed;
                if (Utility.GetSetting("ShowOverlayIcon") == "True") {
                    if (log != null) log("Draw overlay");
                    composed = Utility.CombineBitmap(m_icon,
                        Utility.AppTypeIcon(AppPackageReader.AppType.AndroidApp),
                        new Rectangle(0, 0, outputSize, outputSize),
                        new Rectangle(0, (int)outputSize / 2, (int)outputSize / 2, (int)outputSize / 2),
                        new Size(outputSize, outputSize));
                } else {
                    composed = Utility.ResizeBitmap(m_icon, outputSize);
                }
                m_icon.Dispose();
                m_icon = composed;
                DbgLog.W("THUMB composed -> " + DbgLog.Sample(m_icon));
                DbgLog.Dump(m_icon, "thumb_" + appName);
                return m_icon;
            } catch (Exception ex) {
                ErrorLog.Warn(module, "Error in reading icon from stream, draw default", ex);
                DbgLog.W("THUMB FAIL " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
                // a package that cannot be read keeps the shell's generic thumbnail
                return null;
            }
        }

        /// <summary>
        /// Opens <paramref name="path"/> and renders the thumbnail from it. The readers need a
        /// seekable stream, so one is opened per request and closed with it.
        /// </summary>
        internal static Bitmap Render(string path, uint width, string module) {
            try {
                using (Stream stream = File.OpenRead(path))
                    return Render(stream, width, module);
            } catch (Exception ex) {
                ErrorLog.Warn(module, "Error opening " + Path.GetFileName(path) + " for a thumbnail", ex);
                return null;
            }
        }
    }
}
