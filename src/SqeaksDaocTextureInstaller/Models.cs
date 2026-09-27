using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SqeaksDaocTextures;

public sealed record ArchivePair(string Part1Path, string Part2Path)
{
    public IReadOnlyList<string> Paths => [Part1Path, Part2Path];
}

public enum ComponentState
{
    NotInstalled,
    TextureActive,
    OriginalActive,
    Missing,
    Conflict
}

public sealed class ArchiveComponent : INotifyPropertyChanged
{
    private bool _isSelected;
    private ComponentState _state;

    public required string Name { get; init; }
    public long FileCount { get; init; }
    public long UncompressedSize { get; init; }
    public bool HasArchiveMetadata { get; init; } = true;
    public bool TargetExists { get; set; }
    public bool CanSelect => TargetExists && State != ComponentState.Conflict;
    public string FileCountDisplay => HasArchiveMetadata ? FileCount.ToString("N0") : "—";
    public string SizeDisplay => HasArchiveMetadata ? FileSize.Format(UncompressedSize) : "—";
    public string StateDisplay => State switch
    {
        ComponentState.NotInstalled => "Ready to install",
        ComponentState.TextureActive => "New textures active",
        ComponentState.OriginalActive => "Original textures active",
        ComponentState.Missing => "Folder missing",
        ComponentState.Conflict => "Needs recovery",
        _ => State.ToString()
    };

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public ComponentState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateDisplay));
            OnPropertyChanged(nameof(CanSelect));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class FileSize
{
    public static string Format(long bytes)
    {
        string[] suffixes = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }
        return $"{value:0.##} {suffixes[suffix]}";
    }
}

public sealed class InstallManifest
{
    public int SchemaVersion { get; set; } = 2;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArchiveFingerprint? Archive { get; set; }
    public List<ArchiveFingerprint> Archives { get; set; } = [];
    public Dictionary<string, InstalledComponent> Components { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ArchiveFingerprint
{
    public string FileName { get; set; } = "";
    public long Length { get; set; }
    public long FileCount { get; set; }
    public long UncompressedBytes { get; set; }
}

public sealed class InstalledComponent
{
    public string ActiveVariant { get; set; } = "Texture";
    public DateTimeOffset InstalledAtUtc { get; set; }
}

public sealed class OperationJournal
{
    public int SchemaVersion { get; set; } = 1;
    public string Action { get; set; } = "";
    public string Phase { get; set; } = "";
    public List<string> Components { get; set; } = [];
    public Dictionary<string, string> StartingStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record OperationProgress(
    double Percent,
    string Status,
    string? CurrentFile = null,
    string? ComponentName = null,
    double ComponentPercent = 0);

public static class ProgressMath
{
    public static double ComponentPercent(long completed, long total) =>
        total <= 0 ? 100 : Math.Clamp(completed * 100d / total, 0, 100);

    public static double OverallWorkPercent(long completed, long total) =>
        total <= 0 ? 0 : Math.Clamp(completed * 100d / total, 0, 99);
}
