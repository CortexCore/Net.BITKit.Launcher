[CmdletBinding()]
param(
    [ValidateSet('install', 'download', 'ready')]
    [string]$State = 'install'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$edgeCandidates = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
)
$edge = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($null -eq $edge) {
    throw 'Microsoft Edge was not found.'
}

$page = Join-Path $PSScriptRoot 'index.html'
$pageUri = [Uri]::new([IO.Path]::GetFullPath($page)).AbsoluteUri + '?state=' + $State
$profile = Join-Path $env:TEMP ('bitkit-project-b-prototype-' + [Guid]::NewGuid().ToString('N'))
$startedAt = Get-Date
$process = Start-Process -FilePath $edge -PassThru -ArgumentList @(
    "--app=$pageUri",
    "--user-data-dir=$profile",
    '--no-first-run',
    '--disable-features=msEdgeSidebarV2'
)

$window = $null
for ($attempt = 0; $attempt -lt 100 -and $null -eq $window; $attempt++) {
    Start-Sleep -Milliseconds 100
    $window = Get-Process msedge -ErrorAction SilentlyContinue |
        Where-Object {
            $_.MainWindowHandle -ne [IntPtr]::Zero -and
            $_.MainWindowTitle -like '*Project B Launcher Concept*' -and
            $_.StartTime -ge $startedAt.AddSeconds(-2)
        } |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
}

if ($null -eq $window) {
    if (-not $process.HasExited) {
        $process.Kill()
    }
    throw 'The prototype window did not open in time.'
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class BorderlessPreviewNative
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        IntPtr hWnd, int attribute, ref int value, int valueSize);
}
'@

$handle = $window.MainWindowHandle
$style = [BorderlessPreviewNative]::GetWindowLongPtr($handle, -16).ToInt64()
$style = ($style -band (-bnot [int64]0x00CF0000)) -bor [int64]0x80000000
[void][BorderlessPreviewNative]::SetWindowLongPtr($handle, -16, [IntPtr]::new($style))

$extendedStyle = [BorderlessPreviewNative]::GetWindowLongPtr($handle, -20).ToInt64()
$extendedStyle = $extendedStyle -band (-bnot [int64]0x00020301)
[void][BorderlessPreviewNative]::SetWindowLongPtr($handle, -20, [IntPtr]::new($extendedStyle))

$screen = [Windows.Forms.Screen]::FromHandle($handle).WorkingArea
$width = [Math]::Min(1280, $screen.Width - 48)
$height = [Math]::Round($width * 9 / 16)
if ($height -gt $screen.Height - 48) {
    $height = $screen.Height - 48
    $width = [Math]::Round($height * 16 / 9)
}
$x = $screen.X + [Math]::Floor(($screen.Width - $width) / 2)
$y = $screen.Y + [Math]::Floor(($screen.Height - $height) / 2)

[void][BorderlessPreviewNative]::SetWindowPos($handle, [IntPtr]::Zero, $x, $y, $width, $height, 0x0064)
$cornerPreference = 2
[void][BorderlessPreviewNative]::DwmSetWindowAttribute($handle, 33, [ref]$cornerPreference, 4)
$borderColor = -2
[void][BorderlessPreviewNative]::DwmSetWindowAttribute($handle, 34, [ref]$borderColor, 4)
[void][BorderlessPreviewNative]::SetForegroundWindow($handle)
