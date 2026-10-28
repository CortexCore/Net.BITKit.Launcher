param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'Net.BITKit.Launcher.WinForm\Net.BITKit.Launcher.WinForm.csproj'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("Net.BITKit.Launcher.Smoke." + [Guid]::NewGuid().ToString('N'))
$oldPublish = Join-Path $testRoot 'old'
$newPublish = Join-Path $testRoot 'new'
$installRoot = Join-Path $testRoot 'install'
$serverRoot = Join-Path $testRoot 'server'
$serverJob = $null
$guiProcess = $null

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Get-FreeTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Publish-Launcher([string]$Output, [string]$Version) {
    dotnet publish $projectPath -c $Configuration -r win-x64 --self-contained true `
        -o $Output -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:Version=$Version" "-p:AssemblyVersion=$($Version).0"
    Assert-True ($LASTEXITCODE -eq 0) "Publishing launcher $Version failed."
}

function Invoke-Launcher([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments `
        -WorkingDirectory (Get-Location).Path -PassThru -Wait
    try {
        return $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $oldPublish, $newPublish, $installRoot, $serverRoot | Out-Null

    Publish-Launcher $oldPublish '1.0.0'
    Publish-Launcher $newPublish '2.0.0'

    $executableName = 'Net.BITKit.Launcher.exe'
    $oldExecutable = Join-Path $oldPublish $executableName
    $newExecutable = Join-Path $newPublish $executableName
    $installedExecutable = Join-Path $installRoot $executableName
    $servedExecutable = Join-Path $serverRoot $executableName
    Copy-Item -LiteralPath $oldExecutable -Destination $installedExecutable
    Copy-Item -LiteralPath $newExecutable -Destination $servedExecutable
    [IO.File]::WriteAllText((Join-Path $installRoot 'locked.bin'), 'old', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $serverRoot 'locked.bin'), 'new', [Text.UTF8Encoding]::new($false))

    $oldHash = (Get-FileHash -LiteralPath $oldExecutable -Algorithm SHA256).Hash
    $newHash = (Get-FileHash -LiteralPath $newExecutable -Algorithm SHA256).Hash
    Assert-True ($oldHash -ne $newHash) 'The two test builds are identical.'

    $exitCode = Invoke-Launcher $servedExecutable @('generate', $serverRoot)
    Assert-True ($exitCode -eq 0) 'Generating the remote manifest failed.'
    $remoteManifestPath = Join-Path $serverRoot 'manifest.yaml'
    $remoteYaml = [IO.File]::ReadAllText($remoteManifestPath)
    Assert-True (-not ($remoteYaml -match '(?m)^\s*path:\s*manifest\.yaml\s*$')) `
        'The generated manifest contains itself.'

    $port = Get-FreeTcpPort
    $serverJob = Start-Job -ArgumentList $serverRoot, $port -ScriptBlock {
        param($Root, $Port)
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        try {
            while ($true) {
                if (-not $listener.Pending()) {
                    Start-Sleep -Milliseconds 50
                    continue
                }

                $client = $listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream()
                    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
                    $requestLine = $reader.ReadLine()
                    while (($line = $reader.ReadLine()) -ne '') {
                        if ($null -eq $line) { break }
                    }

                    $requestTarget = ($requestLine -split ' ')[1]
                    $relative = [Uri]::UnescapeDataString(($requestTarget -split '\?')[0]).TrimStart('/')
                    $rootPath = [IO.Path]::GetFullPath($Root)
                    $filePath = [IO.Path]::GetFullPath((Join-Path $rootPath $relative))
                    $rootPrefix = $rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

                    if (-not $filePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($filePath)) {
                        $header = "HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"
                        $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
                        $stream.Write($headerBytes, 0, $headerBytes.Length)
                        continue
                    }

                    $bytes = [IO.File]::ReadAllBytes($filePath)
                    $header = "HTTP/1.1 200 OK`r`nContent-Length: $($bytes.Length)`r`nContent-Type: application/octet-stream`r`nConnection: close`r`n`r`n"
                    $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
                    $stream.Write($headerBytes, 0, $headerBytes.Length)
                    $stream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $client.Dispose()
                }
            }
        }
        finally {
            $listener.Stop()
        }
    }

    Start-Sleep -Milliseconds 300
    $localYaml = $remoteYaml -replace '(?m)^baseUrl:.*$', "baseUrl: http://127.0.0.1:$port"
    [IO.File]::WriteAllText((Join-Path $installRoot 'manifest.yaml'), $localYaml, [Text.UTF8Encoding]::new($false))

    Push-Location $installRoot
    try {
        $guiProcess = Start-Process -FilePath $installedExecutable -WorkingDirectory $installRoot -PassThru
        $windowDeadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            Start-Sleep -Milliseconds 100
            $guiProcess.Refresh()
        } while (-not $guiProcess.HasExited -and $guiProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $windowDeadline)
        Assert-True (-not $guiProcess.HasExited -and $guiProcess.MainWindowHandle -ne 0) `
            'The WPF launcher did not open a window.'

        $exitCode = Invoke-Launcher $installedExecutable @('update', '--dry-run', '--no-launch')
        Assert-True ($exitCode -eq 10) 'A second launcher instance was not rejected.'

        [void]$guiProcess.CloseMainWindow()
        if (-not $guiProcess.WaitForExit(5000)) {
            Stop-Process -Id $guiProcess.Id -Force
            $guiProcess.WaitForExit()
        }
        $guiProcess.Dispose()
        $guiProcess = $null

        $pathPattern = [Regex]::new([Regex]::Escape($executableName))
        $maliciousYaml = $pathPattern.Replace($remoteYaml, '../escape.exe', 1)
        [IO.File]::WriteAllText($remoteManifestPath, $maliciousYaml, [Text.UTF8Encoding]::new($false))
        $exitCode = Invoke-Launcher $installedExecutable @('--simulate', '--no-launch')
        Assert-True ($exitCode -ne 0) 'A path traversal manifest was accepted.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $testRoot 'escape.exe'))) `
            'A path traversal manifest wrote outside the install directory.'
        [IO.File]::WriteAllText($remoteManifestPath, $remoteYaml, [Text.UTF8Encoding]::new($false))

        $beforeSimulationHash = (Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash
        $exitCode = Invoke-Launcher $installedExecutable @('update', '--dry-run', '--no-launch')
        Assert-True ($exitCode -eq 0) 'The dry-run failed.'
        $afterSimulationHash = (Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash
        Assert-True ($beforeSimulationHash -eq $afterSimulationHash) 'The dry-run modified the launcher.'

        $lockedPath = Join-Path $installRoot 'locked.bin'
        $lockedStream = [IO.File]::Open(
            $lockedPath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        try {
            $exitCode = Invoke-Launcher $installedExecutable @('update', '--no-launch')
            Assert-True ($exitCode -eq 11) 'A process using a changed file was not reported.'
        }
        finally {
            $lockedStream.Dispose()
        }

        $exitCode = Invoke-Launcher $installedExecutable @('--no-launch')
        Assert-True ($exitCode -eq 0) 'The launcher failed to schedule its self-update.'
    }
    finally {
        Pop-Location
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $installedHash = if (Test-Path -LiteralPath $installedExecutable) {
            (Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash
        }
        else {
            ''
        }
    } while ($installedHash -ne $newHash -and [DateTime]::UtcNow -lt $deadline)

    Assert-True ($installedHash -eq $newHash) 'Timed out waiting for the launcher replacement.'
    Assert-True ([IO.File]::ReadAllText((Join-Path $installRoot 'locked.bin')) -eq 'new') `
        'The changed non-launcher file was not replaced.'

    $workRoot = Join-Path $installRoot '.bitkit-update'
    $cleanupDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Test-Path -LiteralPath $workRoot) -and [DateTime]::UtcNow -lt $cleanupDeadline) {
        Start-Sleep -Milliseconds 200
    }
    Assert-True (-not (Test-Path -LiteralPath $workRoot)) 'The self-update working directory was not removed.'

    Write-Host 'PASS: GUI, single-instance lock, file-use detection, dry-run, and self-update.' -ForegroundColor Green
}
finally {
    if ($null -ne $guiProcess) {
        if (-not $guiProcess.HasExited) {
            Stop-Process -Id $guiProcess.Id -Force -ErrorAction SilentlyContinue
        }
        $guiProcess.Dispose()
    }

    if ($null -ne $serverJob) {
        Stop-Job -Job $serverJob -ErrorAction SilentlyContinue
        Remove-Job -Job $serverJob -Force -ErrorAction SilentlyContinue
    }

    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTest)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
