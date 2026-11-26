using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using ApkQuickReader;

namespace ApkShellext2
{
    /// <summary>
    /// Last-resort icon lookup for multi-split containers. The base apk of a bundle
    /// often names a launcher icon that only the configuration splits carry: a phone
    /// installs base + the density/language splits that match it, so the manifest
    /// reference resolves to a path the base archive itself does not hold. Without
    /// this probe such a bundle shows the generic icon even though the right bitmap
    /// is a few kilobytes away in a sibling split.
    ///
    /// Only called when the base apk produced no icon at all; a bundle whose base
    /// already resolves an icon keeps it, so nothing that renders today can change.
    /// </summary>
    internal static class ConfigSplitIconSource
    {
        /// <summary>Cap on the bytes one configuration split may occupy.</summary>
        internal const long MaxSplitBytes = 64L * 1024 * 1024;

        /// <summary>Cap on how many splits may be opened before giving up.</summary>
        internal const int MaxSplits = 16;

        // bundletool: splits/split_config.<qualifier>.apk; APKPure/APKMirror variants
        // drop the prefix or the splits/ directory. The qualifier may carry several
        // parts joined by underscores (anydpi_v26, en-rGB_xxhdpi).
        private static readonly Regex SplitName = new Regex(
            @"^(?:splits/)?(?:split_)?config\.(?<qualifier>[A-Za-z0-9_]+)\.apk$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Density keywords as they appear in a qualifier, most specific first. anydpi
        // sits above the concrete densities because it is how bundletool packages
        // vector drawables, which scale to whatever size is asked for.
        private static readonly KeyValuePair<string, int>[] DensityRanks = {
            new KeyValuePair<string, int>("anydpi", 70000),
            new KeyValuePair<string, int>("xxxhdpi", 640),
            new KeyValuePair<string, int>("xxhdpi", 480),
            new KeyValuePair<string, int>("xhdpi", 320),
            new KeyValuePair<string, int>("hdpi", 240),
            new KeyValuePair<string, int>("tvdpi", 213),
            new KeyValuePair<string, int>("mdpi", 160),
            new KeyValuePair<string, int>("ldpi", 120),
            new KeyValuePair<string, int>("nodpi", 0),
        };

        /// <summary>
        /// Looks for <paramref name="iconPath"/> in the configuration splits of
        /// <paramref name="outer"/>, highest density first. Returns null when no split
        /// holds the file, when the archive is malformed, or when every attempt
        /// failed - never throws, so the caller's null result stays the contract.
        /// </summary>
        internal static Bitmap TryRead(IArchive outer, string iconPath, Size size,
            IVectorIconRenderer renderer, Action<string> log)
        {
            if (outer == null || string.IsNullOrEmpty(iconPath)) return null;

            List<KeyValuePair<int, string>> candidates = new List<KeyValuePair<int, string>>();
            foreach (IArchiveEntry entry in outer.Entries) {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                Match match = SplitName.Match(entry.FullName);
                if (!match.Success) continue;
                candidates.Add(new KeyValuePair<int, string>(
                    DensityRank(match.Groups["qualifier"].Value), entry.FullName));
            }
            if (candidates.Count == 0) return null;
            candidates.Sort((a, b) => b.Key.CompareTo(a.Key));

            int opened = 0;
            foreach (KeyValuePair<int, string> candidate in candidates) {
                if (opened >= MaxSplits) break;
                opened++;
                Bitmap icon = ReadFromSplit(outer, candidate.Value, iconPath, size, renderer, log);
                if (icon != null) return icon;
            }
            return null;
        }

        /// <summary>
        /// Orders one qualifier by its density keyword; a qualifier without a known
        /// keyword sorts last so it is only tried after every measurable one.
        /// </summary>
        internal static int DensityRank(string qualifier)
        {
            if (string.IsNullOrEmpty(qualifier)) return -1;
            string[] parts = qualifier.Split('_');
            for (int i = 0; i < parts.Length; i++) {
                for (int r = 0; r < DensityRanks.Length; r++) {
                    if (string.Equals(parts[i], DensityRanks[r].Key, StringComparison.OrdinalIgnoreCase))
                        return DensityRanks[r].Value;
                }
            }
            return -1;
        }

        private static Bitmap ReadFromSplit(IArchive outer, string splitName, string iconPath,
            Size size, IVectorIconRenderer renderer, Action<string> log)
        {
            IArchiveEntry entry = outer.GetEntry(splitName);
            if (entry == null) return null;
            // The declared size is attacker data, but a split that claims to be larger
            // than the cap is not worth inflating at all; ReadEntryBounded then bounds
            // what is actually produced.
            if (entry.Length > MaxSplitBytes) {
                if (log != null)
                    log("config split " + splitName + " declares " + entry.Length + " bytes, over the cap");
                return null;
            }
            try {
                byte[] bytes = outer.ReadEntryBounded(entry, MaxSplitBytes, "config split " + splitName);
                MemoryStream stream = new MemoryStream(bytes, 0, bytes.Length, false, false);
                IArchive split = SafeArchive.Open(stream);
                try {
                    if (!split.Contains(iconPath)) return null;
                    IconRequest request = new IconRequest(size, split) {
                        Reader = IconReaderKind.SplitApk,
                        Renderer = renderer,
                        Log = log
                    };
                    IconSourceChain chain = new IconSourceChain(log)
                        .Add(new ArchiveImageIconSource())
                        .Add(new VectorDrawableIconSource());
                    Bitmap icon;
                    return chain.TryDecode(request, iconPath, out icon) ? icon : null;
                } finally {
                    split.Dispose();
                }
            } catch (Exception ex) {
                if (log != null) log("config split " + splitName + " skipped: " + ex.Message);
                return null;
            }
        }
    }
}
