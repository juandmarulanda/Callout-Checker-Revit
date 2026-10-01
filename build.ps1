# Builds CalloutCheck for every supported Revit version.
#   .\build.ps1                 -> 2025, 2026, 2027
#   .\build.ps1 -Versions 2026  -> just one
param([string[]]$Versions = @("2025", "2026", "2027"), [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
foreach ($v in $Versions) {
    Write-Host "== Building for Revit $v ==" -ForegroundColor Cyan
    dotnet build "$PSScriptRoot\src\CalloutCheck\CalloutCheck.csproj" -c $Configuration -p:RevitVersion=$v
    if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $v" }
}
Write-Host "Output: $PSScriptRoot\bin\$Configuration\<version>\" -ForegroundColor Green
