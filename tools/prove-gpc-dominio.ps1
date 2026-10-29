<#
.SYNOPSIS
Times the significant examples (50 REV003, 99 REV004) with the gPC set currently in lib/GPC.

.DESCRIPTION
Build first (Release; add -p:GpcAllowCandidate=true for a candidate set):
    dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
Every run is executed with "dotnet <BridGHe.Smoke.dll>" from the project root, with BRIDGHE_RESULT_DUMP set,
so the native runners write a numerical fingerprint of all gPC section results (tests/BridGHe.Smoke/ResultFingerprint.cs).
Results go to artifacts/prove-gpc-dominio/<Phase>/<run>: timestamped output, exit code, duration, process CPU time,
machine load, fingerprints and the SHA-256 of every produced file. Generated folders are moved there; tracked files
rewritten by the harness are copied there and then restored byte for byte. Nothing is deleted.
Compare two phases with tools/compare-gpc-fingerprints.ps1.

Runs:
  50-check       --example50-rev003 --check           offline contract of the GHX
  50-regression  --regression bridge-moving-workflow  moving-load combinations (no Rhino solve)
  50-native      --example50-rev003 --native          5 Straus analyses + merge + 32,832 section checks (licence)
  99-replay      --example99 replay004                reopen saved results (no Straus)
  99-native      --modular-bridge-native <REV004 GHX> 4 Straus analyses + checks on the saved REV004 graph (licence)
  99-rev004      --example99 rev004                   regenerates REV004 from REV001 (licence); folder restored after
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9._-]+$')][string]$Phase,
    [ValidateSet('50-check', '50-regression', '50-native', '99-replay', '99-native', '99-rev004')]
    [string[]]$Runs = @('50-check', '50-regression', '50-native', '99-replay', '99-native'),
    [int]$LoadSampleSeconds = 15
)
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$smoke = Join-Path $root 'obj\bin\BridGHe.Smoke\Release\net8.0-windows\BridGHe.Smoke.dll'
if (-not (Test-Path -LiteralPath $smoke)) { throw "Compilare prima: dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release" }
$phaseRoot = Join-Path $root "artifacts\prove-gpc-dominio\$Phase"
New-Item -ItemType Directory -Force -Path $phaseRoot | Out-Null

$ex50 = 'example\50-ponte-quattro-modelli'
$ex99 = 'example\99-Ponte completo GPC\REV004'
$ghx99 = '99-Ponte completo GPC\REV004\flusso-acciaio-misto_REV004.ghx'
# Moved: folders the harness creates (moved into the run folder afterwards).
# Restore: files/folders the harness may rewrite (snapshot before, copy of the result, original restored).
$definitions = @{
    '50-check'      = @{ Args = @('--example50-rev003', '--check'); Moved = @(); Restore = @() }
    '50-regression' = @{ Args = @('--regression', 'bridge-moving-workflow'); Moved = @(); Restore = @() }
    '50-native'     = @{ Args = @('--example50-rev003', '--native'); Moved = @('artifacts\rev003-native'); Restore = @("$ex50\REV003-native-validation.json") }
    '99-replay'     = @{ Args = @('--example99', 'replay004'); Moved = @(); Restore = @($ex99) }
    '99-native'     = @{ Args = @('--modular-bridge-native', $ghx99.Replace('\', '/')); Moved = @('artifacts\modular-bridge\native-flusso-acciaio-misto_REV004'); Restore = @(); Candidate = $ghx99 }
    '99-rev004'     = @{ Args = @('--example99', 'rev004'); Moved = @(); Restore = @($ex99) }
}

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path); $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
    finally { $hasher.Dispose(); $stream.Dispose() }
}
function Get-Relative([string]$Base, [string]$Path) { return $Path.Substring($Base.TrimEnd('\').Length + 1) }
function Copy-Tree([string]$Source, [string]$Target) {
    if (Test-Path -LiteralPath $Source -PathType Container) {
        New-Item -ItemType Directory -Force -Path $Target | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse -Force) {
            $destination = Join-Path $Target (Get-Relative $Source $file.FullName)
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
    elseif (Test-Path -LiteralPath $Source -PathType Leaf) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Target) | Out-Null
        Copy-Item -LiteralPath $Source -Destination $Target -Force
    }
}
function Get-Manifest([string]$Base) {
    if (-not (Test-Path -LiteralPath $Base)) { return @() }
    return @(Get-ChildItem -LiteralPath $Base -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ path = (Get-Relative $Base $_.FullName); bytes = $_.Length; sha256 = (Get-Sha256 $_.FullName); modified = $_.LastWriteTime.ToString('o') } })
}
function Get-MachineLoad {
    # Win32_Processor.LoadPercentage reads 0 on this kind of machine: use the formatted performance counter.
    $load = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime
    $cores = [Environment]::ProcessorCount
    $top = @(Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object { $_.Name -notin @('_Total', 'Idle') } |
        Sort-Object PercentProcessorTime -Descending | Select-Object -First 8 | ForEach-Object {
            [pscustomobject]@{ name = $_.Name; pid = $_.IDProcess; machinePercent = [Math]::Round($_.PercentProcessorTime / $cores, 1); workingSetMB = [int]($_.WorkingSet / 1MB) } })
    return [pscustomobject]@{ time = (Get-Date).ToString('o'); loadPercent = $load; logicalProcessors = $cores; topProcesses = $top }
}
function Get-GpcSet {
    $names = @('GPCUtilities.dll', 'GPCGeometry.dll', 'DelaunayMesh.dll', 'GPCModel.dll', 'GPCChecker.Concrete.dll', 'GPCChecker.Steel.dll', 'GPCChecker.CompositeBridge.dll')
    $manifest = Get-Content -LiteralPath (Join-Path $root 'lib\GPC\release-set.json') -Raw | ConvertFrom-Json
    $files = foreach ($name in $names) {
        $lib = Join-Path $root "lib\GPC\$name"; $bin = Join-Path $root "bin\Release\$name"; $test = Join-Path (Split-Path -Parent $smoke) $name
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($lib); $hash = Get-Sha256 $lib
        [pscustomobject]@{ file = $name; assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($lib).Version.ToString(); productVersion = $info.ProductVersion; sha256 = $hash
            binReleaseMatches = ((Test-Path -LiteralPath $bin) -and (Get-Sha256 $bin) -eq $hash); smokeOutputMatches = ((Test-Path -LiteralPath $test) -and (Get-Sha256 $test) -eq $hash) }
    }
    return [pscustomobject]@{ candidate = $manifest.candidate; created = $manifest.created; manifestSha256 = (Get-Sha256 (Join-Path $root 'lib\GPC\release-set.json')); files = @($files)
        plugin = @('BridGHe.Grasshopper.gha', 'BridGHe.Common.dll') | ForEach-Object { [pscustomobject]@{ file = $_; sha256 = (Get-Sha256 (Join-Path $root "bin\Release\$_")) } } }
}

$gpc = Get-GpcSet
$gpc | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $phaseRoot 'gpc-set.json') -Encoding UTF8
$summary = New-Object Collections.Generic.List[object]
foreach ($name in $Runs) {
    $definition = $definitions[$name]
    $runRoot = Join-Path $phaseRoot $name
    if (Test-Path -LiteralPath $runRoot) { $runRoot = Join-Path $phaseRoot ($name + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
    $outputs = Join-Path $runRoot 'outputs'; $snapshot = Join-Path $runRoot 'snapshot-before'; $fingerprint = Join-Path $runRoot 'fingerprint'
    New-Item -ItemType Directory -Force -Path $runRoot, $outputs | Out-Null
    foreach ($moved in $definition.Moved) {
        $full = Join-Path $root $moved
        if (Test-Path -LiteralPath $full) { New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'preexisting') | Out-Null; Move-Item -LiteralPath $full -Destination (Join-Path $runRoot "preexisting\$(Split-Path -Leaf $moved)") }
    }
    foreach ($item in $definition.Restore) { Copy-Tree (Join-Path $root $item) (Join-Path $snapshot $item) }
    if ($definition.Candidate) {
        # The modular native runner reads the migrated candidate; use the distributed GHX itself.
        $source = Join-Path $root "example\$($definition.Candidate)"; $candidate = Join-Path $root "artifacts\modular-bridge\candidates\$($definition.Candidate)"
        if (-not (Test-Path -LiteralPath $candidate) -or (Get-Sha256 $candidate) -ne (Get-Sha256 $source)) { Copy-Tree $source $candidate }
    }
    $before = Get-MachineLoad
    $loadFile = Join-Path $runRoot 'machine-load.csv'
    $sampler = Start-Job -ArgumentList $loadFile, $LoadSampleSeconds -ScriptBlock {
        param($file, $seconds)
        while ($true) { $l = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime; Add-Content -LiteralPath $file -Value ((Get-Date).ToString('o') + ';' + $l); Start-Sleep -Seconds $seconds }
    }
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.Arguments = '"' + $smoke + '" ' + (($definition.Args | ForEach-Object { '"' + $_ + '"' }) -join ' ')
    $psi.WorkingDirectory = $root; $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['BRIDGHE_RESULT_DUMP'] = $fingerprint
    Write-Host "RUN $name : dotnet $($psi.Arguments)"
    $clock = [Diagnostics.Stopwatch]::StartNew(); $started = Get-Date
    $process = [Diagnostics.Process]::Start($psi)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $log = New-Object IO.StreamWriter((Join-Path $runRoot 'output.log'), $false, (New-Object Text.UTF8Encoding($false)))
    $lines = New-Object Collections.Generic.List[string]
    try {
        while ($null -ne ($line = $process.StandardOutput.ReadLine())) {
            $log.WriteLine([string]::Format($invariant, '{0,9:F1} {1}', $clock.Elapsed.TotalSeconds, $line)); $log.Flush(); $lines.Add($line)
        }
        $process.WaitForExit()
    }
    finally { $log.Dispose() }
    $clock.Stop()
    $stderr = $stderrTask.Result
    [IO.File]::WriteAllText((Join-Path $runRoot 'stderr.log'), $stderr, (New-Object Text.UTF8Encoding($false)))
    $exitCode = $process.ExitCode; $cpu = $process.TotalProcessorTime.TotalSeconds; $process.Dispose()
    Stop-Job $sampler; Remove-Job $sampler -Force
    $after = Get-MachineLoad
    $samples = @(if (Test-Path -LiteralPath $loadFile) { Get-Content -LiteralPath $loadFile | ForEach-Object { [double]::Parse(($_ -split ';')[1], $invariant) } })

    foreach ($moved in $definition.Moved) { $full = Join-Path $root $moved; if (Test-Path -LiteralPath $full) { Move-Item -LiteralPath $full -Destination (Join-Path $outputs (Split-Path -Leaf $moved)) } }
    foreach ($item in $definition.Restore) {
        $full = Join-Path $root $item; $saved = Join-Path $snapshot $item
        Copy-Tree $full (Join-Path $outputs $item)
        if (Test-Path -LiteralPath $saved -PathType Leaf) { Copy-Item -LiteralPath $saved -Destination $full -Force }
        elseif (Test-Path -LiteralPath $saved -PathType Container) {
            # Files created by the run are moved aside (the copy above keeps them as outputs); originals are restored.
            foreach ($file in @(Get-ChildItem -LiteralPath $full -File -Recurse -Force)) {
                if (-not (Test-Path -LiteralPath (Join-Path $saved (Get-Relative $full $file.FullName)))) {
                    $aside = Join-Path $runRoot ("created-by-run\" + $item + '\' + (Get-Relative $full $file.FullName))
                    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $aside) | Out-Null; Move-Item -LiteralPath $file.FullName -Destination $aside
                }
            }
            foreach ($file in Get-ChildItem -LiteralPath $saved -File -Recurse -Force) {
                $target = Join-Path $full (Get-Relative $saved $file.FullName)
                if (-not (Test-Path -LiteralPath $target) -or (Get-Sha256 $target) -ne (Get-Sha256 $file.FullName)) { Copy-Tree $file.FullName $target }
            }
        }
    }
    $restored = @(foreach ($item in $definition.Restore) {
        $full = Join-Path $root $item; $saved = Join-Path $snapshot $item
        if (Test-Path -LiteralPath $full -PathType Leaf) { $same = (Test-Path -LiteralPath $saved -PathType Leaf) -and (Get-Sha256 $full) -eq (Get-Sha256 $saved) }
        else { $same = ((Get-Manifest $full | ForEach-Object { $_.path + '=' + $_.sha256 }) -join '|') -eq ((Get-Manifest $saved | ForEach-Object { $_.path + '=' + $_.sha256 }) -join '|') }
        [pscustomobject]@{ path = $item; identicalToSnapshot = $same }
    })

    $fingerprints = @(if (Test-Path -LiteralPath $fingerprint) { Get-ChildItem -LiteralPath $fingerprint -Filter '*.summary.json' | ForEach-Object {
        $s = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        [pscustomobject]@{ label = $s.Label; rows = $s.Rows; rowsSha256 = $s.RowsSha256; serializationErrors = $s.SerializationErrors; maxStageUtilization = $s.MaxStageUtilization
            maxCheckRatio = $s.MaxCheckRatio; stressPointsOverOne = $s.StressPointsOverOne; localChecksOverOne = $s.LocalChecksOverOne; loadedAssemblies = $s.LoadedAssemblies } } })
    $validations = @(Get-ChildItem -LiteralPath $outputs -Recurse -File -Filter '*validation.json' -ErrorAction SilentlyContinue | ForEach-Object {
        $v = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json; $scalars = [ordered]@{}
        foreach ($p in $v.PSObject.Properties) { if ($p.Value -is [ValueType] -or $p.Value -is [string]) { $scalars[$p.Name] = $p.Value } }
        [pscustomobject]@{ file = (Get-Relative $outputs $_.FullName); values = [pscustomobject]$scalars } })
    $manifest = @(Get-Manifest $outputs) + @(Get-Manifest $fingerprint | ForEach-Object { $_.path = 'fingerprint\' + $_.path; $_ })
    ConvertTo-Json -InputObject @($manifest) -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'hashes.json') -Encoding UTF8
    $passLines = @($lines | Where-Object { $_ -match '\bPASS\b' })
    $failLines = @($lines | Where-Object { $_ -match '\bFAIL\b|Exception' }) + @($stderr -split "`r?`n" | Where-Object { $_ -match '\S' } | Select-Object -First 40)
    $result = [pscustomobject]@{
        run = $name; command = "dotnet $($psi.Arguments)"; workingDirectory = $root; started = $started.ToString('o'); exitCode = $exitCode
        seconds = [Math]::Round($clock.Elapsed.TotalSeconds, 1); processCpuSeconds = [Math]::Round($cpu, 1)
        machineLoadPercent = [pscustomobject]@{ before = $before.loadPercent; after = $after.loadPercent; samples = $samples.Count
            average = $(if ($samples.Count) { [Math]::Round(($samples | Measure-Object -Average).Average, 1) } else { $null }); maximum = $(if ($samples.Count) { ($samples | Measure-Object -Maximum).Maximum } else { $null }) }
        topProcessesBefore = $before.topProcesses; passLines = $passLines.Count; lastPassLines = @($passLines | Select-Object -Last 6); failLines = $failLines
        keyLines = @($lines | Where-Object { $_ -notmatch '^PASS: ' } | Select-Object -Last 60); fingerprints = $fingerprints; validations = $validations; restored = $restored
        files = $manifest.Count
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'run.json') -Encoding UTF8
    $summary.Add($result)
    Write-Host ([string]::Format($invariant, 'DONE {0}: exit {1}, {2:F1} s, CPU {3:F1} s, load avg {4}%, {5} PASS lines, fingerprints {6}', $name, $exitCode, $clock.Elapsed.TotalSeconds, $cpu, $result.machineLoadPercent.average, $passLines.Count, (($fingerprints | ForEach-Object { "$($_.label)=$($_.rows) rows $($_.rowsSha256.Substring(0, 16))" }) -join '; ')))
}
$all = @(Get-ChildItem -LiteralPath $phaseRoot -Directory | ForEach-Object { $r = Join-Path $_.FullName 'run.json'; if (Test-Path -LiteralPath $r) { Get-Content -LiteralPath $r -Raw | ConvertFrom-Json } })
[pscustomobject]@{ phase = $Phase; updated = (Get-Date).ToString('o'); gpc = $gpc; runs = $all } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $phaseRoot 'summary.json') -Encoding UTF8
$hashLines = foreach ($dir in Get-ChildItem -LiteralPath $phaseRoot -Directory) { $h = Join-Path $dir.FullName 'hashes.json'; if (Test-Path -LiteralPath $h) { (Get-Content -LiteralPath $h -Raw | ConvertFrom-Json) | ForEach-Object { $_.sha256 + '  ' + $dir.Name + '\' + $_.path } } }
[IO.File]::WriteAllLines((Join-Path $phaseRoot 'hashes.sha256'), [string[]]@($hashLines), (New-Object Text.UTF8Encoding($false)))
if (@($summary | Where-Object { $_.exitCode -ne 0 }).Count -gt 0) { exit 1 }
exit 0
