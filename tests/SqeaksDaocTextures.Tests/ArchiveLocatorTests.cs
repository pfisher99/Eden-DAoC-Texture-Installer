namespace SqeaksDaocTextures.Tests;

public sealed class ArchiveLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sqeaks-archive-locator", Guid.NewGuid().ToString("N"));

    public ArchiveLocatorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Exact_current_pair_wins_over_future_pairs()
    {
        var expected = TouchPair("Sqeaks-DAoC-Textures-v0.1");
        TouchPair("Sqeaks-DAoC-Textures-v0.2");
        Assert.Equal(expected, ArchiveLocator.FindPreferred(_root));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Selecting_either_part_resolves_the_same_pair(int part)
    {
        var expected = TouchPair("Sqeaks-DAoC-Textures-v0.1");
        var selected = part == 1 ? expected.Part1Path : expected.Part2Path;
        Assert.Equal(expected, ArchiveLocator.ResolveSelected(selected));
    }

    [Fact]
    public void Single_future_matched_pair_is_discovered()
    {
        var expected = TouchPair("Sqeaks-DAoC-Textures-v0.2");
        Assert.Equal(expected, ArchiveLocator.FindPreferred(_root));
    }

    [Fact]
    public void Multiple_non_current_pairs_are_ambiguous()
    {
        TouchPair("Sqeaks-DAoC-Textures-v0.2");
        TouchPair("Sqeaks-DAoC-Textures-v0.3");
        Assert.Null(ArchiveLocator.FindPreferred(_root));
    }

    [Fact]
    public void Orphan_part_is_not_a_complete_pair()
    {
        var part1 = Touch("Sqeaks-DAoC-Textures-v0.1-Part1.7z");
        Assert.False(ArchiveLocator.IsComplete(ArchiveLocator.ResolveSelected(part1)));
        Assert.Contains("Part 2", ArchiveLocator.ValidationMessage(part1));
        Assert.Null(ArchiveLocator.FindPreferred(_root));
    }

    [Fact]
    public void Saved_old_single_path_migrates_by_searching_its_directory()
    {
        var executableDirectory = Path.Combine(_root, "app");
        var downloadDirectory = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(executableDirectory);
        Directory.CreateDirectory(downloadDirectory);
        var expected = TouchPair("Sqeaks-DAoC-Textures-v0.1", downloadDirectory);
        var oldSingle = Path.Combine(downloadDirectory, "Sqeaks-DAoC-Textures-v0.1.7z");
        Assert.Equal(expected, ArchiveLocator.FindPreferred(executableDirectory, oldSingle));
    }

    [Fact]
    public void Old_single_archive_alone_is_no_longer_supported()
    {
        var old = Touch("Sqeaks-DAoC-Textures-v0.1.7z");
        Assert.Null(ArchiveLocator.ResolveSelected(old));
        Assert.Null(ArchiveLocator.FindPreferred(_root, old));
    }

    private ArchivePair TouchPair(string baseName, string? directory = null)
    {
        directory ??= _root;
        return new ArchivePair(
            Touch(baseName + "-Part1.7z", directory),
            Touch(baseName + "-Part2.7z", directory));
    }

    private string Touch(string name, string? directory = null)
    {
        directory ??= _root;
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    public void Dispose() => Directory.Delete(_root, true);
}
