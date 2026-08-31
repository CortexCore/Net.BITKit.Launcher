# Net.BITKit.Launcher Handoff

## Scope

This repository contains a .NET 8 WinForms game launcher/updater for Like Tokyo.
The current v1 design uses a single self-contained `win-x64` EXE.

## Projects

- `Net.BITKit.Launcher/`: core manifest, hashing, update, transaction and self-update logic.
- `Net.BITKit.Launcher.WinForm/`: WinForms GUI, mini installer, update page and custom controls.
- `tools/publish-like-tokyo.ps1`: Like Tokyo publisher for OSS.
- `tools/run-simulate-install.ps1` / `.cmd`: rebuild and launch the simulation preview.
- `run-simulate-install.cmd`: root double-click entry point for simulation.

## Build

```powershell
dotnet build .\Net.BITKit.Launcher.sln -c Release --no-restore
```

The published launcher is self-contained single-file `win-x64`. Product metadata is embedded through MSBuild properties (`LauncherProductId`, `LauncherDisplayName`, `LauncherManifestUrl`, `LauncherInstallDirectory`, `LauncherGameDataDirectory`).

## Simulation

Double-click `run-simulate-install.cmd`, or run:

```powershell
.\tools\run-simulate-install.ps1
```

`--simulate-install` copies only the launcher to the selected target and then opens the update page in simulated mode. The simulated update does not download or modify game files.

## Like Tokyo publishing

Default paths are defined in `tools/publish-like-tokyo.ps1`:

- Game root: `D:\Iris\Documents\Workspace\Net.Like.Xue.Tokyo.V6`
- OSS destination: `oss://like-game/Net.Like.Xue.Tokyo/App/`
- Public manifest: `https://like-game.oss-cn-shenzhen.aliyuncs.com/Net.Like.Xue.Tokyo/App/manifest.yaml`
- ossutil: `D:\Iris\Applications\ossutil-2\ossutil.exe` or `OSSUTIL_PATH`

Dry run:

```powershell
.\tools\publish-like-tokyo.ps1 -DryRun
```

Formal publish:

```powershell
.\tools\publish-like-tokyo.ps1
```

Use `-Force` to skip the `PUBLISH` confirmation. The script syncs files with checksum, does not use `--delete`, uploads `manifest.yaml` last, and verifies the public manifest afterward.

## Runtime behavior

If a bootstrap EXE has no local `manifest.yaml`, it opens the mini installer. The original downloaded EXE is not deleted or moved. It is copied to the chosen install directory and later launches redirect to the installed copy. Configuration defaults to `%LOCALAPPDATA%\BITKit\Launcher`, overridable with `BITKIT_LAUNCHER_HOME` or `--config-dir`.

## Known constraints

- WinForms layout is currently code-based; Rider Designer is unreliable for this .NET 8/custom-control form.
- Taskbar icon is bound from the published EXE icon at runtime; restart an already-running launcher to refresh Windows icon cache.
- Unity persistent data remains `%USERPROFILE%\AppData\LocalLow\LikeXue\LikeTokyo`; the launcher does not relocate it.
- Do not commit `.preview/`, `bin/`, `obj/`, `.bitkit-update/`, or OSS credentials.
