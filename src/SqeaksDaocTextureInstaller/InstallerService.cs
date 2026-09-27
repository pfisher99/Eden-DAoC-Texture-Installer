namespace SqeaksDaocTextures;

public sealed class InstallerService
{
    public const string ManifestName = ".sqeaks-daoc-textures.json";
    public const string JournalName = ".sqeaks-daoc-textures-operation.json";
    public const string LegacyManifestName = ".eden-texture-installer.json";
    public const string LegacyJournalName = ".eden-texture-operation.json";
    private readonly ArchiveService _archives = new();
    private readonly Func<bool> _isGameRunning;

    public InstallerService(Func<bool>? isGameRunning = null) =>
        _isGameRunning = isGameRunning ?? FileSystemService.IsGameRunning;

    public async Task InstallAsync(ArchivePair archives, string targetRoot, IReadOnlyList<ArchiveComponent> components,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        await Task.Run(() => InstallCore(archives, targetRoot, components, progress, token), CancellationToken.None);
    }

    private void InstallCore(ArchivePair archives, string targetRoot, IReadOnlyList<ArchiveComponent> components,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        if (_isGameRunning())
            throw new InvalidOperationException("Close Dark Age of Camelot and the Eden Launcher before installing textures.");
        if (components.Count == 0) throw new InvalidOperationException("Select at least one component.");
        EnsureNoOperationJournal(targetRoot);

        foreach (var component in components)
        {
            if (StateDetector.Detect(targetRoot, component.Name) != ComponentState.NotInstalled)
                throw new InvalidOperationException($"{component.Name} is not in an installable state.");
        }

        var completeInventory = _archives.Inspect(archives)
            .ToDictionary(component => component.Name, StringComparer.OrdinalIgnoreCase);
        if (components.Any(component => !completeInventory.ContainsKey(component.Name)))
            throw new InvalidDataException("The selected component inventory no longer matches the two archive parts. Refresh and try again.");
        EnsureDiskSpace(archives, targetRoot, components);
        var journalPath = Path.Combine(targetRoot, JournalName);
        var journal = new OperationJournal
        {
            Action = "Install",
            Phase = "BackingUp",
            Components = components.Select(c => c.Name).ToList()
        };
        JsonStore.WriteAtomic(journalPath, journal);

        var createdBackups = new List<string>();
        var trackedProgress = new TrackingProgress(progress);
        var backupFileCounts = components.ToDictionary(
            component => component.Name,
            component => FileSystemService.MeasureDirectory(Path.Combine(targetRoot, component.Name)).Files,
            StringComparer.OrdinalIgnoreCase);
        var backupFiles = backupFileCounts.Values.Aggregate(0L, checked((total, count) => total + count));
        var archiveFiles = components.Aggregate(0L,
            checked((total, component) => total + completeInventory[component.Name].FileCount));
        var totalWork = checked(backupFiles + archiveFiles);
        long completedWork = 0;
        try
        {
            foreach (var component in components)
            {
                token.ThrowIfCancellationRequested();
                var source = Path.Combine(targetRoot, component.Name);
                var backup = source + ".backup";
                createdBackups.Add(backup);
                FileSystemService.CopyDirectory(source, backup, component.Name,
                    backupFileCounts[component.Name], token, trackedProgress, ref completedWork, totalWork);
                FileSystemService.VerifyCopy(source, backup);
            }

            journal.Phase = "Extracting";
            JsonStore.WriteAtomic(journalPath, journal);
            _archives.ExtractSelectedDirect(archives, targetRoot,
                components.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                completedWork, totalWork, trackedProgress, token);

            var manifest = LoadManifestForUpdate(targetRoot);
            manifest.SchemaVersion = 2;
            manifest.Archive = null;
            manifest.Archives = _archives.CreateFingerprints(archives).ToList();
            foreach (var component in components)
                manifest.Components[component.Name] = new InstalledComponent
                {
                    ActiveVariant = "Texture",
                    InstalledAtUtc = DateTimeOffset.UtcNow
                };
            SaveManifestAndRetireLegacy(targetRoot, manifest);
            File.Delete(journalPath);
            trackedProgress.Report(new OperationProgress(100, "Installation complete", null,
                components[^1].Name, 100));
        }
        catch
        {
            var recovered = journal.Phase == "Extracting"
                ? RollbackInstall(targetRoot, journal.Components, trackedProgress, trackedProgress.LastPercent)
                : createdBackups.Select(FileSystemService.DeleteDirectoryBestEffort).Aggregate(true, (all, item) => all && item);
            if (recovered && File.Exists(journalPath)) File.Delete(journalPath);
            throw;
        }
    }

    public async Task SwapAsync(string targetRoot, IReadOnlyList<ArchiveComponent> components,
        IProgress<OperationProgress>? progress)
    {
        await Task.Run(() => SwapCore(targetRoot, components, progress));
    }

    public async Task UninstallAsync(string targetRoot, IReadOnlyList<ArchiveComponent> components,
        IProgress<OperationProgress>? progress)
    {
        await Task.Run(() => UninstallCore(targetRoot, components, progress));
    }

    private void UninstallCore(string targetRoot, IReadOnlyList<ArchiveComponent> components,
        IProgress<OperationProgress>? progress)
    {
        if (_isGameRunning())
            throw new InvalidOperationException("Close Dark Age of Camelot and the Eden Launcher before uninstalling textures.");
        if (components.Count == 0) throw new InvalidOperationException("Select at least one installed component.");
        EnsureNoOperationJournal(targetRoot);

        var states = components.ToDictionary(c => c.Name, c => StateDetector.Detect(targetRoot, c.Name),
            StringComparer.OrdinalIgnoreCase);
        if (states.Values.Any(s => s is not (ComponentState.TextureActive or ComponentState.OriginalActive)))
            throw new InvalidOperationException("Every selected component must be fully installed before uninstalling.");

        var journalPath = Path.Combine(targetRoot, JournalName);
        var journal = new OperationJournal
        {
            Action = "Uninstall",
            Phase = "RestoringOriginals",
            Components = components.Select(c => c.Name).ToList(),
            StartingStates = states.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase)
        };
        JsonStore.WriteAtomic(journalPath, journal);
        CompleteUninstall(targetRoot, journal, journalPath, new TrackingProgress(progress));
    }

    private void SwapCore(string targetRoot, IReadOnlyList<ArchiveComponent> components,
        IProgress<OperationProgress>? progress)
    {
        if (components.Count == 0) throw new InvalidOperationException("Select at least one installed component.");
        EnsureNoOperationJournal(targetRoot);

        var states = components.ToDictionary(c => c.Name, c => StateDetector.Detect(targetRoot, c.Name), StringComparer.OrdinalIgnoreCase);
        if (states.Values.Any(s => s is not (ComponentState.TextureActive or ComponentState.OriginalActive)))
            throw new InvalidOperationException("Every selected component must be fully installed before swapping.");

        var journalPath = Path.Combine(targetRoot, JournalName);
        var journal = new OperationJournal
        {
            Action = "Swap",
            Phase = "Swapping",
            Components = components.Select(c => c.Name).ToList(),
            StartingStates = states.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase)
        };
        JsonStore.WriteAtomic(journalPath, journal);
        var trackedProgress = new TrackingProgress(progress);

        try
        {
            for (var index = 0; index < components.Count; index++)
            {
                var name = components[index].Name;
                trackedProgress.Report(new OperationProgress(
                    ProgressMath.OverallWorkPercent(index, components.Count),
                    $"Switching {name}", null, name, 0));
                var active = Path.Combine(targetRoot, name);
                var swap = active + ".swap";
                var fromInactive = states[name] == ComponentState.TextureActive ? active + ".backup" : active + ".texturepack";
                var toInactive = states[name] == ComponentState.TextureActive ? active + ".texturepack" : active + ".backup";
                Directory.Move(active, swap);
                Directory.Move(fromInactive, active);
                Directory.Move(swap, toInactive);
                trackedProgress.Report(new OperationProgress(
                    index == components.Count - 1 ? 99 : ProgressMath.OverallWorkPercent(index + 1, components.Count),
                    $"Switched {name}", null, name, 100));
            }

            var manifest = LoadManifestForUpdate(targetRoot);
            foreach (var component in components)
            {
                if (!manifest.Components.TryGetValue(component.Name, out var installed))
                    manifest.Components[component.Name] = installed = new InstalledComponent { InstalledAtUtc = DateTimeOffset.UtcNow };
                installed.ActiveVariant = states[component.Name] == ComponentState.TextureActive ? "Original" : "Texture";
            }
            SaveManifestAndRetireLegacy(targetRoot, manifest);
            File.Delete(journalPath);
            trackedProgress.Report(new OperationProgress(100, "Switch complete", null, components[^1].Name, 100));
        }
        catch
        {
            RecoverSwap(targetRoot, journal, trackedProgress, trackedProgress.LastPercent);
            if (File.Exists(journalPath)) File.Delete(journalPath);
            throw;
        }
    }

    public static bool RecoverIfNeeded(string targetRoot, IProgress<OperationProgress>? progress = null,
        Func<bool>? isGameRunning = null)
    {
        var currentPath = Path.Combine(targetRoot, JournalName);
        var legacyPath = Path.Combine(targetRoot, LegacyJournalName);
        var hasCurrent = File.Exists(currentPath);
        var hasLegacy = File.Exists(legacyPath);
        if (hasCurrent && hasLegacy)
            throw new InvalidOperationException(
                $"Both {JournalName} and {LegacyJournalName} exist. Recovery is ambiguous; preserve both files and resolve the conflict manually.");

        var path = hasCurrent ? currentPath : legacyPath;
        var journal = JsonStore.Read<OperationJournal>(path);
        if (journal is null) return false;

        progress?.Report(new OperationProgress(0, "Recovering an interrupted operation"));
        if (journal.Action == "Install")
        {
            var recovered = journal.Phase == "Extracting"
                ? RollbackInstall(targetRoot, journal.Components, progress, 0)
                : journal.Components.Select(name => FileSystemService.DeleteDirectoryBestEffort(Path.Combine(targetRoot, name + ".backup")))
                    .Aggregate(true, (all, item) => all && item);
            if (!recovered) throw new IOException("The interrupted installation could not be fully cleaned up. Close programs using the DAoC folder and retry.");
        }
        else if (journal.Action == "Swap") RecoverSwap(targetRoot, journal, progress, 0);
        else if (journal.Action == "Uninstall")
        {
            CompleteUninstall(targetRoot, journal, path, new TrackingProgress(progress),
                isGameRunning ?? FileSystemService.IsGameRunning);
            return true;
        }
        File.Delete(path);
        return true;
    }

    public static IReadOnlyList<ArchiveComponent> DiscoverInstalledComponents(string targetRoot)
    {
        if (!Directory.Exists(targetRoot)) return [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = JsonStore.Read<InstallManifest>(Path.Combine(targetRoot, ManifestName));
        var legacy = JsonStore.Read<InstallManifest>(Path.Combine(targetRoot, LegacyManifestName));
        if (current is not null) names.UnionWith(current.Components.Keys);
        if (legacy is not null) names.UnionWith(legacy.Components.Keys);

        foreach (var path in Directory.EnumerateDirectories(targetRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
                names.Add(name[..^".backup".Length]);
            else if (name.EndsWith(".texturepack", StringComparison.OrdinalIgnoreCase))
                names.Add(name[..^".texturepack".Length]);
            else if (name.EndsWith(".swap", StringComparison.OrdinalIgnoreCase))
                names.Add(name[..^".swap".Length]);
            else
            {
                var rollback = name.IndexOf(".rollback-", StringComparison.OrdinalIgnoreCase);
                if (rollback > 0) names.Add(name[..rollback]);
            }
        }

        return names.Select(name => (Name: name, State: StateDetector.Detect(targetRoot, name)))
            .Where(item => item.State is ComponentState.TextureActive or ComponentState.OriginalActive or ComponentState.Conflict)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ArchiveComponent
            {
                Name = item.Name,
                HasArchiveMetadata = false,
                TargetExists = Directory.Exists(Path.Combine(targetRoot, item.Name)),
                State = item.State
            })
            .ToList();
    }

    private static void EnsureNoOperationJournal(string targetRoot)
    {
        if (File.Exists(Path.Combine(targetRoot, JournalName)) ||
            File.Exists(Path.Combine(targetRoot, LegacyJournalName)))
            throw new InvalidOperationException("An unfinished texture operation must be recovered before starting another operation.");
    }

    private static InstallManifest LoadManifestForUpdate(string targetRoot)
    {
        var current = JsonStore.Read<InstallManifest>(Path.Combine(targetRoot, ManifestName));
        var legacy = JsonStore.Read<InstallManifest>(Path.Combine(targetRoot, LegacyManifestName));
        if (current is null) return legacy ?? new InstallManifest();
        if (legacy is null) return current;

        foreach (var (name, component) in legacy.Components)
            current.Components.TryAdd(name, component);
        return current;
    }

    private static void SaveManifestAndRetireLegacy(string targetRoot, InstallManifest manifest)
    {
        UpgradeManifest(manifest);
        JsonStore.WriteAtomic(Path.Combine(targetRoot, ManifestName), manifest);
        var legacyPath = Path.Combine(targetRoot, LegacyManifestName);
        if (File.Exists(legacyPath)) File.Delete(legacyPath);
    }

    private static void UpgradeManifest(InstallManifest manifest)
    {
        manifest.Archives ??= [];
        if (manifest.Archives.Count == 0 && manifest.Archive is not null)
            manifest.Archives.Add(manifest.Archive);
        manifest.Archive = null;
        manifest.SchemaVersion = 2;
    }

    private static void RemoveManifestComponents(string targetRoot, IEnumerable<string> names)
    {
        var manifest = LoadManifestForUpdate(targetRoot);
        foreach (var name in names) manifest.Components.Remove(name);
        var currentPath = Path.Combine(targetRoot, ManifestName);
        var legacyPath = Path.Combine(targetRoot, LegacyManifestName);
        if (manifest.Components.Count == 0)
        {
            if (File.Exists(currentPath)) File.Delete(currentPath);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
        else SaveManifestAndRetireLegacy(targetRoot, manifest);
    }

    private static bool RollbackInstall(string targetRoot, IEnumerable<string> names,
        IProgress<OperationProgress>? progress, double overallPercent)
    {
        var recovered = true;
        foreach (var name in names)
        {
            progress?.Report(new OperationProgress(overallPercent, $"Rolling back {name}", null, name, 0));
            var active = Path.Combine(targetRoot, name);
            var backup = active + ".backup";
            if (Directory.Exists(backup))
            {
                var partial = active + $".rollback-{Guid.NewGuid():N}";
                if (Directory.Exists(active)) Directory.Move(active, partial);
                Directory.Move(backup, active);
            }
            foreach (var partial in Directory.EnumerateDirectories(targetRoot, name + ".rollback-*", SearchOption.TopDirectoryOnly))
                recovered &= FileSystemService.DeleteDirectoryBestEffort(partial);
            progress?.Report(new OperationProgress(overallPercent, $"Restored {name}", null, name, 100));
        }
        return recovered;
    }

    private sealed class TrackingProgress(IProgress<OperationProgress>? destination) : IProgress<OperationProgress>
    {
        public double LastPercent { get; private set; }

        public void Report(OperationProgress value)
        {
            LastPercent = Math.Max(LastPercent, value.Percent);
            destination?.Report(value with { Percent = LastPercent });
        }
    }

    private static void RecoverSwap(string targetRoot, OperationJournal journal,
        IProgress<OperationProgress>? progress, double overallPercent)
    {
        foreach (var name in journal.Components)
        {
            progress?.Report(new OperationProgress(overallPercent, $"Restoring {name}", null, name, 0));
            if (!journal.StartingStates.TryGetValue(name, out var stateText) ||
                !Enum.TryParse<ComponentState>(stateText, out var starting)) continue;
            var active = Path.Combine(targetRoot, name);
            var swap = active + ".swap";
            var originalInactive = starting == ComponentState.TextureActive ? active + ".backup" : active + ".texturepack";
            var finalInactive = starting == ComponentState.TextureActive ? active + ".texturepack" : active + ".backup";

            if (!Directory.Exists(swap)) continue;
            if (!Directory.Exists(active))
            {
                if (Directory.Exists(originalInactive))
                {
                    Directory.Move(originalInactive, active);
                    Directory.Move(swap, finalInactive);
                }
                else Directory.Move(swap, active);
            }
            else if (!Directory.Exists(finalInactive)) Directory.Move(swap, finalInactive);
            progress?.Report(new OperationProgress(overallPercent, $"Restored {name}", null, name, 100));
        }
    }

    private static void CompleteUninstall(string targetRoot, OperationJournal journal, string journalPath,
        TrackingProgress progress, Func<bool>? isGameRunning = null)
    {
        foreach (var name in journal.Components)
        {
            progress.Report(new OperationProgress(progress.LastPercent,
                $"Restoring originals for {name}", null, name, 0));
            EnsureOriginalActiveForUninstall(targetRoot, name);
            progress.Report(new OperationProgress(progress.LastPercent,
                $"Originals restored for {name}", null, name, 100));
        }

        if (isGameRunning?.Invoke() == true)
            throw new InvalidOperationException(
                "Original folders were restored, but DAoC or Eden is running. Close them to finish removing the texture copies.");

        journal.Phase = "RemovingTextures";
        JsonStore.WriteAtomic(journalPath, journal);
        var counts = journal.Components.ToDictionary(name => name,
            name => FileSystemService.CountFilesForRemoval(Path.Combine(targetRoot, name + ".texturepack")),
            StringComparer.OrdinalIgnoreCase);
        var totalWork = counts.Values.Aggregate(0L,
            (total, count) => checked(total + Math.Max(1, count)));
        long completedWork = 0;
        foreach (var name in journal.Components)
            FileSystemService.DeleteDirectoryWithProgress(Path.Combine(targetRoot, name + ".texturepack"),
                name, counts[name], progress, ref completedWork, totalWork);

        RemoveManifestComponents(targetRoot, journal.Components);
        File.Delete(journalPath);
        progress.Report(new OperationProgress(100, "Uninstall complete", null,
            journal.Components[^1], 100));
    }

    private static void EnsureOriginalActiveForUninstall(string targetRoot, string name)
    {
        var active = Path.Combine(targetRoot, name);
        var backup = active + ".backup";
        var texture = active + ".texturepack";
        var hasActive = Directory.Exists(active);
        var hasBackup = Directory.Exists(backup);
        var hasTexture = Directory.Exists(texture);

        if (hasActive && hasBackup && !hasTexture)
        {
            Directory.Move(active, texture);
            try { Directory.Move(backup, active); }
            catch
            {
                if (!Directory.Exists(active) && Directory.Exists(texture)) Directory.Move(texture, active);
                throw;
            }
            return;
        }

        if (hasActive && !hasBackup) return;
        if (!hasActive && hasBackup)
        {
            Directory.Move(backup, active);
            return;
        }

        throw new IOException($"Cannot safely restore the original {name} folder. Resolve its folder-state conflict before retrying.");
    }

    private void EnsureDiskSpace(ArchivePair archives, string targetRoot, IReadOnlyList<ArchiveComponent> components)
    {
        long backupBytes = 0;
        foreach (var component in components)
            backupBytes = checked(backupBytes + FileSystemService.MeasureDirectory(Path.Combine(targetRoot, component.Name)).Bytes);
        var growth = _archives.EstimateInstalledGrowth(archives, targetRoot,
            components.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var margin = Math.Max(1L << 30, (long)((backupBytes + growth) * .02));
        var conservativeRequired = checked(backupBytes + growth + margin);
        var root = Path.GetPathRoot(Path.GetFullPath(targetRoot))!;
        var free = new DriveInfo(root).AvailableFreeSpace;
        if (free < conservativeRequired)
            throw new IOException($"Insufficient disk space. Required: {FileSize.Format(conservativeRequired)}; available: {FileSize.Format(free)}.");
    }
}
