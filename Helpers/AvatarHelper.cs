using System.Windows.Media;

namespace CallAnalog.Softphone.Helpers;

public static class AvatarHelper
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x43, 0x61, 0xEE),
        Color.FromRgb(0x10, 0xB9, 0x81),
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0xEC, 0x48, 0x99),
        Color.FromRgb(0x8B, 0x5C, 0xF6),
        Color.FromRgb(0x06, 0xB6, 0xD4),
        Color.FromRgb(0xEF, 0x44, 0x44),
        Color.FromRgb(0x84, 0xCC, 0x16)
    ];

    public static string GetInitials(string? name, string? fallbackNumber = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var parts = name.Trim().Split([' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
            }

            if (parts.Length == 1 && parts[0].Length > 0)
            {
                return parts[0].Length >= 2
                    ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[0][1])}"
                    : char.ToUpperInvariant(parts[0][0]).ToString();
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackNumber))
        {
            var digits = new string(fallbackNumber.Where(char.IsDigit).TakeLast(2).ToArray());
            return digits.Length > 0 ? digits : "?";
        }

        return "?";
    }

    public static Brush GetAvatarBrush(string seed)
    {
        var hash = 0;
        foreach (var ch in seed)
        {
            hash = (hash * 31) + ch;
        }

        var color = Palette[Math.Abs(hash) % Palette.Length];
        return new SolidColorBrush(color);
    }
}
