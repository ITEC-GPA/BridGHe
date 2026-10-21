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
if ($Package -and $Configuration -ne 'Release') { throw 'I pacchetti installabili richiedono Release.' }
Push-Location -LiteralPath $projectRoot
try {
    & dotnet build BridGHe.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }
    & py tools/audit_contracts.py --baseline-only
    if ($LASTEXITCODE -ne 0) { throw 'Audit delle porte/GUID non riuscito.' }
    if ($Smoke -or $Native) {
        if ($Configuration -ne 'Release') { throw 'Gli smoke test richiedono Release.' }
        if ($Native) { & dotnet run --project tests/BridGHe.Smoke -c Release -- --native | Tee-Object -Variable smokeOutput }
        else { & dotnet run --project tests/BridGHe.Smoke -c Release | Tee-Object -Variable smokeOutput }
        if ($LASTEXITCODE -ne 0) { throw 'Smoke test non riusciti.' }
        if (-not ($smokeOutput -match '^PASS TOTAL \d+$')) { throw 'Smoke test interrotti prima del riepilogo finale, anche se il processo ha restituito exit code 0.' }
        # Drawing integrations run in independent Rhino.Inside processes.
        foreach ($group in @('bridge-plots','plot-preview')) {
            & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression $group --preview-capture | Tee-Object -Variable drawingOutput
            if ($LASTEXITCODE -ne 0 -or -not ($drawingOutput -match '^REGRESSION TOTAL 1$')) {
                throw "Test grafico incompleto o fallito: $group"
            }
        }
        foreach ($replay in @($false, $true)) {
            $exampleArgs = @('--example-layout')
            if ($replay) { $exampleArgs += '--solve-portable' }
            & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- @exampleArgs | Tee-Object -Variable exampleOutput
            if ($LASTEXITCODE -ne 0 -or -not ($exampleOutput -match '^EXAMPLE REOPEN PASS:')) {
                throw "Test dei GHX distribuiti incompleto o fallito: replay=$replay"
            }
        }
        Write-Host 'SMOKE SUITE COMPLETE: core, drawing integrations, current GHX contracts and saved portable examples passed.'
    }
    if ($Examples) {
        if ($Configuration -ne 'Release') { throw 'Gli esempi richiedono Release.' }
        & dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione degli esempi non riuscita.' }
        foreach ($mode in @('--examples','--bridge-native','--gh-examples')) {
            & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- $mode
            if ($LASTEXITCODE -ne 0) { throw "Esempi non riusciti: $mode" }
        }
        $validationRoot = Join-Path $projectRoot 'example\00-documentazione\validazioni'
        New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
        $nativeRunId = (Get-Content -LiteralPath (Join-Path $validationRoot 'latest-passed.txt') -Raw).Trim()
        $nativeRun = Join-Path $validationRoot "esecuzioni\$nativeRunId"
        Copy-Item -LiteralPath (Join-Path $nativeRun 'report.json') -Destination (Join-Path $validationRoot 'native.json')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'example\22-fasi-sezione-gpc\calcoli\fasi-sezione\report.json') -Destination (Join-Path $validationRoot 'bridgeChecks.json')
        $ghReports = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'example') -Directory | Where-Object { $_.Name -match '^(0[1-9]|10)-' } | ForEach-Object {
            [pscustomobject]@{example=$_.Name;measurements=(Get-Content -LiteralPath (Join-Path $_.FullName 'calcoli\grasshopper\report.json') -Raw | ConvertFrom-Json)}
        }
        $ghReports | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $validationRoot 'grasshopper.json') -Encoding utf8
        # Native files remain generated artifacts; definitions, recipes and reports are versioned.
        & py tools/package_native_examples.py $nativeRunId
        if ($LASTEXITCODE -ne 0) { throw 'Creazione pacchetto dei benchmark nativi non riuscita.' }
    }
    if ($BridgeGrillage) {
        if ($Configuration -ne 'Release') { throw 'Il ponte a graticcio richiede Release.' }
        & dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione del ponte non riuscita.' }
        & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-grillage --gh
        if ($LASTEXITCODE -ne 0) { throw 'Verifica del ponte a graticcio non riuscita.' }
        $examplePackages = Join-Path $projectRoot 'example\00-documentazione\archivio-pacchetti'
        New-Item -ItemType Directory -Path $examplePackages -Force | Out-Null
        Compress-Archive -Path (Join-Path $projectRoot 'example\11-ponte-graticcio\calcoli\*') -DestinationPath (Join-Path $examplePackages 'BridGHe-ponte-graticcio-native.zip') -Force
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
        $compiled = Join-Path $projectRoot 'bin\Release'
        $packageName = 'BridGHe-0.1.0-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff')
        $packageRoot = Join-Path $projectRoot "obj\packages\$packageName"
        $archiveRoot = Join-Path $projectRoot 'artifacts\releases'
        New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
        $archive = Join-Path $archiveRoot "$packageName.zip"
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        try {
            $assemblies = @('BridGHe.Grasshopper.gha','BridGHe.Common.dll','GPCUtilities.dll','GPCGeometry.dll','DelaunayMesh.dll',
                'GPCModel.dll','GPCChecker.Concrete.dll','GPCChecker.Steel.dll','GPCChecker.CompositeBridge.dll','MathNet.Numerics.dll','Svg.dll','ExCSS.dll')
            foreach ($name in $assemblies) { Copy-Item -LiteralPath (Join-Path $compiled $name) -Destination $packageRoot }
            Get-ChildItem -LiteralPath $compiled -Filter 'BridGHe.Grasshopper.*.json' | Copy-Item -Destination $packageRoot
            Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageRoot
            Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $packageRoot -Recurse
            Copy-Item -LiteralPath (Join-Path $projectRoot 'migration') -Destination $packageRoot -Recurse
            # Ship definitions and documentation; native runs and historical packages
            # stay beside their examples locally, but must not bloat the plugin package.
            $exampleRoot = Join-Path $projectRoot 'example'
            foreach ($file in Get-ChildItem -LiteralPath $exampleRoot -File -Recurse) {
                $relative = [IO.Path]::GetRelativePath($exampleRoot, $file.FullName)
                if ($relative -match '(^|[\\/])(calcoli|archivio-pacchetti|esecuzioni|backup-prima-fasi)([\\/]|$)' -or $file.Name.StartsWith('~$')) { continue }
                $target = Join-Path (Join-Path $packageRoot 'example') $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target
            }
            $packageAssets = Join-Path $packageRoot 'assets'
            New-Item -ItemType Directory -Path $packageAssets -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\icons') -Destination $packageAssets -Recurse
            $manifest = Get-ChildItem -LiteralPath $packageRoot -File | ForEach-Object {
                [pscustomobject]@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
            }
            $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding utf8
            Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive
            Write-Host "Pacchetto: $archive"
        }
        finally {
            $resolvedPackage = [IO.Path]::GetFullPath($packageRoot)
            $stagingPrefix = [IO.Path]::GetFullPath((Join-Path $projectRoot 'obj\packages')) + '\'
            if (-not $resolvedPackage.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Percorso temporaneo del pacchetto non valido: $resolvedPackage"
            }
            Remove-Item -LiteralPath $resolvedPackage -Recurse -Force
        }
    }
}
finally { Pop-Location }
