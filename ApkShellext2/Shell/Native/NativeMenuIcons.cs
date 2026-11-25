using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using ApkShellext2.Shell.Commands;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// Turns the model's icon tokens into menu bitmaps. <c>hbmpItem</c> is an owned handle: the
    /// menu stores the raw value and the shell may draw it after the handler returned, so a
    /// bitmap created for one menu cannot be freed when that call ends. The handles are therefore
    /// cached per command and size for the life of the process, and the cache is bounded so an
    /// unusual selection set cannot grow GDI usage without limit.
    /// </summary>
    internal static class NativeMenuIcons {
        private const int SmCxSmallIcon = 0x0031;
        private const int MaxCachedHandles = 128;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, IntPtr> Cache = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        private static readonly Queue<string> CacheOrder = new Queue<string>();

        /// <summary>
        /// Edge length of a menu image in pixels. Items the shell fills in itself use the
        /// small-icon metric, so a bitmap of that size keeps the rows the same height as the
        /// rest of the menu. The value already includes the system DPI scaling.
        /// </summary>
        internal static int ImageSize {
            get {
                int size = GetSystemMetrics(SmCxSmallIcon);
                if (size <= 0) size = 16;
                return size;
            }
        }

        /// <summary>HBITMAP for the icon token, or <see cref="IntPtr.Zero"/> when the item has no image.</summary>
        internal static IntPtr Get(CommandIcon icon, int size, IReadOnlyList<string> selectedPaths) {
            if (icon == CommandIcon.None || size <= 0) return IntPtr.Zero;
            string key = BuildKey(icon, size, selectedPaths);
            lock (Gate) {
                IntPtr cached;
                if (Cache.TryGetValue(key, out cached)) return cached;

                IntPtr handle = Create(icon, size, selectedPaths);
                if (handle == IntPtr.Zero) return IntPtr.Zero;

                if (CacheOrder.Count >= MaxCachedHandles) {
                    string oldest = CacheOrder.Dequeue();
                    IntPtr stale;
                    if (Cache.TryGetValue(oldest, out stale)) {
                        Cache.Remove(oldest);
                        DeleteObject(stale);
                    }
                }
                Cache[key] = handle;
                CacheOrder.Enqueue(key);
                return handle;
            }
        }

        private static IntPtr Create(CommandIcon icon, int size, IReadOnlyList<string> selectedPaths) {
            using (Bitmap bitmap = CommandIconResolver.Resolve(icon, size, selectedPaths)) {
                if (bitmap == null) return IntPtr.Zero;
                return bitmap.GetHbitmap(Color.FromArgb(0));
            }
        }

        private static string BuildKey(CommandIcon icon, int size, IReadOnlyList<string> selectedPaths) {
            string key = ((int)icon).ToString() + "|" + size;
            if (icon != CommandIcon.PackageThumbnails || selectedPaths == null) return key;
            var builder = new System.Text.StringBuilder(key);
            for (int i = 0; i < selectedPaths.Count && i < 8; i++) {
                builder.Append('|').Append(selectedPaths[i]);
            }
            return builder.ToString();
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
