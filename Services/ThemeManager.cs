using System.Windows;
using Wpf.Ui.Appearance;

namespace HyperNote.Services;

public enum AppTheme { Light, Dark }

public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>Raised after the theme dictionary is swapped, so open editors can recolor.</summary>
    public static event Action<AppTheme>? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        Current = theme;

        // 1. Swap our app-specific override dictionary (editor colors, banner, etc.)
        var uri = new Uri(theme == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;

        // Index 0 = WPF-UI ThemesDictionary, Index 1 = WPF-UI ControlsDictionary, Index 2 = our app override
        if (merged.Count >= 3) merged[2] = dict;
        else if (merged.Count > 0) merged[merged.Count - 1] = dict;
        else merged.Add(dict);

        // 2. Sync WPF-UI's own theme engine so all ui:* controls switch too
        try
        {
            ApplicationThemeManager.Apply(
                theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch { /* WPF-UI not available in test environment */ }

        // 3. Apply dark/light title bar theme to all open windows
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (theme == AppTheme.Dark)
                {
                    WindowBackgroundManager.ApplyDarkThemeToWindow(window);
                }
                else
                {
                    WindowBackgroundManager.RemoveDarkThemeFromWindow(window);
                }
            }
        }
        catch { }

        SettingsService.Instance.Settings.DarkTheme = theme == AppTheme.Dark;
        SettingsService.Instance.Save();
        ThemeChanged?.Invoke(theme);
    }

    public static void Toggle() =>
        Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
