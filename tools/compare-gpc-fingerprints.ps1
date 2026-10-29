<#
.SYNOPSIS
Compares two phases written by tools/prove-gpc-dominio.ps1 (for example baseline and candidate gPC sets).

.DESCRIPTION
For each run: exit codes, durations, PASS lines, validation values, SHA-256 of produced files and, row by row,
the numerical fingerprints of all gPC section results. Writes a Markdown and a JSON report next to the candidate
phase. Exit code 0 only when every run has the same exit code and every fingerprint row is bit-identical.
Phases are folder names under artifacts/prove-gpc-dominio, or full paths.
#>
param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Candidate,
    [string]$Output,
    [int]$MaxListed = 20
)
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function Resolve-Phase([string]$Name) {
    if (Test-Path -LiteralPath $Name -PathType Container) { return [IO.Path]::GetFullPath($Name) }
    $path = Join-Path $root "artifacts\prove-gpc-dominio\$Name"
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Fase non trovata: $Name" }
    return $path
}
function Get-Runs([string]$Phase) {
    $runs = @{}
    foreach ($dir in Get-ChildItem -LiteralPath $Phase -Directory) {
        $file = Join-Path $dir.FullName 'run.json'; if (-not (Test-Path -LiteralPath $file)) { continue }
        $run = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        $run | Add-Member -NotePropertyName folder -NotePropertyValue $dir.FullName
        # A repeated run gets a timestamped folder: compare the latest one.
        if (-not $runs.ContainsKey($run.run) -or $runs[$run.run].started -lt $run.started) { $runs[$run.run] = $run }
    }
    return $runs
}
function Read-Rows([string]$Path) {
    $rows = @{}
    $lines = [IO.File]::ReadAllLines($Path)
    for ($i = 1; $i -lt $lines.Length; $i++) {
        $f = $lines[$i].Split("`t"); if ($f.Length -lt 14) { continue }
        $rows[($f[0..4] -join "`t")] = [pscustomobject]@{ source = $f[5]; stage = $f[6]; maxUtilization = $f[7]; checkRatio = $f[11]; input = $f[12]; sha = $f[13] }
    }
    return $rows
}
function Read-Hashes([string]$Folder) { $h = Join-Path $Folder 'hashes.json'; if (-not (Test-Path -LiteralPath $h)) { return @() }; $text = Get-Content -LiteralPath $h -Raw; if (-not $text) { return @() }; $parsed = $text | ConvertFrom-Json; return @($parsed | ForEach-Object { $_ }) }
function Get-Number([string]$Text) { $v = 0.0; if ([double]::TryParse($Text, [Globalization.NumberStyles]::Float, $invariant, [ref]$v)) { return $v }; return [double]::NaN }
function Format-Ratio($a, $b) { if ($a -and $b -and [double]$a -gt 0) { return [string]::Format($invariant, '{0:F2}', [double]$b / [double]$a) }; return 'n/d' }
function Get-Phases($summary) {
    # Duration between consecutive TIMING marks, labelled by the mark that opens the interval.
    $marks = @($summary.Marks); $result = [ordered]@{}
    for ($i = 0; $i -lt $marks.Count - 1; $i++) { $result[$marks[$i].Label + ' -> ' + $marks[$i + 1].Label] = [double]$marks[$i + 1].Seconds - [double]$marks[$i].Seconds }
    return $result
}

$basePath = Resolve-Phase $Baseline; $candPath = Resolve-Phase $Candidate
if (-not $Output) { $Output = Join-Path $candPath ("comparison-vs-" + (Split-Path -Leaf $basePath) + '.md') }
$baseRuns = Get-Runs $basePath; $candRuns = Get-Runs $candPath
$names = @($baseRuns.Keys + $candRuns.Keys | Sort-Object -Unique)
$md = New-Object Collections.Generic.List[string]; $report = New-Object Collections.Generic.List[object]; $ok = $true
$md.Add("# Confronto gPC: $(Split-Path -Leaf $basePath) -> $(Split-Path -Leaf $candPath)"); $md.Add('')
foreach ($set in @(@('Baseline', $basePath), @('Candidato', $candPath))) {
    $g = Join-Path $set[1] 'gpc-set.json'
    if (Test-Path -LiteralPath $g) { $gpc = Get-Content -LiteralPath $g -Raw | ConvertFrom-Json; $md.Add("**$($set[0])** (candidate=$($gpc.candidate)): " + (($gpc.files | ForEach-Object { "$($_.file) $($_.assemblyVersion) ``$($_.sha256.Substring(0, 12))``" }) -join ', ')); $md.Add('') }
}
$md.Add('| Run | Exit B/C | Durata B/C [s] | Rapporto | CPU B/C [s] | Carico medio B/C [%] | PASS B/C | Righe identiche | File identici |')
$md.Add('|---|---|---|---|---|---|---|---|---|')
$details = New-Object Collections.Generic.List[string]
foreach ($name in $names) {
    $b = $baseRuns[$name]; $c = $candRuns[$name]
    if (-not $b -or -not $c) { $md.Add("| $name | $(if ($b) { $b.exitCode } else { '-' })/$(if ($c) { $c.exitCode } else { '-' }) | | | | | | manca in una fase | |"); $ok = $false; continue }
    if ($b.exitCode -ne $c.exitCode) { $ok = $false }
    # Fingerprint rows.
    $rowSummary = @(); $identical = 0; $total = 0
    foreach ($tsv in @(Get-ChildItem -LiteralPath (Join-Path $b.folder 'fingerprint') -Filter '*.rows.tsv' -ErrorAction SilentlyContinue)) {
        $other = Join-Path (Join-Path $c.folder 'fingerprint') $tsv.Name
        if (-not (Test-Path -LiteralPath $other)) { $rowSummary += [pscustomobject]@{ file = $tsv.Name; missing = $true }; $ok = $false; continue }
        $br = Read-Rows $tsv.FullName; $cr = Read-Rows $other
        $diff = New-Object Collections.Generic.List[object]; $same = 0; $maxUtil = 0.0; $maxRatio = 0.0; $inputDiff = 0
        foreach ($key in $br.Keys) {
            $total++
            if (-not $cr.ContainsKey($key)) { $diff.Add([pscustomobject]@{ key = $key; change = 'solo baseline' }); continue }
            $x = $br[$key]; $y = $cr[$key]
            if ($x.sha -eq $y.sha) { $same++; $identical++; continue }
            if ($x.input -ne $y.input) { $inputDiff++ }
            $du = [Math]::Abs((Get-Number $y.maxUtilization) - (Get-Number $x.maxUtilization)); if ($du -gt $maxUtil) { $maxUtil = $du }
            $dr = [Math]::Abs((Get-Number $y.checkRatio) - (Get-Number $x.checkRatio)); if ($dr -gt $maxRatio) { $maxRatio = $dr }
            $diff.Add([pscustomobject]@{ key = $key; source = $x.source; stage = $x.stage; maxUtilization = "$($x.maxUtilization) -> $($y.maxUtilization)"; checkRatio = "$($x.checkRatio) -> $($y.checkRatio)"; inputChanged = ($x.input -ne $y.input) })
        }
        foreach ($key in $cr.Keys) { if (-not $br.ContainsKey($key)) { $total++; $diff.Add([pscustomobject]@{ key = $key; change = 'solo candidato' }) } }
        if ($diff.Count) { $ok = $false }
        $rowSummary += [pscustomobject]@{ file = $tsv.Name; baselineRows = $br.Count; candidateRows = $cr.Count; identical = $same; different = $diff.Count; inputsChanged = $inputDiff
            maxAbsUtilizationChange = $maxUtil; maxAbsCheckRatioChange = $maxRatio; firstDifferences = @($diff | Select-Object -First $MaxListed) }
    }
    # Produced files (fingerprints excluded: compared above).
    $bh = @{}; $ch = @{}
    foreach ($e in @(Read-Hashes $b.folder)) { if ($e.path -notlike 'fingerprint\*') { $bh[$e.path] = $e.sha256 } }
    foreach ($e in @(Read-Hashes $c.folder)) { if ($e.path -notlike 'fingerprint\*') { $ch[$e.path] = $e.sha256 } }
    $fileDiff = @(@($bh.Keys + $ch.Keys | Sort-Object -Unique) | Where-Object { $bh[$_] -ne $ch[$_] } | ForEach-Object {
        [pscustomobject]@{ path = $_; change = $(if (-not $bh.ContainsKey($_)) { 'solo candidato' } elseif (-not $ch.ContainsKey($_)) { 'solo baseline' } else { 'diverso' }) } })
    $sameFiles = @($bh.Keys | Where-Object { $ch.ContainsKey($_) -and $bh[$_] -eq $ch[$_] }).Count
    # Validation scalars.
    $validation = @()
    foreach ($v in @($b.validations)) {
        $w = @($c.validations | Where-Object { $_.file -eq $v.file }) | Select-Object -First 1
        foreach ($p in $v.values.PSObject.Properties) {
            $other = if ($w) { $w.values.($p.Name) } else { $null }
            if ("$($p.Value)" -ne "$other") { $validation += [pscustomobject]@{ file = $v.file; field = $p.Name; baseline = $p.Value; candidate = $other } }
        }
    }
    if ($validation.Count) { $ok = $false }
    # gPC profiler times and phase intervals.
    $times = @(); $cacheLines = @()
    foreach ($bs in @(Get-ChildItem -LiteralPath (Join-Path $b.folder 'fingerprint') -Filter '*.summary.json' -ErrorAction SilentlyContinue)) {
        $cs = Join-Path (Join-Path $c.folder 'fingerprint') $bs.Name; if (-not (Test-Path -LiteralPath $cs)) { continue }
        $x = Get-Content -LiteralPath $bs.FullName -Raw | ConvertFrom-Json; $y = Get-Content -LiteralPath $cs -Raw | ConvertFrom-Json
        foreach ($t in @($x.ProcessorTimes | Select-Object -First 12)) {
            $u = @($y.ProcessorTimes | Where-Object { $_.Id -eq $t.Id }) | Select-Object -First 1
            $times += [pscustomobject]@{ summary = $bs.Name; component = "$($t.Type) / $($t.NickName)"; baselineSeconds = [Math]::Round($t.Milliseconds / 1000, 2); candidateSeconds = $(if ($u) { [Math]::Round($u.Milliseconds / 1000, 2) } else { $null }) }
        }
        foreach ($side in @(@('baseline', $x), @('candidato', $y))) {
            $cache = $side[1].DomainCache
            if ($cache) { $cacheLines += ("- Cache dei domini GPC ($($side[0]), $($bs.Name)): capacita $($cache.Capacity), voci $($cache.Count), hit $($cache.Hits), miss $($cache.Misses), domini calcolati $($cache.Computations), mesh $($cache.MeshBuilds)") }
        }
        $px = Get-Phases $x; $py = Get-Phases $y
        foreach ($k in $px.Keys) { $times += [pscustomobject]@{ summary = $bs.Name; component = "intervallo: $k"; baselineSeconds = [Math]::Round($px[$k], 1); candidateSeconds = $(if ($py.Contains($k)) { [Math]::Round($py[$k], 1) } else { $null }) } }
    }
    $rowsText = if ($total) { "$identical/$total" } else { 'n/d' }
    $md.Add("| $name | $($b.exitCode)/$($c.exitCode) | $($b.seconds)/$($c.seconds) | $(Format-Ratio $b.seconds $c.seconds) | $($b.processCpuSeconds)/$($c.processCpuSeconds) | $($b.machineLoadPercent.average)/$($c.machineLoadPercent.average) | $($b.passLines)/$($c.passLines) | $rowsText | $sameFiles/$($bh.Count) |")
    $details.Add(''); $details.Add("## $name"); $details.Add('')
    foreach ($r in $rowSummary) {
        if ($r.missing) { $details.Add("- $($r.file): manca nel candidato"); continue }
        $details.Add("- $($r.file): $($r.identical) righe identiche su $($r.baselineRows) (candidato $($r.candidateRows)); diverse $($r.different), con input diversi $($r.inputsChanged); max |delta| utilizzo $($r.maxAbsUtilizationChange), rapporto verifiche $($r.maxAbsCheckRatioChange)")
        foreach ($d in $r.firstDifferences) { $details.Add("  - ``$($d.key -replace "`t", ' | ')`` $($d.change)$($d.source) $($d.stage) util $($d.maxUtilization) ratio $($d.checkRatio)") }
    }
    foreach ($line in $cacheLines) { $details.Add($line) }
    if ($validation.Count) { $details.Add('- Valori di validazione diversi:'); foreach ($v in $validation) { $details.Add("  - $($v.file) $($v.field): $($v.baseline) -> $($v.candidate)") } }
    # Straus files embed solution time, CPU time and free scratch space: they differ between identical runs.
    $volatile = @($fileDiff | Where-Object { $_.change -eq 'diverso' -and $_.path -match '\.(st7|lsa|lsl)$' })
    $relevant = @($fileDiff | Where-Object { $volatile -notcontains $_ })
    if ($volatile.Count) { $details.Add("- File Straus (.st7/.LSA/.LSL) con hash diverso: $($volatile.Count); attesi, contengono data e tempi della soluzione") }
    if ($relevant.Count) { $details.Add("- Altri file con hash diverso o presenti in una sola fase ($($relevant.Count)):"); foreach ($f in @($relevant | Select-Object -First 60)) { $details.Add("  - $($f.path): $($f.change)") } }
    if ($times.Count) {
        $details.Add(''); $details.Add('| Tempo | Baseline [s] | Candidato [s] | Rapporto |'); $details.Add('|---|---|---|---|')
        foreach ($t in $times) { $details.Add("| $($t.component) | $($t.baselineSeconds) | $($t.candidateSeconds) | $(Format-Ratio $t.baselineSeconds $t.candidateSeconds) |") }
    }
    $report.Add([pscustomobject]@{ run = $name; baseline = $b.folder; candidate = $c.folder; exitCodes = @($b.exitCode, $c.exitCode); seconds = @($b.seconds, $c.seconds)
        cpuSeconds = @($b.processCpuSeconds, $c.processCpuSeconds); passLines = @($b.passLines, $c.passLines); fingerprints = $rowSummary; validationDifferences = $validation
        files = [pscustomobject]@{ identical = $sameFiles; baseline = $bh.Count; candidate = $ch.Count; differences = $fileDiff }; times = $times })
}
$md.Add(''); $md.Add("Esito: $(if ($ok) { 'tutti i risultati gPC identici bit per bit e stessi exit code' } else { 'DIFFERENZE o run mancanti: vedere i dettagli' })")
foreach ($line in $details) { $md.Add($line) }
[IO.File]::WriteAllLines($Output, [string[]]$md, (New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{ baseline = $basePath; candidate = $candPath; identical = $ok; runs = $report } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($Output, '.json')) -Encoding UTF8
Write-Host "Confronto scritto in $Output"
if (-not $ok) { exit 1 }
exit 0
