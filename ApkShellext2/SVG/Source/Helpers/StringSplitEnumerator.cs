using System;

namespace Svg.Helpers
{
    /// <summary>
    /// Splits a <see cref="TextSlice"/> on any of the given characters, skipping empty parts.
    /// Same semantics as the previous span-based implementation (leading, trailing and repeated
    /// separators produce no empty part), only now it walks a string window.
    /// </summary>
    internal struct StringSplitEnumerator
    {
        private TextSlice _str;
        private readonly char[] _chars;

        public StringSplitEnumerator(TextSlice str, char[] chars)
        {
            _str = str;
            _chars = chars;
            Current = default;
        }

        public StringSplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            while (true)
            {
                var str = _str;

                if (str.Length == 0)
                {
                    return false;
                }

                var index = str.IndexOfAny(_chars);
                if (index == -1)
                {
                    Current = new StringPart(str);
                    _str = default;
                    return true;
                }

                var slice = str.Slice(0, index);
                _str = str.Slice(index + 1);
                if (slice.Length == 0)
                {
                    continue;
                }
                Current = new StringPart(slice);
                return true;
            }
        }

        public StringPart Current { get; private set; }
    }

    internal readonly struct StringPart
    {
        public StringPart(TextSlice value)
        {
            Value = value;
        }

        public TextSlice Value { get; }

        public static implicit operator TextSlice(StringPart part) => part.Value;
    }
}
