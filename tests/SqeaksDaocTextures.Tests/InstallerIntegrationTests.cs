using System.Text;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;

namespace SqeaksDaocTextures.Tests;

public sealed class InstallerIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sqeaks-daoc-texture-tests", Guid.NewGuid().ToString("N"));

    public InstallerIntegrationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Install_streams_directly_preserves_unrelated_files_and_swaps_both_ways()
    {
        var token = TestContext.Current.CancellationToken;
        var game = Path.Combine(_root, "game");
        var zones = Path.Combine(game, "zones");
        Directory.CreateDirectory(zones);
        await File.WriteAllTextAsync(Path.Combine(zones, "replaced.dds"), "old texture", token);
        await File.WriteAllTextAsync(Path.Combine(zones, "untouched.dat"), "keep me", token);

        var archives = CreateArchivePair("textures", ("zones/replaced.dds", "new texture"), ("zones/added.dds", "added"));
        var component = Assert.Single(new ArchiveService().Inspect(archives));
        var installUpdates = new List<OperationProgress>();

        var installer = new InstallerService(() => false);
        await installer.InstallAsync(archives, game, [component], new ImmediateProgress(installUpdates.Add), token);

        Assert.Equal("new texture", await File.ReadAllTextAsync(Path.Combine(zones, "replaced.dds"), token));
        Assert.Equal("added", await File.ReadAllTextAsync(Path.Combine(zones, "added.dds"), token));
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(zones, "untouched.dat"), token));
        Assert.Equal("old texture", await File.ReadAllTextAsync(Path.Combine(game, "zones.backup", "replaced.dds"), token));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.installing")));
        Assert.DoesNotContain(Directory.EnumerateDirectories(game), p => Path.GetFileName(p).Contains("temp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(installUpdates, update => update.Status.StartsWith("Backing up zones", StringComparison.Ordinal) &&
                                                  update.ComponentName == "zones" && update.ComponentPercent is >= 0 and <= 100);
        Assert.Contains(installUpdates, update => update.Status.StartsWith("Extracting zones", StringComparison.Ordinal) &&
                                                  update.ComponentName == "zones" && update.ComponentPercent is >= 0 and <= 100);
        Assert.Equal(100, installUpdates[^1].Percent);
        Assert.Equal(installUpdates.Select(update => update.Percent).OrderBy(value => value),
            installUpdates.Select(update => update.Percent));
        var installedManifest = JsonStore.Read<InstallManifest>(Path.Combine(game, InstallerService.ManifestName));
        Assert.NotNull(installedManifest);
        Assert.Equal(2, installedManifest.SchemaVersion);
        Assert.Null(installedManifest.Archive);
        Assert.Equal(2, installedManifest.Archives.Count);
        Assert.Equal(2, installedManifest.Archives.Sum(item => item.FileCount));

        var swapUpdates = new List<OperationProgress>();
        await new InstallerService(() => true).SwapAsync(
            game, [component], new ImmediateProgress(swapUpdates.Add));
        Assert.Equal("old texture", await File.ReadAllTextAsync(Path.Combine(zones, "replaced.dds"), token));
        Assert.False(File.Exists(Path.Combine(zones, "added.dds")));
        Assert.True(Directory.Exists(Path.Combine(game, "zones.texturepack")));
        Assert.Contains(swapUpdates, update => update.ComponentName == "zones" && update.ComponentPercent == 0);
        Assert.Contains(swapUpdates, update => update.ComponentName == "zones" && update.ComponentPercent == 100);
        Assert.Equal(100, swapUpdates[^1].Percent);

        await installer.SwapAsync(game, [component], null);
        Assert.Equal("new texture", await File.ReadAllTextAsync(Path.Combine(zones, "replaced.dds"), token));
        Assert.Equal("added", await File.ReadAllTextAsync(Path.Combine(zones, "added.dds"), token));
        Assert.True(Directory.Exists(Path.Combine(game, "zones.backup")));
    }

    [Fact]
    public async Task Uninstall_new_active_restores_original_and_removes_texture_copy_and_manifest()
    {
        var token = TestContext.Current.CancellationToken;
        var game = Path.Combine(_root, "uninstall-new-active");
        var zones = Path.Combine(game, "zones");
        Directory.CreateDirectory(zones);
        await File.WriteAllTextAsync(Path.Combine(zones, "replaced.dds"), "original", token);
        var archives = CreateArchivePair("uninstall-new-active", ("zones/replaced.dds", "texture"), ("zones/added.dds", "added"));
        var component = Assert.Single(new ArchiveService().Inspect(archives));
        var installer = new InstallerService(() => false);
        await installer.InstallAsync(archives, game, [component], null, token);
        var updates = new List<OperationProgress>();

        await installer.UninstallAsync(game, [component], new ImmediateProgress(updates.Add));

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(zones, "replaced.dds"), token));
        Assert.False(File.Exists(Path.Combine(zones, "added.dds")));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.texturepack")));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.ManifestName)));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.JournalName)));
        Assert.Contains(updates, update => update.ComponentName == "zones" &&
                                           update.Status.StartsWith("Removing textures", StringComparison.Ordinal));
        Assert.Equal(100, updates[^1].Percent);
        Assert.Equal(updates.Select(update => update.Percent).OrderBy(value => value),
            updates.Select(update => update.Percent));
    }

    [Fact]
    public async Task Uninstall_original_active_preserves_original_and_does_not_need_archive()
    {
        var token = TestContext.Current.CancellationToken;
        var game = Path.Combine(_root, "uninstall-original-active");
        var zones = Path.Combine(game, "zones");
        Directory.CreateDirectory(zones);
        await File.WriteAllTextAsync(Path.Combine(zones, "texture.dds"), "original", token);
        var archives = CreateArchivePair("uninstall-original-active", ("zones/texture.dds", "texture"));
        var component = Assert.Single(new ArchiveService().Inspect(archives));
        var installer = new InstallerService(() => false);
        await installer.InstallAsync(archives, game, [component], null, token);
        await installer.SwapAsync(game, [component], null);
        File.Delete(archives.Part1Path);
        File.Delete(archives.Part2Path);

        var discovered = Assert.Single(InstallerService.DiscoverInstalledComponents(game));
        Assert.False(discovered.HasArchiveMetadata);
        Assert.Equal("—", discovered.FileCountDisplay);
        Assert.Equal("—", discovered.SizeDisplay);
        await installer.UninstallAsync(game, [discovered], null);

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(zones, "texture.dds"), token));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.texturepack")));
        Assert.Equal(ComponentState.NotInstalled, StateDetector.Detect(game, "zones"));
    }

    [Fact]
    public async Task Selective_uninstall_preserves_other_installed_components_and_manifest_entry()
    {
        var token = TestContext.Current.CancellationToken;
        var game = Path.Combine(_root, "selective-uninstall");
        Directory.CreateDirectory(Path.Combine(game, "zones"));
        Directory.CreateDirectory(Path.Combine(game, "items"));
        await File.WriteAllTextAsync(Path.Combine(game, "zones", "z.dds"), "original z", token);
        await File.WriteAllTextAsync(Path.Combine(game, "items", "i.dds"), "original i", token);
        var archives = CreateArchivePair("selective-uninstall", ("zones/z.dds", "texture z"), ("items/i.dds", "texture i"));
        var components = new ArchiveService().Inspect(archives);
        var installer = new InstallerService(() => false);
        await installer.InstallAsync(archives, game, components, null, token);

        await installer.UninstallAsync(game, [components.Single(c => c.Name == "zones")], null);

        Assert.Equal(ComponentState.NotInstalled, StateDetector.Detect(game, "zones"));
        Assert.Equal(ComponentState.TextureActive, StateDetector.Detect(game, "items"));
        Assert.Equal("original z", await File.ReadAllTextAsync(Path.Combine(game, "zones", "z.dds"), token));
        Assert.Equal("texture i", await File.ReadAllTextAsync(Path.Combine(game, "items", "i.dds"), token));
        var manifest = JsonStore.Read<InstallManifest>(Path.Combine(game, InstallerService.ManifestName));
        Assert.NotNull(manifest);
        Assert.False(manifest.Components.ContainsKey("zones"));
        Assert.True(manifest.Components.ContainsKey("items"));
    }

    [Fact]
    public async Task Uninstall_rejects_running_game_without_changing_folders()
    {
        var game = Path.Combine(_root, "uninstall-running");
        Directory.CreateDirectory(Path.Combine(game, "zones"));
        Directory.CreateDirectory(Path.Combine(game, "zones.backup"));
        var component = new ArchiveComponent { Name = "zones", TargetExists = true };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InstallerService(() => true).UninstallAsync(game, [component], null));

        Assert.Contains("Close Dark Age of Camelot", error.Message);
        Assert.True(Directory.Exists(Path.Combine(game, "zones")));
        Assert.True(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.JournalName)));
    }

    [Fact]
    public void Interrupted_uninstall_restores_original_then_finishes_texture_removal()
    {
        var game = Path.Combine(_root, "uninstall-recovery");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(Path.Combine(game, "zones.backup"));
        Directory.CreateDirectory(Path.Combine(game, "zones.texturepack"));
        File.WriteAllText(Path.Combine(game, "zones.backup", "original.dat"), "original");
        File.WriteAllText(Path.Combine(game, "zones.texturepack", "texture.dat"), "texture");
        JsonStore.WriteAtomic(Path.Combine(game, InstallerService.JournalName), new OperationJournal
        {
            Action = "Uninstall",
            Phase = "RestoringOriginals",
            Components = ["zones"]
        });

        Assert.True(InstallerService.RecoverIfNeeded(game, null, () => false));

        Assert.Equal("original", File.ReadAllText(Path.Combine(game, "zones", "original.dat")));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.texturepack")));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.JournalName)));
    }

    [Fact]
    public async Task Failed_texture_removal_keeps_original_active_and_recovery_journal()
    {
        var game = Path.Combine(_root, "uninstall-locked");
        var active = Path.Combine(game, "zones");
        var texture = Path.Combine(game, "zones.texturepack");
        Directory.CreateDirectory(active);
        Directory.CreateDirectory(texture);
        await File.WriteAllTextAsync(Path.Combine(active, "original.dat"), "original", TestContext.Current.CancellationToken);
        var lockedPath = Path.Combine(texture, "texture.dat");
        await File.WriteAllTextAsync(lockedPath, "texture", TestContext.Current.CancellationToken);
        var component = new ArchiveComponent { Name = "zones", TargetExists = true };

        await using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() =>
                new InstallerService(() => false).UninstallAsync(game, [component], null));
            Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(active, "original.dat"),
                TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(game, InstallerService.JournalName)));
        }

        Assert.True(InstallerService.RecoverIfNeeded(game, null, () => false));
        Assert.False(Directory.Exists(texture));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.JournalName)));
    }

    [Fact]
    public async Task Invalid_archive_after_backup_restores_the_original_folder()
    {
        var token = TestContext.Current.CancellationToken;
        var game = Path.Combine(_root, "broken-game");
        var zones = Path.Combine(game, "zones");
        Directory.CreateDirectory(zones);
        await File.WriteAllTextAsync(Path.Combine(zones, "original.dat"), "original", token);
        var validPart1 = Path.Combine(_root, "invalid-Part1.7z");
        var invalidPart2 = Path.Combine(_root, "invalid-Part2.7z");
        CreateArchive(validPart1, ("zones/new.dat", "new"));
        await File.WriteAllTextAsync(invalidPart2, "not a seven zip archive", token);
        var archives = new ArchivePair(validPart1, invalidPart2);
        var component = new ArchiveComponent { Name = "zones", FileCount = 1, UncompressedSize = 1 };

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new InstallerService(() => false).InstallAsync(archives, game, [component], null, token));

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(zones, "original.dat"), token));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.JournalName)));
    }

    [Fact]
    public void Inventory_merges_a_component_split_across_both_parts()
    {
        var archives = CreateArchivePairSplit("merged",
            [("zones/one.dds", "one")],
            [("zones/two.dds", "two"), ("items/item.dds", "item")]);

        var components = new ArchiveService().Inspect(archives);

        Assert.Equal(2, components.Count);
        Assert.Equal(2, components.Single(component => component.Name == "zones").FileCount);
        Assert.Equal(3, components.Sum(component => component.FileCount));
    }

    [Fact]
    public void Duplicate_destination_across_parts_is_rejected()
    {
        var archives = CreateArchivePairSplit("duplicate",
            [("zones/same.dds", "part one")],
            [("ZONES/SAME.DDS", "part two")]);

        var error = Assert.Throws<InvalidDataException>(() => new ArchiveService().Inspect(archives));

        Assert.Contains("Duplicate archive destination", error.Message);
    }

    [Fact]
    public async Task Missing_part_is_rejected_before_backup_creation()
    {
        var token = TestContext.Current.CancellationToken;
        var game = Path.Combine(_root, "missing-part");
        Directory.CreateDirectory(Path.Combine(game, "zones"));
        await File.WriteAllTextAsync(Path.Combine(game, "zones", "original.dat"), "original", token);
        var part1 = Path.Combine(_root, "missing-Part1.7z");
        CreateArchive(part1, ("zones/new.dat", "new"));
        var archives = new ArchivePair(part1, Path.Combine(_root, "missing-Part2.7z"));
        var component = new ArchiveComponent { Name = "zones", FileCount = 1, UncompressedSize = 3 };

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new InstallerService(() => false).InstallAsync(archives, game, [component], null, token));

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(game, "zones", "original.dat"), token));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.JournalName)));
    }

    [Fact]
    public async Task Part2_failure_after_verified_backup_rolls_back_original_folder()
    {
        var token = TestContext.Current.CancellationToken;
        var game = Path.Combine(_root, "part2-failure");
        var zones = Path.Combine(game, "zones");
        Directory.CreateDirectory(zones);
        await File.WriteAllTextAsync(Path.Combine(zones, "original.dat"), "original", token);
        var archives = CreateArchivePairSplit("part2-failure",
            [("zones/part1.dat", "one")],
            [("zones/part2.dat", "two")]);
        var component = Assert.Single(new ArchiveService().Inspect(archives));
        var corrupted = false;
        var progress = new ImmediateProgress(update =>
        {
            if (corrupted || !update.Status.StartsWith("Verifying backup", StringComparison.Ordinal)) return;
            File.WriteAllText(archives.Part2Path, "corrupt after preflight");
            corrupted = true;
        });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new InstallerService(() => false).InstallAsync(archives, game, [component], progress, token));

        Assert.True(corrupted);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(zones, "original.dat"), token));
        Assert.False(File.Exists(Path.Combine(zones, "part1.dat")));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.JournalName)));
    }

    [Fact]
    public async Task Cancellation_during_direct_extraction_restores_all_original_files()
    {
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var game = Path.Combine(_root, "cancel-game");
        var zones = Path.Combine(game, "zones");
        Directory.CreateDirectory(zones);
        await File.WriteAllTextAsync(Path.Combine(zones, "first.dds"), "old first", tokenSource.Token);
        await File.WriteAllTextAsync(Path.Combine(zones, "second.dds"), "old second", tokenSource.Token);

        var archives = CreateArchivePair("cancel", ("zones/first.dds", "new first"), ("zones/second.dds", "new second"));
        var component = Assert.Single(new ArchiveService().Inspect(archives));
        var progress = new ImmediateProgress(update =>
        {
            if (update.Status.StartsWith("Extracting", StringComparison.Ordinal) && update.CurrentFile is not null)
                tokenSource.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InstallerService(() => false).InstallAsync(archives, game, [component], progress, tokenSource.Token));

        Assert.Equal("old first", await File.ReadAllTextAsync(Path.Combine(zones, "first.dds"), TestContext.Current.CancellationToken));
        Assert.Equal("old second", await File.ReadAllTextAsync(Path.Combine(zones, "second.dds"), TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.Empty(Directory.EnumerateDirectories(game, "zones.rollback-*"));
    }

    [Fact]
    public void Legacy_backup_journal_is_recovered_and_removed()
    {
        var game = Path.Combine(_root, "legacy-recovery");
        Directory.CreateDirectory(Path.Combine(game, "zones"));
        Directory.CreateDirectory(Path.Combine(game, "zones.backup"));
        File.WriteAllText(Path.Combine(game, "zones.backup", "copied.dat"), "copy");
        JsonStore.WriteAtomic(Path.Combine(game, InstallerService.LegacyJournalName), new OperationJournal
        {
            Action = "Install",
            Phase = "BackingUp",
            Components = ["zones"]
        });

        Assert.True(InstallerService.RecoverIfNeeded(game));
        Assert.False(Directory.Exists(Path.Combine(game, "zones.backup")));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.LegacyJournalName)));
    }

    [Fact]
    public void Current_and_legacy_journals_are_reported_as_a_conflict()
    {
        var game = Path.Combine(_root, "journal-conflict");
        Directory.CreateDirectory(game);
        var journal = new OperationJournal { Action = "Install", Phase = "BackingUp", Components = ["zones"] };
        JsonStore.WriteAtomic(Path.Combine(game, InstallerService.JournalName), journal);
        JsonStore.WriteAtomic(Path.Combine(game, InstallerService.LegacyJournalName), journal);

        var error = Assert.Throws<InvalidOperationException>(() => InstallerService.RecoverIfNeeded(game));
        Assert.Contains("Recovery is ambiguous", error.Message);
        Assert.True(File.Exists(Path.Combine(game, InstallerService.JournalName)));
        Assert.True(File.Exists(Path.Combine(game, InstallerService.LegacyJournalName)));
    }

    [Fact]
    public async Task Updating_install_state_migrates_the_legacy_manifest()
    {
        var game = Path.Combine(_root, "legacy-manifest");
        Directory.CreateDirectory(Path.Combine(game, "zones"));
        Directory.CreateDirectory(Path.Combine(game, "zones.backup"));
        await File.WriteAllTextAsync(Path.Combine(game, "zones", "texture.dat"), "texture", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(game, "zones.backup", "original.dat"), "original", TestContext.Current.CancellationToken);
        JsonStore.WriteAtomic(Path.Combine(game, InstallerService.LegacyManifestName), new InstallManifest
        {
            SchemaVersion = 1,
            Archive = new ArchiveFingerprint { FileName = "legacy.7z", Length = 10, FileCount = 1, UncompressedBytes = 1 },
            Archives = [],
            Components = new Dictionary<string, InstalledComponent>(StringComparer.OrdinalIgnoreCase)
            {
                ["zones"] = new() { ActiveVariant = "Texture", InstalledAtUtc = DateTimeOffset.UtcNow }
            }
        });
        var component = new ArchiveComponent { Name = "zones", FileCount = 1, UncompressedSize = 1, TargetExists = true };

        await new InstallerService(() => false).SwapAsync(game, [component], null);

        Assert.True(File.Exists(Path.Combine(game, InstallerService.ManifestName)));
        Assert.False(File.Exists(Path.Combine(game, InstallerService.LegacyManifestName)));
        var migrated = JsonStore.Read<InstallManifest>(Path.Combine(game, InstallerService.ManifestName));
        Assert.Equal("Original", migrated!.Components["zones"].ActiveVariant);
        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Null(migrated.Archive);
        Assert.Single(migrated.Archives);
        Assert.Equal("legacy.7z", migrated.Archives[0].FileName);
    }

    private static void CreateArchive(string path, params (string Name, string Contents)[] entries)
    {
        using var output = File.Create(path);
        using var writer = WriterFactory.OpenWriter(output, ArchiveType.SevenZip,
            new SevenZipWriterOptions(CompressionType.LZMA2) { CompressHeader = true });
        foreach (var (name, contents) in entries)
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(contents));
            writer.Write(name, content, DateTime.UtcNow);
        }
    }

    private ArchivePair CreateArchivePair(string name, params (string Name, string Contents)[] part1Entries)
    {
        var part1 = Path.Combine(_root, name + "-Part1.7z");
        var part2 = Path.Combine(_root, name + "-Part2.7z");
        var split = Math.Max(1, part1Entries.Length / 2);
        CreateArchive(part1, part1Entries[..split]);
        var secondEntries = part1Entries[split..];
        if (secondEntries.Length == 0)
        {
            var top = part1Entries[0].Name.Replace('\\', '/').Split('/')[0];
            secondEntries = [(top + "/.sqeaks-part2", "part 2")];
        }
        CreateArchive(part2, secondEntries);
        return new ArchivePair(part1, part2);
    }

    private ArchivePair CreateArchivePairSplit(string name,
        (string Name, string Contents)[] part1Entries,
        (string Name, string Contents)[] part2Entries)
    {
        var part1 = Path.Combine(_root, name + "-Part1.7z");
        var part2 = Path.Combine(_root, name + "-Part2.7z");
        CreateArchive(part1, part1Entries);
        CreateArchive(part2, part2Entries);
        return new ArchivePair(part1, part2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class ImmediateProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
