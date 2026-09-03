using System.Windows;

namespace CallAnalog.Softphone.Helpers;

public static class ThemeManager
{
    private const string DarkDictionaryPath = "Styles/PhoneThemeDark.xaml";
    private static ResourceDictionary? _darkDictionary;

    public static bool IsDarkApplied { get; private set; }

    public static void ApplyDarkMode(bool useDark = true)
    {
        _ = useDark;
        if (Application.Current?.Resources is not ResourceDictionary root)
        {
            return;
        }

        var merged = root.MergedDictionaries;
        _darkDictionary ??= new ResourceDictionary { Source = new Uri(DarkDictionaryPath, UriKind.Relative) };

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is not null
                && source.Contains("PhoneThemeLight.xaml", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }

        if (!merged.Contains(_darkDictionary))
        {
            merged.Add(_darkDictionary);
        }

        IsDarkApplied = true;
    }

    public static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            < 12 => "Good morning",
            < 17 => "Good afternoon",
            _ => "Good evening"
        };
    }
}
