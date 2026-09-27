using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SqeaksDaocTextures;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public static class ThemeManager
{
    private const string LightThemeSource = "Themes/Theme.Light.xaml";
    private const string DarkThemeSource = "Themes/Theme.Dark.xaml";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private static bool _initialized;

    public static AppTheme Preference { get; private set; } = AppTheme.System;
    public static AppTheme EffectiveTheme { get; private set; } = AppTheme.Dark;
    public static event EventHandler? ThemeChanged;

    public static AppTheme ParsePreference(string? value) =>
        Enum.TryParse<AppTheme>(value, true, out var theme) ? theme : AppTheme.System;

    public static AppTheme ResolveEffective(AppTheme preference, bool systemUsesLight) =>
        preference == AppTheme.System ? (systemUsesLight ? AppTheme.Light : AppTheme.Dark) : preference;

    public static void Initialize(AppTheme preference)
    {
        if (!_initialized)
        {
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            _initialized = true;
        }
        SetPreference(preference);
    }

    public static void SetPreference(AppTheme preference)
    {
        Preference = preference;
        Apply(ResolveEffective(preference, SystemUsesLightTheme()));
    }

    public static void ApplyToWindow(Window window)
    {
        void ApplyTitleBar() => SetImmersiveDarkMode(new WindowInteropHelper(window).Handle, EffectiveTheme == AppTheme.Dark);
        if (new WindowInteropHelper(window).Handle == IntPtr.Zero) window.SourceInitialized += (_, _) => ApplyTitleBar();
        else ApplyTitleBar();
    }

    public static void Shutdown()
    {
        if (!_initialized) return;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _initialized = false;
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Preference != AppTheme.System || Application.Current is null) return;
        Application.Current.Dispatcher.BeginInvoke(() => Apply(ResolveEffective(AppTheme.System, SystemUsesLightTheme())));
    }

    private static void Apply(AppTheme effectiveTheme)
    {
        EffectiveTheme = effectiveTheme;
        if (Application.Current is null) return;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(IsThemeDictionary);
        var replacement = new ResourceDictionary
        {
            Source = new Uri(effectiveTheme == AppTheme.Light ? LightThemeSource : DarkThemeSource, UriKind.Relative)
        };
        if (existing is null) dictionaries.Insert(0, replacement);
        else dictionaries[dictionaries.IndexOf(existing)] = replacement;

        foreach (Window window in Application.Current.Windows) ApplyToWindow(window);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source?.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) == true ||
               source?.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value ? value != 0 : true;
        }
        catch { return true; }
    }

    private static void SetImmersiveDarkMode(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        var enabled = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
