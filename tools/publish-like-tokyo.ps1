[CmdletBinding()]
param(
    [string]$GameRoot = 'D:\Iris\Documents\Workspace\Net.Like.Xue.Tokyo.V6',
    [string]$OssDestination = 'oss://like-game/Net.Like.Xue.Tokyo/App/',
    [string]$PublicBaseUrl = 'https://like-game.oss-cn-shenzhen.aliyuncs.com/Net.Like.Xue.Tokyo/App',
    [string]$OssUtilPath = $env:OSSUTIL_PATH,
    [string]$IconPng = 'D:\Iris\Documents\GitHub\Net.Like.Xue.Tokyo\Assets\Artists\Arts\Logos\logo.png',
    [string]$GameName = 'Net.Like.Xue.Tokyo.V6',
    [string]$GameExecutable = 'LikeTokyo.exe',
    [switch]$DryRun,
    [switch]$Force,
    [switch]$KeepStaging
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$launcherProject = Join-Path $repositoryRoot 'Net.BITKit.Launcher.WinForm\Net.BITKit.Launcher.WinForm.csproj'
$manifestName = 'manifest.yaml'
$launcherName = 'Net.BITKit.Launcher.exe'
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)
$stagingRoot = $null
$manifestBackup = $null
$launcherBackupPath = $null
$launcherWasReplaced = $false
$committed = $false

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked([string]$Executable, [string[]]$ArgumentList) {
    & $Executable @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "命令失败，退出码 $LASTEXITCODE：$Executable $($ArgumentList -join ' ')"
    }
}

function Resolve-OssUtil([string]$ConfiguredPath) {
    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        $candidates.Add($ConfiguredPath)
    }

    $candidates.Add('D:\Iris\Applications\ossutil-2\ossutil.exe')
    $command = Get-Command 'ossutil.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $candidates.Add($command.Source)
    }

    foreach ($candidate in $candidates) {
        $path = if (Test-Path -LiteralPath $candidate -PathType Container) {
            Join-Path $candidate 'ossutil.exe'
        }
        else {
            $candidate
        }

        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return [IO.Path]::GetFullPath($path)
        }
    }

    throw '找不到 ossutil.exe。请设置 OSSUTIL_PATH，或使用 -OssUtilPath 指定路径。'
}

function Convert-PngToMultiSizeIco([string]$SourcePath, [string]$DestinationPath) {
    Add-Type -AssemblyName System.Drawing
    $sizes = @(16, 32, 48, 64, 128, 256)
    $source = [Drawing.Bitmap]::new($SourcePath)
    $images = [Collections.Generic.List[byte[]]]::new()
    try {
        foreach ($size in $sizes) {
            $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            }
            finally {
                $graphics.Dispose()
            }

            $memory = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
                $images.Add($memory.ToArray())
            }
            finally {
                $memory.Dispose()
                $bitmap.Dispose()
            }
        }
    }
    finally {
        $source.Dispose()
    }

    $stream = [IO.File]::Open($DestinationPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$index].Length
        }

        foreach ($image in $images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Restore-File([string]$Path, [byte[]]$Backup) {
    if ($null -eq $Backup) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }
    else {
        [IO.File]::WriteAllBytes($Path, $Backup)
    }
}

$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
if (-not (Test-Path -LiteralPath $resolvedGameRoot -PathType Container)) {
    throw "游戏目录不存在：$resolvedGameRoot"
}

if (-not (Test-Path -LiteralPath $launcherProject -PathType Leaf)) {
    throw "启动器项目不存在：$launcherProject"
}

if (-not (Test-Path -LiteralPath $IconPng -PathType Leaf)) {
    throw "图标 PNG 不存在：$IconPng"
}

$resolvedOssUtil = Resolve-OssUtil $OssUtilPath
$gameManifestPath = Join-Path $resolvedGameRoot $manifestName
$gameLauncherPath = Join-Path $resolvedGameRoot $launcherName
$gameExecutablePath = Join-Path $resolvedGameRoot $GameExecutable
if (-not (Test-Path -LiteralPath $gameExecutablePath -PathType Leaf)) {
    throw "游戏主程序不存在：$gameExecutablePath"
}

Write-Host 'Like Tokyo OSS 专用发布' -ForegroundColor Yellow
Write-Host "游戏目录：$resolvedGameRoot"
Write-Host "OSS 目标：$OssDestination"
Write-Host "ossutil：$resolvedOssUtil"
Write-Host "模式：$(if ($DryRun) { '模拟，不写 OSS' } else { '正式发布' })"

if (-not $DryRun -and -not $Force) {
    $confirmation = Read-Host '输入 PUBLISH 继续正式发布'
    if ($confirmation -cne 'PUBLISH') {
        throw '发布已取消。'
    }
}

$manifestBackup = if (Test-Path -LiteralPath $gameManifestPath -PathType Leaf) {
    [IO.File]::ReadAllBytes($gameManifestPath)
}
else {
    $null
}

$parent = Split-Path -Parent $resolvedGameRoot
$stagingRoot = Join-Path $parent ('.bitkit-publish-' + [Guid]::NewGuid().ToString('N'))
$launcherOutput = Join-Path $stagingRoot 'launcher'
$payloadRoot = Join-Path $stagingRoot 'payload'
$fileListPath = Join-Path $stagingRoot 'files.txt'
$iconPath = Join-Path $stagingRoot 'launcher.ico'
$remoteManifestCheck = Join-Path $stagingRoot 'manifest.remote.yaml'
New-Item -ItemType Directory -Path $launcherOutput, $payloadRoot | Out-Null

try {
    Write-Step '生成多尺寸 Windows 图标并发布单文件启动器'
    Convert-PngToMultiSizeIco $IconPng $iconPath
    Invoke-Checked 'dotnet' @(
        'publish', $launcherProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-o', $launcherOutput,
        "-p:ApplicationIcon=$iconPath",
        '-p:LauncherProductId=net.like-xue.like-tokyo',
        '-p:LauncherDisplayName=Like Tokyo',
        "-p:LauncherManifestUrl=$($PublicBaseUrl.TrimEnd('/'))/manifest.yaml",
        '-p:LauncherInstallDirectory=Net.Like.Xue.Tokyo',
        '-p:LauncherGameDataDirectory=%USERPROFILE%\AppData\LocalLow\LikeXue\LikeTokyo'
    )

    $publishedLauncher = Join-Path $launcherOutput $launcherName
    if (-not (Test-Path -LiteralPath $publishedLauncher -PathType Leaf)) {
        throw "未找到发布后的启动器：$publishedLauncher"
    }

    if (Test-Path -LiteralPath $gameLauncherPath -PathType Leaf) {
        $launcherBackupPath = Join-Path $stagingRoot 'launcher.previous.exe'
        Copy-Item -LiteralPath $gameLauncherPath -Destination $launcherBackupPath
    }

    Copy-Item -LiteralPath $publishedLauncher -Destination $gameLauncherPath -Force
    $launcherWasReplaced = $true

    Write-Step '生成 SHA-256 清单和精确发布文件列表'
    Invoke-Checked $publishedLauncher @(
        'generate', $resolvedGameRoot,
        '--file-list', $fileListPath
    )

    $manifestText = [IO.File]::ReadAllText($gameManifestPath)
    $manifestText = [Text.RegularExpressions.Regex]::Replace(
        $manifestText,
        '(?m)^name:.*$',
        "name: $GameName")
    $manifestText = [Text.RegularExpressions.Regex]::Replace(
        $manifestText,
        '(?m)^baseUrl:.*$',
        "baseUrl: $($PublicBaseUrl.TrimEnd('/'))")
    $manifestText = [Text.RegularExpressions.Regex]::Replace(
        $manifestText,
        '(?m)^launcher:.*$',
        "launcher: $GameExecutable")
    [IO.File]::WriteAllText($gameManifestPath, $manifestText, $utf8WithoutBom)

    if ($manifestText -notmatch '(?m)^hashAlgorithm:\s*SHA256\s*$') {
        throw '生成后的清单没有使用 SHA256。'
    }

    if ($manifestText -match '(?mi)^\s*- path:\s*(manifest\.yaml|\.gitignore)\s*$') {
        throw '生成后的清单错误地包含 manifest.yaml 或 .gitignore。'
    }

    $relativeFiles = [IO.File]::ReadAllLines($fileListPath) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($relativeFiles.Count -eq 0) {
        throw '发布文件列表为空。'
    }

    Write-Step "按清单创建硬链接发布区（$($relativeFiles.Count) 个文件）"
    $rootPrefix = $resolvedGameRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    [int64]$totalBytes = 0
    $copiedFallbackCount = 0
    foreach ($relativeFile in $relativeFiles) {
        $normalized = $relativeFile.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $sourcePath = [IO.Path]::GetFullPath((Join-Path $resolvedGameRoot $normalized))
        if (-not $sourcePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "清单路径越出游戏目录：$relativeFile"
        }

        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "清单文件不存在：$relativeFile"
        }

        $targetPath = Join-Path $payloadRoot $normalized
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        try {
            New-Item -ItemType HardLink -Path $targetPath -Target $sourcePath | Out-Null
        }
        catch {
            Copy-Item -LiteralPath $sourcePath -Destination $targetPath
            $copiedFallbackCount++
        }

        $totalBytes += (Get-Item -LiteralPath $sourcePath).Length
    }

    Write-Host ("发布内容：{0} 个文件，{1:N2} GiB；硬链接失败后复制：{2}" -f `
            $relativeFiles.Count, ($totalBytes / 1GB), $copiedFallbackCount)

    Write-Step '批量同步游戏文件（manifest.yaml 尚未发布）'
    $syncArguments = @(
        'sync',
        ($payloadRoot.TrimEnd('\') + '\'),
        $OssDestination,
        '--checksum',
        '--force'
    )
    if ($DryRun) {
        $syncArguments += '--dry-run'
    }

    Invoke-Checked $resolvedOssUtil $syncArguments

    Write-Step '最后单独发布 manifest.yaml'
    $manifestArguments = @(
        'cp',
        $gameManifestPath,
        ($OssDestination.TrimEnd('/') + '/' + $manifestName),
        '--force',
        '--content-type', 'application/yaml',
        '--cache-control', 'no-cache, no-store, must-revalidate'
    )
    if ($DryRun) {
        $manifestArguments += '--dry-run'
    }

    Invoke-Checked $resolvedOssUtil $manifestArguments

    if ($DryRun) {
        Write-Host "`nDRY RUN 通过：OSS 未发生更改。" -ForegroundColor Green
    }
    else {
        $committed = $true
        Write-Step '从公网重新下载 manifest.yaml 并核对内容'
        $verificationUrl = $PublicBaseUrl.TrimEnd('/') + '/' + $manifestName + '?publish=' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $webClient = [Net.WebClient]::new()
        try {
            $webClient.DownloadFile($verificationUrl, $remoteManifestCheck)
        }
        finally {
            $webClient.Dispose()
        }

        $localHash = (Get-FileHash -LiteralPath $gameManifestPath -Algorithm SHA256).Hash
        $remoteHash = (Get-FileHash -LiteralPath $remoteManifestCheck -Algorithm SHA256).Hash
        if ($localHash -ne $remoteHash) {
            throw 'OSS 已提交新清单，但公网回读内容不一致。请检查缓存或 CDN 配置。'
        }

        Write-Host "`n发布完成：$PublicBaseUrl/$manifestName" -ForegroundColor Green
    }
}
finally {
    if ($DryRun -or -not $committed) {
        Restore-File $gameManifestPath $manifestBackup
        if ($launcherWasReplaced) {
            if ($null -ne $launcherBackupPath -and (Test-Path -LiteralPath $launcherBackupPath -PathType Leaf)) {
                Copy-Item -LiteralPath $launcherBackupPath -Destination $gameLauncherPath -Force
            }
            else {
                Remove-Item -LiteralPath $gameLauncherPath -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if (-not $KeepStaging -and $null -ne $stagingRoot) {
        $resolvedParent = [IO.Path]::GetFullPath((Split-Path -Parent $resolvedGameRoot))
        $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
        $parentPrefix = $resolvedParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($resolvedStaging.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedStaging).StartsWith('.bitkit-publish-', [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
