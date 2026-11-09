<#
    Builds the release artifacts:

      * FamilyTimeChef-<Version>-ep3-dx11-portable.exe  single self-extracting EXE
      * FamilyTimeChef-<Version>-ep3-dx11-setup.exe     single installer EXE
      * SHA256SUMS.txt                                 checksums for both

    No ZIP is produced: the portable is one EXE built with the 7-Zip SFX module.

    Usage:
      powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1
      powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1 -ManagedDir "D:\Game\FamilyTime_Data\Managed"
      powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1 -PortableOnly
#>
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$ManagedDir = "",
    [switch]$PortableOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dist = Join-Path $root "dist"

# 1. Build everything first.
$buildArgs = @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "build.ps1"), "-Configuration", $Configuration)
if ($ManagedDir) { $buildArgs += @("-ManagedDir", $ManagedDir) }
& powershell @buildArgs
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed." }

$name = "FamilyTimeChef-$Version-ep3-dx11"
$stage = Join-Path $dist "$name-portable"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# 2. Runtime payload only: no game files, no third-party binaries, no sources.
$payload = @(
    "FamilyTimeChef.Studio.exe",
    "FamilyTimeChef.InGame.dll",
    "ftagent.dll",
    "ftinject.exe",
    "ftinput.exe"
)
foreach ($file in $payload) {
    $src = Join-Path $dist $file
    if (-not (Test-Path $src)) { throw "Missing build output: $file" }
    Copy-Item $src $stage -Force
}

Copy-Item (Join-Path $root "packaging\install.ps1") $stage -Force
Copy-Item (Join-Path $root "packaging\uninstall.ps1") $stage -Force
Copy-Item (Join-Path $root "README.md") $stage -Force
Copy-Item (Join-Path $root "LICENSE") $stage -Force
Copy-Item (Join-Path $root "docs\INSTALL.md") (Join-Path $stage "INSTALL.md") -Force
Copy-Item (Join-Path $root "docs\LEGAL.md") (Join-Path $stage "LEGAL.md") -Force

# 3. Portable: one self-extracting EXE (7-Zip SFX).
$sevenZip = @(
    "C:\Program Files\7-Zip\7z.exe",
    "C:\Program Files (x86)\7-Zip\7z.exe",
    (Get-Command 7z -ErrorAction SilentlyContinue).Source
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $sevenZip) { throw "7-Zip not found. Install 7-Zip to build the portable EXE." }

$sfx = Join-Path (Split-Path -Parent $sevenZip) "7z.sfx"
if (-not (Test-Path $sfx)) { throw "7-Zip SFX module not found: $sfx" }

$portable = Join-Path $dist "$name-portable.exe"
Remove-Item $portable -Force -ErrorAction SilentlyContinue
& $sevenZip a -mx=9 "-sfx$sfx" $portable (Join-Path $stage "*") | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $portable)) { throw "Portable EXE build failed." }

# 4. Installer: one EXE (Inno Setup).
$setup = $null
if (-not $PortableOnly) {
    $iscc = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        (Get-Command iscc -ErrorAction SilentlyContinue).Source
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found (iscc.exe). Install it, or pass -PortableOnly." }

    & $iscc "/DMyVersion=$Version" "/DMySource=$stage" "/O$dist" (Join-Path $root "packaging\installer\FamilyTimeChef.iss") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
    $setup = Join-Path $dist "$name-setup.exe"
    if (-not (Test-Path $setup)) { throw "Installer was not produced." }
}

# 5. Checksums over every published artifact.
$artifacts = @($portable)
if ($setup) { $artifacts += $setup }
$sums = Join-Path $dist "SHA256SUMS.txt"
$lines = foreach ($file in $artifacts) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "{0}  {1}" -f $hash, (Split-Path -Leaf $file)
}
# LF line endings and no BOM so sha256sum -c works on Windows and Unix tooling.
[System.IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Artifacts:"
foreach ($file in $artifacts) {
    $size = (Get-Item $file).Length
    Write-Host ("  {0}  ({1:N1} MB)" -f (Split-Path -Leaf $file), ($size / 1MB))
}
Write-Host ""
Get-Content $sums
