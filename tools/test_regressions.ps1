param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string[]]$Groups = @(),
    [switch]$ViewportCapture,
    [string]$ReportDirectory = 'artifacts/regressions-isolated'
)
$ErrorActionPreference = 'Stop'
$bridgeRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $bridgeRoot
try {
    # Each Rhino.Inside host owns native state and drawing resources. A crashed
    # host can return 0: require the final marker as well as the process exit code.
    $source = Get-Content -LiteralPath 'tests/BridGHe.Smoke/RegressionTests.cs' -Raw
    $available = [regex]::Matches($source, '\["([a-z0-9-]+)"\]\s*=') | ForEach-Object { $_.Groups[1].Value }
    if ($Groups.Count -eq 0) { $Groups = @($available) }
    foreach ($group in $Groups) { if ($group -notin $available) { throw "Unknown regression: $group" } }
    $folder = [IO.Path]::GetFullPath((Join-Path $bridgeRoot $ReportDirectory))
    if (-not $folder.StartsWith($bridgeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Report directory must be inside the repository.' }
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $results = @()
    foreach ($group in $Groups) {
        $log = Join-Path $folder ($group + '.log')
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $arguments = @('--regression', $group)
        if ($group -in @('bridge-plots','plot-preview','bridge-report-component') -or $ViewportCapture) { $arguments += '--preview-capture' }
        & dotnet run --project tests/BridGHe.Smoke -c $Configuration --no-build -- @arguments > $log 2>&1
        $code = $LASTEXITCODE
        $complete = Select-String -LiteralPath $log -Pattern '^REGRESSION TOTAL 1$|^PASS REGRESSION: bridge-report$' -Quiet
        $passed = $code -eq 0 -and $complete
        $results += [pscustomobject]@{group=$group;passed=$passed;exitCode=$code;completed=[bool]$complete;seconds=[math]::Round($watch.Elapsed.TotalSeconds,2);log=[IO.Path]::GetRelativePath($bridgeRoot,$log)}
        $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'results.json') -Encoding utf8
        if ($passed) { Write-Host "PASS $group" }
        else { Write-Host "FAIL $group"; Get-Content -LiteralPath $log -Tail 10 }
    }
    $failed = @($results | Where-Object { -not $_.passed })
    Write-Host "ISOLATED REGRESSIONS: $($results.Count) groups, $($failed.Count) failures."
    if ($failed.Count -gt 0) { exit 1 }
}
finally { Pop-Location }
