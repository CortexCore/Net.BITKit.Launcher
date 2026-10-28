[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublicBaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [string]$GameRoot = 'D:\Iris\Documents\Workspace\Com.Project.B.Unity',
    [string]$IconPng = 'D:\Iris\Documents\GitHub\Com.Project.B.Unity\Assets\Artists\Arts\Icons\Logo.jpg',
    [string]$BannerImage = 'D:\Iris\Documents\GitHub\Com.Project.B.Unity\Assets\Artists\Arts\Screenshots\Sarovsk_Custom..._09.02.2024_04-02-00.png',
    [string]$PublishIgnore,
    [string]$ProductId = 'com.project.b',
    [string]$DisplayName = 'BITFALL',
    [string]$InstallDirectory = 'BITFALL',
    [string]$GameExecutable = 'Project B.exe',
    [string]$GameDataDirectory = '%USERPROFILE%\AppData\LocalLow\Atlas Dev Labs\Project B',
    [switch]$DryRun,
    [switch]$Force,
    [switch]$CopyAll,
    [switch]$SkipVerification,
    [switch]$KeepStaging
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$consoleEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $consoleEncoding
$OutputEncoding = $consoleEncoding

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$launcherProject = Join-Path $repositoryRoot 'Net.BITKit.Launcher.WinForm\Net.BITKit.Launcher.WinForm.csproj'
$launcherName = 'Net.BITKit.Launcher.exe'
$manifestName = 'manifest.yaml'
if ([string]::IsNullOrWhiteSpace($PublishIgnore)) {
    $PublishIgnore = Join-Path $GameRoot '.gitignore'
}
$stagingRoot = $null

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked([string]$Executable, [string[]]$ArgumentList) {
    & $Executable @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败，退出码 $LASTEXITCODE：$Executable $($ArgumentList -join ' ')"
    }
}

function Get-CompatibleRelativePath([string]$Root, [string]$Path) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootUri = [Uri]::new($rootPath)
    $pathUri = [Uri]::new($fullPath)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
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

function New-SnapshotPayload(
    [string]$SourceRoot,
    [string]$PayloadRoot,
    [ref]$FileCount,
    [ref]$TotalBytes) {
    $files = @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Force)
    foreach ($file in $files) {
        $relativePath = Get-CompatibleRelativePath $SourceRoot $file.FullName
        if ($relativePath -in @('.gitignore', $manifestName, $launcherName)) {
            continue
        }

        $targetPath = Join-Path $PayloadRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $targetPath

        $FileCount.Value++
        $TotalBytes.Value += $file.Length
    }
}

function Copy-FileAtomically([string]$SourcePath, [string]$DestinationPath) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationPath) -Force | Out-Null
    $temporaryPath = $DestinationPath + '.bitkit-upload-' + [Guid]::NewGuid().ToString('N')
    try {
        Copy-Item -LiteralPath $SourcePath -Destination $temporaryPath
        Move-Item -LiteralPath $temporaryPath -Destination $DestinationPath -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

$baseUri = $null
if (-not ([Uri]::TryCreate($PublicBaseUrl, [UriKind]::Absolute, [ref]$baseUri)) -or
    $baseUri.Scheme -notin @('http', 'https') -or
    -not [string]::IsNullOrEmpty($baseUri.Query) -or
    -not [string]::IsNullOrEmpty($baseUri.Fragment)) {
    throw 'PublicBaseUrl 必须是不含查询参数和片段的 HTTP/HTTPS 目录地址。'
}

$publicBaseUrl = $PublicBaseUrl.TrimEnd('/')
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedDestination = [IO.Path]::GetFullPath($DestinationDirectory)
$resolvedBannerImage = $null
if (-not (Test-Path -LiteralPath $resolvedGameRoot -PathType Container)) {
    throw "游戏目录不存在：$resolvedGameRoot"
}

if (-not (Test-Path -LiteralPath $launcherProject -PathType Leaf)) {
    throw "启动器项目不存在：$launcherProject"
}

if (-not (Test-Path -LiteralPath $IconPng -PathType Leaf)) {
    throw "启动器图标不存在：$IconPng"
}

if (-not [string]::IsNullOrWhiteSpace($BannerImage)) {
    $resolvedBannerImage = [IO.Path]::GetFullPath($BannerImage)
    if (-not (Test-Path -LiteralPath $resolvedBannerImage -PathType Leaf)) {
        throw "启动器横幅图片不存在：$resolvedBannerImage"
    }
}

if (-not (Test-Path -LiteralPath $PublishIgnore -PathType Leaf)) {
    throw "发布排除规则不存在：$PublishIgnore"
}

$gameExecutablePath = Join-Path $resolvedGameRoot $GameExecutable
if (-not (Test-Path -LiteralPath $gameExecutablePath -PathType Leaf)) {
    throw "游戏主程序不存在：$gameExecutablePath"
}

$gameRootPrefix = $resolvedGameRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($resolvedDestination.Equals($resolvedGameRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedDestination.StartsWith($gameRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw '服务器发布目录不能位于游戏目录内部。'
}

Write-Host 'BITFALL 自托管发布器' -ForegroundColor Yellow
Write-Host "游戏目录：$resolvedGameRoot"
Write-Host "服务器目录：$resolvedDestination"
Write-Host "公网地址：$publicBaseUrl"
Write-Host "发布模式：$(if ($DryRun) { '模拟发布，不写入服务器' } else { '正式发布' })"

$stagingParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stagingRoot = Join-Path $stagingParent ('.bitkit-publish-project-b-' + [Guid]::NewGuid().ToString('N'))
$payloadRoot = Join-Path $stagingRoot 'payload'
$launcherOutput = Join-Path $stagingRoot 'launcher'
$fileListPath = Join-Path $stagingRoot 'files.txt'
$changedFileListPath = Join-Path $stagingRoot 'changed-files.txt'
$iconPath = Join-Path $stagingRoot 'launcher.ico'
$remoteManifestCheck = Join-Path $stagingRoot 'manifest.remote.yaml'
$deployedManifestPath = Join-Path $resolvedDestination $manifestName
New-Item -ItemType Directory -Path $payloadRoot, $launcherOutput | Out-Null

try {
    Write-Step '创建不可变的 Player 发布快照'
    $fileCount = 0
    [int64]$totalBytes = 0
    New-SnapshotPayload $resolvedGameRoot $payloadRoot ([ref]$fileCount) ([ref]$totalBytes)
    Copy-Item -LiteralPath $PublishIgnore -Destination (Join-Path $payloadRoot '.gitignore')

    Write-Host ("输入快照：{0} 个文件，{1:N2} GiB" -f $fileCount, ($totalBytes / 1GB))

    Write-Step '构建带 BITFALL 品牌的自包含单文件启动器'
    Convert-PngToMultiSizeIco $IconPng $iconPath
    $publishArguments = @(
        'publish', $launcherProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-o', $launcherOutput,
        "-p:LauncherBuildRoot=$(Join-Path $stagingRoot 'build')",
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        "-p:ApplicationIcon=$iconPath",
        "-p:LauncherProductId=$ProductId",
        "-p:LauncherDisplayName=$DisplayName",
        "-p:LauncherManifestUrl=$publicBaseUrl/$manifestName",
        "-p:LauncherInstallDirectory=$InstallDirectory",
        "-p:LauncherGameDataDirectory=$GameDataDirectory"
    )
    if ($null -ne $resolvedBannerImage) {
        $publishArguments += "-p:LauncherArtwork=$resolvedBannerImage"
    }
    Invoke-Checked 'dotnet' $publishArguments

    $publishedLauncher = Join-Path $launcherOutput $launcherName
    if (-not (Test-Path -LiteralPath $publishedLauncher -PathType Leaf)) {
        throw "未找到编译后的单文件启动器：$publishedLauncher"
    }
    Copy-Item -LiteralPath $publishedLauncher -Destination (Join-Path $payloadRoot $launcherName)

    Write-Step '生成 SHA-256 文件清单和发布预览'
    $generateArguments = @(
        'generate', $payloadRoot,
        '--file-list', $fileListPath,
        '--changed-file-list', $changedFileListPath,
        '--preview',
        '--name', $DisplayName,
        '--base-url', $publicBaseUrl,
        '--launcher', $GameExecutable
    )
    if (Test-Path -LiteralPath $deployedManifestPath -PathType Leaf) {
        $generateArguments += @(
            '--previous-manifest', $deployedManifestPath,
            '--previous-root', $resolvedDestination
        )
    }
    if ($CopyAll) {
        $generateArguments += '--force-all'
    }
    Invoke-Checked $publishedLauncher $generateArguments

    $manifestPath = Join-Path $payloadRoot $manifestName
    $relativeFiles = @([IO.File]::ReadAllLines($fileListPath) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $uploadFiles = @([IO.File]::ReadAllLines($changedFileListPath) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($relativeFiles.Count -eq 0) {
        throw '发布文件列表为空。'
    }
    if ($relativeFiles -notcontains $launcherName -or $relativeFiles -notcontains $GameExecutable) {
        throw '发布文件列表缺少启动器或游戏主程序。'
    }
    foreach ($uploadFile in $uploadFiles) {
        if ($relativeFiles -notcontains $uploadFile) {
            throw "待上传文件不在发布文件列表中：$uploadFile"
        }
    }

    $skippedCount = $relativeFiles.Count - $uploadFiles.Count
    if ($DryRun) {
        Write-Host "`n模拟发布通过，服务器目录未发生更改。" -ForegroundColor Green
        return
    }

    if (-not $Force) {
        $confirmation = Read-Host '确认以上发布计划后，输入 PUBLISH 开始上传'
        if ($confirmation -cne 'PUBLISH') {
            throw '发布已取消。'
        }
    }

    Write-Step '复制发生变化的文件，暂时保持旧清单在线'
    New-Item -ItemType Directory -Path $resolvedDestination -Force | Out-Null
    $copiedCount = 0
    foreach ($relativeFile in $uploadFiles) {
        $sourcePath = Join-Path $payloadRoot $relativeFile
        $destinationPath = Join-Path $resolvedDestination $relativeFile
        Copy-FileAtomically $sourcePath $destinationPath
        $copiedCount++
    }
    Write-Host "资源复制完成：已复制 $copiedCount 个文件，跳过 $skippedCount 个未变化文件。"

    Write-Step '发布新清单前校验服务器上的全部文件'
    foreach ($relativeFile in $relativeFiles) {
        $sourcePath = Join-Path $payloadRoot $relativeFile
        $destinationPath = Join-Path $resolvedDestination $relativeFile
        if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
            throw "服务器缺少文件：$relativeFile"
        }
        if ((Get-Item -LiteralPath $destinationPath).Length -ne (Get-Item -LiteralPath $sourcePath).Length) {
            throw "服务器文件大小不正确：$relativeFile"
        }
    }

    Write-Step '最后替换 manifest.yaml'
    Copy-FileAtomically $manifestPath (Join-Path $resolvedDestination $manifestName)

    if (-not $SkipVerification) {
        Write-Step '通过公网地址回读并校验 manifest.yaml'
        $verificationUrl = $publicBaseUrl + '/' + $manifestName + '?publish=' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $webClient = [Net.WebClient]::new()
        try {
            $webClient.DownloadFile($verificationUrl, $remoteManifestCheck)
        }
        finally {
            $webClient.Dispose()
        }

        $localHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        $remoteHash = (Get-FileHash -LiteralPath $remoteManifestCheck -Algorithm SHA256).Hash
        if ($localHash -ne $remoteHash) {
            throw '公网清单与服务器清单不一致，请检查 URL 映射和缓存规则。'
        }
    }

    Copy-FileAtomically $publishedLauncher (Join-Path $resolvedGameRoot $launcherName)
    Copy-FileAtomically $manifestPath (Join-Path $resolvedGameRoot $manifestName)

    Write-Host "`n发布完成：$publicBaseUrl/$manifestName" -ForegroundColor Green
    Write-Host "安装器地址：$publicBaseUrl/$launcherName" -ForegroundColor Green
}
finally {
    if ($KeepStaging -and $null -ne $stagingRoot) {
        Write-Host "已保留临时发布目录：$stagingRoot" -ForegroundColor Yellow
    }
    elseif ($null -ne $stagingRoot) {
        $resolvedParent = $stagingParent
        $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
        $parentPrefix = $resolvedParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($resolvedStaging.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedStaging).StartsWith('.bitkit-publish-project-b-', [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
