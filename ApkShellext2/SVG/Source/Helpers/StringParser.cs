using System;
using System.Globalization;

namespace Svg.Helpers
{
    /// <summary>
    /// Number parsing for the SVG attribute parsers. Every parse is culture-invariant and uses
    /// the same <see cref="NumberStyles"/> as before, so the accepted syntax and the
    /// <see cref="FormatException"/>/ <see cref="OverflowException"/> behaviour did not change
    /// when the <c>ReadOnlySpan&lt;char&gt;</c> parameters became <see cref="TextSlice"/>
    /// (System.Memory is no longer referenced; see TextSlice).
    /// </summary>
    internal static class StringParser
    {
        private static readonly CultureInfo Format = CultureInfo.InvariantCulture;

        public static float ToFloat(TextSlice value)
        {
            return float.Parse(value.ToString(), NumberStyles.Float, Format);
        }

        public static float ToFloatAny(TextSlice value)
        {
            return float.Parse(value.ToString(), NumberStyles.Any, Format);
        }

        public static double ToDouble(TextSlice value)
        {
            return double.Parse(value.ToString(), NumberStyles.Any, Format);
        }

        public static int ToInt(TextSlice value)
        {
            return int.Parse(value.ToString(), NumberStyles.Integer, Format);
        }
    }
}
