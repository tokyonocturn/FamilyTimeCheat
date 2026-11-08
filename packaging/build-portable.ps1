<#
    Builds the portable release package and, optionally, the installer.

    Usage:
      powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1
      powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1 -Installer
      powershell -ExecutionPolicy Bypass -File packaging\build-portable.ps1 -ManagedDir "D:\Game\FamilyTime_Data\Managed"
#>
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$ManagedDir = "",
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dist = Join-Path $root "dist"

# 1. Build everything first.
$buildArgs = @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "build.ps1"), "-Configuration", $Configuration)
if ($ManagedDir) { $buildArgs += @("-ManagedDir", $ManagedDir) }
& powershell @buildArgs
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed." }

$name = "FamilyTimeChef-$Version-ep3-dx11-portable"
$stage = Join-Path $dist $name
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

# 3. Zip.
$zip = Join-Path $dist "$name.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

# 4. Installer (optional).
$setup = $null
if ($Installer) {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) { throw "Inno Setup (iscc.exe) not found on PATH. Install Inno Setup 6 or omit -Installer." }
    & $iscc.Source `
        "/DMyVersion=$Version" `
        "/DMySource=$stage" `
        "/O$dist" `
        (Join-Path $root "packaging\installer\FamilyTimeChef.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
    $setup = Join-Path $dist "FamilyTimeChef-$Version-ep3-dx11-setup.exe"
    if (-not (Test-Path $setup)) { throw "Installer was not produced." }
}

# 5. Checksums over every published artifact.
$artifacts = @($zip)
if ($setup) { $artifacts += $setup }
$sums = Join-Path $dist "SHA256SUMS.txt"
$lines = foreach ($file in $artifacts) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "{0}  {1}" -f $hash, (Split-Path -Leaf $file)
}
$lines | Set-Content -Path $sums -Encoding ascii

Write-Host ""
Write-Host "Artifacts:"
foreach ($file in $artifacts) {
    $size = (Get-Item $file).Length
    Write-Host ("  {0}  ({1:N1} KB)" -f (Split-Path -Leaf $file), ($size / 1KB))
}
Write-Host ""
Get-Content $sums
