using System.Diagnostics;

namespace SqeaksDaocTextures;

public sealed class FileSystemService
{
    public static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".sqeaks-daoc-textures-write-test-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return !File.Exists(probe);
        }
        catch { return false; }
    }

    public static bool IsGameRunning()
    {
        foreach (var name in new[] { "game", "camelot", "camtest", "EdenLauncher" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    public static void CopyDirectory(string source, string destination, string componentName,
        long componentFiles, CancellationToken token, IProgress<OperationProgress>? progress,
        ref long completedWork, long totalWork)
    {
        var sourceInfo = new DirectoryInfo(source);
        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Reparse points are not supported: {source}");

        Directory.CreateDirectory(destination);
        long componentCompleted = 0;
        progress?.Report(new OperationProgress(
            ProgressMath.OverallWorkPercent(completedWork, totalWork),
            $"Backing up {componentName}", null, componentName,
            ProgressMath.ComponentPercent(componentCompleted, componentFiles)));
        foreach (var directory in sourceInfo.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse points are not supported: {directory.FullName}");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory.FullName)));
        }

        foreach (var file in sourceInfo.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse points are not supported: {file.FullName}");
            var relative = Path.GetRelativePath(source, file.FullName);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            file.CopyTo(target, false);
            File.SetLastWriteTimeUtc(target, file.LastWriteTimeUtc);
            componentCompleted++;
            completedWork++;
            progress?.Report(new OperationProgress(
                ProgressMath.OverallWorkPercent(completedWork, totalWork),
                $"Backing up {componentName}", relative, componentName,
                ProgressMath.ComponentPercent(componentCompleted, componentFiles)));
        }

        progress?.Report(new OperationProgress(
            ProgressMath.OverallWorkPercent(completedWork, totalWork),
            $"Verifying backup for {componentName}", null, componentName, 100));
    }

    public static (long Files, long Bytes) MeasureDirectory(string path)
    {
        long files = 0, bytes = 0;
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse points are not supported: {file.FullName}");
            files++;
            bytes = checked(bytes + file.Length);
        }
        return (files, bytes);
    }

    public static void VerifyCopy(string source, string copy)
    {
        var expected = MeasureDirectory(source);
        var actual = MeasureDirectory(copy);
        if (expected != actual)
            throw new IOException($"Backup verification failed for {Path.GetFileName(source)}. Expected {expected.Files} files/{expected.Bytes} bytes, found {actual.Files}/{actual.Bytes}.");

        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var copiedFile = Path.Combine(copy, relative);
            if (!File.Exists(copiedFile) || new FileInfo(sourceFile).Length != new FileInfo(copiedFile).Length)
                throw new IOException($"Backup verification failed for {relative}.");
        }
    }

    public static bool DeleteDirectoryBestEffort(string path)
    {
        if (!Directory.Exists(path)) return true;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, true);
            return true;
        }
        catch { return !Directory.Exists(path); }
    }

    public static long CountFilesForRemoval(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long count = 0;
        foreach (var entry in EnumerateRemovalTree(new DirectoryInfo(path)))
            if (entry is FileInfo) count++;
        return count;
    }

    public static void DeleteDirectoryWithProgress(string path, string componentName, long componentFiles,
        IProgress<OperationProgress>? progress, ref long completedWork, long totalWork)
    {
        if (!Directory.Exists(path))
        {
            progress?.Report(new OperationProgress(
                ProgressMath.OverallWorkPercent(completedWork, totalWork),
                $"Removed textures for {componentName}", null, componentName, 100));
            return;
        }

        var root = new DirectoryInfo(path);
        var entries = EnumerateRemovalTree(root).ToList();
        long componentCompleted = 0;
        progress?.Report(new OperationProgress(
            ProgressMath.OverallWorkPercent(completedWork, totalWork),
            $"Removing textures for {componentName}", null, componentName,
            ProgressMath.ComponentPercent(componentCompleted, componentFiles)));

        foreach (var file in entries.OfType<FileInfo>())
        {
            var relative = Path.GetRelativePath(path, file.FullName);
            file.Attributes = FileAttributes.Normal;
            file.Delete();
            componentCompleted++;
            completedWork++;
            progress?.Report(new OperationProgress(
                ProgressMath.OverallWorkPercent(completedWork, totalWork),
                $"Removing textures for {componentName}", relative, componentName,
                ProgressMath.ComponentPercent(componentCompleted, componentFiles)));
        }

        foreach (var directory in entries.OfType<DirectoryInfo>().OrderByDescending(d => d.FullName.Length))
        {
            directory.Attributes = FileAttributes.Normal;
            directory.Delete(false);
        }
        root.Attributes = FileAttributes.Normal;
        root.Delete(false);

        if (componentFiles == 0) completedWork++;
        progress?.Report(new OperationProgress(
            ProgressMath.OverallWorkPercent(completedWork, totalWork),
            $"Removed textures for {componentName}", null, componentName, 100));
    }

    private static IEnumerable<FileSystemInfo> EnumerateRemovalTree(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Reparse points are not supported: {directory.FullName}");

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse points are not supported: {entry.FullName}");
            if (entry is DirectoryInfo child)
                foreach (var descendant in EnumerateRemovalTree(child)) yield return descendant;
            yield return entry;
        }
    }
}
