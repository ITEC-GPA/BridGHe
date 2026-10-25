param([switch]$Force, [switch]$NoBuild, [switch]$Native,
      [ValidateSet('Release','Validation')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$definition = Join-Path $PSScriptRoot '50-ponte-quattro-modelli.ghx'
if (Test-Path -LiteralPath $definition) {
    if (-not $Force) { throw 'Il GHX esiste. Usare -Force per rigenerarlo con backup.' }
    $backupFolder = Join-Path $projectRoot 'artifacts\four-models-backups'
    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    $backup = Join-Path $backupFolder ('50-ponte-quattro-modelli-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.ghx')
    Copy-Item -LiteralPath $definition -Destination $backup
    Write-Host "Backup: $backup"
}
Push-Location -LiteralPath $projectRoot
try {
    if (-not $NoBuild) {
        & dotnet build tests/BridGHe.Smoke -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita; il file precedente resta disponibile.' }
    }
    $arguments = @('--parametric-bridge-phases')
    if ($Native) { $arguments += '--native' }
    & dotnet run --project tests/BridGHe.Smoke -c $Configuration --no-build -- @arguments | Tee-Object -Variable output
    if (-not ($output -match '^FOUR MODELS EXAMPLE PASS:')) { throw 'Generazione o verifica incompleta. Controllare gli errori.' }
    if ($Native -and -not ($output -match '^FOUR MODELS NATIVE PASS:')) { throw 'Analisi Straus o merge finale non completati.' }
    if ($LASTEXITCODE -ne 0) { throw 'Esecuzione incompleta. Controllare gli errori.' }
    Write-Host "Creato e verificato: $definition"
}
finally { Pop-Location }
