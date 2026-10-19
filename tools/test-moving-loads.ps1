param([switch]$Native, [string]$Scenario = 'all')
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
try {
    & dotnet build tests/BridGHe.Smoke -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione dei benchmark non riuscita.' }
    $testArgs = @('--moving-load-tests', $Scenario)
    if ($Native) { $testArgs += '--native' }
    & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- @testArgs | Tee-Object -Variable report
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark carichi mobili non riuscito; consultare report.json.' }
    $expected = if ($Native) { '^MOVING NATIVE PASS$' } else { '^MOVING PREPARED:' }
    if (-not ($report -match $expected)) { throw 'Processo terminato prima di completare i benchmark.' }
}
finally { Pop-Location }
