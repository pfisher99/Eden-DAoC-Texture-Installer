# Sqeak's DAoC Textures
[Created by Sqeak. This texture pack was built and tested for Eden. Compatibility with Live and other Dark Age of Camelot servers is untested.](https://www.nexusmods.com/darkageofcamelot/mods/2)

The Windows installer treats `Sqeaks-DAoC-Textures-v0.1-Part1.7z` and `Sqeaks-DAoC-Textures-v0.1-Part2.7z` as one texture pack, backs up selected DAoC folders, streams both archives directly into the live folders, switches between the original and texture-pack variants, and safely restores originals when selected textures are uninstalled.

## Versions

- Texture pack: v0.1
- Installer: v1.2

## Build

```powershell
dotnet publish src\SqeaksDaocTextureInstaller\SqeaksDaocTextureInstaller.csproj -c Release -r win-x64 --self-contained false -o dist
```

The published installer is a framework-dependent, single-file `win-x64` application. The target computer must have the [x64 .NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed. If the required runtime is missing, the standard .NET launch error identifies it and provides a download link. See Microsoft's documentation for [framework-dependent deployment](https://learn.microsoft.com/dotnet/core/deploying/) and [missing-runtime launch errors](https://learn.microsoft.com/dotnet/core/runtime-discovery/troubleshoot-app-launch).

For distribution, place `Sqeaks-DAoC-Textures-v0.1-Part1.7z` and `Sqeaks-DAoC-Textures-v0.1-Part2.7z` beside `Sqeaks-DAoC-Texture-Installer-v1.2.exe`. Both archives are required and are deliberately not embedded or copied into the application. Browsing to either part automatically locates its matching sibling.

## Safety model

- Every selected live folder is fully copied to `<folder>.backup` and verified before the archive can modify any live file.
- Archive entries stream from Part 1 and then Part 2 directly to live folders; no texture staging directory is used.
- A failed or cancelled extraction restores every selected live folder from its verified backup.
- Installed variants switch via `<folder>.backup` and `<folder>.texturepack` sibling renames.
- Uninstall restores the original folder before permanently deleting its texture-pack copy and can recover after interruption.
- The archives are not required to switch or uninstall an existing installation.
- DAoC and the Eden Launcher must be closed during installation and uninstall. Installed texture folders may be switched while the game is running.
