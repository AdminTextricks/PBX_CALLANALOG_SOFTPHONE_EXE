using System.Windows;
using Microsoft.Win32;

namespace CallAnalog.Softphone.Helpers;

public static class ThemeManager
{
    private const string DarkDictionaryPath = "Styles/PhoneThemeDark.xaml";
    private const string LightDictionaryPath = "Styles/PhoneThemeLight.xaml";
    private static ResourceDictionary? _darkDictionary;
    private static ResourceDictionary? _lightDictionary;

    public static bool IsDarkApplied { get; private set; }

    public static void ApplyFromSettings(bool darkModeEnabled, bool followSystemTheme)
    {
        var useDark = followSystemTheme ? IsWindowsAppsDarkMode() : darkModeEnabled;
        ApplyDarkMode(useDark);
    }

    public static void ApplyDarkMode(bool useDark)
    {
        if (Application.Current?.Resources is not ResourceDictionary root)
        {
            return;
        }

        var merged = root.MergedDictionaries;
        _darkDictionary ??= new ResourceDictionary { Source = new Uri(DarkDictionaryPath, UriKind.Relative) };
        _lightDictionary ??= new ResourceDictionary { Source = new Uri(LightDictionaryPath, UriKind.Relative) };

        if (useDark)
        {
            if (merged.Contains(_lightDictionary))
            {
                merged.Remove(_lightDictionary);
            }

            if (!merged.Contains(_darkDictionary))
            {
                merged.Add(_darkDictionary);
            }
        }
        else
        {
            if (merged.Contains(_darkDictionary))
            {
                merged.Remove(_darkDictionary);
            }

            if (!merged.Contains(_lightDictionary))
            {
                merged.Add(_lightDictionary);
            }
        }

        IsDarkApplied = useDark;
    }

    public static bool IsWindowsAppsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return true;
        }
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
