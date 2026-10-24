param([switch]$Force, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$definition = Join-Path $PSScriptRoot '49-ponte-parametrico.ghx'
if (Test-Path -LiteralPath $definition) {
    if (-not $Force) { throw 'Il GHX esiste già. Usare -Force per rigenerarlo salvando prima un backup.' }
    $backupFolder = Join-Path $projectRoot 'artifacts\parametric-bridge-backups'
    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    $backup = Join-Path $backupFolder ('49-ponte-parametrico-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.ghx')
    Copy-Item -LiteralPath $definition -Destination $backup
    Write-Host "Backup: $backup"
}
Push-Location -LiteralPath $projectRoot
try {
    if (-not $NoBuild) {
        & dotnet build tests/BridGHe.Smoke -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita; GHX precedente conservato.' }
    }
    & dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --parametric-bridge-example | Tee-Object -Variable output
    if ($LASTEXITCODE -ne 0 -or -not ($output -match '^PARAMETRIC BRIDGE EXAMPLE PASS:')) {
        throw 'Generazione o verifica incompleta. Leggere gli errori; backup disponibile quando il GHX preesisteva.'
    }
    Write-Host "Creato e verificato: $definition"
}
finally { Pop-Location }
