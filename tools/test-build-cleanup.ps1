$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\build-cleanup-tests'))
$fixture = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$repository = Join-Path $fixture 'project with spaces'
$cleaner = Join-Path $repository 'tools\clean-builds.ps1'
$junctions = @()
$checks = 0

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:checks++
    Write-Host "PASS: $Message"
}
function Write-Marker([string]$Path) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    Set-Content -LiteralPath $Path -Value 'preserve or remove as specified by test' -Encoding utf8
}

try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $cleaner) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'clean-builds.ps1') -Destination $cleaner
    $legacy = @('src\A\bin', 'src\A\obj', 'src\nested\B\bin', 'tests\T\bin', 'tests\T\obj',
        'artifacts\packages', 'artifacts\review\bin', 'artifacts\review\obj', 'scratch\probe\bin',
        'bin\BridGHe.Grasshopper', 'bin\BridGHe.Common', 'bin\BridGHe.Smoke', 'bin\obj', 'bin\Debug', 'bin\packages')
    foreach ($path in $legacy) { Write-Marker (Join-Path $repository "$path\old.dll") }
    $preserved = @('src\A\source.cs', 'build\references.props', 'bin\Release\new.dll', 'obj\A\project.assets.json',
        'obj\bin\A\Release\new.dll', 'artifacts\releases\current.zip', 'artifacts\bridge-suite\model.st7',
        'examples\bridge\model.ghx', '.git\objects\bin\data', '.codex\cache\bin\data')
    foreach ($path in $preserved) { Write-Marker (Join-Path $repository $path) }
    $externalMarker = Join-Path $fixture 'Checker\bin\gpc.dll'
    Write-Marker $externalMarker

    & $cleaner -WhatIf
    Require (@($legacy | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repository $_)) }).Count -eq 0) 'WhatIf preserves every legacy output'
    & $cleaner
    Require (@($legacy | Where-Object { Test-Path -LiteralPath (Join-Path $repository $_) }).Count -eq 0) 'all legacy outputs removed, including nested projects and packages'
    Require (@($preserved | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repository $_)) }).Count -eq 0) 'current bin, sources, native results and examples preserved'
    Require (Test-Path -LiteralPath $externalMarker) 'sibling gPC builds preserved'
    & $cleaner
    Require ($true) 'cleanup is idempotent'

    # Publication mirrors the complete compiler output, including nested runtime assets.
    $source = Join-Path $repository 'obj\bin\BridGHe.Grasshopper\Release\net8.0-windows'
    $release = Join-Path $repository 'bin\Release'
    $plugin = Join-Path $source 'BridGHe.Grasshopper.gha'
    Write-Marker $plugin
    Write-Marker (Join-Path $source 'GPCModel.dll')
    Write-Marker (Join-Path $source 'runtimes\win-x64\native.dll')
    Write-Marker (Join-Path $release 'obsolete\unused.dll')
    & $cleaner -ReleaseSource $source -WhatIf
    Require ((Test-Path -LiteralPath (Join-Path $release 'obsolete\unused.dll')) -and
        -not (Test-Path -LiteralPath (Join-Path $release 'BridGHe.Grasshopper.gha'))) 'WhatIf neither publishes nor prunes Release'
    & $cleaner -ReleaseSource $source
    $sourceFiles = @(Get-ChildItem -LiteralPath $source -File -Recurse)
    $mismatches = @($sourceFiles | Where-Object {
        $target = Join-Path $release $_.FullName.Substring($source.Length + 1)
        -not (Test-Path -LiteralPath $target) -or
            (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash
    })
    Require ($mismatches.Count -eq 0 -and @(Get-ChildItem -LiteralPath $release -File -Recurse).Count -eq $sourceFiles.Count) 'Release contains exactly the compiler files with identical SHA256 hashes'
    Require (-not (Test-Path -LiteralPath (Join-Path $release 'obsolete'))) 'obsolete dependency directories removed'
    Require (@(Get-ChildItem -LiteralPath (Join-Path $repository 'bin')).Count -eq 1) 'only Release remains in root bin'
    $publishedPlugin = Join-Path $release 'BridGHe.Grasshopper.gha'
    (Get-Item -LiteralPath $publishedPlugin).LastWriteTimeUtc = [datetime]'2020-01-01'
    & $cleaner -ReleaseSource $source
    Require ((Get-Item -LiteralPath $publishedPlugin).LastWriteTimeUtc -eq [datetime]'2020-01-01') 'identical outputs are not rewritten on incremental builds'
    Set-Content -LiteralPath $plugin -Value 'updated plugin'
    & $cleaner -ReleaseSource $source
    Require ((Get-FileHash -LiteralPath $plugin).Hash -eq (Get-FileHash -LiteralPath $publishedPlugin).Hash) 'changed compiler output replaces the published plugin'

    Write-Marker (Join-Path $source 'file-to-directory\asset.dll')
    Write-Marker (Join-Path $release 'file-to-directory')
    Write-Marker (Join-Path $source 'directory-to-file')
    Write-Marker (Join-Path $release 'directory-to-file\obsolete.dll')
    & $cleaner -ReleaseSource $source
    Require ((Test-Path -LiteralPath (Join-Path $release 'file-to-directory\asset.dll') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $release 'directory-to-file') -PathType Leaf)) 'publication handles file/directory replacements'

    $safeMarker = Join-Path $repository 'src\A\bin\old.dll'
    Write-Marker $safeMarker
    $publishedHash = (Get-FileHash -LiteralPath $publishedPlugin).Hash
    $lockedFile = [IO.File]::Open($safeMarker, 'Open', 'Read', 'Read')
    try {
        $rejected = $false
        try { & $cleaner -ReleaseSource $source } catch { $rejected = $_.Exception.Message -like '*file in uso o non accessibile*' }
        Require ($rejected -and (Test-Path -LiteralPath $safeMarker) -and
            (Get-FileHash -LiteralPath $publishedPlugin).Hash -eq $publishedHash) 'locked legacy DLL fails before publication or partial deletion'
    }
    finally { $lockedFile.Dispose() }

    $ready = Join-Path $repository 'transient-lock-ready.txt'
    $reader = Start-Job -ScriptBlock {
        param($Path, $Ready)
        $stream = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
        try { Set-Content -LiteralPath $Ready -Value 'ready'; Start-Sleep -Milliseconds 600 }
        finally { $stream.Dispose() }
    } -ArgumentList $safeMarker, $ready
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not (Test-Path -LiteralPath $ready)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Timeout del lettore temporaneo di test.' }
            Start-Sleep -Milliseconds 20
        }
        & $cleaner -ReleaseSource $source
        Require (-not (Test-Path -LiteralPath $safeMarker)) 'transient sharing lock is retried and cleanup completes'
    }
    finally { Stop-Job -Job $reader; Remove-Job -Job $reader }
    Write-Marker $safeMarker
    $rejected = $false
    try { & $cleaner -ReleaseSource (Join-Path $repository 'obj\missing') } catch { $rejected = $_.Exception.Message -like '*Output Release non valido*' }
    Require ($rejected -and (Test-Path -LiteralPath $safeMarker) -and
        (Get-FileHash -LiteralPath $publishedPlugin).Hash -eq $publishedHash) 'missing output preserves the previous Release and defers cleanup'
    $rejected = $false
    try { & $cleaner -ReleaseSource (Split-Path -Parent $externalMarker) } catch { $rejected = $_.Exception.Message -like '*esterno al progetto*' }
    Require ($rejected -and (Test-Path -LiteralPath $externalMarker)) 'external publication source rejected'

    foreach ($parent in @($source, $release)) {
        $link = Join-Path $parent 'linked-runtime'
        New-Item -ItemType Junction -Path $link -Target (Split-Path -Parent $externalMarker) | Out-Null
        $junctions += $link
        $rejected = $false
        try { & $cleaner -ReleaseSource $source } catch { $rejected = $_.Exception.Message -like '*link o junction*' }
        Require ($rejected -and (Test-Path -LiteralPath $safeMarker) -and (Test-Path -LiteralPath $externalMarker)) 'publication junction rejected before copying or deleting'
        [IO.Directory]::Delete($link)
        $junctions = @()
    }

    # No external target may be traversed, even inside a candidate output tree.
    $safeMarker = Join-Path $repository 'src\A\bin\old.dll'
    Write-Marker $safeMarker
    $link = Join-Path $repository 'tests\T\obj'
    New-Item -ItemType Junction -Path $link -Target (Split-Path -Parent $externalMarker) | Out-Null
    $junctions += $link
    $rejected = $false
    try { & $cleaner } catch { $rejected = $_.Exception.Message -like '*link o junction*' }
    Require ($rejected -and (Test-Path -LiteralPath $externalMarker) -and (Test-Path -LiteralPath $safeMarker)) 'output junction rejected before any deletion'
    [IO.Directory]::Delete($link)
    $junctions = @()

    $link = Join-Path $repository 'src\A\bin\external'
    New-Item -ItemType Junction -Path $link -Target (Split-Path -Parent $externalMarker) | Out-Null
    $junctions += $link
    $rejected = $false
    try { & $cleaner } catch { $rejected = $_.Exception.Message -like '*link o junction*' }
    Require ($rejected -and (Test-Path -LiteralPath $externalMarker) -and (Test-Path -LiteralPath $safeMarker)) 'junction inside an output tree rejected before any deletion'
    [IO.Directory]::Delete($link)
    $junctions = @()

    $link = Join-Path $repository 'src\ExternalProject'
    New-Item -ItemType Junction -Path $link -Target (Split-Path -Parent (Split-Path -Parent $externalMarker)) | Out-Null
    $junctions += $link
    & $cleaner
    Require ((Test-Path -LiteralPath $externalMarker) -and -not (Test-Path -LiteralPath $safeMarker)) 'linked source directories are not traversed'
    Write-Host "BUILD CLEANUP TOTAL $checks"
}
finally {
    # Remove junctions themselves without recursion before removing the fixture.
    foreach ($link in $junctions) {
        if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    }
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($fixtureParent + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Percorso del fixture non valido: $resolvedFixture"
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
