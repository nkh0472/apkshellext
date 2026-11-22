using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using ApkQuickReader;
using ApkShellext2.Properties;
using System.Globalization;
using System.Xml;
using System.Resources;
using System.Drawing;
using System.Drawing.Drawing2D;
using WebPWrapper;

namespace ApkShellext2
{
    /// <summary>
    /// A Android App Bundler Reader
    /// Not implemented yet
    /// </summary>
    public class AabReader: AppPackageReader
    {
        private IArchive zip;
        private byte[] manifest;

        private const string AndroidManifestXML = @"base/manifest/androidmanifest.xml";
        private const string TagApplication = @"manifest/application";
        private const string TagManifest = @"manifest";
        private const string AttrLabel = @"label";
        private const string AttrVersionName = @"versionName";
        private const string AttrVersionCode = @"versionCode";
        private const string AttrIcon = @"icon";
        private const string AttrPackage = @"package";

        private const string ConstTrue = "true";
        private const string ConstFalse = "false";

        public AabReader(string path) 
        {
            Log("create AabReader new");
            FileName = path;
            // FileShare.Delete so Explorer can delete/rename the package while handlers are alive (kkguo/apkshellext#103)
            openStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
        }


        private void openStream(Stream stream)
        {
            zip = SafeArchive.Open(stream);
            IArchiveEntry en = zip.GetEntry(AndroidManifestXML);
            Log("find manifest: " + (en == null).ToString());
            // bounded read: attacker-declared entry sizes must never drive
            // (int) casts or buffer allocation in explorer's process
            manifest = zip.ReadEntryBounded(en, SafeArchive.MAX_MANIFEST, "AndroidManifest.xml");
        }

        public override AppPackageReader.AppType Type
        {
            get
            {
                return AppType.AndroidAab;
            }
        }

        public override string AppName
        {
            get
            {
                return getAttribute(TagApplication, AttrLabel);
            }
        }
        public override string Version
        {
            get
            {
                return getAttribute(TagManifest, AttrVersionName);
            }
        }
        public override string Revision
        {
            get
            {
                return getAttribute(TagManifest, AttrVersionCode);
            }
        }

        public override Bitmap Icon => getImage(TagApplication, AttrIcon, new Size(64, 64));

        public override string PackageName => getAttribute(TagManifest, AttrPackage);

        // get an icon with size
        public override Bitmap getIcon(Size size)
        {
            Bitmap icon = Utility.AppTypeIcon(AppPackageReader.AppType.AndroidApp);
            return icon;
            //return getImage(TagApplication, AttrIcon, size);
        }
        /// <summary>
        /// Get a Image object from manifest and resources
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="attr"></param>
        /// <returns></returns>
        public Bitmap getImage(string tag, string attr, Size size)
        {
            Log(tag+ "..." + attr + "..." + size.ToString());
            //if (getFlag("Density") == null)
            //{
            //    setFlag("Density", 1);
            //}
            //string path = QuickSearchManifestXml(tag, attr);
            //if (path == "")
            //{
            //    Log("Cannot find image for <" + tag + ">.<" + attr + ">");
            //    throw new Exception("Cannot find image for <" + tag + ">.<" + attr + ">");
            //}
            string path = "ic_launcher.png";
            try
            {
                if (path != "")
                {
                    return getImage(path, size);
                }
                throw new Exception("Cannot found image " + tag + "@" + attr);
            }
            catch (Exception ex)
            {
                Log("Image path : " + path);
                Log("Error happens during extracting image, " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// return the bitmap of a image file path located in apk zip
        /// accepting .png, adaptive-icon or vectordrawable
        /// </summary>
        /// <param name="path">png or xml file within apk zip</param>
        /// <returns>Bitmap or </returns>
        public Bitmap getImage(string path, Size size)
        {
            IArchiveEntry iconz;
            if (zip.Contains(path))
            {
                iconz = zip.GetEntryExact(path);
                // case-insensitive extension dispatch, see kkguo/apkshellext#99
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                    ext == ".gif" || ext == ".tif" || ext == ".tiff")
                {
                    // fully buffer before decoding: GDI+ decodes PNG lazily and renders BLANK pixels when
                    // scaled from a non-seekable zip entry stream after it is closed
                    byte[] imgBytes = zip.ReadEntryBounded(iconz, SafeArchive.MAX_ICON, "image " + path);
                    using (MemoryStream ms = new MemoryStream(imgBytes, 0, imgBytes.Length, false, false))
                        return Utility.ResizeBitmap((Bitmap)Image.FromStream(ms), size);
                }
                else if (ext == ".webp")
                {
                    WebP webp = new WebP();
                    byte[] bytes = zip.ReadEntryBounded(iconz, SafeArchive.MAX_ICON, "webp " + path);
                    //webp.Decode(bytes).Save(FileName+".webp");
                    return Utility.ResizeBitmap(webp.Decode(bytes), size);
                }
                else if (ext == ".xml")
                {
                    XmlDocument doc = ExtractCompressedXml(path);
                    if (doc.FirstChild.Name == "adaptive-icon")
                    {
                        return parseAdaptiveIcon(doc, size);
                    }
                    else if (doc.FirstChild.Name == "vector")
                    { // this is a vectordrawable
                        return parseVectorDrawable(doc, size);
                    }
                    else if (doc.FirstChild.Name == "shape")
                    {
                        return parseShape(doc, size);
                    }
                    else if (doc.FirstChild.Name == "layer-list")
                    {
                        return parseLayerDrawable(doc, size);
                    }
                    else
                    {
                        throw new Exception("unsupported image file " + path + " with tag " + doc.FirstChild.Name);
                    }
                }
            }
            return null;
        }

        private Bitmap parseLayerDrawable(XmlNode node, Size size)
        {
            try
            {
                XmlNodeList nl = node.SelectNodes("/layer-list/item");
                List<Bitmap> bitmapList = new List<Bitmap>(nl.Count);
                foreach (XmlElement e in nl)
                {
                    if (e.HasAttribute("drawable"))
                    {
                        Bitmap b = getImage(e.GetAttribute("drawable"), size);
                        bitmapList.Add(b);
                    }
                    else
                    {
                        XmlElement shape = (XmlElement)e.SelectSingleNode("shape");
                        if (shape != null)
                        {
                            bitmapList.Add(parseShape(shape, size));
                        }
                        else
                        {
                            throw new Exception("unrecorgnized element in layer-list.");
                        }
                    }
                }
                Bitmap bmp = new Bitmap(size.Width, size.Height);
                Graphics g = Graphics.FromImage(bmp);
                foreach (Bitmap b in bitmapList)
                {
                    g.DrawImage(Utility.ResizeBitmap(b, new Size(bmp.Width, bmp.Height)), 0, 0);
                }
                return bmp;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happens during parsing layer-list: " + node.InnerXml);
            }
        }

        private Bitmap parseVectorDrawable(XmlNode node, Size size)
        {
            // Preferred path: translate the VectorDrawable to SVG and let the vendored
            // Svg.NET renderer do the drawing (transforms, gradients, clips are mature
            // there). The legacy GDI+ walker remains as fallback for anything the
            // translator refuses. Mirrors ApkQuickReader.parseVectorDrawable.
            try
            {
                XmlElement vec = node as XmlElement;
                if (vec == null) vec = node.SelectSingleNode("/vector") as XmlElement;
                if (vec != null)
                {
                    string svg = VectorDrawableToSvg.Translate(vec, new Func<string, XmlDocument>(ExtractCompressedXml));
                    Svg.SvgDocument svgDoc = Svg.SvgDocument.FromSvg<Svg.SvgDocument>(svg);
                    if (svgDoc != null)
                    {
                        using (Bitmap rendered = svgDoc.Draw(size.Width, size.Height))
                        {
                            if (rendered != null) return new Bitmap(rendered);
                        }
                    }
                }
            }
            catch (Exception exSvg)
            {
                Log("svg vector render fallback for " + size.Width + "px: " + exSvg.Message);
            }
            try
            {
                return parseVectorDrawableLegacy(node, size);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happens during parsing vectordrawable: " + node.InnerXml);
            }
        }

        private Bitmap parseVectorDrawableLegacy(XmlNode node, Size size)
        {
            try
            {
                XmlElement vector = (XmlElement)node.SelectSingleNode("/vector");
                float viewportWidth = 0, viewportHeight = 0;
                // hostile XML may carry "NaN"/garbage where a float is expected -
                // parse leniently and let ClampCanvas decide, never crash the icon thread
                viewportWidth = GetXmlFloat(vector, "viewportWidth", 0f);
                viewportHeight = GetXmlFloat(vector, "viewportHeight", 0f);
                if (viewportWidth == 0) viewportWidth = viewportHeight;
                if (viewportHeight == 0) viewportHeight = viewportWidth;
                // android:width/android:height and their units are advisory only for the
                // legacy renderer: the canvas is sized from the clamped viewport below and
                // resized to the caller's icon size afterwards. (Upstream parsed them into
                // locals it never read; the dead parse also rejected hostile width values
                // that the viewport path handles fine.)
                // viewport comes from attacker XML: a huge/NaN value would OOM explorer's
                // process via the intermediate canvas. Real adaptive vectors are ≤ 108dp.
                Bitmap b = new Bitmap(ClampCanvas(viewportWidth), ClampCanvas(viewportHeight));
                using (Graphics g = Graphics.FromImage(b))
                {
                    DrawVectorGroup(g, vector, new Matrix());
                }
                Bitmap legacyResized = Utility.ResizeBitmap(b, size);
                b.Dispose(); // native-size canvas is dead once resized
                return legacyResized;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happens during parsing vectordrawable: " + node.InnerXml);
            }
        }

        private static int ClampCanvas(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 1f) return 1;
            // icons are rasterised at <=256px by every caller; 1024 keeps even
            // 8x supersampling honest while a hostile viewport can no longer
            // demand a 4096x4096 (64 MB) bitmap
            return (int)Math.Min(v, 1024f);
        }

        private static float GetXmlFloat(XmlElement elem, string name, float def)
        {
            if (!elem.HasAttribute(name)) return def;
            float v;
            return float.TryParse(elem.GetAttribute(name), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def;
        }

        /// <summary>
        /// Fills paths of a &lt;vector&gt; or nested &lt;group&gt;, honouring the group
        /// transforms (scale/rotation about pivot, then translation) defined by Android
        /// VectorDrawable. Older code read only the paths of one first-level group and
        /// silently ignored every transform, misplacing artwork such as the adaptive
        /// foreground of 'Light Start' (drawn upper-left instead of centered).
        /// </summary>
        private void DrawVectorGroup(Graphics g, XmlElement parent, Matrix world)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                XmlElement elem = child as XmlElement;
                if (elem == null) continue;
                if (elem.Name == "path")
                {
                    try
                    {
                        string pathdata = elem.GetAttribute("pathData");
                        if (pathdata == "") continue;
                        GraphicsPath gpath = VectorDrawableRender.Convert2Path(pathdata);
                        gpath.Transform(world);
                        Brush fill = null;
                        if (elem.HasAttribute("fillColor"))
                        {
                            string fillcolor = elem.GetAttribute("fillColor");
                            if (fillcolor.EndsWith(".xml"))
                            {//gradient or indirect color resource
                                try
                                {
                                    fill = parseGradient(fillcolor);
                                }
                                catch (Exception exGrad)
                                {
                                    Log("vector gradient failed, black fill: " + exGrad.Message.Split('\n')[0]);
                                }
                            }
                            else
                            {
                                try { fill = new SolidBrush(stringToColor(fillcolor)); }
                                catch { /* color resource ref - painted black below */ }
                            }
                        }
                        if (fill == null) fill = new SolidBrush(System.Drawing.Color.Black);
                        g.FillPath(fill, gpath);
                    }
                    catch (Exception exPath)
                    {
                        // one unparseable path (clipped/animated art) must not kill the icon
                        Log("vector path skipped: " + exPath.Message.Split('\n')[0]);
                    }
                }
                else if (elem.Name == "group")
                {
                    Matrix m = (Matrix)world.Clone();
                    float pivotX = GetXmlFloat(elem, "pivotX", 0f);
                    float pivotY = GetXmlFloat(elem, "pivotY", 0f);
                    float sx = GetXmlFloat(elem, "scaleX", 1f);
                    float sy = GetXmlFloat(elem, "scaleY", 1f);
                    float rot = GetXmlFloat(elem, "rotation", 0f);
                    // Android applies scale then rotation about the pivot, then translation;
                    // appending mirrors that order for point transformation.
                    if (sx != 1f || sy != 1f) m.Scale(sx, sy, MatrixOrder.Append);
                    if (pivotX != 0f || pivotY != 0f) m.Translate(-pivotX, -pivotY, MatrixOrder.Append);
                    if (rot != 0f) m.Rotate(rot, MatrixOrder.Append);
                    if (pivotX != 0f || pivotY != 0f) m.Translate(pivotX, pivotY, MatrixOrder.Append);
                    m.Translate(GetXmlFloat(elem, "translateX", 0f), GetXmlFloat(elem, "translateY", 0f), MatrixOrder.Append);
                    DrawVectorGroup(g, elem, m);
                    m.Dispose();
                }
            }
            world.Dispose();
        }

        private Bitmap parseAdaptiveIcon(XmlNode node, Size size)
        {
            /// Get adaptive - icon
            /// 
            /*
            <?xml version="1.0" encoding="utf-8"?>
            <adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
            <background android:drawable="@drawable/ic_launcher_background" />
            <foreground android:drawable="@drawable/ic_launcher_foreground" />
            </adaptive-icon>
            */
            try
            {
                XmlElement elem = (XmlElement)node.SelectSingleNode("/adaptive-icon/background");
                Bitmap b = null, f = null;
                Bitmap bmp = new Bitmap(size.Width, size.Height);
                Graphics g = Graphics.FromImage(bmp);
                if (elem.HasAttribute("drawable"))
                {
                    b = getImage(elem.GetAttribute("drawable"), size);
                    if (b != null)
                        g.DrawImage(b, 0, 0);
                    else
                    {
                        Color c = stringToColor(elem.GetAttribute("drawable"));
                        g.FillRectangle(new SolidBrush(c), 0, 0, bmp.Width, bmp.Height);
                    }
                }
                elem = (XmlElement)node.SelectSingleNode("/adaptive-icon/foreground");
                if (elem.HasAttribute("drawable"))
                {
                    f = getImage(elem.GetAttribute("drawable"), size);
                    if (f != null)
                        g.DrawImage(f, 0, 0);
                    else
                    {
                        Color c = stringToColor(elem.GetAttribute("drawable"));
                        g.FillRectangle(new SolidBrush(c), 0, 0, bmp.Width, bmp.Height);
                    }
                }
                return bmp;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happening during parse AdaptiveIcon.");
            }
        }

        private Brush parseGradient(XmlNode node)
        {
            try
            {
                XmlElement gradient = (XmlElement)node.FirstChild;
                gradientType type = (gradientType)int.Parse(gradient.GetAttribute("type"));
                if (type == gradientType.linear)
                {
                    Color startC = stringToColor(gradient.GetAttribute("startColor"));
                    Color endC = stringToColor(gradient.GetAttribute("endColor"));
                    float startX = float.Parse(gradient.GetAttribute("startX"));
                    float startY = float.Parse(gradient.GetAttribute("startY"));
                    float endX = float.Parse(gradient.GetAttribute("endX"));
                    float endY = float.Parse(gradient.GetAttribute("endY"));
                    return new LinearGradientBrush(new PointF(startX, startY), new PointF(endX, endY), startC, endC);
                }
                else if (type == gradientType.radial)
                {
                    XmlNodeList points = node.SelectNodes("/gradient/item");
                    int cx = int.Parse(gradient.GetAttribute("centerX"));
                    int cy = int.Parse(gradient.GetAttribute("centerY"));
                    float radius = float.Parse(gradient.GetAttribute("gradientRadius"));
                    List<Color> colors = new List<Color>(points.Count);
                    List<float> offsets = new List<float>(points.Count);
                    foreach (XmlElement p in points)
                    {
                        string fillcolor = p.GetAttribute("color");
                        colors.Add(stringToColor(p.GetAttribute("color")));
                        offsets.Add(float.Parse(p.GetAttribute("offset")));
                    }
                    return new SolidBrush(colors[colors.Count - 1]);
                    //fill = new Media.RadialGradientBrush();
                }
                else if (type == gradientType.sweep)
                {
                    return null;
                }
                else
                {
                    throw new Exception("Unknown gradient type when parsing gradient xml");
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happened during parse Gradient " + node.InnerXml);
            }
        }

        private Brush parseGradient(string xml)
        {
            try
            {
                XmlDocument doc = ExtractCompressedXml(xml);
                if (doc.FirstChild.Name == "gradient")
                {
                    return parseGradient(doc);
                }
                else
                {
                    throw new Exception("Doesn't find a gradient XML");
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happens during parsing gradient xml " + xml);
            }
        }

        private Bitmap parseShape(XmlNode shapeNode, Size size)
        {
            try
            {
                Bitmap b;
                XmlElement eShape;
                if (shapeNode.NodeType == XmlNodeType.Document)
                    eShape = ((XmlDocument)shapeNode).DocumentElement;
                else
                    eShape = (XmlElement)shapeNode;
                shapeType type = (shapeType)int.Parse(eShape.GetAttribute("shape"));
                XmlElement eSize = (XmlElement)shapeNode.SelectSingleNode("size");
                if (eSize != null)
                    b = new Bitmap(int.Parse(eSize.GetAttribute("width")), int.Parse(eSize.GetAttribute("height")));
                else
                {
                    b = new Bitmap(size.Width, size.Height);
                }
                Graphics g = Graphics.FromImage(b);
                GraphicsPath p = new GraphicsPath();
                Brush brush;
                XmlElement ebrush = (XmlElement)eShape.SelectSingleNode("solid");
                if (ebrush != null)
                {
                    brush = new SolidBrush(stringToColor(ebrush.GetAttribute("color")));
                }
                else
                {
                    if ((ebrush = (XmlElement)eShape.SelectSingleNode("gradient")) != null)
                    {
                        brush = parseGradient(ebrush);
                    }
                    else
                        brush = new SolidBrush(Color.Black);
                }
                if (type == shapeType.rectangle)
                {
                    XmlElement corners = (XmlElement)eShape.SelectSingleNode("corners");
                    if (corners != null)
                    {
                        //ToDo, support round corner
                    }
                    p.AddRectangle(new RectangleF(0, 0, b.Width, b.Height));
                }
                else if (type == shapeType.oval)
                {
                    p.AddEllipse(0, 0, b.Width, b.Height);
                }
                else if (type == shapeType.line)
                {
                    // todo: support line

                }
                else if (type == shapeType.ring)
                {
                    // todo: support ring
                }
                else
                {
                    throw new Exception("Unsupported shape type: " + (int)type);
                }
                g.FillPath(brush, p);
                return Utility.ResizeBitmap(b, size);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happening during parsing shape:" + shapeNode.InnerXml);
            }
        }

        private Color stringToColor(string color)
        {
            color = color.Trim();
            if (color.StartsWith("#"))
            {
                return Color.FromArgb((int)uint.Parse(color.Substring(1), NumberStyles.HexNumber));
            }
            else
            {
                return Color.FromArgb((int)uint.Parse(color));
            }
        }


        public string getAttribute(string tag, string attr)
        {
            return QuickSearchManifestXml(tag, attr);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="attribute"></param>
        /// <returns></returns>
        public string QuickSearchManifestXml(string tag, string attribute)
        {
            return QuickSearchCompressedXml(manifest, tag, attribute);
        }

        /// <summary>
        /// The binary XML walk lives in the shared AxmlParser. An aab carries no
        /// resources.arsc, so reference attributes keep the bare number rendering they
        /// had before the parsers were merged instead of resolving through a table that
        /// does not exist.
        /// </summary>
        private AxmlParser NewAxmlParser()
        {
            return new AxmlParser(null, false, false, false, Log);
        }

        private string QuickSearchCompressedXml(byte[] xml, string xpath, string attribute)
        {
            return NewAxmlParser().QuickSearchCompressedXml(xml, xpath, attribute, false);
        }

        public XmlDocument ExtractCompressedXml(string path)
        {
            try
            {
                IArchiveEntry en = zip.GetEntryExact(path);
                byte[] bytes = zip.ReadEntryBounded(en, SafeArchive.MAX_XML, "xml " + path);
                return NewAxmlParser().ExtractCompressedXml(bytes);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + "\nError happens during dump " + path);
            }
        }

        #region IDispose
        private bool disposed = false;
        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                // resources = null;
                manifest = null;
                if (zip != null)
                    zip.Dispose();
            }
            disposed = true;
            base.Dispose(disposing);
        }

        public void Close()
        {
            Dispose(true);
        }

        ~AabReader()
        {
            Dispose(true);
        }
        #endregion
    }
}
