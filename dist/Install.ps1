# CalloutCheck installer: copies the add-in for every Revit version included in this package
# (2025, 2026, 2027) into %AppData%\Autodesk\Revit\Addins\<version>\. Close Revit first.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$installed = @()
foreach ($v in @("2025", "2026", "2027")) {
    $src = Join-Path $root $v
    if (-not (Test-Path (Join-Path $src "CalloutCheck\CalloutCheck.dll"))) { continue }
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
    New-Item -ItemType Directory -Force -Path (Join-Path $addins "CalloutCheck") | Out-Null
    Copy-Item (Join-Path $src "CalloutCheck\CalloutCheck.dll") (Join-Path $addins "CalloutCheck") -Force
    Copy-Item (Join-Path $src "CalloutCheck.addin") $addins -Force
    # Files downloaded from the internet are blocked by Windows; Revit will not load them otherwise.
    Get-ChildItem $addins -Recurse -Filter "CalloutCheck*" | Unblock-File
    $installed += $v
}
if ($installed.Count -eq 0) { Write-Host "Nothing to install." -ForegroundColor Yellow }
else { Write-Host ("CalloutCheck installed for Revit " + ($installed -join ", ") + ". Start Revit and look for the CalloutCheck tab.") -ForegroundColor Green }
