using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Globalization;
using ApkQuickReader;
using System.IO;

namespace ApkShellext2 {
    /// <summary>
    /// This is the base class for package reader
    /// defines common method a package reader should have
    /// for reusing and simplify in the code
    /// </summary>
    public class AppPackageReader : IPackageReader {
        public const string extAPK = ".apk";
        public const string extAAB = ".aab";
        public const string extIPA = ".ipa";
        public const string extAPPX = ".appx";
        public const string extAPPXBUNDLE = ".appxbundle";
        // multi-split android containers (bundletool / APKMirror / APKPure)
        public const string extAPKS = ".apks";
        public const string extAPKM = ".apkm";
        public const string extXAPK = ".xapk";

        public enum AppType {
            AndroidApp,
            // Android App Bundle
            AndroidAab,
            iOSApp,
            WindowsPhoneAppBundle,
            WindowsPhoneApp
        }
        private Dictionary<string, object> Flags;

        public virtual string FileName { get; protected set; }
        // App name 
        public virtual string AppName { get { return ""; } }
        // Package Name
        public virtual string PackageName { get { return ""; } }

        // App version
        public virtual string Version { get { return ""; } }
        // Sub version
        public virtual string Revision { get { return ""; } }

        public virtual CultureInfo Culture { get; set; }

        // Publisher
        public virtual string Publisher { get { return ""; } }

        public virtual Bitmap Icon { get { return null; } }

        // Containers that do not parse a manifest icon reference answer with the
        // empty path; ApkReader resolves it and SplitApkBundleReader forwards the
        // base apk's answer.
        public virtual string IconEntryPath { get { return ""; } }

        public virtual string AppID { get { return ""; } }

        public virtual AppType Type { get { return AppType.AndroidApp; } }

        // Platform facts. A container that does not declare them answers with the
        // empty value; the readers that do parse a manifest override all four.
        public virtual string MinSdk { get { return ""; } }
        public virtual string TargetSdk { get { return ""; } }
        public virtual bool? Debuggable { get { return null; } }
        public virtual string Packer { get { return ""; } }

        // use for other information, or file type specific info
        public virtual void setFlag(string flag, object value) {
            if (Flags == null)
                Flags = new Dictionary<string, object>();
            Flags[flag] = value;
        }

        public virtual object getFlag(string flag) {
            if (Flags != null && Flags.ContainsKey(flag))
                return Flags[flag];
            else
                return null;
        }

        public virtual Bitmap getIcon(Size size) {
            return Utility.ResizeBitmap(Icon, size);
        }

        // Public implementation of Dispose pattern callable by consumers. 
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool disposed;
        // Protected implementation of Dispose pattern. 
        protected virtual void Dispose(bool disposing) {
            if (disposed)
                return;
            if (Flags != null)
                Flags.Clear();
            Flags = null;
            disposed = true;
        }

        protected void Log(string message) {
            Utility.Log(this, Path.GetFileName(FileName), message);
        }
    }
}
