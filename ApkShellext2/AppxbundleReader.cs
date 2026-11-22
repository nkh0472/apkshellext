using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using System.Xml;
using System.IO;
using System.Drawing;
using ApkQuickReader;

namespace ApkShellext2 {
    /// <summary>
    /// Read AppxBundle
    /// </summary>
    public class AppxBundleReader :AppPackageReader {
        private ZipFile zip;
        private AppxReader appxReader= null;

        private const string AppxBundleManifestXml = @"AppxMetadata/AppxBundleManifest.xml";
        private const string ElemIdentity = @"Identity";
        //private const string ElemProperties = @"Properties";
        //private const string ElemDisplayName = @"DisplayName";
        private const string ElemPackage = @"Package";
        //private const string AttrVersion = @"Version";
        private const string AttrName = @"Name";
        //private const string AttrPublisher = @"Publisher";
        private const string AttrType = @"Type";
        private const string ValApplication = @"application";
        private const string AttrFileName = @"FileName";

        // the manifest, its package list and the file names it points at all come
        // from the bundle: keep each of them small enough that a malformed bundle
        // cannot turn an explorer thumbnail request into unbounded work
        private const long MaxAppxBundleManifestBytes = 16L * 1024 * 1024;
        private const int MaxAppxBundleManifestPackages = 4096;
        private const int MaxAppxBundleFileNameLength = 1024;

        public AppxBundleReader(Stream stream) {
            FileName = "";
            openFile(stream);
        }

        public AppxBundleReader(string path) {
            FileName = path;
            // FileShare.Delete so Explorer can delete/rename the package while handlers are alive (kkguo/apkshellext#103)
            openFile(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
        }

        private void openFile(Stream stream) {
            string appxFileName = "";
            zip = new ZipFile(stream);
            ZipEntry en = zip.GetEntry(AppxBundleManifestXml);
            if (en == null)
                throw new EntryPointNotFoundException("cannot find " + AppxBundleManifestXml);

            // the central directory declares the uncompressed size, so an oversized
            // manifest is rejected before it is inflated at all
            if (en.Size > MaxAppxBundleManifestBytes)
                throw new InvalidDataException(AppxBundleManifestXml + " declares " + en.Size + " bytes, over the "
                    + MaxAppxBundleManifestBytes + "-byte limit");

            byte[] manifestBytes = ZipSafety.ReadEntryBounded(zip, en, MaxAppxBundleManifestBytes, AppxBundleManifestXml);

            using (XmlReader reader = XmlReader.Create(new MemoryStream(manifestBytes, false))) {
                reader.ReadToFollowing(ElemIdentity);
                reader.MoveToAttribute(AttrName);

                // ReadToFollowing returns false at EOF; a manifest whose Package
                // elements never carry Type="application" used to spin here forever,
                // so the scan is driven by the return value and stops at EOF
                int packages = 0;
                while (true) {
                    if (!reader.ReadToFollowing(ElemPackage))
                        throw new Exception("Cannot find application in " + AppxBundleManifestXml);
                    if (++packages > MaxAppxBundleManifestPackages)
                        throw new InvalidDataException(AppxBundleManifestXml + " declares more than "
                            + MaxAppxBundleManifestPackages + " packages");
                    if (!reader.MoveToAttribute(AttrType) || reader.Value != ValApplication)
                        continue;
                    if (!reader.MoveToAttribute(AttrFileName))
                        throw new EntryPointNotFoundException("application package in " + AppxBundleManifestXml
                            + " has no " + AttrFileName);

                    appxFileName = reader.Value;
                    if (appxFileName.Length == 0)
                        throw new EntryPointNotFoundException("application package in " + AppxBundleManifestXml
                            + " has an empty " + AttrFileName);
                    if (appxFileName.Length > MaxAppxBundleFileNameLength)
                        throw new InvalidDataException(AttrFileName + " is " + appxFileName.Length
                            + " characters, over the " + MaxAppxBundleFileNameLength + "-character limit");
                    break;
                }
            }

            en = zip.GetEntry(appxFileName);
            if (en == null) {
                // newer Store bundles are packed with percent-encoded entry names
                // ("Bluetooth Audio…" → "Bluetooth%20Audio…") while the manifest keeps raw names
                string encoded = Uri.EscapeDataString(appxFileName);
                foreach (ZipEntry cand in zip) {
                    if (cand.IsFile && (cand.Name == encoded || cand.Name == appxFileName.Replace(" ", "%20"))) {
                        en = cand;
                        break;
                    }
                }
            }
            if (en == null)
                throw new EntryPointNotFoundException("cannot find appx " + appxFileName);

            appxReader = new AppxReader(zip.GetInputStream(en));
        }

        public override AppPackageReader.AppType Type {
            get {
                return AppType.WindowsPhoneAppBundle;
            }
        }

        public override Bitmap Icon {
            get {
                return appxReader.Icon;
            }
        }

        public override string AppName {
            get {
                return appxReader.AppName;
            }
        }

        public override string PackageName {
            get {
                return appxReader.PackageName;
            }
        }

        public override string Version {
            get {
                return appxReader.Version;
            }
        }

        public override string Publisher {
            get {
                return appxReader.Publisher;
            }
        }

        public override string AppID {
            get {
                return appxReader.AppID;
            }
        }
        private bool disposed = false;
        protected override void Dispose(bool disposing) {
            if (disposed) return;
            if (disposing) {
                if (appxReader != null) {
                    appxReader.Close();
                }
                if (zip != null)
                    zip.Close();
            }
            disposed = true;
            base.Dispose(disposing);
        }

        public void Close() {
            Dispose(true);
        }

        ~AppxBundleReader() {
            Dispose(true);
        }
    }
}
