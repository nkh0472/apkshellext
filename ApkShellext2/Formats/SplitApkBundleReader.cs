using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ApkShellext2;

namespace ApkQuickReader {
    /// <summary>
    /// Reader for multi-split Android package containers: .apks (bundletool),
    /// .apkm (APKMirror) and .xapk (APKPure). All three are plain ZIP archives
    /// holding several .apk files plus container metadata. The application's
    /// identity (manifest/resources/icon) lives in the BASE apk; split_config.*
    /// entries only add per-density/language resources on top of it.
    /// Base selection order mirrors apk-info (Apache-2.0) base_apk_candidates:
    ///   xapk manifest.json split with id=="base"  >  literal "base.apk"  >
    ///   bundletool toc.pb well-known paths  >  root-level *.apk guess
    /// (candidates whose manifest carries android:split are rejected).
    /// .apkm/.xapk usually also ship a ready-made icon.png at the archive root,
    /// which is preferred for icon display (zero parse cost).
    /// </summary>
    public class SplitApkBundleReader : AppPackageReader {
        private FileStream outerStream;
        private IArchive outer;
        private AppPackageReader inner;
        private string baseEntryName;

        // bundletool mode paths, splits/ before instant/ on purpose
        private static readonly string[] tocBasePaths = {
            "splits/base-master.apk", "universal.apk", "system/system.apk",
            "archive/archive.apk", "instant/instant-base-master.apk",
        };

        // caps + ratio doctrine centralised in SafeArchive (shared by all readers)
        private const long MAX_JSON_BYTES = 2L * 1024 * 1024;

        public SplitApkBundleReader(string filename) {
            FileName = filename;
            outerStream = new FileStream(filename, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            outer = SafeArchive.Open(outerStream);

            baseEntryName = selectBaseEntry();
            if (baseEntryName == null)
                throw new NotSupportedException(filename + " contains no base apk (encrypted apkm or unsupported layout?)");
            // the base apk itself is only inflated on demand (EnsureInner):
            // icon and infotip usually resolve from the root icon.png alone,
            // which costs a few KB instead of decompressing up to 512 MB
        }

        private void EnsureInner() {
            if (inner != null) return;
            IArchiveEntry baseEntry = outer.GetEntry(baseEntryName);
            // The nested apk must be seekable for ZipArchive; entry streams are
            // not, so buffer the whole entry — under hard caps so a
            // zip bomb cannot balloon explorer.
            byte[] baseBytes = outer.ReadEntryBounded(baseEntry, SafeArchive.MAX_BASE_APK, "base apk " + baseEntryName);
            MemoryStream ms = new MemoryStream(baseBytes, 0, baseBytes.Length, false, false);
            AppPackageReader candidate = new ApkReader(ms);
            // wrong-base guard: a split has android:split in its manifest
            if (!string.IsNullOrEmpty(((ApkReader)candidate).getAttribute("manifest", "split"))) {
                candidate.Dispose();
                Log("selected base " + baseEntryName + " looks like a split, rejecting");
                throw new NotSupportedException("base candidate " + baseEntryName + " is a split apk");
            }
            inner = candidate;
        }

        private string selectBaseEntry() {
            // 1. xapk manifest.json: {"split_apks":[{"file":"...","id":"base"}, ...]}
            IArchiveEntry mf = outer.GetEntry("manifest.json");
            if (mf != null) {
                string json = ReadEntryText(mf);
                // tolerant to BOM/whitespace; pair file/id inside each flat object
                foreach (Match block in Regex.Matches(json, @"\{[^{}]*\}")) {
                    Match id = Regex.Match(block.Value, @"""id""\s*:\s*""([^""]*)""");
                    Match file = Regex.Match(block.Value, @"""file""\s*:\s*""([^""]*)""");
                    if (id.Success && file.Success &&
                        string.Equals(id.Groups[1].Value, "base", StringComparison.OrdinalIgnoreCase)) {
                        string cand = file.Groups[1].Value.Replace('\\', '/');
                        if (IsValidEntryName(cand)) return cand;
                        Log("manifest.json base name rejected: " + cand);
                    }
                }
                // alternate xapk schema: {"name":"pkg","split_apks":[{"apkFile":"..."}]}
                Match alt = Regex.Match(json, @"""apkFile""\s*:\s*""([^""]*base[^""]*\.apk)""", RegexOptions.IgnoreCase);
                if (alt.Success) {
                    string cand = alt.Groups[1].Value.Replace('\\', '/');
                    if (IsValidEntryName(cand)) return cand;
                    Log("manifest.json base name rejected: " + cand);
                }
            }

            // 2. APKMirror / most tooling: literal base.apk at root
            if (outer.GetEntry("base.apk") != null) return "base.apk";

            // 3. bundletool toc.pb container: try the well-known base paths
            if (outer.GetEntry("toc.pb") != null) {
                foreach (string p in tocBasePaths)
                    if (outer.GetEntry(p) != null) return p;
                Match standalone = null; // standalones/standalone<N>.apk, lowest N
                int bestN = int.MaxValue;
                foreach (IArchiveEntry e in outer.Entries) {
                    if (!outer.IsFile(e)) continue;
                    Match m = Regex.Match(e.FullName, @"standalones/standalone(\d{1,6})\.apk$", RegexOptions.IgnoreCase);
                    int n;
                    if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n < bestN) {
                        bestN = n;
                        standalone = m;
                    }
                }
                if (standalone != null) return standalone.Value;
            }

            // 4. guess: root-level *.apk that is not a config/split companion; largest wins
            IArchiveEntry best = null;
            foreach (IArchiveEntry e in outer.Entries) {
                if (!outer.IsFile(e)) continue;
                string n = e.FullName;
                if (!n.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                string leaf = Path.GetFileName(n);
                if (leaf.IndexOf("config", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    leaf.StartsWith("split", StringComparison.OrdinalIgnoreCase)) continue;
                int depth = n.Count(c => c == '/');
                int score = (depth == 0 ? 1 << 30 : 0) + (int)(SizeOf(e) >> 20);
                int bestScore = best == null ? -1
                    : (best.FullName.Count(c => c == '/') == 0 ? 1 << 30 : 0) + (int)(SizeOf(best) >> 20);
                if (score > bestScore) best = e;
            }
            return best == null ? null : best.FullName;
        }

        // an entry that declares no usable size (the 0xFFFFFFFF sentinel without a zip64
        // record) scores 0 instead of four gigabytes
        private long SizeOf(IArchiveEntry entry) {
            long declared = outer.DeclaredLength(entry);
            return declared < 0 ? 0 : declared;
        }

        // names come from an attacker-controlled manifest.json; we only ever use
        // them as zip lookups (never filesystem paths) but keep them clean anyway
        private static bool IsValidEntryName(string name) {
            if (string.IsNullOrEmpty(name) || name.Length > 260) return false;
            if (name.IndexOf(':') >= 0) return false;               // drive letter / NTFS stream
            if (name.StartsWith("/") || name.StartsWith("\\")) return false;
            foreach (string seg in name.Split('/'))
                if (seg == "..") return false;
            return true;
        }

        private string ReadEntryText(IArchiveEntry entry) {
            byte[] b = outer.ReadEntryBounded(entry, MAX_JSON_BYTES, "metadata " + entry.FullName);
            int off = 0;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) off = 3; // BOM
            return Encoding.UTF8.GetString(b, off, b.Length - off);
        }

        public override AppPackageReader.AppType Type { get { return AppType.AndroidApp; } }
        public override string AppName { get { EnsureInner(); return inner.AppName; } }
        public override string PackageName { get { EnsureInner(); return inner.PackageName; } }
        public override string Version { get { EnsureInner(); return inner.Version; } }
        public override string Revision { get { EnsureInner(); return inner.Revision; } }
        public override string Publisher { get { EnsureInner(); return inner.Publisher; } }
        public override string AppID { get { EnsureInner(); return inner.AppID; } }
        // the base apk carries the manifest and the signing block of the whole bundle
        public override string MinSdk { get { EnsureInner(); return inner.MinSdk; } }
        public override string TargetSdk { get { EnsureInner(); return inner.TargetSdk; } }
        public override bool? Debuggable { get { EnsureInner(); return inner.Debuggable; } }
        public override string Packer { get { EnsureInner(); return inner.Packer; } }
        public override CultureInfo Culture {
            get { EnsureInner(); return inner.Culture; }
            set { EnsureInner(); inner.Culture = value; }
        }

        // Root-level icon.png shipped by APKMirror/APKPure — the cheap, high-quality path.
        // It is one source in the shared chain; the base apk reader covers everything else.
        public override Bitmap Icon {
            get {
                Bitmap b;
                if (TryGetRootIcon(out b)) return b;
                EnsureInner();
                return inner.Icon;
            }
        }

        public override Bitmap getIcon(Size size) {
            Bitmap b;
            if (TryGetRootIcon(out b)) {
                using (b) return Utility.ResizeBitmap(b, size);
            }
            EnsureInner();
            return inner.getIcon(size);
        }

        private bool TryGetRootIcon(out Bitmap icon) {
            IconRequest request = new IconRequest(new Size(0, 0), outer) {
                Reader = IconReaderKind.SplitApk,
                Lookup = IconLookupKind.BundleRootIcon,
                Log = Log
            };
            return RootIconChain().TryGetIcon(request, out icon);
        }

        private IconSourceChain rootIconChain;

        private IconSourceChain RootIconChain() {
            if (rootIconChain == null) {
                rootIconChain = new IconSourceChain(Log).Add(new BundleRootIconSource());
            }
            return rootIconChain;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (inner != null) { inner.Dispose(); inner = null; }
                if (outer != null) { outer.Dispose(); outer = null; }
                if (outerStream != null) { outerStream.Dispose(); outerStream = null; }
            }
            base.Dispose(disposing);
        }
    }
}
