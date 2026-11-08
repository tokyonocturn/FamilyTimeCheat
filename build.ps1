<#
    Builds every shipped component into dist\.

    Prerequisites:
      * tools\fetch-refs.ps1 has been run (or pass -ManagedDir), so Unity
        reference assemblies are available for the shim.
      * a C compiler for the three native tools (this script uses gcc; see
        docs\BUILD.md for the MSVC equivalents).

    Usage:
      powershell -ExecutionPolicy Bypass -File build.ps1
      powershell -ExecutionPolicy Bypass -File build.ps1 -ManagedDir "D:\Game\FamilyTime_Data\Managed"
#>
param(
    [string]$Configuration = "Release",
    [string]$ManagedDir = "",
    [switch]$SkipNative
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$native = Join-Path $root "build\native"

if (-not $ManagedDir) {
    if (-not (Test-Path (Join-Path $root "lib\unity\UnityEngine.dll"))) {
        throw "Missing lib\unity. Run tools\fetch-refs.ps1 -GameDir <install> or pass -ManagedDir."
    }
}

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist, $native | Out-Null

$props = @()
if ($ManagedDir) { $props += "-p:FamilyTimeManagedDir=$ManagedDir" }

Write-Host "Building shim..."
dotnet build (Join-Path $root "src\FamilyTimeChef.Plugin\FamilyTimeChef.Plugin.csproj") -c $Configuration @props
if ($LASTEXITCODE -ne 0) { throw "Shim build failed." }
Copy-Item (Join-Path $root "src\FamilyTimeChef.Plugin\bin\$Configuration\FamilyTimeChef.InGame.dll") $dist -Force

Write-Host "Publishing Studio..."
dotnet publish (Join-Path $root "src\FamilyTimeChef.Studio\FamilyTimeChef.Studio.csproj") -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $dist
if ($LASTEXITCODE -ne 0) { throw "Studio publish failed." }

if (-not $SkipNative) {
    Write-Host "Building native components..."
    $cc = Get-Command gcc -ErrorAction SilentlyContinue
    if (-not $cc) {
        throw "gcc not found. Install MinGW-w64, or build the native tools with MSVC as shown in docs\BUILD.md, then re-run with -SkipNative."
    }
    & gcc -O2 -shared -o (Join-Path $native "ftagent.dll") (Join-Path $root "src\agent\agent.c")
    if ($LASTEXITCODE -ne 0) { throw "ftagent build failed." }
    & gcc -O2 -o (Join-Path $native "ftinject.exe") (Join-Path $root "tools\injector\injector.c")
    if ($LASTEXITCODE -ne 0) { throw "ftinject build failed." }
    & gcc -O2 -o (Join-Path $native "ftinput.exe") (Join-Path $root "tools\input\input.c")
    if ($LASTEXITCODE -ne 0) { throw "ftinput build failed." }
    Copy-Item (Join-Path $native "ftagent.dll"), (Join-Path $native "ftinject.exe"), (Join-Path $native "ftinput.exe") $dist -Force
}

Write-Host ""
Write-Host "Build complete:"
Get-ChildItem $dist -File | Select-Object Name, Length | Format-Table -AutoSize
