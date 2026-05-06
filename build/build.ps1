# Build helper for Loadarr (Windows / PowerShell).
# Usage:
#   pwsh ./build/build.ps1                                     # uses default LaunchBox path
#   pwsh ./build/build.ps1 -LaunchBoxPath "D:\Games\LaunchBox"  # explicit path
#   pwsh ./build/build.ps1 -Install                             # also copy to <LaunchBox>/Plugins/Loadarr

param(
    [string]$LaunchBoxPath = "$env:USERPROFILE\LaunchBox",
    [switch]$Install,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."
$proj = Join-Path $root "src/Loadarr/Loadarr.csproj"

if (-not (Test-Path "$LaunchBoxPath/Core/Unbroken.LaunchBox.Plugins.dll")) {
    Write-Warning "Could not find Unbroken.LaunchBox.Plugins.dll at $LaunchBoxPath/Core/. Build will likely fail."
}

dotnet restore $proj
dotnet build $proj `
    -c $Configuration `
    /p:LaunchBoxPath=$LaunchBoxPath

if ($Install) {
    $outDir = Join-Path $root "src/Loadarr/bin/$Configuration"
    $pluginDir = Join-Path $LaunchBoxPath "Plugins/Loadarr"
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    Copy-Item -Force -Recurse "$outDir/*" $pluginDir
    Write-Host "Installed to $pluginDir"
}
