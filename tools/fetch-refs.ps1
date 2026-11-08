<#
    Copies the Unity reference assemblies the plugin compiles against.

    These are Unity build artefacts that cannot be redistributed with this
    project, so they are taken from a Family Time installation you own.

    Usage:
      powershell -ExecutionPolicy Bypass -File tools\fetch-refs.ps1 `
        -GameDir "D:\Games\Family_Time_Ep_3_WINDOWS_DX11_b108"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$unityDir = Join-Path $root "lib\unity"
New-Item -ItemType Directory -Force -Path $unityDir | Out-Null

$managed = Join-Path $GameDir "FamilyTime_Data\Managed"
if (-not (Test-Path $managed)) {
    throw "Could not find $managed. Pass -GameDir pointing at a Family Time install."
}

foreach ($dll in @("UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.IMGUIModule.dll")) {
    Copy-Item (Join-Path $managed $dll) $unityDir -Force
}

Write-Host "Reference assemblies copied to $unityDir"
