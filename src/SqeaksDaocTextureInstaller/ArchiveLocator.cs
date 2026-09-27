namespace SqeaksDaocTextures;

public static class ArchiveLocator
{
    public const string CurrentPart1FileName = "Sqeaks-DAoC-Textures-v0.1-Part1.7z";
    public const string CurrentPart2FileName = "Sqeaks-DAoC-Textures-v0.1-Part2.7z";
    private const string VersionedPattern = "Sqeaks-DAoC-Textures-v*-Part*.7z";

    public static ArchivePair? FindPreferred(string executableDirectory, string? savedPath = null)
    {
        var current = new ArchivePair(
            Path.Combine(executableDirectory, CurrentPart1FileName),
            Path.Combine(executableDirectory, CurrentPart2FileName));
        if (IsComplete(current)) return current;

        var versioned = FindCompletePairs(executableDirectory)
            .Where(pair => !pair.Part1Path.Equals(current.Part1Path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (versioned.Count == 1) return versioned[0];
        if (versioned.Count > 1) return null;

        var saved = ResolveSelected(savedPath);
        if (saved is not null && IsComplete(saved)) return saved;

        var savedDirectory = string.IsNullOrWhiteSpace(savedPath) ? null : Path.GetDirectoryName(savedPath);
        var savedPairs = FindCompletePairs(savedDirectory).ToList();
        return savedPairs.Count == 1 ? savedPairs[0] : null;
    }

    public static ArchivePair? ResolveSelected(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return null;
        var fileName = Path.GetFileName(selectedPath);
        const string part1 = "-Part1.7z";
        const string part2 = "-Part2.7z";
        string baseName;
        if (fileName.EndsWith(part1, StringComparison.OrdinalIgnoreCase))
            baseName = fileName[..^part1.Length];
        else if (fileName.EndsWith(part2, StringComparison.OrdinalIgnoreCase))
            baseName = fileName[..^part2.Length];
        else return null;
        if (!baseName.StartsWith("Sqeaks-DAoC-Textures-v", StringComparison.OrdinalIgnoreCase)) return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(selectedPath))!;
        return new ArchivePair(
            Path.Combine(directory, baseName + part1),
            Path.Combine(directory, baseName + part2));
    }

    public static bool IsComplete(ArchivePair? pair) => pair is not null &&
        File.Exists(pair.Part1Path) && File.Exists(pair.Part2Path);

    public static string ValidationMessage(string? selectedPath)
    {
        var pair = ResolveSelected(selectedPath);
        if (pair is null) return "Choose a matching Part 1 or Part 2 archive";
        var part1 = File.Exists(pair.Part1Path);
        var part2 = File.Exists(pair.Part2Path);
        return (part1, part2) switch
        {
            (true, true) => "Both archive parts ready",
            (false, true) => "Part 1 not found beside Part 2",
            (true, false) => "Part 2 not found beside Part 1",
            _ => "Both archive parts are missing"
        };
    }

    private static IEnumerable<ArchivePair> FindCompletePairs(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) yield break;
        var candidates = Directory.EnumerateFiles(directory, VersionedPattern, SearchOption.TopDirectoryOnly)
            .Select(ResolveSelected)
            .Where(pair => pair is not null)
            .Cast<ArchivePair>()
            .DistinctBy(pair => pair.Part1Path, StringComparer.OrdinalIgnoreCase)
            .Where(IsComplete)
            .OrderBy(pair => pair.Part1Path, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidates) yield return pair;
    }
}
