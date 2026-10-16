param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$Smoke,
    [switch]$Native,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    & dotnet build BridGHe.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }
    & py tools/audit_contracts.py --baseline-only
    if ($LASTEXITCODE -ne 0) { throw 'Audit delle porte/GUID non riuscito.' }
    if ($Smoke -or $Native) {
        if ($Configuration -ne 'Release') { throw 'Gli smoke test richiedono Release.' }
        if ($Native) { & dotnet run --project tests/BridGHe.Smoke -c Release -- --native }
        else { & dotnet run --project tests/BridGHe.Smoke -c Release }
        if ($LASTEXITCODE -ne 0) { throw 'Smoke test non riusciti.' }
    }
    if ($Package) {
        $compiled = Join-Path $projectRoot "src\BridGHe.Grasshopper\bin\$Configuration\net8.0-windows"
        $packageRoot = Join-Path $projectRoot ('artifacts\packages\BridGHe-0.1.0-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $assemblies = @('BridGHe.Grasshopper.gha','BridGHe.Common.dll','GPCUtilities.dll','GPCGeometry.dll','DelaunayMesh.dll',
            'GPCModel.dll','GPCChecker.Concrete.dll','GPCChecker.Steel.dll','GPCChecker.CompositeBridge.dll','MathNet.Numerics.dll')
        foreach ($name in $assemblies) { Copy-Item -LiteralPath (Join-Path $compiled $name) -Destination $packageRoot }
        Get-ChildItem -LiteralPath $compiled -Filter 'BridGHe.Grasshopper.*.json' | Copy-Item -Destination $packageRoot
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageRoot
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $packageRoot -Recurse
        Copy-Item -LiteralPath (Join-Path $projectRoot 'migration') -Destination $packageRoot -Recurse
        $packageAssets = Join-Path $packageRoot 'assets'
        New-Item -ItemType Directory -Path $packageAssets -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\icons') -Destination $packageAssets -Recurse
        $manifest = Get-ChildItem -LiteralPath $packageRoot -File | ForEach-Object {
            [pscustomobject]@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
        }
        $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding utf8
        Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath ($packageRoot + '.zip')
        Write-Host "Pacchetto: $packageRoot.zip"
    }
}
finally { Pop-Location }
