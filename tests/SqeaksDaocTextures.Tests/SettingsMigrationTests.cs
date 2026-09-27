namespace SqeaksDaocTextures.Tests;

public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sqeaks-settings", Guid.NewGuid().ToString("N"));

    public SettingsMigrationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Legacy_settings_are_migrated_with_paths_and_theme_intact()
    {
        var current = Path.Combine(_root, "new", "settings.json");
        var legacy = Path.Combine(_root, "old", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        JsonStore.WriteAtomic(legacy, new UserSettings
        {
            TargetPath = @"C:\Games\DAoC",
            ArchivePath = @"C:\Downloads\textures.7z",
            Theme = nameof(AppTheme.Dark)
        });

        var migrated = SettingsService.LoadWithMigration(current, legacy);

        Assert.Equal(@"C:\Games\DAoC", migrated.TargetPath);
        Assert.Equal(@"C:\Downloads\textures.7z", migrated.ArchivePath);
        Assert.Equal(nameof(AppTheme.Dark), migrated.Theme);
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void Existing_current_settings_take_precedence()
    {
        var current = Path.Combine(_root, "new", "settings.json");
        var legacy = Path.Combine(_root, "old", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        JsonStore.WriteAtomic(current, new UserSettings { Theme = nameof(AppTheme.Light) });
        JsonStore.WriteAtomic(legacy, new UserSettings { Theme = nameof(AppTheme.Dark) });

        Assert.Equal(nameof(AppTheme.Light), SettingsService.LoadWithMigration(current, legacy).Theme);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
