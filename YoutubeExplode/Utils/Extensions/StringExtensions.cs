using System;
using System.Linq;
using System.Text;

namespace YoutubeExplode.Utils.Extensions;

internal static class StringExtensions
{
    extension(string str)
    {
        public string? NullIfWhiteSpace() => !string.IsNullOrWhiteSpace(str) ? str : null;

        public string SubstringUntil(
            string sub,
            StringComparison comparison = StringComparison.Ordinal
        )
        {
            var index = str.IndexOf(sub, comparison);
            return index < 0 ? str : str[..index];
        }

        public string SubstringAfter(
            string sub,
            StringComparison comparison = StringComparison.Ordinal
        )
        {
            var index = str.IndexOf(sub, comparison);

            return index < 0
                ? string.Empty
                : str.Substring(index + sub.Length, str.Length - index - sub.Length);
        }

        public string StripNonDigit()
        {
            var buffer = new StringBuilder();

            foreach (var c in str.Where(char.IsDigit))
                buffer.Append(c);

            return buffer.ToString();
        }

        public string Reverse()
        {
            var buffer = new StringBuilder(str.Length);

            for (var i = str.Length - 1; i >= 0; i--)
                buffer.Append(str[i]);

            return buffer.ToString();
        }

        public string SwapChars(int firstCharIndex, int secondCharIndex) =>
            new StringBuilder(str)
            {
                [firstCharIndex] = str[secondCharIndex],
                [secondCharIndex] = str[firstCharIndex],
            }.ToString();

        public static string ConcatToString<T>(this IEnumerable<T> source) => string.Concat(source);

        public static long ParseLongWithSizeSuffix(this string s)
        {
            var suffix = s[^1];

            if (char.IsDigit(suffix))
                return long.Parse(s);

            var value = double.Parse(
                s[0..^1],
                NumberStyles.Float | NumberStyles.AllowThousands,
                NumberFormatInfo.InvariantInfo
            );
            ;

            return (long)(
                value
                * suffix switch
                {
                    'K' => 1e3,
                    'M' => 1e6,
                    'B' => 1e9,
                    _ => throw new FormatException($"Invalid suffix '{suffix}' for '{s}'")
                }
            );
        }
    }
}
