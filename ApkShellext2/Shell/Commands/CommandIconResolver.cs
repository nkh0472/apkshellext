using System;
using System.Collections.Generic;
using System.Drawing;
using ApkQuickReader;
using ApkShellext2.Diagnostics;
using ApkShellext2.Properties;

namespace ApkShellext2.Shell.Commands {
    /// <summary>
    /// Resolves a <see cref="CommandIcon"/> to a bitmap. Both UI layers call this, so the
    /// WinForms menu and the native menu show the same images at the same size; the native
    /// layer only converts the bitmap to an <c>HBITMAP</c> afterwards.
    /// </summary>
    public static class CommandIconResolver {
        /// <summary>
        /// The image for an icon token at the given edge size, or null for
        /// <see cref="CommandIcon.None"/>. Each call returns a new bitmap the caller owns.
        /// </summary>
        public static Bitmap Resolve(CommandIcon icon, int size, IReadOnlyList<string> selectedPaths) {
            switch (icon) {
                case CommandIcon.Rename:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.iconRename, size);
                case CommandIcon.DumpOutput:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.output, size);
                case CommandIcon.Logo:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.logo, size);
                case CommandIcon.GooglePlay:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.iconGooglePlay, size);
                case CommandIcon.AmazonStore:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.iconAmazonStore, size);
                case CommandIcon.ApkMirror:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.iconApkMirror, size);
                case CommandIcon.AppleStore:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.iconAppStore, size);
                case CommandIcon.MicrosoftStore:
                    return Utility.ResizeBitmap(Properties.NonLocalizeResources.iconMSStore, size);
                case CommandIcon.PackageThumbnails:
                    return ComposePackageIcons(size, selectedPaths);
                default:
                    return null;
            }
        }

        /// <summary>
        /// The root image: up to three package icons stacked diagonally, falling back to the
        /// product logo when a package cannot supply one.
        /// </summary>
        private static Bitmap ComposePackageIcons(int size, IReadOnlyList<string> selectedPaths) {
            try { // draw multiple icons
                int totalIconToDraw = (selectedPaths.Count > 3) ? 3 : selectedPaths.Count;
                Bitmap b = new Bitmap(size, size);
                for (int i = 0; i < totalIconToDraw; i++) {
                    using (IPackageReader reader = PackageReaderFactory.Create(selectedPaths[totalIconToDraw - i - 1]))
                    using (Graphics g = Graphics.FromImage((Image)b)) {
                        reader.setFlag("ImageSize", size);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(reader.Icon, i * 3, i * 3, size - 2 * totalIconToDraw, size - 2 * totalIconToDraw);
                    }
                }
                return b;
            } catch (Exception ex) {
                ErrorLog.Warn("ApkContextMenu", "Error happens during extracting icon", ex);
                return Utility.ResizeBitmap(Properties.NonLocalizeResources.logo, size);
            }
        }
    }
}
