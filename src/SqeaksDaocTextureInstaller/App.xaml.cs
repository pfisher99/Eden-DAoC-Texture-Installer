using System.Windows;

namespace SqeaksDaocTextures;

public partial class App : Application
{
    public static StartupOptions StartupOptions { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupOptions = StartupOptions.Parse(e.Args);
        ThemeManager.Initialize(ThemeManager.ParsePreference(SettingsService.Load().Theme));
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}

public sealed record StartupOptions(
    string? ElevatedAction = null,
    string? TargetPath = null,
    string? ArchivePath = null,
    IReadOnlyList<string>? Components = null)
{
    public static StartupOptions Parse(string[] args)
    {
        string? action = null, target = null, archive = null;
        var components = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--elevated-action": action = Next(); break;
                case "--target": target = Next(); break;
                case "--archive": archive = Next(); break;
                case "--component":
                    var value = Next();
                    if (!string.IsNullOrWhiteSpace(value)) components.Add(value);
                    break;
            }
        }

        return new StartupOptions(action, target, archive, components);
    }
}
