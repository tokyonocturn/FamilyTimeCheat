<#
    Installs FamilyTimeChef into %LOCALAPPDATA%\FamilyTimeChef.

    Nothing is written into the game folder and no game file is modified.
    No administrator rights are required.
#>
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA "FamilyTimeChef")
)

$ErrorActionPreference = "Stop"
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = @(
    "FamilyTimeChef.Studio.exe",
    "FamilyTimeChef.InGame.dll",
    "ftagent.dll",
    "ftinject.exe",
    "ftinput.exe"
)

foreach ($file in $files) {
    if (-not (Test-Path (Join-Path $source $file))) {
        throw "Package is incomplete: $file is missing next to install.ps1"
    }
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null
foreach ($file in $files) {
    Copy-Item (Join-Path $source $file) (Join-Path $Target $file) -Force
}

Write-Host "Installed to $Target"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Start Family Time (Episode 3, DX11, b108)."
Write-Host "  2. Inject once per game session:"
Write-Host "       `$pid2 = (Get-Process FamilyTime).Id"
Write-Host "       & `"$Target\ftinject.exe`" `$pid2 `"$Target\ftagent.dll`""
Write-Host "  3. Launch the GUI:"
Write-Host "       & `"$Target\FamilyTimeChef.Studio.exe`""
Write-Host ""
Write-Host "Uninstall by deleting $Target (or run uninstall.ps1)."
