using Microsoft.Win32;

namespace SqeaksDaocTextures;

public sealed class UserSettings
{
    public string? TargetPath { get; set; }
    public string? ArchivePath { get; set; }
    public string Theme { get; set; } = nameof(AppTheme.System);
}

public static class SettingsService
{
    public const string AppDataFolderName = "SqeaksDAoCTextures";
    public const string LegacyAppDataFolderName = "EdenTextureInstaller";
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName);
    private static readonly string LegacyDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppDataFolderName);
    private static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly string LegacySettingsPath = Path.Combine(LegacyDirectoryPath, "settings.json");
    public static string LogDirectory => Path.Combine(DirectoryPath, "logs");

    public static UserSettings Load() => LoadWithMigration(SettingsPath, LegacySettingsPath);

    public static UserSettings LoadWithMigration(string settingsPath, string legacySettingsPath)
    {
        var current = JsonStore.Read<UserSettings>(settingsPath);
        if (current is not null) return current;

        var legacy = JsonStore.Read<UserSettings>(legacySettingsPath);
        if (legacy is null) return new UserSettings();

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        JsonStore.WriteAtomic(settingsPath, legacy);
        return legacy;
    }
    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        JsonStore.WriteAtomic(SettingsPath, settings);
    }

    public static string? FindDaocPath(string? saved)
    {
        if (IsDaocPath(saved)) return saved;
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var keyName in uninstall.GetSubKeyNames())
                    {
                        using var key = uninstall.OpenSubKey(keyName);
                        var display = key?.GetValue("DisplayName") as string;
                        var location = key?.GetValue("InstallLocation") as string;
                        if (display?.Contains("Dark Age of Camelot", StringComparison.OrdinalIgnoreCase) == true && IsDaocPath(location))
                            return location;
                    }
                }
                catch { }
            }
        }

        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Electronic Arts", "Dark Age of Camelot");
        return IsDaocPath(standard) ? standard : null;
    }

    public static bool IsDaocPath(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "game.dll")) && File.Exists(Path.Combine(path, "camelot.exe"));
}
