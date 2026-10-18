[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\', '/')
$rootPrefix = $projectRoot + [IO.Path]::DirectorySeparatorChar

function Assert-LocalPath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Percorso di pulizia esterno al progetto: $fullPath"
    }
    # Check ancestors as well: a junction could otherwise redirect a local path.
    for ($current = $fullPath; $current -ne $projectRoot; $current = Split-Path -Parent $current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Pulizia interrotta: link o junction nel percorso $current"
        }
    }
}

function Find-LegacyOutputs([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return }
    if ($Directory -ne $projectRoot) { Assert-LocalPath $Directory }
    foreach ($child in Get-ChildItem -LiteralPath $Directory -Directory -Force) {
        if ($child.FullName -eq (Join-Path $projectRoot 'bin') -or $child.Name -in @('.git', '.vs', '.agents', '.codex', '.aws')) { continue }
        if ($child.Name -in @('bin', 'obj') -or $child.FullName -eq (Join-Path $projectRoot 'artifacts\packages')) { $child.FullName }
        elseif (-not ($child.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Find-LegacyOutputs $child.FullName
        }
    }
}

function Assert-NoLinkedContents([string]$Directory) {
    foreach ($child in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Pulizia interrotta: link o junction nell'output $($child.FullName)"
        }
        if ($child.PSIsContainer) { Assert-NoLinkedContents $child.FullName }
    }
}

# MSBuild can start this target in several projects concurrently. Serialize only
# legacy cleanup; current outputs under the root bin directory are never touched.
$hasher = [Security.Cryptography.SHA256]::Create()
try { $key = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($projectRoot.ToUpperInvariant()))).Replace('-', '') }
finally { $hasher.Dispose() }
$mutex = New-Object Threading.Mutex($false, "Local\BridGHe.BuildCleanup.$key")
$locked = $false
try {
    try { $locked = $mutex.WaitOne(30000) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Timeout: un altro processo sta pulendo gli output precedenti.' }

    $candidates = @(Find-LegacyOutputs $projectRoot)
    # Validate every target and its contents before deleting any directory.
    foreach ($path in $candidates) {
        Assert-LocalPath $path
        Assert-NoLinkedContents $path
    }
    foreach ($path in $candidates) {
        if ($PSCmdlet.ShouldProcess($path, 'Elimina output di compilazione precedente')) {
            Remove-Item -LiteralPath $path -Recurse -Force
            Write-Host "Output precedente eliminato: $path"
        }
    }
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
