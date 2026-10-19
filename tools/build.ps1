param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$Smoke,
    [switch]$Native,
    [switch]$Examples,
    [switch]$BridgeGrillage,
    [switch]$BridgeSuite,
    [switch]$BridgeSpine,
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
    if ($Examples) {
        if ($Configuration -ne 'Release') { throw 'Gli esempi richiedono Release.' }
        & dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione degli esempi non riuscita.' }
        foreach ($mode in @('--examples','--bridge-native','--gh-examples')) {
            & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- $mode
            if ($LASTEXITCODE -ne 0) { throw "Esempi non riusciti: $mode" }
        }
        $validationRoot = Join-Path $projectRoot 'examples\validation'
        New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
        $nativeRun = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\examples\latest-passed.txt') -Raw
        Copy-Item -LiteralPath (Join-Path $nativeRun 'report.json') -Destination (Join-Path $validationRoot 'native.json')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts\native-bridge-phases\report.json') -Destination (Join-Path $validationRoot 'bridgeChecks.json')
        $ghReports = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'artifacts\gh-examples') -Directory | ForEach-Object {
            [pscustomobject]@{example=$_.Name;measurements=(Get-Content -LiteralPath (Join-Path $_.FullName 'report.json') -Raw | ConvertFrom-Json)}
        }
        $ghReports | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $validationRoot 'grasshopper.json') -Encoding utf8
        # Native files remain generated artifacts; definitions, recipes and reports are versioned.
        $nativeZip = Join-Path $projectRoot 'artifacts\BridGHe-native-examples.zip'
        Compress-Archive -Path (Join-Path $nativeRun '*') -DestinationPath $nativeZip -Force
        Write-Host "Esempi nativi: $nativeZip"
    }
    if ($BridgeGrillage) {
        if ($Configuration -ne 'Release') { throw 'Il ponte a graticcio richiede Release.' }
        & dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione del ponte non riuscita.' }
        & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-grillage --gh
        if ($LASTEXITCODE -ne 0) { throw 'Verifica del ponte a graticcio non riuscita.' }
        Compress-Archive -Path (Join-Path $projectRoot 'artifacts\bridge-grillage\*') -DestinationPath (Join-Path $projectRoot 'artifacts\BridGHe-ponte-graticcio-native.zip') -Force
    }
    if ($BridgeSuite) {
        if ($Configuration -ne 'Release') { throw 'I quattro ponti richiedono Release.' }
        & dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione della suite ponti non riuscita.' }
        & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-suite --gh
        if ($LASTEXITCODE -ne 0) { throw 'Verifica della suite ponti non riuscita.' }
        & py tools/package_bridge_suite.py
        if ($LASTEXITCODE -ne 0) { throw 'Creazione pacchetto nativo dei ponti non riuscita.' }
    }
    if ($BridgeSpine) {
        if ($Configuration -ne 'Release') { throw 'I ponti con spina centrale richiedono Release.' }
        & dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione degli esempi con spina centrale non riuscita.' }
        & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-spine all --gh
        if ($LASTEXITCODE -ne 0) { throw 'Verifica degli esempi con spina centrale non riuscita.' }
        & py tools/package_bridge_suite.py --spine
        if ($LASTEXITCODE -ne 0) { throw 'Creazione pacchetto nativo con spina centrale non riuscita.' }
    }
    if ($Package) {
        $compiled = Join-Path $projectRoot "bin\BridGHe.Grasshopper\$Configuration\net8.0-windows"
        $packageRoot = Join-Path $projectRoot ("bin\packages\$Configuration\BridGHe-0.1.0-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $assemblies = @('BridGHe.Grasshopper.gha','BridGHe.Common.dll','GPCUtilities.dll','GPCGeometry.dll','DelaunayMesh.dll',
            'GPCModel.dll','GPCChecker.Concrete.dll','GPCChecker.Steel.dll','GPCChecker.CompositeBridge.dll','MathNet.Numerics.dll')
        foreach ($name in $assemblies) { Copy-Item -LiteralPath (Join-Path $compiled $name) -Destination $packageRoot }
        Get-ChildItem -LiteralPath $compiled -Filter 'BridGHe.Grasshopper.*.json' | Copy-Item -Destination $packageRoot
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageRoot
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $packageRoot -Recurse
        Copy-Item -LiteralPath (Join-Path $projectRoot 'migration') -Destination $packageRoot -Recurse
        Copy-Item -LiteralPath (Join-Path $projectRoot 'examples') -Destination $packageRoot -Recurse
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
