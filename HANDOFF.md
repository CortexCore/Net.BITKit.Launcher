# Net.BITKit.Launcher Handoff

## Scope

This repository contains a .NET 10 WPF game launcher/updater.
Windows releases use a self-contained `win-x64` single EXE. The separate Runtime bootstrap experiment was abandoned and removed after two failed validation efforts; see `docs/runtime-bootstrap-abandoned.md`.

## Projects

- `Net.BITKit.Launcher/`: core manifest, hashing, update, transaction and self-update logic.
- `Net.BITKit.Launcher.WinForm/`: native WPF GUI, installer, update page, and CLI executable. The legacy directory name is retained for publish-script compatibility.
- `tools/publish-project-b-self-hosted.ps1`: Project B self-contained publisher for a static web directory.
- `tools/publish-like-tokyo.ps1`: Like Tokyo publisher for OSS.
- `tools/run-simulate-install.ps1` / `.cmd`: rebuild and launch the simulation preview.
- `run-simulate-install.cmd`: root double-click entry point for simulation.

## Build

```powershell
dotnet build .\Net.BITKit.Launcher.sln -c Release --no-restore
```

The Project B publisher builds a .NET 10 `win-x64` self-contained single EXE. Product metadata is embedded through MSBuild properties (`LauncherProductId`, `LauncherDisplayName`, `LauncherManifestUrl`, `LauncherInstallDirectory`, `LauncherGameDataDirectory`).

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
- CDN base URL: `https://like-game.oss-accelerate.aliyuncs.com/Net.Like.Xue.Tokyo/App`
- ossutil: `D:\Iris\Applications\ossutil-2\ossutil.exe` or `OSSUTIL_PATH`

Dry run:

```powershell
.\tools\publish-like-tokyo.ps1 -DryRun
```

Formal publish:

```powershell
.\tools\publish-like-tokyo.ps1 `
  -CdnBaseUrl https://like-game.oss-accelerate.aliyuncs.com/Net.Like.Xue.Tokyo/App
```

Use `-Force` to skip the `PUBLISH` confirmation. Uploads always use `OssDestination` (`oss://...`), while the embedded installer URL and generated manifest use `CdnBaseUrl`. The script generates `files.txt` through `Net.BITKit.Launcher.exe generate`, then runs `ossutil sync --files-from-raw files.txt` so ignored files are not uploaded. It does not use `--delete`, uploads `manifest.yaml` last, and verifies it through the CDN afterward.

## Runtime behavior

The distributed launcher is the application itself, not a bootstrap host. It carries the .NET 10 runtime and updates that same EXE in place through the temporary self-update helper. The native WPF UI does not require WebView2.

## Known constraints

- The launcher is over 100 MiB because it is self-contained. Do not reintroduce the removed Runtime bootstrap without a new design decision; see `docs/runtime-bootstrap-abandoned.md`.
- The project path still contains `WinForm` for compatibility, but the production UI is WPF.
- Taskbar icon is bound from the published EXE icon at runtime; restart an already-running launcher to refresh Windows icon cache.
- Unity persistent data remains `%USERPROFILE%\AppData\LocalLow\LikeXue\LikeTokyo`; the launcher does not relocate it.
- Do not commit `.preview/`, `bin/`, `obj/`, `.bitkit-update/`, or OSS credentials.
