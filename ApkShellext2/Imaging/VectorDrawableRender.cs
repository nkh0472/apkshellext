using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Svg;

namespace ApkShellext2
{
    /// <summary>
    /// Converts Android VectorDrawable path-data ("android:pathData") into a
    /// GDI+ GraphicsPath via the vendored Svg.NET path parser.
    ///
    /// Only the path conversion lives here. Full VectorDrawable rendering goes through
    /// VectorDrawableToSvg + SvgDocument.Draw in ApkQuickReader; the AAB reader and the
    /// GDI+ fallback walker still draw with this converter because they rasterise
    /// through GDI+ themselves.
    /// </summary>
    public class VectorDrawableRender
    {
        public static GraphicsPath Convert2Path(string pathdata) {
            GraphicsPath path = new GraphicsPath(FillMode.Alternate);
            try {
                // Svg 3.5.0: segment.AddToPath(GraphicsPath) is obsolete — it passes a null
                // segment list, which breaks relative commands. Thread the current point through
                // the new overload exactly like SvgPath.Drawing.cs does, and manage figures
                // ourselves so GraphicsPath never lineTo's from the implicit (0,0).
                Svg.Pathing.SvgPathSegmentList l = SvgPathBuilder.Parse(pathdata);
                PointF cur = PointF.Empty;
                bool figureOpen = false;
                foreach (Svg.Pathing.SvgPathSegment s in l) {
                    if (s is Svg.Pathing.SvgMoveToSegment) {
                        if (figureOpen)
                            path.CloseFigure();
                        path.StartFigure();
                        figureOpen = true;
                        cur = s.End;
                        continue;
                    }
                    if (!figureOpen) {
                        path.StartFigure();
                        figureOpen = true;
#pragma warning disable CS0618 // SvgPathSegment.Start is flagged "will be removed" in the
                        // pinned Svg 3.5.0 but has no successor API yet; keep reading it as-is
                        if (s.Start.IsEmpty)
                            cur = PointF.Empty;
#pragma warning restore CS0618
                    }
                    if (s is Svg.Pathing.SvgClosePathSegment) {
                        path.CloseFigure();
                        figureOpen = false;
                        cur = (l.Count > 0) ? l[0].End : PointF.Empty;
                        continue;
                    }
                    cur = s.AddToPath(path, cur, l);
                }
            } catch (Exception e){
                Utility.Log(null, "VectorDrawable, d=" + pathdata, e.Message);
            }
            return path;
        }
    }
}
