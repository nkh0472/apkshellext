using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Drawing;
using System.Text.RegularExpressions;

using ApkQuickReader;

namespace ApkShellext2 {
    /// <summary>
    /// 
    /// </summary>
    public class AppxReader : AppPackageReader {
        private const string AppxManifestXml = @"AppxManifest.xml";
        //private const string elemPackage = @"Package";
        private const string elemIdentity = @"Identity";
        private const string elemProperties = @"Properties";
        private const string elemDisplayName = @"DisplayName";
        private const string elemLogo = @"Logo";
        private const string elemPhoneIdentity = @"mp:PhoneIdentity";

        private const string attrVersion = @"Version";
        private const string attrName = @"Name";
        private const string attrPublisher = @"Publisher";
        private const string attrPhoneProductID = @"PhoneProductId";

        private IArchive zip;
        private string iconPath;

        private string version;
        private string appname;
        private string packageName;
        private string publisher;
        private string productid;

        public AppxReader(Stream stream) {
            FileName = "";
            zip = SafeArchive.Open(stream);
            Extract();
        }

        public AppxReader(string path) {
            FileName = path;
            // open with FileShare.Delete so Explorer can delete/rename the package while handlers are alive (kkguo/apkshellext#103);
            // the archive owns the stream (SafeArchive.Open passes leaveOpen:false), Dispose closes it
            zip = SafeArchive.Open(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
            Extract();
        }

        public void Extract() {
            IArchiveEntry en = zip.GetEntry(AppxManifestXml);
            if (en == null)
                throw new EntryPointNotFoundException("cannot find " + AppxManifestXml);
            // bounded read (also fixes silent partial Stream.Read truncation on big entries)
            byte[] xmlbytes = zip.ReadEntryBounded(en, SafeArchive.MAX_MANIFEST, "AppxManifest.xml");

            XmlDocument xml = new XmlDocument();
            xml.XmlResolver = null;
            xml.Load(new MemoryStream(xmlbytes, 0, xmlbytes.Length, false, false));

            XmlElement packageNode = xml.DocumentElement;
            XmlElement Identity = packageNode[elemIdentity];
            version = Identity.Attributes[attrVersion].Value.ToString();
            packageName = Identity.Attributes[attrName].Value.ToString();
            publisher = Identity.Attributes[attrPublisher].Value.ToString();
            Match m = Regex.Match(publisher,@"CN=([^,]*),?");
            if (m.Success) {
                publisher = m.Groups[1].Value;
            }
            
            XmlElement DisplayName = packageNode[elemProperties][elemDisplayName];
            appname = DisplayName.FirstChild.Value.ToString();
            XmlElement Logo = packageNode[elemProperties][elemLogo];
            iconPath = Logo.FirstChild.Value.ToString().Replace(@"\",@"/");
            XmlElement PhoneIdentity = packageNode[elemPhoneIdentity];
            productid = PhoneIdentity.Attributes[attrPhoneProductID].Value.ToString();
        }

        public override AppPackageReader.AppType Type {
            get {
                return AppType.WindowsPhoneApp;
            }
        }

        // an appx declares no android platform facts and carries no apk signing block
        public override string MinSdk { get { return ""; } }
        public override string TargetSdk { get { return ""; } }
        public override bool? Debuggable { get { return null; } }
        public override string Packer { get { return ""; } }

        public override string AppName {
            get {
                return appname;
            }
        }

        public override string Version {
            get {
                return version;
            }
        }

        public override string Publisher {
            get {
                return publisher;
            }
        }

        public override string PackageName {
            get {
                return packageName;
            }
        }

        public override string AppID {
            get {
                return productid;
            }
        }

        public override Bitmap Icon {
            get {
                if (iconPath == "")
                    throw new Exception("Cannot find logo path");
                // The manifest logo (and its scale-qualified variants) is one source in
                // the shared chain; a missing file still throws the legacy exception so
                // the shell keeps drawing its default icon.
                IconRequest request = new IconRequest(new Size(0, 0), zip) {
                    Reader = IconReaderKind.Appx,
                    Lookup = IconLookupKind.ManifestLogo,
                    LogoPath = iconPath,
                    Log = Log
                };
                Bitmap icon;
                if (IconChain().TryGetIcon(request, out icon)) {
                    return icon;
                }
                throw new EntryPointNotFoundException("Cannot find Logo file: " + iconPath);
            }
        }

        private IconSourceChain iconChain;

        private IconSourceChain IconChain() {
            if (iconChain == null) {
                iconChain = new IconSourceChain(Log).Add(new AppxManifestLogoIconSource());
            }
            return iconChain;
        }

        private bool disposed = false;
        protected override void Dispose(bool disposing) {
            if (disposed) return;
            if (disposing) {
                if (zip != null)
                    zip.Dispose();
            }
            disposed = true;
            base.Dispose(disposing);
        }

        public void Close() {
            Dispose(true);
        }

        ~AppxReader() {
            Dispose(true);
        }

    }
}
