using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SqeaksDaocTextures;

public sealed class ArchiveService
{
    public IReadOnlyList<ArchiveComponent> Inspect(ArchivePair archives)
    {
        EnsureBothExist(archives);

        var stats = new Dictionary<string, (long Files, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in archives.Paths)
        {
            using var archive = SevenZipArchive.OpenArchive(archivePath);
            foreach (var entry in archive.Entries)
            {
                var (top, relative) = PathSafety.ValidateArchiveEntry(entry.Key ?? "");
                if (!entry.IsDirectory && !destinations.Add(relative))
                    throw new InvalidDataException($"Duplicate archive destination across the texture parts: {entry.Key}");
                if (!stats.TryGetValue(top, out var stat)) stat = (0, 0);
                if (!entry.IsDirectory) stat = (stat.Files + 1, checked(stat.Bytes + entry.Size));
                stats[top] = stat;
            }
        }

        return stats.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ArchiveComponent
            {
                Name = x.Key,
                FileCount = x.Value.Files,
                UncompressedSize = x.Value.Bytes
            }).ToList();
    }

    public long EstimateInstalledGrowth(ArchivePair archives, string targetRoot, ISet<string> selected)
    {
        EnsureBothExist(archives);
        long growth = 0;
        foreach (var archivePath in archives.Paths)
        {
            using var archive = SevenZipArchive.OpenArchive(archivePath);
            foreach (var entry in archive.Entries)
            {
                var (top, relative) = PathSafety.ValidateArchiveEntry(entry.Key ?? "");
                if (entry.IsDirectory || !selected.Contains(top)) continue;
                var destination = PathSafety.ResolveUnder(targetRoot, relative);
                var existingSize = File.Exists(destination) ? new FileInfo(destination).Length : 0;
                if (entry.Size > existingSize) growth = checked(growth + entry.Size - existingSize);
            }
        }
        return growth;
    }

    public void ExtractSelectedDirect(
        ArchivePair archives,
        string targetRoot,
        ISet<string> selected,
        long completedBeforeExtraction,
        long totalWork,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        Inspect(archives);
        var totalsByComponent = archives.Paths.SelectMany(path =>
            {
                using var archive = SevenZipArchive.OpenArchive(path);
                return archive.Entries.Where(e => !e.IsDirectory)
                    .Select(e => PathSafety.ValidateArchiveEntry(e.Key ?? "").TopLevel).ToList();
            })
            .Where(selected.Contains)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.OrdinalIgnoreCase);
        var completedByComponent = totalsByComponent.Keys
            .ToDictionary(name => name, _ => 0L, StringComparer.OrdinalIgnoreCase);
        long extractionCompleted = 0;

        foreach (var archivePath in archives.Paths)
        {
            using var archive = SevenZipArchive.OpenArchive(archivePath);
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = reader.Entry;
                var (top, relative) = PathSafety.ValidateArchiveEntry(entry.Key ?? "");
                if (entry.IsDirectory || !selected.Contains(top)) continue;

                var destination = PathSafety.ResolveUnder(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var componentCompleted = completedByComponent.GetValueOrDefault(top);
                var componentTotal = totalsByComponent.GetValueOrDefault(top);
                progress?.Report(new OperationProgress(
                    ProgressMath.OverallWorkPercent(completedBeforeExtraction + extractionCompleted, totalWork),
                    $"Extracting {top}", relative, top,
                    ProgressMath.ComponentPercent(componentCompleted, componentTotal)));
                reader.WriteEntryToFile(destination, new ExtractionOptions
                {
                    Overwrite = true,
                    ExtractFullPath = false,
                    CheckCrc = true
                });
                extractionCompleted++;
                componentCompleted++;
                completedByComponent[top] = componentCompleted;
                progress?.Report(new OperationProgress(
                    ProgressMath.OverallWorkPercent(completedBeforeExtraction + extractionCompleted, totalWork),
                    $"Extracting {top}", relative, top,
                    ProgressMath.ComponentPercent(componentCompleted, componentTotal)));
            }
        }
    }

    public IReadOnlyList<ArchiveFingerprint> CreateFingerprints(ArchivePair archives)
    {
        EnsureBothExist(archives);
        var result = new List<ArchiveFingerprint>(2);
        foreach (var path in archives.Paths)
        {
            long files = 0, bytes = 0;
            using var archive = SevenZipArchive.OpenArchive(path);
            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                files++;
                bytes = checked(bytes + entry.Size);
            }
            var info = new FileInfo(path);
            result.Add(new ArchiveFingerprint
            {
                FileName = info.Name,
                Length = info.Length,
                FileCount = files,
                UncompressedBytes = bytes
            });
        }
        return result;
    }

    private static void EnsureBothExist(ArchivePair archives)
    {
        if (!File.Exists(archives.Part1Path))
            throw new FileNotFoundException("Texture archive Part 1 was not found.", archives.Part1Path);
        if (!File.Exists(archives.Part2Path))
            throw new FileNotFoundException("Texture archive Part 2 was not found.", archives.Part2Path);
    }
}
