$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$preview = Join-Path $repo '.preview'
$project = Join-Path $repo 'Net.BITKit.Launcher.WinForm\Net.BITKit.Launcher.WinForm.csproj'

Get-Process -Name Net.BITKit.Launcher -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
if (Test-Path -LiteralPath $preview) {
    Remove-Item -LiteralPath $preview -Recurse -Force
}

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:LauncherProductId=net.like-xue.like-tokyo `
    -p:LauncherDisplayName='Like Tokyo' `
    -p:LauncherManifestUrl='https://like-game.oss-cn-shenzhen.aliyuncs.com/Net.Like.Xue.Tokyo/App/manifest.yaml' `
    -p:LauncherInstallDirectory='Net.Like.Xue.Tokyo' `
    -p:LauncherGameDataDirectory='%USERPROFILE%\AppData\LocalLow\LikeXue\LikeTokyo' `
    -o $preview

$exe = Join-Path $preview 'Net.BITKit.Launcher.exe'
Start-Process -FilePath $exe -ArgumentList '--simulate-install' -WorkingDirectory $preview
