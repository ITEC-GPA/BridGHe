[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # Passed only by the plugin's successful Release post-build event.
    [string]$ReleaseSource
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\', '/')
$rootPrefix = $projectRoot + [IO.Path]::DirectorySeparatorChar
$cacheRoot = Join-Path $projectRoot 'obj'
$distributionRoot = Join-Path $projectRoot 'bin'
$releaseRoot = Join-Path $distributionRoot 'Release'

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
        if ($child.FullName -eq $cacheRoot -or $child.Name -in @('.git', '.vs', '.agents', '.codex', '.aws')) { continue }
        if ($child.FullName -eq $distributionRoot) {
            Assert-LocalPath $child.FullName
            # The root bin is a distribution folder, not a project/configuration cache.
            Get-ChildItem -LiteralPath $child.FullName -Force | Where-Object { $_.FullName -ne $releaseRoot } | ForEach-Object { $_.FullName }
            continue
        }
        if ($child.Name -in @('bin', 'obj') -or $child.FullName -eq (Join-Path $projectRoot 'artifacts\packages')) { $child.FullName }
        elseif (-not ($child.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Find-LegacyOutputs $child.FullName
        }
    }
}

function Assert-NoLinkedContents([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return }
    foreach ($child in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Pulizia interrotta: link o junction nell'output $($child.FullName)"
        }
        if ($child.PSIsContainer) { Assert-NoLinkedContents $child.FullName }
    }
}

function Assert-OutputUnlocked([string]$Path) {
    # Detect DLLs loaded by Rhino before partially deleting any old output tree.
    foreach ($file in Get-ChildItem -LiteralPath $Path -File -Recurse -Force) {
        for ($attempt = 0; $attempt -lt 6; $attempt++) {
            $stream = $null
            try {
                $stream = [IO.File]::Open($file.FullName, 'Open', 'ReadWrite', 'None')
                break
            }
            catch {
                $cause = $_.Exception.GetBaseException()
                # Sharing/lock violations can be brief (e.g. readers of freshly built PDBs).
                # Retry for at most one second; permanent locks still fail before deletion.
                if (($cause -is [IO.IOException]) -and (($cause.HResult -band 0xffff) -in @(32, 33)) -and $attempt -lt 5) {
                    Start-Sleep -Milliseconds 200
                    continue
                }
                throw "Post-build: file in uso o non accessibile: $($file.FullName). Salvare il lavoro e chiudere Rhino e gli altri processi che usano queste DLL, quindi ripetere la build. Nessuna cartella precedente e' stata eliminata. Dettaglio: $($cause.Message)"
            }
            finally { if ($stream) { $stream.Dispose() } }
        }
    }
}

function Get-ContentDigest([string]$Path) {
    # MSBuild workers can inherit a PSModulePath that omits Get-FileHash.
    # Use the runtime directly so publication also works from those IDE sessions.
    $stream = [IO.File]::OpenRead($Path)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)) }
    finally { $hasher.Dispose(); $stream.Dispose() }
}

function Sync-Release([string]$Source) {
    $sourcePrefix = $Source + [IO.Path]::DirectorySeparatorChar
    $files = @{}
    $directories = @{}
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Recurse -Force) {
        $relative = $item.FullName.Substring($sourcePrefix.Length)
        $target = Join-Path $releaseRoot $relative
        if ($item.PSIsContainer) {
            $directories[$relative] = $true
            if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
            New-Item -ItemType Directory -Path $target -Force | Out-Null
        }
        else {
            $files[$relative] = $true
            if (Test-Path -LiteralPath $target -PathType Container) { Remove-Item -LiteralPath $target -Recurse -Force }
            # Do not rewrite identical DLLs (incremental builds / Rhino may have them loaded).
            $same = (Test-Path -LiteralPath $target -PathType Leaf) -and
                (Get-Item -LiteralPath $target).Length -eq $item.Length -and
                (Get-ContentDigest $target) -eq (Get-ContentDigest $item.FullName)
            if (-not $same) { Copy-Item -LiteralPath $item.FullName -Destination $target -Force }
        }
    }
    # Prune obsolete dependencies only after all new files have been copied.
    $destinationPrefix = $releaseRoot + [IO.Path]::DirectorySeparatorChar
    foreach ($item in Get-ChildItem -LiteralPath $releaseRoot -File -Recurse -Force) {
        if (-not $files.ContainsKey($item.FullName.Substring($destinationPrefix.Length))) {
            Remove-Item -LiteralPath $item.FullName -Force
        }
    }
    foreach ($item in Get-ChildItem -LiteralPath $releaseRoot -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending) {
        if (-not $directories.ContainsKey($item.FullName.Substring($destinationPrefix.Length))) {
            Remove-Item -LiteralPath $item.FullName -Recurse -Force
        }
    }
    Write-Host "Post-build: Release sincronizzata in $releaseRoot ($($files.Count) file)."
}

# Project builds can overlap. Serialize publication and cleanup; never remove
# the central obj cache, because another project may still be compiling there.
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
    # Validate all sources and destinations before copying or deleting anything.
    foreach ($path in $candidates) {
        Assert-LocalPath $path
        Assert-NoLinkedContents $path
    }
    if ($ReleaseSource) {
        $source = [IO.Path]::GetFullPath($ReleaseSource).TrimEnd('\', '/')
        Assert-LocalPath $source
        if (-not $source.StartsWith($cacheRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath (Join-Path $source 'BridGHe.Grasshopper.gha') -PathType Leaf)) {
            throw "Output Release non valido: atteso il plugin compilato nella cache $cacheRoot. Sorgente: $source"
        }
        Assert-NoLinkedContents $source
        Assert-LocalPath $releaseRoot
        Assert-NoLinkedContents $releaseRoot
    }
    foreach ($path in $candidates) { Assert-OutputUnlocked $path }
    if ($ReleaseSource) {
        if ($PSCmdlet.ShouldProcess($releaseRoot, 'Sincronizza output Release dopo la compilazione')) {
            Sync-Release $source
        }
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
