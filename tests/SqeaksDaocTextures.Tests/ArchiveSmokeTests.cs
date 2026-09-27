namespace SqeaksDaocTextures.Tests;

public sealed class ArchiveSmokeTests
{
    [Fact]
    [Trait("Category", "LargeArchive")]
    public void Supplied_archive_has_expected_inventory()
    {
        var selected = Environment.GetEnvironmentVariable("SQEAKS_DAOC_TEXTURE_ARCHIVE_PART1") ??
                       Environment.GetEnvironmentVariable("SQEAKS_DAOC_TEXTURE_ARCHIVE");
        if (string.IsNullOrWhiteSpace(selected)) return;
        var archives = ArchiveLocator.ResolveSelected(selected);
        if (!ArchiveLocator.IsComplete(archives)) return;

        var components = new ArchiveService().Inspect(archives!);
        Assert.Equal(10, components.Count);
        Assert.Equal(10_994, components.Sum(c => c.FileCount));
        Assert.InRange(components.Sum(c => c.UncompressedSize), 46L * 1024 * 1024 * 1024, 47L * 1024 * 1024 * 1024);
    }
}
