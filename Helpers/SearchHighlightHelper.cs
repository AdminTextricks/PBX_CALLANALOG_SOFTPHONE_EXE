using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CallAnalog.Softphone.Helpers;

public static class SearchHighlightHelper
{
    public static void Apply(TextBlock target, string? text, string? query, Brush? highlightBrush = null)
    {
        target.Inlines.Clear();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            target.Inlines.Add(new Run(text));
            return;
        }

        highlightBrush ??= (Brush)target.FindResource("PhoneTextPrimaryBrush");

        var normalizedQuery = query.Trim();
        var comparison = StringComparison.OrdinalIgnoreCase;
        var start = 0;
        var matchedAny = false;

        while (start < text.Length)
        {
            var index = text.IndexOf(normalizedQuery, start, comparison);
            if (index < 0)
            {
                break;
            }

            matchedAny = true;
            if (index > start)
            {
                target.Inlines.Add(new Run(text[start..index]));
            }

            var matchLength = normalizedQuery.Length;
            var matched = text.Substring(index, matchLength);
            target.Inlines.Add(new Run(matched)
            {
                FontWeight = FontWeights.Bold,
                Foreground = highlightBrush
            });
            start = index + matchLength;
        }

        if (matchedAny)
        {
            if (start < text.Length)
            {
                target.Inlines.Add(new Run(text[start..]));
            }

            return;
        }

        if (TryGetDigitHighlightRange(text, normalizedQuery, out var digitStart, out var digitLength))
        {
            if (digitStart > 0)
            {
                target.Inlines.Add(new Run(text[..digitStart]));
            }

            target.Inlines.Add(new Run(text.Substring(digitStart, digitLength))
            {
                FontWeight = FontWeights.Bold,
                Foreground = highlightBrush
            });

            if (digitStart + digitLength < text.Length)
            {
                target.Inlines.Add(new Run(text[(digitStart + digitLength)..]));
            }

            return;
        }

        target.Inlines.Add(new Run(text));
    }

    public static bool MatchesDigitsOrLetters(string? haystack, string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(haystack))
        {
            return false;
        }

        if (haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var queryDigits = new string(query.Where(char.IsDigit).ToArray());
        if (queryDigits.Length == 0)
        {
            return false;
        }

        var haystackDigits = new string(haystack.Where(char.IsDigit).ToArray());
        return haystackDigits.Contains(queryDigits, StringComparison.Ordinal);
    }

    private static bool TryGetDigitHighlightRange(string text, string query, out int start, out int length)
    {
        start = 0;
        length = 0;

        var queryDigits = new string(query.Where(char.IsDigit).ToArray());
        if (queryDigits.Length == 0)
        {
            return false;
        }

        var digitChars = new List<char>();
        var digitToTextIndex = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
            {
                continue;
            }

            digitChars.Add(text[i]);
            digitToTextIndex.Add(i);
        }

        if (digitChars.Count == 0)
        {
            return false;
        }

        var haystackDigits = new string(digitChars.ToArray());
        var matchIndex = haystackDigits.IndexOf(queryDigits, StringComparison.Ordinal);
        if (matchIndex < 0)
        {
            return false;
        }

        var firstTextIndex = digitToTextIndex[matchIndex];
        var lastTextIndex = digitToTextIndex[matchIndex + queryDigits.Length - 1];
        start = firstTextIndex;
        length = lastTextIndex - firstTextIndex + 1;
        return true;
    }
}
