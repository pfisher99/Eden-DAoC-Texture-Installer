namespace SqeaksDaocTextures;

public static class StateDetector
{
    public static ComponentState Detect(string targetRoot, string componentName)
    {
        var active = Directory.Exists(Path.Combine(targetRoot, componentName));
        var backup = Directory.Exists(Path.Combine(targetRoot, componentName + ".backup"));
        var texture = Directory.Exists(Path.Combine(targetRoot, componentName + ".texturepack"));
        var swap = Directory.Exists(Path.Combine(targetRoot, componentName + ".swap"));
        var rollback = Directory.Exists(targetRoot) &&
            Directory.EnumerateDirectories(targetRoot, componentName + ".rollback-*", SearchOption.TopDirectoryOnly).Any();

        if (swap || rollback || (backup && texture) || (!active && (backup || texture))) return ComponentState.Conflict;
        if (!active) return ComponentState.Missing;
        if (backup) return ComponentState.TextureActive;
        if (texture) return ComponentState.OriginalActive;
        return ComponentState.NotInstalled;
    }
}
