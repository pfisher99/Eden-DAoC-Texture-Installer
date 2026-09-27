using System.Windows;

using System.Reflection;

namespace SqeaksDaocTextures.Tests;

public sealed class ThemeManagerTests
{
    [Theory]
    [InlineData(null, AppTheme.System)]
    [InlineData("", AppTheme.System)]
    [InlineData("unknown", AppTheme.System)]
    [InlineData("system", AppTheme.System)]
    [InlineData("LIGHT", AppTheme.Light)]
    [InlineData("Dark", AppTheme.Dark)]
    public void Preference_parsing_is_safe_and_case_insensitive(string? value, AppTheme expected)
    {
        Assert.Equal(expected, ThemeManager.ParsePreference(value));
    }

    [Theory]
    [InlineData(AppTheme.System, true, AppTheme.Light)]
    [InlineData(AppTheme.System, false, AppTheme.Dark)]
    [InlineData(AppTheme.Light, false, AppTheme.Light)]
    [InlineData(AppTheme.Dark, true, AppTheme.Dark)]
    public void Effective_theme_respects_manual_overrides(AppTheme preference, bool systemLight, AppTheme expected)
    {
        Assert.Equal(expected, ThemeManager.ResolveEffective(preference, systemLight));
    }

    [Fact]
    public void Missing_theme_setting_defaults_to_system()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sqeaks-theme-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"targetPath\":\"C:\\\\Games\\\\DAoC\"}");
            var settings = JsonStore.Read<UserSettings>(path);
            Assert.NotNull(settings);
            Assert.Equal(nameof(AppTheme.System), settings.Theme);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Theme_resource_dictionaries_load_and_share_required_tokens()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var light = Load("Theme.Light.xaml");
                var dark = Load("Theme.Dark.xaml");
                var controls = Load("Controls.xaml");
                string[] required =
                [
                    "WindowBackgroundBrush", "SurfaceBrush", "RaisedSurfaceBrush", "InputBackgroundBrush",
                    "PrimaryTextBrush", "SecondaryTextBrush", "BorderBrush", "HoverBrush", "SelectedBrush",
                    "DisabledSurfaceBrush", "DisabledTextBrush", "AccentBrush", "AccentTextBrush",
                    "SuccessBrush", "SuccessSurfaceBrush", "WarningBrush", "WarningSurfaceBrush",
                    "ErrorBrush", "ErrorSurfaceBrush", "FocusBrush"
                ];
                foreach (var key in required)
                {
                    Assert.True(light.Contains(key), $"Light theme is missing {key}.");
                    Assert.True(dark.Contains(key), $"Dark theme is missing {key}.");
                }
                Assert.True(controls.Contains("PrimaryButtonStyle"));
                Assert.True(controls.Contains("GhostButtonStyle"));
                Assert.True(controls.Contains("DestructiveButtonStyle"));
                Assert.True(controls.Contains("ComponentListStyle"));

                var host = new System.Windows.Controls.Grid();
                host.Resources.MergedDictionaries.Add(light);
                host.Resources.MergedDictionaries.Add(controls);
                var list = new System.Windows.Controls.ListBox
                {
                    Style = Assert.IsType<Style>(controls["ComponentListStyle"])
                };
                host.Children.Add(list);
                list.ApplyTemplate();
                Assert.NotNull(list.Template.FindName("PART_ScrollViewer", list));

                var progressBar = new System.Windows.Controls.ProgressBar
                {
                    Style = Assert.IsType<Style>(controls[typeof(System.Windows.Controls.ProgressBar)])
                };
                host.Children.Add(progressBar);
                progressBar.ApplyTemplate();
                Assert.NotNull(progressBar.Template.FindName("PART_Track", progressBar));
                Assert.NotNull(progressBar.Template.FindName("PART_Indicator", progressBar));

                var scrollBar = new System.Windows.Controls.Primitives.ScrollBar
                {
                    Style = Assert.IsType<Style>(controls[typeof(System.Windows.Controls.Primitives.ScrollBar)])
                };
                host.Children.Add(scrollBar);
                scrollBar.ApplyTemplate();
                Assert.NotNull(scrollBar.Template.FindName("PART_Track", scrollBar));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    private static ResourceDictionary Load(string fileName) =>
        (ResourceDictionary)Application.LoadComponent(
            new Uri($"/SqeaksDaocTextureInstaller;component/Themes/{fileName}", UriKind.Relative));

    [Fact]
    public void Assembly_metadata_uses_the_public_brand_and_installer_version()
    {
        var assembly = typeof(App).Assembly;
        Assert.Equal("Sqeak's DAoC Textures", assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal("Sqeak", assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal(new Version(1, 2, 0, 0), assembly.GetName().Version);
    }
}
