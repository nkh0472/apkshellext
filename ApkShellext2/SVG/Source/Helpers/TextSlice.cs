using System;

namespace Svg.Helpers
{
    /// <summary>
    /// A read-only window over an existing <see cref="string"/>: (source, start, length).
    ///
    /// The SVG parser used to be written against <c>ReadOnlySpan&lt;char&gt;</c>, which on
    /// .NET Framework 4.8 only exists through the System.Memory facade package. Dropping that
    /// package means the parsing code has to index a <see cref="string"/> directly; this type
    /// carries the offset so the rewrites stay readable instead of threading raw
    /// (start, length) pairs through every method, and it keeps the allocation-free property
    /// of the span version (no <see cref="string.Substring(string)"/> per token - only the
    /// final number parse materializes a string).
    ///
    /// Semantics are the ones the call sites relied on: <see cref="Trim"/> and friends use
    /// <see cref="char.IsWhiteSpace(char)"/> exactly like <c>MemoryExtensions.Trim</c>, and
    /// <see cref="Slice(int, int)"/> is a zero-based window like <c>Span.Slice</c>.
    /// </summary>
    internal readonly struct TextSlice
    {
        private readonly string _source;
        private readonly int _start;
        private readonly int _length;

        public TextSlice(string source, int start, int length)
        {
            _source = source;
            _start = start;
            _length = length;
        }

        public TextSlice(string source)
            : this(source, 0, source?.Length ?? 0)
        {
        }

        public static implicit operator TextSlice(string source) => new TextSlice(source);

        public int Length => _length;

        public bool IsEmpty => _length == 0;

        public char this[int index] => _source[_start + index];

        public TextSlice Slice(int start, int length) => new TextSlice(_source, _start + start, length);

        public TextSlice Slice(int start) => new TextSlice(_source, _start + start, _length - start);

        public TextSlice Trim()
        {
            var start = 0;
            var end = _length;
            while (start < end && char.IsWhiteSpace(this[start]))
                ++start;
            while (end > start && char.IsWhiteSpace(this[end - 1]))
                --end;
            return new TextSlice(_source, _start + start, end - start);
        }

        public TextSlice TrimStart()
        {
            var start = 0;
            while (start < _length && char.IsWhiteSpace(this[start]))
                ++start;
            return new TextSlice(_source, _start + start, _length - start);
        }

        public TextSlice TrimEnd()
        {
            var end = _length;
            while (end > 0 && char.IsWhiteSpace(this[end - 1]))
                --end;
            return new TextSlice(_source, _start, end);
        }

        public TextSlice Trim(char trimChar)
        {
            var start = 0;
            var end = _length;
            while (start < end && this[start] == trimChar)
                ++start;
            while (end > start && this[end - 1] == trimChar)
                --end;
            return new TextSlice(_source, _start + start, end - start);
        }

        public int IndexOf(char value)
        {
            for (var i = 0; i < _length; ++i)
            {
                if (this[i] == value)
                    return i;
            }
            return -1;
        }

        public int IndexOfAny(char[] anyOf)
        {
            for (var i = 0; i < _length; ++i)
            {
                var current = this[i];
                for (var j = 0; j < anyOf.Length; ++j)
                {
                    if (current == anyOf[j])
                        return i;
                }
            }
            return -1;
        }

        /// <summary>Ordinal (case-sensitive) comparison with a whole string.</summary>
        public bool SequenceEqual(string other)
        {
            if (other == null || other.Length != _length)
                return false;
            for (var i = 0; i < _length; ++i)
            {
                if (this[i] != other[i])
                    return false;
            }
            return true;
        }

        /// <summary>Ordinal comparison with a whole string, optionally ignoring case.</summary>
        public bool SequenceEqual(string other, StringComparison comparison)
        {
            if (other == null || other.Length != _length)
                return false;
            return string.Compare(_source, _start, other, 0, other.Length, comparison) == 0;
        }

        public override string ToString()
        {
            if (_length == 0)
                return string.Empty;
            return _source.Substring(_start, _length);
        }
    }
}
