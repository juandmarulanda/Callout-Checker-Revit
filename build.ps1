# Builds CalloutCheck for every supported Revit version and makes the installer zip.
#   .\build.ps1                 -> 2025, 2026, 2027 + CalloutCheck-Installer.zip
#   .\build.ps1 -Versions 2026  -> just one version
param([string[]]$Versions = @("2025", "2026", "2027"))
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    dotnet tool restore | Out-Null
    foreach ($v in $Versions) {
        Write-Host "== Building for Revit $v ==" -ForegroundColor Cyan
        dotnet build "src\CalloutCheck\CalloutCheck.csproj" -c Release -p:RevitVersion=$v
        if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $v" }
    }

    $pkg = Join-Path $PSScriptRoot "package\CalloutCheck"
    Remove-Item (Join-Path $PSScriptRoot "package") -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($v in $Versions) {
        New-Item -ItemType Directory -Force -Path "$pkg\$v\CalloutCheck" | Out-Null
        Copy-Item "bin\Release\$v\CalloutCheck.dll" "$pkg\$v\CalloutCheck\"
        Copy-Item "CalloutCheck.addin" "$pkg\$v\"
    }
    Copy-Item "dist\*" $pkg
    $zip = Join-Path $PSScriptRoot "CalloutCheck-Installer.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path $pkg -DestinationPath $zip
    Write-Host "Installer: $zip" -ForegroundColor Green
}
finally { Pop-Location }
