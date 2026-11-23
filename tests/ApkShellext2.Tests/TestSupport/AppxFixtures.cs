using System;
using System.Text;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Appx / AppxBundle fixtures assembled in memory.
    ///
    /// The readers only need XML plus a nested zip, so the fixtures stay hermetic and
    /// no signed package (which would drag in a certificate and a sample library) is
    /// involved. Keep the manifests structurally identical to what Windows emits:
    /// the readers index children by qualified name and require Identity,
    /// Properties/DisplayName, Properties/Logo and mp:PhoneIdentity to exist.
    /// </summary>
    internal static class AppxFixtures
    {
        public const string AppxManifestEntry = "AppxManifest.xml";
        public const string BundleManifestEntry = "AppxMetadata/AppxBundleManifest.xml";

        public const string DefaultAppName = "Test App";
        public const string DefaultVersion = "1.2.3.4";
        public const string DefaultPackageName = "Test.App";
        public const string DefaultPublisher = "CN=Test Publisher, O=Test Org";
        public const string DefaultProductId = "{11111111-2222-3333-4444-555555555555}";
        public const string DefaultLogo = @"Assets\Logo.png";

        public static string AppxManifest(
            string appName = DefaultAppName,
            string version = DefaultVersion,
            string packageName = DefaultPackageName,
            string publisher = DefaultPublisher,
            string logo = DefaultLogo,
            string productId = DefaultProductId)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.Append("<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"");
            xml.Append(" xmlns:mp=\"http://schemas.microsoft.com/appx/2014/phone/manifest\">");
            xml.Append("<Identity Name=\"").Append(XmlEscape(packageName)).Append("\"");
            xml.Append(" Version=\"").Append(XmlEscape(version)).Append("\"");
            xml.Append(" Publisher=\"").Append(XmlEscape(publisher)).Append("\" />");
            xml.Append("<Properties>");
            xml.Append("<DisplayName>").Append(XmlEscape(appName)).Append("</DisplayName>");
            xml.Append("<Logo>").Append(XmlEscape(logo)).Append("</Logo>");
            xml.Append("</Properties>");
            xml.Append("<mp:PhoneIdentity PhoneProductId=\"").Append(XmlEscape(productId)).Append("\"");
            xml.Append(" PhonePublisherId=\"{00000000-0000-0000-0000-000000000000}\" />");
            xml.Append("</Package>");
            return xml.ToString();
        }

        /// <summary>An .appx zip whose only payload is a valid AppxManifest.xml.</summary>
        public static byte[] Appx(
            string appName = DefaultAppName,
            string version = DefaultVersion,
            string packageName = DefaultPackageName,
            string publisher = DefaultPublisher,
            string logo = DefaultLogo,
            string productId = DefaultProductId)
        {
            return ZipBuilder.Build(new ZipBuilder.EntrySpec
            {
                Name = AppxManifestEntry,
                Data = Encoding.UTF8.GetBytes(AppxManifest(appName, version, packageName, publisher, logo, productId))
            });
        }

        /// <summary>A bundle manifest pointing at one nested .appx.</summary>
        public static string BundleManifest(string fileName, int resourcePackages = 0)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.Append("<Bundle xmlns=\"http://schemas.microsoft.com/appx/2013/bundle\">");
            xml.Append("<Identity Name=\"Test.Bundle\" Version=\"9.9.9.9\" />");
            xml.Append("<Packages>");
            for (int i = 0; i < resourcePackages; i++)
            {
                xml.Append("<Package Type=\"resource\" FileName=\"resource").Append(i).Append(".appx\" />");
            }
            if (fileName != null)
            {
                xml.Append("<Package Type=\"application\" FileName=\"").Append(XmlEscape(fileName)).Append("\" />");
            }
            xml.Append("</Packages>");
            xml.Append("</Bundle>");
            return xml.ToString();
        }

        /// <summary>A bundle zip: manifest plus the nested .appx it points at.</summary>
        public static byte[] Bundle(byte[] innerAppx, string fileName = "Test.appx")
        {
            return ZipBuilder.Build(
                new ZipBuilder.EntrySpec
                {
                    Name = BundleManifestEntry,
                    Data = Encoding.UTF8.GetBytes(BundleManifest(fileName))
                },
                new ZipBuilder.EntrySpec
                {
                    Name = fileName,
                    Data = innerAppx
                });
        }

        private static string XmlEscape(string value)
        {
            return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}

