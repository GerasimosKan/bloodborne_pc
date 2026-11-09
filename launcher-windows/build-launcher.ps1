# Builds a testable Windows package of this repository (0.7 layout): the engine (build.sh in MSYS2 CLANG64)
# on top of deadinside28's official Windows package, with this fork's launcher (see packaging\windows\repack.ps1).
#
#   .\launcher-windows\build-launcher.ps1                          build engine + zip dist\UPDATE.<upstream>-local-Bloodborne-PC.zip
#   .\launcher-windows\build-launcher.ps1 -InstallDir D:\Game      also copy the package over that folder (no zip then)
#   -SkipRuntime   reuse out\bb-probe.exe instead of running build.sh
#   -NoZip         leave the package as the folder dist\Bloodborne-PC-local
#   -v 0.8         version of the package, also -Version v0.8 (default <upstream>-local)
#   -Source x.zip  a Windows package zip instead of deadinside28's latest release
# An install keeps bbport.ini, user\, mods\ and saves: the package does not contain them.
param([string]$InstallDir, [Alias('v')][string]$Version, [string]$Source, [switch]$NoZip, [switch]$SkipRuntime)
$ErrorActionPreference = 'Stop'
$Version = $Version.TrimStart('v', 'V')  # "-v v0.8" and "-v 0.8" both give 0.8 (the updater strips the tag's v too)
$repo = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repo 'dist'
$msysBash = 'C:\msys64\usr\bin\bash.exe'

function Find-Sdk {
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    foreach ($dotnet in @($(if ($onPath) { $onPath.Source }), "$env:ProgramFiles\dotnet\dotnet.exe")) {
        if ($dotnet -and (Test-Path $dotnet) -and (& $dotnet --list-sdks | Where-Object { [int]($_ -split '\.')[0] -ge 8 })) { return $dotnet }
    }
}
function Invoke-Msys([string]$command) {
    $env:MSYSTEM = 'CLANG64'
    $repoUnix = '/' + $repo.Substring(0, 1).ToLower() + ($repo.Substring(2) -replace '\\', '/')
    & $msysBash -lc "cd '$repoUnix' && $command"
    if ($LASTEXITCODE -ne 0) { throw "MSYS2 command failed: $command" }
}

$dotnet = Find-Sdk
if (-not $dotnet) { throw 'Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0' }
$env:PATH = (Split-Path $dotnet) + ';' + $env:PATH  # repack.ps1 calls plain "dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($InstallDir) {
    $InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')  # a new test folder need not exist yet
    if (Get-Process Bloodborne, BloodborneLauncher, bbport-run, bb-probe -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir\*" }) {
        throw 'Close the launcher and the game in the install folder first.'
    }
}

if (-not $SkipRuntime) {
    if (-not (Test-Path $msysBash)) { throw 'MSYS2 is needed for the engine (C:\msys64, see README "Windows").' }
    Write-Host '== Building the engine (build.sh; it runs no tests without --test)'
    Invoke-Msys './build.sh'
}
if (-not (Test-Path "$repo\out\bb-probe.exe")) { throw 'out\bb-probe.exe is missing: run without -SkipRuntime.' }

$repack = @{ Engine = "$repo\out" }
if ($Version) { $repack.Version = $Version }
if ($Source) { $repack.Source = $Source }
if ($NoZip -or $InstallDir) { $repack.NoZip = $true }
& "$repo\packaging\windows\repack.ps1" @repack

if ($InstallDir) {
    $pkg = Join-Path $dist 'Bloodborne-PC-local'
    if (-not (Test-Path "$pkg\bbport-run.exe")) { throw "$pkg is not a package folder." }
    Write-Host "== Installing into $InstallDir"
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Copy-Item "$pkg\*" $InstallDir -Recurse -Force
    Write-Host "Installed: $InstallDir"
}
