namespace SqeaksDaocTextures;

public sealed class LogService
{
    public string LogPath { get; }

    public LogService()
    {
        Directory.CreateDirectory(SettingsService.LogDirectory);
        LogPath = Path.Combine(SettingsService.LogDirectory, $"installer-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public void Write(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch { }
    }
}
