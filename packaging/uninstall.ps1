<#
    Removes FamilyTimeChef from %LOCALAPPDATA%\FamilyTimeChef.

    Quit the game and the Studio window first; the shim lives inside the game
    process and cannot be unloaded while it is running.
#>
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA "FamilyTimeChef"),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Target)) {
    Write-Host "Nothing to remove: $Target does not exist."
    return
}

if ((Get-Process FamilyTime -ErrorAction SilentlyContinue) -and -not $Force) {
    throw "Family Time is running. Quit the game first, or pass -Force."
}
if ((Get-Process FamilyTimeChef.Studio -ErrorAction SilentlyContinue) -and -not $Force) {
    throw "FamilyTimeChef Studio is running. Close it first, or pass -Force."
}

if (-not $Force) {
    $answer = Read-Host "Remove $Target ? [y/N]"
    if ($answer -notmatch '^(y|yes)$') { Write-Host "Cancelled."; return }
}

Remove-Item $Target -Recurse -Force
Write-Host "Removed $Target"
Write-Host "No registry keys, services or start-up entries were created, so nothing else remains."
