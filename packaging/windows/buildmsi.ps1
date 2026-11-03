#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Windows installer of Enigma Git Client: artifacts\Enigma.GitClient.X.Y.Z.msi.

.DESCRIPTION
    Runs, from wherever it is started, on the repository it sits in:

        1. git clean -dxf    deletes every file git does not track, ignored ones included
                             (bin\, obj\, artifacts\, .vs\, and any new file not yet added)
        2. publish.ps1       dotnet publish .\src\Enigma.GitClient.Desktop\ -c Release -r win-x64
                             -p:PublishReadyToRun=true
        3. Enigma.Msi        packages the publish folder into the MSI, uncompressed for speed

    X.Y.Z is the <Version> in Directory.Build.props. The MSI installs "Enigma Git Client" for all
    users in %ProgramFiles%\Enigma Git Client, with a shortcut on the desktop and one in the program
    menu. It upgrades a version installed from an earlier MSI: every MSI carries the same upgrade code.
    The Enigma.Msi profile it was built from is kept beside it, as Enigma.GitClient.X.Y.Z.msipkg.json.

    git, the .NET 10 SDK and the WiX CLI (dotnet tool install --global wix) must be on the PATH. They
    are checked before anything is cleaned. Enigma.Msi's worker is in Enigma.Msi.Worker\, next to this
    script.

.EXAMPLE
    .\packaging\windows\buildmsi.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File packaging\windows\buildmsi.ps1

    Windows PowerShell's default policy refuses to run local scripts; this runs this one without
    changing the policy. buildmsi.bat, beside this script, does the same from a cmd prompt.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The product line's identity in Windows Installer, the same in every MSI of Enigma Git Client: it is
# what makes a new version replace the installed one instead of installing beside it. Never change it.
$UpgradeCode = '871622d1-a9c6-4c72-b6a3-e8d8104ad5c3'
$ProductName = 'Enigma Git Client'
# Josue Clement with its two e-acutes: this file is ASCII because Windows PowerShell 5.1 reads a script
# without a byte-order mark in the ANSI code page.
$Manufacturer = "Josu$([char]0x00E9) Cl$([char]0x00E9)ment"
$Assembly = 'Enigma.GitClient.Desktop'

function Stop-Build([string] $Reason, [int] $ExitCode = 1) {
    [Console]::Error.WriteLine("buildmsi: $Reason")
    exit $ExitCode
}

function Write-Step([string] $Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# Resolved from this script rather than the current directory, so it runs from anywhere.
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$worker = Join-Path $PSScriptRoot 'Enigma.Msi.Worker\Enigma.Msi.Worker.exe'
$publish = Join-Path $root "src\$Assembly\bin\Release\net10.0\win-x64\publish"
$artifacts = Join-Path $root 'artifacts'

# Everything that can be missing is checked before git clean deletes anything.
$missing = @(
    @{ Command = 'git'; Hint = 'install Git for Windows' },
    @{ Command = 'dotnet'; Hint = 'install the .NET 10 SDK' },
    @{ Command = 'wix'; Hint = 'dotnet tool install --global wix' }
) | Where-Object { -not (Get-Command $_.Command -ErrorAction SilentlyContinue) }

foreach ($tool in $missing) {
    Stop-Build "$($tool.Command) is not on the PATH: $($tool.Hint)."
}

if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) {
    Stop-Build "Enigma.Msi's worker is missing: $worker"
}

# XmlDocument.Load honours the file's UTF-8 encoding, which Get-Content would read as ANSI.
$props = New-Object System.Xml.XmlDocument
$props.Load((Join-Path $root 'Directory.Build.props'))
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')

if ($null -eq $versionNode -or $versionNode.InnerText.Trim() -notmatch '^\d+\.\d+\.\d+$') {
    Stop-Build 'Directory.Build.props has no <Version> of the form X.Y.Z.'
}

$version = $versionNode.InnerText.Trim()
$name = "Enigma.GitClient.$version"

Write-Step "Cleaning the repository: git clean -dxf"
& git -C $root clean -dxf
if ($LASTEXITCODE -ne 0) {
    Stop-Build "git clean failed (exit code $LASTEXITCODE)." $LASTEXITCODE
}

Write-Step "Publishing $version for win-x64: publish.ps1"
& (Join-Path $PSScriptRoot 'publish.ps1')
if ($LASTEXITCODE -ne 0) {
    Stop-Build "the publish failed (exit code $LASTEXITCODE)." $LASTEXITCODE
}

$executable = Join-Path $publish "$Assembly.exe"
$icon = Join-Path $publish 'app.ico'

foreach ($file in $executable, $icon) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        Stop-Build "the publish folder has no $(Split-Path $file -Leaf): $publish"
    }
}

Write-Step "Building $name.msi: Enigma.Msi"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

# Enigma.Msi's profile format (schema 1). Its paths are absolute, because the worker resolves relative
# ones against the current directory. The product code is new for every build.
$shortcutTarget = "[INSTALLDIR]\$Assembly.exe"
$package = [ordered] @{
    schemaVersion = 1
    appName       = $ProductName
    version       = $version
    productId     = [guid]::NewGuid().ToString()
    upgradeCode   = $UpgradeCode
    manufacturer  = $Manufacturer
    scope         = 'PerMachine'
    install       = [ordered] @{
        installPath = "%ProgramFiles%\$ProductName"
        releasePath = $publish
    }
    output        = [ordered] @{
        outputPath  = $artifacts
        msiFilename = $name
    }
    compression   = 'None'
    controlPanel  = [ordered] @{
        productIcon = $icon
    }
    shortcuts     = @(
        [ordered] @{
            shortcutPath = '%Desktop%'
            shortcutName = $ProductName
            targetPath   = $shortcutTarget
            iconPath     = $icon
        },
        [ordered] @{
            shortcutPath = '%ProgramMenu%'
            shortcutName = $ProductName
            targetPath   = $shortcutTarget
            iconPath     = $icon
        }
    )
}

$profilePath = Join-Path $artifacts "$name.msipkg.json"
# -Depth: the shortcuts sit three levels down, below ConvertTo-Json's default of two.
$json = $package | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($profilePath, $json, (New-Object System.Text.UTF8Encoding $false))

& $worker build $profilePath
if ($LASTEXITCODE -ne 0) {
    Stop-Build "Enigma.Msi could not build the MSI (exit code $LASTEXITCODE): see its reasons above." $LASTEXITCODE
}

$msi = Join-Path $artifacts "$name.msi"
if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) {
    Stop-Build "Enigma.Msi reported success, but $msi does not exist."
}

Write-Host ''
Write-Host ("Built {0} in {1:mm\:ss}." -f $msi, $stopwatch.Elapsed) -ForegroundColor Green
