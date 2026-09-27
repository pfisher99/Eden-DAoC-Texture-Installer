namespace SqeaksDaocTextures.Tests;

public sealed class StartupOptionsTests
{
    [Fact]
    public void Elevated_uninstall_arguments_preserve_target_archive_and_components()
    {
        var options = StartupOptions.Parse(
        [
            "--elevated-action", "uninstall",
            "--target", @"C:\Games\DAoC",
            "--archive", "",
            "--component", "zones",
            "--component", "items"
        ]);

        Assert.Equal("uninstall", options.ElevatedAction);
        Assert.Equal(@"C:\Games\DAoC", options.TargetPath);
        Assert.Equal("", options.ArchivePath);
        Assert.Equal(["zones", "items"], options.Components);
    }
}
