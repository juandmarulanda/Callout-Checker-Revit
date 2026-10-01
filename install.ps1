# Copies the built add-in into %AppData%\Autodesk\Revit\Addins\<version>\ for each built version.
# Close Revit before running.
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
foreach ($v in @("2025", "2026", "2027")) {
    $src = Join-Path $PSScriptRoot "bin\$Configuration\$v"
    if (-not (Test-Path "$src\CalloutCheck.dll")) { continue }
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
    $target = Join-Path $addins "CalloutCheck"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item "$src\CalloutCheck.dll" $target -Force
    if (Test-Path "$src\CalloutCheck.pdb") { Copy-Item "$src\CalloutCheck.pdb" $target -Force }
    Copy-Item (Join-Path $PSScriptRoot "CalloutCheck.addin") $addins -Force
    Write-Host "Installed for Revit $v -> $addins" -ForegroundColor Green
}
