using System;
using System.Collections;
using System.Globalization;
using NUnit.Framework;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Behaviour pin for the SVG parsers, which read string slices (TextSlice) instead of
    /// Span&lt;char&gt;. These tests assert the numbers, unit kinds, path segments and
    /// transform text those parsers produce - including the odd corners (an unknown
    /// transform name is skipped silently, a truncated path stops without throwing,
    /// "NONE" is not the same token as "none").
    ///
    /// Everything goes through the shipped public Parse(string) entry points by
    /// reflection, the same way the other guard tests reach non-public product code:
    /// the product sources stay untouched by the test project.
    /// </summary>
    [TestFixture]
    public class SvgParserTests
    {
        private const string NumberListType = "Svg.SvgNumberCollectionConverter";
        private const string UnitType = "Svg.SvgUnitConverter";
        private const string UnitListType = "Svg.SvgUnitCollectionConverter";
        private const string TransformListType = "Svg.Transforms.SvgTransformConverter";
        private const string PathType = "Svg.SvgPathBuilder";

        [Test]
        public void ParsesSignedDecimalAndExponentNumbers()
        {
            Assert.That(
                Numbers("1 2.5 -3e2, .5"),
                Is.EqualTo(new[] { 1f, 2.5f, -300f, 0.5f }),
                "signed values, decimals, exponents and a comma separator all belong to one list");
            Assert.That(Numbers(".5e-3 7E+2"), Is.EqualTo(new[] { 0.0005f, 700f }));
        }

        [Test]
        public void SkipsLeadingTrailingAndRepeatedSeparators()
        {
            Assert.That(Numbers(" 1 2 "), Is.EqualTo(new[] { 1f, 2f }));
            Assert.That(Numbers("1,,2"), Is.EqualTo(new[] { 1f, 2f }), "empty pieces between separators are skipped");
            Assert.That(Numbers("1\t2\n3\r4"), Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
            Assert.That(Numbers(string.Empty), Is.Empty);
        }

        [Test]
        public void ParsesNumbersWithTheInvariantCulture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                // de-DE reads "1.5" as fifteen; the parser must not follow the host culture.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(Numbers("1.5 1000"), Is.EqualTo(new[] { 1.5f, 1000f }));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Test]
        public void RejectsATokenThatIsNotANumber()
        {
            // The message comes from float.Parse and is localised, so only the type is pinned.
            Assert.Throws<FormatException>(() => Numbers("1 x"));
        }

        [Test]
        public void ParsesEveryUnitSuffix()
        {
            AssertUnit("12px", 12f, "Pixel");
            AssertUnit("0.5in", 0.5f, "Inch");
            AssertUnit("3pt", 3f, "Point");
            AssertUnit("2mm", 2f, "Millimeter");
            AssertUnit("2cm", 2f, "Centimeter");
            AssertUnit("10pc", 10f, "Pica");
            AssertUnit("1.5em", 1.5f, "Em");
            AssertUnit("2ex", 2f, "Ex");
            AssertUnit("50%", 50f, "Percentage");
        }

        [Test]
        public void ParsesUnitlessAndKeywordUnits()
        {
            AssertUnit("36", 36f, "User");
            AssertUnit(" 7 ", 7f, "User");
            AssertUnit("none", 0f, "None");
            AssertUnit("medium", 1f, "Em");
        }

        [Test]
        public void AcceptsSpacingAndUpperCaseUnitSuffixes()
        {
            AssertUnit("12 px", 12f, "Pixel");
            AssertUnit("12PX", 12f, "Pixel");
            // 'e' is the exponent marker, not the start of a unit, unless a letter follows.
            AssertUnit("1e2px", 100f, "Pixel");
            AssertUnit("-4px", -4f, "Pixel");
        }

        [Test]
        public void RejectsAnUnknownUnitSuffix()
        {
            // The keyword table is case sensitive, so "NONE" falls through to the number
            // parse of an empty value and surfaces as a plain format error.
            FormatException unknownSuffix = Assert.Throws<FormatException>(() => ProductApi.ParseSvg(UnitType, "12foo"));
            Assert.That(unknownSuffix.Message, Is.EqualTo("Unit is in an invalid format '12foo'."));

            Assert.Throws<FormatException>(() => ProductApi.ParseSvg(UnitType, "NONE"));
            Assert.Throws<FormatException>(() => ProductApi.ParseSvg(UnitType, "px"));
            Assert.Throws<FormatException>(() => ProductApi.ParseSvg(UnitType, string.Empty));
        }

        [Test]
        public void ParsesUnitLists()
        {
            Assert.That(
                UnitDescriptions("1px, 2em 3%"),
                Is.EqualTo(new[] { "1:Pixel", "2:Em", "3:Percentage" }));
            Assert.That(UnitDescriptions(" 4 5 "), Is.EqualTo(new[] { "4:User", "5:User" }));
            Assert.That(UnitDescriptions("1px  2px"), Is.EqualTo(new[] { "1:Pixel", "2:Pixel" }));
        }

        [Test]
        public void DropsTheNoneKeywordFromUnitLists()
        {
            // "none" means "no dash array", so the converter yields an empty collection
            // instead of a single None unit.
            Assert.That(UnitDescriptions("none"), Is.Empty);
            Assert.That(UnitDescriptions(string.Empty), Is.Empty);
        }

        [Test]
        public void ParsesACombinedTransformList()
        {
            Assert.That(
                TransformStrings("matrix(1,0,0,1,10,20) translate(5 6) scale(2) rotate(45) skewX(10)"),
                Is.EqualTo(new[]
                {
                    "matrix(1, 0, 0, 1, 10, 20)",
                    "translate(5, 6)",
                    "scale(2)",
                    "rotate(45, 0, 0)",
                    "skewX(10)",
                }));
        }

        [Test]
        public void ParsesTransformsWithoutWhitespaceAndWithPadding()
        {
            Assert.That(
                TransformStrings("translate(5,6)scale(2,3)"),
                Is.EqualTo(new[] { "translate(5, 6)", "scale(2, 3)" }));
            Assert.That(TransformStrings(" rotate( 1, 2, 3 ) "), Is.EqualTo(new[] { "rotate(1, 2, 3)" }));
            Assert.That(TransformStrings("skewY(4)"), Is.EqualTo(new[] { "skewY(4)" }));
            Assert.That(TransformStrings("shear(1 2)"), Is.EqualTo(new[] { "shear(1, 2)" }));
            Assert.That(TransformStrings("translate(1 2) translate(3)"), Is.EqualTo(new[] { "translate(1, 2)", "translate(3, 0)" }));
        }

        [Test]
        public void SkipsTransformsWithAnUnknownName()
        {
            // The name lookup compares seven known names exactly; anything else is
            // dropped silently, and an empty list is a valid "no transform" answer.
            Assert.That(TransformStrings("foo(1)"), Is.Empty);
            Assert.That(TransformStrings(string.Empty), Is.Empty);
        }

        [Test]
        public void RejectsTransformsWithTheWrongArgumentCount()
        {
            AssertTransformError(
                "translate(1 2 3)",
                "Translate transforms must be in the format 'translate(x [y])'");
            AssertTransformError(
                "matrix(1 2 3 4 5)",
                "Matrix transforms must be in the format 'matrix(m11 m12 m21 m22 dx dy)'");
            AssertTransformError(
                "rotate(1 2)",
                "Rotate transforms must be in the format 'rotate(angle [cx cy])'");
            AssertTransformError(
                "scale()",
                "Scale transforms must be in the format 'scale(x [y])'");
        }

        [Test]
        public void ParsesAMoveLineClosePath()
        {
            string[] segments = Segments("M10 20L30 40z");

            Assert.That(segments, Is.EqualTo(new[]
            {
                "SvgMoveToSegment(10,20)rel=False",
                "SvgLineSegment(30,40)rel=False",
                "SvgClosePathSegment(0,0)rel=True",
            }));
        }

        [Test]
        public void RepeatsTheLastPathCommandForExtraCoordinates()
        {
            // A single MoveTo followed by four numbers is a move plus two implicit lines.
            Assert.That(Segments("M10 20 30 40 50 60"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(10,20)rel=False",
                "SvgLineSegment(30,40)rel=False",
                "SvgLineSegment(50,60)rel=False",
            }));
        }

        [Test]
        public void KeepsLowercaseCommandsRelative()
        {
            Assert.That(Segments("m10 20 l 5 5"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(10,20)rel=True",
                "SvgLineSegment(5,5)rel=True",
            }));
            Assert.That(Segments("M1 2 Z")[1], Is.EqualTo("SvgClosePathSegment(0,0)rel=False"));
        }

        [Test]
        public void UsesNaNForTheOmittedCoordinateOfHorizontalAndVerticalLines()
        {
            Assert.That(Segments("M0 0 H30 V40"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(0,0)rel=False",
                "SvgLineSegment(30,NaN)rel=False",
                "SvgLineSegment(NaN,40)rel=False",
            }));
        }

        [Test]
        public void ParsesCurveAndArcCommands()
        {
            Assert.That(Segments("M1 2 C3 4 5 6 7 8 S9 10 11 12"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(1,2)rel=False",
                "SvgCubicCurveSegment(7,8)rel=False",
                "SvgCubicCurveSegment(11,12)rel=False",
            }));
            Assert.That(Segments("M1 2 Q3 4 5 6 T7 8"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(1,2)rel=False",
                "SvgQuadraticCurveSegment(5,6)rel=False",
                "SvgQuadraticCurveSegment(7,8)rel=False",
            }));
            Assert.That(Segments("M1 2 A5 5 0 0 1 10 10"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(1,2)rel=False",
                "SvgArcSegment(10,10)rel=False",
            }));
        }

        [Test]
        public void TruncatedOrEmptyPathDataStopsWithoutThrowing()
        {
            // The path parser historically tolerated partial data: it consumes what it
            // can and leaves the rest. Pinning it keeps a stricter rewrite from turning
            // a broken icon into a failed thumbnail.
            Assert.That(Segments(string.Empty), Is.Empty);
            Assert.That(Segments("M"), Is.Empty);
            Assert.That(Segments("10 20 30 40"), Is.Empty, "numbers without a command produce no segment");
            Assert.That(Segments("M1 2 L3"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(1,2)rel=False",
            }));
            Assert.That(Segments("M1 2 L3 x"), Is.EqualTo(new[]
            {
                "SvgMoveToSegment(1,2)rel=False",
            }));
        }

        private static void AssertUnit(string text, float value, string type)
        {
            object unit = ProductApi.ParseSvg(UnitType, text);
            float actual = (float)ProductApi.ReadSvgProperty(unit, "Value");
            Assert.That(actual, Is.EqualTo(value), "value of unit '" + text + "'");
            Assert.That(
                ProductApi.ReadSvgProperty(unit, "Type").ToString(),
                Is.EqualTo(type),
                "unit type of '" + text + "'");
        }

        private static void AssertTransformError(string text, string message)
        {
            FormatException error = Assert.Throws<FormatException>(() => ProductApi.ParseSvg(TransformListType, text));
            Assert.That(error.Message, Is.EqualTo(message));
        }

        private static IList Parse(string typeName, string value)
        {
            return (IList)ProductApi.ParseSvg(typeName, value);
        }

        private static float[] Numbers(string value)
        {
            IList numbers = Parse(NumberListType, value);
            float[] result = new float[numbers.Count];
            for (int i = 0; i < numbers.Count; i++)
            {
                result[i] = (float)numbers[i];
            }

            return result;
        }

        private static string[] UnitDescriptions(string value)
        {
            IList units = Parse(UnitListType, value);
            string[] result = new string[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                result[i] =
                    ((float)ProductApi.ReadSvgProperty(units[i], "Value")).ToString(CultureInfo.InvariantCulture) +
                    ":" + ProductApi.ReadSvgProperty(units[i], "Type");
            }

            return result;
        }

        private static string[] TransformStrings(string value)
        {
            IList transforms = Parse(TransformListType, value);
            string[] result = new string[transforms.Count];
            for (int i = 0; i < transforms.Count; i++)
            {
                result[i] = ProductApi.CallSvgMethod(transforms[i], "WriteToString");
            }

            return result;
        }

        private static string[] Segments(string path)
        {
            object segments = ProductApi.ParseSvg(PathType, path);
            string[] result = new string[ProductApi.SvgCount(segments)];
            for (int i = 0; i < result.Length; i++)
            {
                object segment = ProductApi.SvgItem(segments, i);
                object end = ProductApi.ReadSvgProperty(segment, "End");
                result[i] =
                    segment.GetType().Name +
                    "(" + Format(((float)ProductApi.ReadSvgProperty(end, "X"))) +
                    "," + Format(((float)ProductApi.ReadSvgProperty(end, "Y"))) +
                    ")rel=" + ProductApi.ReadSvgProperty(segment, "IsRelative");
            }

            return result;
        }

        private static string Format(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}


