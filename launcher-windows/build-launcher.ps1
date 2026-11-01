# Builds the Windows package from this repository: the runtime (build.sh in MSYS2 CLANG64) and
# BloodborneLauncher.exe, then a portable zip named by version (dist\UPDATE.<version>-Bloodborne-PC.zip).
# Optionally upgrades an install in place too.
# Only what a player needs is copied: no setup.bat, __pycache__ or exe backups.
#
#   .\launcher-windows\build-launcher.ps1                                    build + zip
#   .\launcher-windows\build-launcher.ps1 -InstallDir D:\Bloodborne          also upgrade that install
#   -NoZip: no zip   -SkipRuntime: reuse the runtime in out\   -SkipTests: skip the launcher tests
#   -Version 0.8: the zip's version (default: one more than the last tag or package, see Packaging)
#
# Everything comes from the repository build. An install provides what the repository does not
# have: tools\python, fsr4_shaders and out\nvngx_dlss.dll. That is -AssetsDir, else -InstallDir,
# else the folder used last time (dist\assets-dir.txt).
# Without arguments (e.g. from build-launcher.bat) it asks for the install folder.
param([string]$InstallDir, [string]$AssetsDir, [string]$Version, [switch]$NoZip, [switch]$SkipTests, [switch]$SkipRuntime)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $PSScriptRoot 'BloodborneLauncher\BloodborneLauncher.csproj'
$dist = Join-Path $repo 'dist'
$msysBash = 'C:\msys64\usr\bin\bash.exe'
$runtimeExes = 'bb-probe.exe', 'bb-gpu-capabilities.exe', 'bb-engine-assets.exe'

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
# Copies one directory's contents into another, merging with what is there.
function Copy-Tree([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force $to | Out-Null
    Copy-Item (Join-Path $from '*') $to -Recurse -Force
}
# Runtime, scripts and patches from the repository build into $dest (an install or a package).
function Copy-Runtime([string]$dest) {
    New-Item -ItemType Directory -Force "$dest\out" | Out-Null
    foreach ($exe in $runtimeExes) { Copy-Item "$repo\out\$exe" "$dest\out" -Force }
    # Release: build.sh keeps debug info (RelWithDebInfo) for development; players get it stripped
    # (bb-probe 164 MB -> 17 MB, same optimized code).
    & 'C:\msys64\clang64\bin\llvm-strip.exe' --strip-debug ($runtimeExes | ForEach-Object { "$dest\out\$_" })
    if ($LASTEXITCODE -ne 0) { throw 'llvm-strip failed.' }
    foreach ($dll in $runtimeDlls) { Copy-Item $dll "$dest\out" -Force }
    Copy-Item "$PSScriptRoot\BloodborneLauncher\Assets\bloodborne.ico" "$dest\out" -Force
    # The launcher starts scripts\run_windows.py itself, so run.bat, build.sh and setup.bat are for
    # source checkouts only.
    # mods: the bundled file mods (Visual Upgrade Mod), enabled by default by the mod loader.
    foreach ($d in 'scripts', 'patches', 'licenses', 'mods') { Copy-Tree "$repo\$d" "$dest\$d" }
    # The GPL needs its text shipped with the binaries; kept in licenses\ off the top folder.
    Copy-Item "$repo\LICENSE" "$dest\licenses\LICENSE.txt" -Force
    Get-ChildItem "$dest\scripts" -Recurse -Directory -Filter __pycache__ | Remove-Item -Recurse -Force
    # The shipped MSYS2 DLLs' licenses, as in CI.
    Copy-Tree 'C:\msys64\clang64\share\licenses' "$dest\licenses\msys2"
}

$dotnet = Find-Sdk
if (-not $dotnet) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0' }
    if ((Read-Host 'The .NET 8 SDK is needed. Install it now with winget? (y/n)') -ne 'y') { exit 1 }
    winget install --id Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
    $dotnet = Find-Sdk
    if (-not $dotnet) { throw '.NET 8 SDK still not found; open a new terminal and run this again.' }
}
# Verify-Launcher.ps1 calls plain "dotnet".
$env:PATH = (Split-Path $dotnet) + ';' + $env:PATH
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (-not (Test-Path $msysBash)) { throw 'MSYS2 is needed for the runtime (C:\msys64, see README "Windows").' }

if (-not $PSBoundParameters.ContainsKey('InstallDir')) {
    $InstallDir = Read-Host 'Install folder to upgrade, e.g. D:\Bloodborne (empty = only build the zip)'
}
if ($InstallDir -and -not (Test-Path (Join-Path $InstallDir 'BloodborneLauncher.exe'))) { throw "$InstallDir is not a Bloodborne PC install (no BloodborneLauncher.exe)." }
$assetsFile = Join-Path $dist 'assets-dir.txt'
if (-not $NoZip) {
    if (-not $AssetsDir) { $AssetsDir = if ($InstallDir) { $InstallDir } elseif (Test-Path $assetsFile) { (Get-Content $assetsFile -Raw).Trim() } }
    if (-not $AssetsDir -or -not (Test-Path "$AssetsDir\tools\python\python.exe")) {
        throw "The zip takes tools\python and fsr4_shaders from an install: pass -InstallDir or -AssetsDir once (it is remembered)."
    }
    New-Item -ItemType Directory -Force $dist | Out-Null
    Set-Content $assetsFile (Resolve-Path $AssetsDir).Path
}

if (-not $SkipRuntime) {
    Write-Host '== Building the runtime (build.sh)'
    Invoke-Msys './build.sh'
}
foreach ($exe in $runtimeExes) { if (-not (Test-Path "$repo\out\$exe")) { throw "out\$exe is missing: run without -SkipRuntime." } }
# The MSYS2 DLLs the runtime programs load, as in CI (windows-offline-build.yml).
# Filtered here, not with a bash pipeline: its xargs hangs in VS Code's PowerShell Extension terminal.
$runtimeDlls = Invoke-Msys ('for e in ' + (($runtimeExes | ForEach-Object { "out/$_" }) -join ' ') + '; do ldd "$e"; done') |
    # Match by name, not resolved path: ldd resolves a DLL this PC also has in System32 (e.g. xvidcore.dll
    # from an Xvid codec install) there, and the package then lacks it. vulkan-1.dll comes from the GPU driver.
    Select-String '^\s*(\S+\.dll) =>' | ForEach-Object { "C:\msys64\clang64\bin\$($_.Matches[0].Groups[1].Value)" } |
    Where-Object { (Test-Path $_) -and (Split-Path $_ -Leaf) -ne 'vulkan-1.dll' } | Sort-Object -Unique

if (-not $SkipTests) {
    Write-Host '== Testing the launcher'
    & (Join-Path $PSScriptRoot 'BloodborneLauncher\Verify-Launcher.ps1')
}

Write-Host '== Building BloodborneLauncher.exe'
# nuget.org is passed explicitly so the build works even without a configured package source.
& $dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true --source https://api.nuget.org/v3/index.json -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$exe = Join-Path $dist 'BloodborneLauncher.exe'
Write-Host "Built $exe"

if ($InstallDir) {
    $InstallDir = (Resolve-Path $InstallDir).Path
    if (Get-Process BloodborneLauncher, bb-probe -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir\*" }) {
        throw 'Close the launcher and the game in the install folder first.'
    }
    Write-Host "== Upgrading $InstallDir"
    # Settings, saves and the game are not touched. Leftovers of older upgrades are removed: exe
    # backups (the programs are rebuilt from the repository) and setup.bat.
    Get-ChildItem $InstallDir, "$InstallDir\out" -Filter *.exe.bak | Remove-Item -Force
    foreach ($f in 'setup.bat', 'run.bat', 'build.sh', 'README.md', 'README-FIRST.txt', 'LICENSE') { Remove-Item "$InstallDir\$f" -Force -ErrorAction SilentlyContinue }
    Copy-Item $exe $InstallDir -Force
    Copy-Runtime $InstallDir
    # The upgraded launcher reads mods from this repository's mods folder (MainWindow FindModsDir).
    Set-Content "$InstallDir\out\repo_dir.txt" $repo
    Write-Host 'Upgraded'
}

if (-not $NoZip) {
    Write-Host '== Packaging'
    # -Version, else the last packaged version (dist\version.txt, 0.7 before the first) with its last
    # number raised by one: 0.7 -> 0.8 -> 0.9 -> 0.10. Not git tags: 0.51 there sorts above 0.7.
    $versionFile = Join-Path $dist 'version.txt'
    if (-not $Version) {
        $last = (Get-Content $versionFile -ErrorAction SilentlyContinue | Select-Object -First 1)
        if ($last -notmatch '^\d+(\.\d+)*$') { $last = '0.7' }
        $parts = $last.Split('.'); $parts[-1] = [int]$parts[-1] + 1; $Version = $parts -join '.'
    }
    $stage = Join-Path ([IO.Path]::GetTempPath()) "bbpc-package-$([Guid]::NewGuid().ToString('N'))"
    $pkg = Join-Path $stage 'Bloodborne-PC'
    try {
        Copy-Runtime $pkg  # creates $pkg
        Copy-Item $exe $pkg
        # Not in the repository: taken from $AssetsDir. fsr4_411 is left out: it is built from
        # the user's own AMD DLL.
        foreach ($d in 'fsr4_shaders', 'tools') { if (Test-Path "$AssetsDir\$d") { Copy-Tree "$AssetsDir\$d" "$pkg\$d" } }
        if (Test-Path "$AssetsDir\out\nvngx_dlss.dll") { Copy-Item "$AssetsDir\out\nvngx_dlss.dll" "$pkg\out" }
        Get-ChildItem $pkg -Recurse -Directory -Filter __pycache__ | Remove-Item -Recurse -Force  # tools\python's too
        # No readme: the launcher itself says to put the dump in CUSA03173 next to it.
        $zipPath = Join-Path $dist "UPDATE.$version-Bloodborne-PC.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
        Write-Host ("Packaged {0} ({1:N0} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB))
        # Uploaded with the zip: the launcher's updater refuses a zip without it (sha256sum format, no BOM).
        $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
        [IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS'), "$hash  $(Split-Path $zipPath -Leaf)`n")
        Write-Host "Release v$Version on GitHub: upload $zipPath and $dist\SHA256SUMS"
        Set-Content $versionFile $Version  # the next build counts up from this one
    }
    finally { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
