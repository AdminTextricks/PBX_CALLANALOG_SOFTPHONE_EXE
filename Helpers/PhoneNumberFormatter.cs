using System.Text;

namespace CallAnalog.Softphone.Helpers;

public static class PhoneNumberFormatter
{
    /// <summary>Strips formatting characters, keeps digits and * # +.</summary>
    public static string Unformat(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return new string(value.Where(c => char.IsDigit(c) || c is '*' or '#' or '+').ToArray());
    }

    /// <summary>Formats US-style numbers as (866) 555-1234 while typing.</summary>
    public static string FormatForDisplay(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        if (raw.Any(c => c is '*' or '#'))
        {
            return raw;
        }

        if (raw.StartsWith("+", StringComparison.Ordinal))
        {
            var digits = new string(raw.Skip(1).Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                return "+";
            }

            if (digits.Length <= 1)
            {
                return $"+{digits}";
            }

            if (digits.Length <= 4)
            {
                return $"+{digits[..1]} ({digits[1..]})";
            }

            if (digits.Length <= 7)
            {
                return $"+{digits[..1]} ({digits[1..4]}) {digits[4..]}";
            }

            return $"+{digits[..1]} ({digits[1..4]}) {digits[4..7]}-{digits[7..Math.Min(11, digits.Length)]}";
        }

        var local = new string(raw.Where(char.IsDigit).ToArray());
        if (local.Length == 0)
        {
            return string.Empty;
        }

        if (local.Length <= 3)
        {
            return local;
        }

        if (local.Length <= 6)
        {
            return $"({local[..3]}) {local[3..]}";
        }

        if (local.Length <= 10)
        {
            return $"({local[..3]}) {local[3..6]}-{local[6..]}";
        }

        var countryLen = local.Length - 10;
        return $"+{local[..countryLen]} ({local[countryLen..(countryLen + 3)]}) {local[(countryLen + 3)..(countryLen + 6)]}-{local[(countryLen + 6)..]}";
    }

    /// <summary>Maps caret index in formatted text to raw index.</summary>
    public static int FormattedIndexToRawIndex(string formatted, int formattedIndex)
    {
        var rawLen = 0;
        for (var i = 0; i < formattedIndex && i < formatted.Length; i++)
        {
            if (char.IsDigit(formatted[i]) || formatted[i] is '*' or '#' or '+')
            {
                rawLen++;
            }
        }

        return rawLen;
    }

    /// <summary>Maps raw index to formatted caret index.</summary>
    public static int RawIndexToFormattedIndex(string raw)
    {
        return FormatForDisplay(raw).Length;
    }
}
