namespace SqeaksDaocTextures.Tests;

public sealed class StateDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eden-installer-tests", Guid.NewGuid().ToString("N"));

    public StateDetectorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Active_only_is_not_installed()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zones"));
        Assert.Equal(ComponentState.NotInstalled, StateDetector.Detect(_root, "zones"));
    }

    [Fact]
    public void Backup_means_texture_is_active()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zones"));
        Directory.CreateDirectory(Path.Combine(_root, "zones.backup"));
        Assert.Equal(ComponentState.TextureActive, StateDetector.Detect(_root, "zones"));
    }

    [Fact]
    public void Texturepack_means_original_is_active()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zones"));
        Directory.CreateDirectory(Path.Combine(_root, "zones.texturepack"));
        Assert.Equal(ComponentState.OriginalActive, StateDetector.Detect(_root, "zones"));
    }

    [Fact]
    public void Both_inactive_variants_are_a_conflict()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zones"));
        Directory.CreateDirectory(Path.Combine(_root, "zones.backup"));
        Directory.CreateDirectory(Path.Combine(_root, "zones.texturepack"));
        Assert.Equal(ComponentState.Conflict, StateDetector.Detect(_root, "zones"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
