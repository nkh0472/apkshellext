using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using ApkQuickReader;
using ApkShellext2.Diagnostics;

namespace ApkShellext2 {
    /// <summary>
    /// The icon pipeline behind the icon handler.
    ///
    /// The shell creates one COM instance per request, so without a cache every folder refresh
    /// re-opened and inflated every package in view; the bounded cache removes that churn. Only the
    /// base icon is cached - opening the package and rasterising it is the expensive part - and the
    /// overlay is composed per request so its setting takes effect immediately. An evicted bitmap
    /// is left to the GC because another request may still hold it.
    /// </summary>
    internal static class IconRenderer {
        private const int IconCacheSize = 64;
        private static readonly object iconCacheLock = new object();
        private static readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>();
        private static readonly Queue<string> iconCacheOrder = new Queue<string>();

        /// <summary>
        /// Returns the icon for <paramref name="path"/> at <paramref name="iconSize"/> pixels, or
        /// null when no icon exists at all (an .aab has no platform icon to fall back to).
        /// <paramref name="module"/> names the handler in the failure log, so the log tells which
        /// of the two implementations ran.
        /// </summary>
        internal static Icon Render(string path, uint iconSize, string module) {
            Bitmap baseIcon = ResolveBaseIcon(path, iconSize, module);
            if (baseIcon == null) return null;
            DbgLog.W("ICON result " + Path.GetFileName(path) + " -> " + DbgLog.Sample(baseIcon));
            // ResizeBitmap always returns a fresh bitmap, and Icon.FromHandle does not own the
            // HICON, so the shell is free to destroy it.
            using (Bitmap resized = Utility.ResizeBitmap(baseIcon, new Size((int)iconSize, (int)iconSize)))
                return resized == null ? null : Icon.FromHandle(resized.GetHicon());
        }

        private static Bitmap ResolveBaseIcon(string path, uint iconSize, string module) {
            string key = IconCacheKey(path, iconSize);
            Bitmap icon = LookupIcon(key);
            if (icon != null) return ApplyOverlay(icon, path, module);

            try {
                using (IPackageReader reader = PackageReaderFactory.Create(path)) {
                    if (reader.Type == AppPackageReader.AppType.iOSApp && !Utility.GetSettingBool("ShowIpaIcon", true)) {
                        icon = Utility.AppTypeIcon(reader.Type);
                    } else if ((reader.Type == AppPackageReader.AppType.WindowsPhoneApp ||
                        reader.Type == AppPackageReader.AppType.WindowsPhoneAppBundle) &&
                        !Utility.GetSettingBool("ShowAppxIcon", false)) {
                        icon = Utility.AppTypeIcon(reader.Type);
                    } else {
                        icon = reader.getIcon(new Size((int)iconSize, (int)iconSize));
                    }
                    if (icon == null)
                        throw new Exception("Cannot find Icon for " + Path.GetFileName(path) + ", draw default");
                }
                StoreIcon(key, icon);
            } catch (Exception ex) {
                ErrorLog.Warn(module, "Error in reading icon for " + Path.GetFileName(path) + ", draw default", ex);
                DbgLog.W("ICON default for " + Path.GetFileName(path) + " : " + ex.Message.Split('\n')[0]);
                // read error, draw the default icon for the platform the package belongs to
                icon = Utility.AppTypeIcon(PackageReaderFactory.GetAppType(path));
            }
            return ApplyOverlay(icon, path, module);
        }

        private static Bitmap ApplyOverlay(Bitmap icon, string path, string module) {
            if (icon == null || !Utility.GetSettingBool("ShowOverLayIcon", false)) return icon;
            try {
                Bitmap composed = Utility.CombineBitmap(icon,
                    Utility.AppTypeIcon(PackageReaderFactory.GetAppType(path)),
                    new Rectangle((int)(icon.Width * 0.05), 0, (int)(icon.Width * 0.95), (int)(icon.Height * 0.95)),
                    new Rectangle(0, (int)icon.Height / 2, (int)icon.Width / 2, (int)icon.Height / 2),
                    new Size((int)icon.Width, (int)icon.Height));
                return composed ?? icon;
            } catch (Exception ex) {
                ErrorLog.Warn(module, "Overlay failed for " + Path.GetFileName(path) + ", keeping base icon", ex);
                return icon;
            }
        }

        // The key carries the file's write time and length so a replaced package cannot serve a
        // stale icon, and the requested size because the shell asks for several.
        private static string IconCacheKey(string path, uint iconSize) {
            try {
                var fi = new FileInfo(path);
                return path.ToLowerInvariant() + "|" + fi.LastWriteTimeUtc.Ticks + "|" + fi.Length + "|" + iconSize;
            } catch {
                return null;
            }
        }

        private static Bitmap LookupIcon(string key) {
            if (key == null) return null;
            lock (iconCacheLock) {
                Bitmap b;
                return iconCache.TryGetValue(key, out b) ? b : null;
            }
        }

        private static void StoreIcon(string key, Bitmap bitmap) {
            if (key == null || bitmap == null) return;
            lock (iconCacheLock) {
                if (!iconCache.ContainsKey(key)) {
                    iconCache[key] = bitmap;
                    iconCacheOrder.Enqueue(key);
                    if (iconCacheOrder.Count > IconCacheSize)
                        iconCache.Remove(iconCacheOrder.Dequeue());
                }
            }
        }
    }
}
