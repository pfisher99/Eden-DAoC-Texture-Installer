namespace SqeaksDaocTextures;

public static class PathSafety
{
    public static (string TopLevel, string RelativePath) ValidateArchiveEntry(string entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
            throw new InvalidDataException("The archive contains an empty path.");

        var normalized = entryKey.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(normalized))
            throw new InvalidDataException($"Unsafe archive path: {entryKey}");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException($"Unsafe archive path: {entryKey}");

        return (segments[0], Path.Combine(segments));
    }

    public static string ResolveUnder(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes its destination: {relativePath}");
        return candidate;
    }
}
