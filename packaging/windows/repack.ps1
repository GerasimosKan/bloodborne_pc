# Builds a release of this fork on Windows: deadinside28's official Windows package with this
# repository's launcher (launcher/windows, which updates itself) swapped in. Needs the .NET 9 SDK.
#   .\packaging\windows\repack.ps1                  next version: 0.7 -> 0.7.1 -> 0.7.2 (dist\version.txt)
#   -Version 0.7.5                                  a version of your own
#   -Source C:\path\to\package.zip                  a Windows package zip instead of deadinside28's latest release
# Output: dist\UPDATE.<version>-Bloodborne-PC.zip and dist\SHA256SUMS. Publish both as release v<version>.
param([string]$Version, [string]$Source)
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
    if (-not $Version) {
        $last = "$(Get-Content "$dist\version.txt" -TotalCount 1 -ErrorAction SilentlyContinue)"
        $Version = if ($last -match "^$([regex]::Escape($upstream))\.(\d+)$") { "$upstream.$([int]$Matches[1] + 1)" } else { "$upstream.1" }
    }

    Write-Host "== Building the launcher ($Version)"
    # The flags of packaging/windows/package.sh.
    dotnet publish "$repo\launcher\windows" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$work\launcher"
    if ($LASTEXITCODE) { throw 'dotnet publish failed.' }
    Copy-Item "$work\launcher\Bloodborne.exe" $pkg -Force
    [IO.File]::WriteAllText("$pkg\VERSION", "$Version`n")  # the updater compares this with the latest release
    Rename-Item $pkg 'Bloodborne-PC'

    Write-Host '== Packaging'
    $zip = Join-Path $dist "UPDATE.$Version-Bloodborne-PC.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash $zip).Hash.ToLower()
    [IO.File]::WriteAllText("$dist\SHA256SUMS", "$hash  $(Split-Path $zip -Leaf)`n")  # sha256sum format, no BOM
    Set-Content "$dist\version.txt" $Version
    Write-Host ("Built {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
    Write-Host "Publish: gh release create v$Version `"$zip`" `"$dist\SHA256SUMS`" -R GerasimosKan/bloodborne_pc"
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
