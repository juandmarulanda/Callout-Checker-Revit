# Removes CalloutCheck from Revit 2025, 2026 and 2027. Close Revit first.
foreach ($v in @("2025", "2026", "2027")) {
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
    Remove-Item (Join-Path $addins "CalloutCheck.addin") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $addins "CalloutCheck") -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "CalloutCheck removed." -ForegroundColor Green
