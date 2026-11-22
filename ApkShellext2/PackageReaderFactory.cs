using System;
using System.IO;
using ApkQuickReader;

namespace ApkShellext2 {
    /// <summary>
    /// Maps a file extension to the app type it carries, and builds the
    /// matching reader. This is the single entry point for the shell
    /// handlers — AppPackageReader itself only defines the reader contract.
    /// </summary>
    public static class PackageReaderFactory {
        public static AppPackageReader.AppType GetAppType(string path) {
            // case-insensitive: FOO.APK is still an apk (extension is compared
            // against lowercase constants below)
            string suffix = Path.GetExtension(path).ToLowerInvariant();
            if (suffix == AppPackageReader.extAPK)
            {
                return AppPackageReader.AppType.AndroidApp;
            }
            else if (suffix == AppPackageReader.extIPA)
            {
                return AppPackageReader.AppType.iOSApp;
            }
            else if (suffix == AppPackageReader.extAPPXBUNDLE)
            {
                return AppPackageReader.AppType.WindowsPhoneAppBundle;
            }
            else if (suffix == AppPackageReader.extAPPX)
            {
                return AppPackageReader.AppType.WindowsPhoneApp;
            }
            else if (suffix == AppPackageReader.extAAB) 
            {
                return AppPackageReader.AppType.AndroidAab;
            } 
            else if (suffix == AppPackageReader.extAPKS || suffix == AppPackageReader.extAPKM || suffix == AppPackageReader.extXAPK)
            {
                return AppPackageReader.AppType.AndroidApp; // split container: identity == its base apk
            } 
            else 
            {
                throw new NotSupportedException(suffix + " type is not supported.");
            }
        }

        public static IPackageReader Create(string path) {
            switch (GetAppType(path)) {
                case AppPackageReader.AppType.AndroidApp:
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == AppPackageReader.extAPKS || ext == AppPackageReader.extAPKM || ext == AppPackageReader.extXAPK)
                        return new SplitApkBundleReader(path);
                    return new ApkReader(path);
                case AppPackageReader.AppType.iOSApp:
                    return new IpaReader(path);
                case AppPackageReader.AppType.WindowsPhoneAppBundle:
                    return new AppxBundleReader(path);
                case AppPackageReader.AppType.WindowsPhoneApp:
                    return new AppxReader(path);
                case AppPackageReader.AppType.AndroidAab:
                    Utility.Log(null, "AndroidAab", "load AabReader");
                    return new AabReader(path);
                default :
                    throw new NotSupportedException("File type is not supported.");
            }
        }
    }
}
