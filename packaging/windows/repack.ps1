# Builds a release of this fork on Windows: deadinside28's official Windows package with this
# fork's launcher (launcher-windows/BloodborneLauncher, which updates itself) in place of Bloodborne.exe.
# Needs the .NET 8 SDK.
#   .\packaging\windows\repack.ps1                  next version: 0.7 -> 0.7.1 -> 0.7.2 (dist\version.txt)
#   -Version 0.7.5                                  a version of your own
#   -Source C:\path\to\package.zip                  a Windows package zip instead of deadinside28's latest release
# Output: dist\UPDATE.<version>-Bloodborne-PC.zip and dist\SHA256SUMS. Publish both as release v<version>.
# Test build (launcher-windows\build-launcher.ps1 uses it):
#   -Engine C:\path\out     also swap in bb-probe.exe and bb-gpu-capabilities.exe built there, plus the MSYS2 DLLs they
#                           load. Default version <upstream>-local; dist\version.txt and dist\SHA256SUMS stay untouched.
#   -NoZip                  leave the package as the folder dist\Bloodborne-PC-local instead of a zip
param([string]$Version, [string]$Source, [string]$Engine, [switch]$NoZip)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) "bbport-repack-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    if (-not $Source) {
        $release = Invoke-RestMethod 'https://api.github.com/repos/deadinside28/bloodborne_pc/releases/latest' -Headers @{ 'User-Agent' = 'bbport-repack' }
        $asset = $release.assets | Where-Object name -like '*-windows-x64.zip' | Select-Object -First 1
        $sums = $release.assets | Where-Object name -eq 'SHA256SUMS' | Select-Object -First 1
        if (-not $asset -or -not $sums) { throw "deadinside28's release $($release.tag_name) has no Windows zip with SHA256SUMS." }
        Write-Host "== Downloading $($asset.name) (deadinside28 $($release.tag_name))"
        $Source = Join-Path $work $asset.name
        Invoke-WebRequest $asset.browser_download_url -OutFile $Source
        Invoke-WebRequest $sums.browser_download_url -OutFile "$work\SHA256SUMS"
        $line = Get-Content "$work\SHA256SUMS" | Where-Object { ($_ -split '\s+')[-1].TrimStart('*') -eq $asset.name }
        if (-not $line -or (Get-FileHash $Source).Hash -ne ($line -split '\s+')[0]) { throw "Checksum mismatch for $($asset.name)." }
    }

    Write-Host '== Unpacking'
    $stage = Join-Path $work 'stage'
    [IO.Compression.ZipFile]::ExtractToDirectory($Source, $stage)
    $pkg = @(Get-ChildItem $stage)
    if ($pkg.Count -ne 1 -or -not (Test-Path "$($pkg[0].FullName)\bbport-run.exe")) { throw 'Expected one package folder with bbport-run.exe inside the zip.' }
    $pkg = $pkg[0].FullName
    $upstream = (Get-Content "$pkg\VERSION" -TotalCount 1).Trim()
    if (-not $Version -and $Engine) { $Version = "$upstream-local" }  # a test build must not consume a release number
    if (-not $Version) {
        $last = "$(Get-Content "$dist\version.txt" -TotalCount 1 -ErrorAction SilentlyContinue)"
        $Version = if ($last -match "^$([regex]::Escape($upstream))\.(\d+)$") { "$upstream.$([int]$Matches[1] + 1)" } else { "$upstream.1" }
    }

    if ($Engine) {
        Write-Host "== Swapping in the engine from $Engine"
        foreach ($exe in 'bb-probe.exe', 'bb-gpu-capabilities.exe') {
            if (-not (Test-Path "$Engine\$exe")) { throw "$Engine\$exe is missing: build the engine first." }
            Copy-Item "$Engine\$exe" "$pkg\bin" -Force
        }
        # The package's DLLs come from the Linux cross sysroot; the local build needs its MSYS2 versions.
        $enginePath = (Resolve-Path "$Engine\bb-probe.exe").Path
        $env:MSYSTEM = 'CLANG64'
        $lddOut = & 'C:\msys64\usr\bin\bash.exe' -lc "ldd `"`$(cygpath -u '$enginePath')`""
        if ($LASTEXITCODE) { throw 'MSYS2 ldd failed.' }
        # vulkan-1.dll is the GPU driver's, as in the old build script.
        $dlls = $lddOut | Select-String '^\s*(\S+\.dll) => /clang64/bin/' | ForEach-Object { $_.Matches[0].Groups[1].Value } |
            Where-Object { $_ -ne 'vulkan-1.dll' -and (Test-Path "C:\msys64\clang64\bin\$_") } | Sort-Object -Unique
        foreach ($dll in $dlls) { Copy-Item "C:\msys64\clang64\bin\$dll" "$pkg\bin" -Force }
        Write-Host "   $(@($dlls).Count) MSYS2 DLLs copied"
    }

    Write-Host "== Building the launcher ($Version)"
    # This fork's launcher (launcher-windows) replaces 0.7's Bloodborne.exe: one launcher exe per package.
    dotnet publish "$repo\launcher-windows\BloodborneLauncher\BloodborneLauncher.csproj" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$work\launcher"
    if ($LASTEXITCODE) { throw 'dotnet publish failed.' }
    Remove-Item "$pkg\Bloodborne.exe"
    Copy-Item "$work\launcher\BloodborneLauncher.exe" $pkg -Force
    # Plain-source parts this fork changes (the engine binaries stay deadinside28's): run.py imports
    # trophies earned with the old fork (scripts\trophy_import.py).
    Copy-Item "$repo\run.py" $pkg -Force
    Copy-Item "$repo\scripts\trophy_import.py", "$repo\scripts\mods.py", "$repo\scripts\patches.py" "$pkg\scripts" -Force  # eboot.bin and patch XML mods
    # Fixes for the 0.7 engine as game patches, on by default (a switch on the launcher's Patches card).
    Copy-Item "$PSScriptRoot\patches\*.xml" "$pkg\patches" -Force
    [IO.File]::WriteAllText("$pkg\VERSION", "$Version`n")  # the updater compares this with the latest release
    # 0.7's launcher folder is empty in the Windows package. The Tk files stay: bbport-run.exe's
    # PyInstaller boot hook refuses to start without _tcl_data, even for --play.
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$pkg\launcher"
    Copy-Item "$PSScriptRoot\README-Windows.txt" $pkg -Force  # 0.7's README describes its own launcher
    Rename-Item $pkg 'Bloodborne-PC'

    if ($NoZip) {
        $out = Join-Path $dist 'Bloodborne-PC-local'
        Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item "$stage\Bloodborne-PC" $out -Recurse
        Write-Host "Package folder: $out"
        return
    }
    Write-Host '== Packaging'
    $zip = Join-Path $dist "UPDATE.$Version-Bloodborne-PC.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    # Entry names with '/': Windows PowerShell's CreateFromDirectory writes '\', which other unzip tools mangle.
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($file in Get-ChildItem $stage -Recurse -File) {
            $name = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, 'Optimal')
        }
    } finally { $archive.Dispose() }
    if ($Engine) { Write-Host "Test build: $zip"; return }
    $hash = (Get-FileHash $zip).Hash.ToLower()
    [IO.File]::WriteAllText("$dist\SHA256SUMS", "$hash  $(Split-Path $zip -Leaf)`n")  # sha256sum format, no BOM
    Set-Content "$dist\version.txt" $Version
    Write-Host ("Built {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
    Write-Host "Publish: gh release create v$Version `"$zip`" `"$dist\SHA256SUMS`" -R GerasimosKan/bloodborne_pc"
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
