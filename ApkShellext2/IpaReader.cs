using PlistCS;
using PNGDecrush;
using SharpShell.Attributes;
using SharpShell.Extensions;
using SharpShell.Diagnostics;
using SharpShell.Exceptions;
using SharpShell.ServerRegistration;
using SharpShell.SharpIconHandler;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using ApkQuickReader;

namespace ApkShellext2
{
    public class IpaReader : AppPackageReader, IPlistIconLookup
    {
        private string strAppRoot;
        private Dictionary<string, object> infoPlistDic;
        private Dictionary<string, object> itunesMetadataDic;
        private IArchive zip;

        private const string iTunesMetadataPath = @"iTunesMetadata.plist";
        private const string infoPlistPath = @"(Payload/.*\.app/)Info\.plist";
        private const string CFBundleIcons = @"CFBundleIcons";
        private const string CFBundlePrimaryIcon = @"CFBundlePrimaryIcon";
        private const string CFBundleIconFile = @"CFBundleIconFile";
        private const string CFBundleIconFiles = @"CFBundleIconFiles";
        private const string CFBundleDisplayName = @"CFBundleDisplayName";
        private const string FacebookDisplayName = @"FacebookDisplayName";
        private const string CFBundleIdentifier = @"CFBundleIdentifier";
        private const string CFBundleShortVersionString = @"CFBundleShortVersionString";
        private const string CFBundleVersion = @"CFBundleVersion";
        private const string CFBundleResourceSpecification = @"CFBundleResourceSpecification";

        public const string flagAppId = @"itemId";
        public const string flagCopyright = @"copyright";

        public IpaReader(string path) {
            FileName = path;
            // FileShare.Delete so Explorer can delete/rename the package while handlers are alive (kkguo/apkshellext#103)
            openStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
        }

        public IpaReader(Stream stream) {
            openStream(stream);
        }

        private void openStream(Stream stream) {
            zip = SafeArchive.Open(stream);
            IArchiveEntry infoPlist = null;
            foreach (IArchiveEntry en in zip.Entries) {
                Match m = Regex.Match(en.FullName, infoPlistPath);
                if (m.Success) {
                    strAppRoot = m.Groups[1].Value;
                    infoPlist = en;
                    break;
                }
            }

            if (infoPlist == null) {
                throw new EntryPointNotFoundException("cannot find info.plist");
            }

            // bounded read (also fixes silent partial Stream.Read truncation)
            byte[] infoBytes = zip.ReadEntryBounded(infoPlist, SafeArchive.MAX_METADATA, "info.plist");

            infoPlistDic = (Dictionary<string, object>)Plist.readPlist(infoBytes);
        }

        public string[] getStrings(Dictionary<string, object> dic, string[] keys) {
            for (int i = 0; i < keys.Length - 1; i++) {
                if (dic.ContainsKey(keys[i])) {
                    dic = (Dictionary<string, object>)dic[keys[i]];
                } else {
                    return new string[] { };
                    //throw new Exception("Given ID is not valid");
                }
            }
            if (dic.ContainsKey(keys[keys.Length - 1])) {
                if (dic[keys[keys.Length - 1]] is string) {
                    return new string[] { dic[keys[keys.Length - 1]] as string };
                }
                if (dic[keys[keys.Length - 1]] is int) {
                    return new string[] { dic[keys[keys.Length - 1]].ToString() };
                } else { // is list
                    object[] arr = ((List<object>)dic[keys[keys.Length - 1]]).ToArray();
                    return Array.ConvertAll<object, string>(arr, x => x.ToString());
                }
            } else {
                return new string[] { };
            }
        }

        public Bitmap getImage(string[] keys) {
            IconRequest request = new IconRequest(new Size(64, 64), zip) {
                Reader = IconReaderKind.Ipa,
                Lookup = IconLookupKind.PListKeys,
                Keys = keys,
                RootPrefix = strAppRoot,
                PlistLookup = this,
                Log = Log
            };
            Bitmap icon;
            return IconChain().TryGetIcon(request, out icon) ? icon : null;
        }

        public Bitmap getImage(string name) {
            IconRequest request = new IconRequest(new Size(64, 64), zip) {
                Reader = IconReaderKind.Ipa,
                Lookup = IconLookupKind.ArchivePath,
                Path = name,
                RootPrefix = strAppRoot,
                Log = Log
            };
            Bitmap icon;
            return IconChain().TryGetIcon(request, out icon) ? icon : null;
        }

        /// <summary>The one source this reader needs: iOS Info.plist icons (crushed PNG).</summary>
        private IconSourceChain IconChain() {
            if (iconChain == null) {
                iconChain = new IconSourceChain(Log).Add(new IpaPlistIconSource());
            }
            return iconChain;
        }

        private IconSourceChain iconChain;

        string IPlistIconLookup.FindIconName(string[] keys) {
            string[] images = getStrings(infoPlistDic, keys);
            if (images.Count() > 0) {
                return images[0];
            }
            return "";
        }

        public override AppPackageReader.AppType Type {
            get {
                return AppType.iOSApp;
            }
        }

        public override string AppName {
            get {
                try {
                    string[] n = getStrings(infoPlistDic, new string[] {
                    CFBundleDisplayName});
                    if (n.Count() == 0) {
                        n = getStrings(infoPlistDic, new string[] {
                        FacebookDisplayName });
                    }
                    return n[0];
                } catch {
                    return "";
                }
            }
        }

        public override string Version {
            get {
                try {
                    return getStrings(infoPlistDic, new string[] {
                    CFBundleShortVersionString})[0];
                } catch {
                    return "";
                }
            }
        }

        public override string Revision {
            get {
                try {
                    return getStrings(infoPlistDic, new string[] {
                    CFBundleVersion})[0];
                } catch {
                    return "";
                }
            }
        }

        public override string PackageName {
            get {
                try {
                    return getStrings(infoPlistDic, new string[] {
                    CFBundleIdentifier})[0];
                } catch {
                    return "";
                }
            }
        }
        public override Bitmap Icon {
            get {
                try {
                    Bitmap icon = getImage(new string[] {
                        CFBundleIcons,
                        CFBundlePrimaryIcon,
                        CFBundleIconFiles }); ;
                    if (icon == null) {
                        icon = getImage(new string[] {
                        CFBundleIcons,
                        CFBundlePrimaryIcon,
                        CFBundleIconFile }); ;
                    }
                    if (icon == null) {
                        icon = getImage(new string[] {
                        CFBundleIconFiles });
                    }
                    if (icon == null) {
                        icon = getImage(new string[] {
                        CFBundleIconFile});
                    }
                    if (icon == null) {
                        icon = getImage("AppIcon");
                    }
                    if (icon == null) {
                        icon = getImage("Icon");
                    }
                    return icon;
                } catch {
                    return null;
                }
            }
        }

        public override string Publisher {
            get {
                try {
                    IArchiveEntry itunesMetadata = zip.GetEntry(iTunesMetadataPath);
                    if (itunesMetadata == null)
                        return "";
                    byte[] itunesMetadataBytes = zip.ReadEntryBounded(itunesMetadata, SafeArchive.MAX_METADATA, "iTunesMetadata.plist");
                    itunesMetadataDic = (Dictionary<string, object>)Plist.readPlist(itunesMetadataBytes);
                    return getStrings(itunesMetadataDic, new string[] { flagCopyright })[0];
                } catch {
                    return "";
                }
            }
        }

        public override string AppID {
            get {
                try {
                    IArchiveEntry itunesMetadata = zip.GetEntry(iTunesMetadataPath);
                    if (itunesMetadata == null)
                        return "";
                    byte[] itunesMetadataBytes = zip.ReadEntryBounded(itunesMetadata, SafeArchive.MAX_METADATA, "iTunesMetadata.plist");
                    itunesMetadataDic = (Dictionary<string, object>)Plist.readPlist(itunesMetadataBytes);
                    return getStrings(itunesMetadataDic, new string[] { flagAppId })[0];
                } catch {
                    return "";
                }
            }
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

        ~IpaReader() {
            Dispose(true);
        }
    }
}
