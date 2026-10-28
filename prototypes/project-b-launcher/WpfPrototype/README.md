# Project B WPF Launcher Prototype

Native WPF proof of concept for the Project B installer and launcher UI. It does not use WebView2.

Run the interactive prototype:

```powershell
dotnet run --project .\prototypes\project-b-launcher\WpfPrototype\ProjectB.LauncherWpfPrototype.csproj
```

Render a deterministic preview without interacting with the window:

```powershell
dotnet run --project .\prototypes\project-b-launcher\WpfPrototype\ProjectB.LauncherWpfPrototype.csproj -- `
  --render .\wpf-install.png install
```

The last argument can be `install`, `sync`, or `ready`.

Publish a native `win-x64` self-contained executable:

```powershell
dotnet publish .\prototypes\project-b-launcher\WpfPrototype\ProjectB.LauncherWpfPrototype.csproj `
  -c Release -r win-x64 --self-contained true -o .\artifacts\wpf-prototype
```

The project bundles the WPF native rendering libraries into the executable. The
October 2026 verification build was 136.20 MiB including its optional PDB and did
not require WebView2.
