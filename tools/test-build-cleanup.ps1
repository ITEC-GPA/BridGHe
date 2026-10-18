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
    $legacy = @('src\A\bin', 'src\A\obj', 'src\nested\B\bin', 'tests\T\bin', 'tests\T\obj', 'obj',
        'artifacts\packages', 'artifacts\review\bin', 'artifacts\review\obj', 'scratch\probe\bin')
    foreach ($path in $legacy) { Write-Marker (Join-Path $repository "$path\old.dll") }
    $preserved = @('src\A\source.cs', 'build\references.props', 'bin\A\Release\new.dll', 'bin\obj\A\project.assets.json',
        'bin\packages\current.zip', 'artifacts\bridge-suite\model.st7', 'examples\bridge\model.ghx', '.git\objects\bin\data', '.codex\cache\bin\data')
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
