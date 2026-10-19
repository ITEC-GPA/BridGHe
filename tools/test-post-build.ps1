$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\post-build-tests'))
$fixture = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N') + ' with spaces')
$checks = 0
$buildNumber = 0

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:checks++
    Write-Host "PASS: $Message"
}

function Run-Build([string[]]$BuildArguments, [bool]$ExpectSuccess = $true) {
    $script:buildNumber++
    $log = Join-Path $fixture "build-$buildNumber.log"
    & dotnet @BuildArguments *> $log
    $succeeded = $LASTEXITCODE -eq 0
    if ($succeeded -ne $ExpectSuccess) {
        Get-Content -LiteralPath $log | Write-Host
        throw "Esito MSBuild inatteso: $($BuildArguments -join ' ')"
    }
    return $log
}

try {
    # An isolated SDK project exercises the real targets without touching an open Rhino session.
    $sourceRoot = Join-Path $fixture 'src\BridGHe.Grasshopper'
    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture 'tools') -Force | Out-Null
    foreach ($file in @('Directory.Build.props', 'Directory.Build.targets')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $fixture
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'clean-builds.ps1') -Destination (Join-Path $fixture 'tools')
    $project = Join-Path $sourceRoot 'BridGHe.Grasshopper.csproj'
    Set-Content -LiteralPath $project -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetExt>.gha</TargetExt></PropertyGroup></Project>'
    $code = Join-Path $sourceRoot 'Plugin.cs'
    $validCode = 'public sealed class Plugin { public int Version => 1; }'
    Set-Content -LiteralPath $code -Value $validCode
    $release = Join-Path $fixture 'bin\Release'
    $published = Join-Path $release 'BridGHe.Grasshopper.gha'

    $null = Run-Build @('build', $project, '-c', 'Release')
    Require (Test-Path -LiteralPath $published) 'Release build publishes the plugin through AfterTargets=Build'
    $sourceOutput = Join-Path $fixture 'obj\bin\BridGHe.Grasshopper\Release\net8.0-windows\BridGHe.Grasshopper.gha'
    Require ((Get-FileHash -LiteralPath $published).Hash -eq (Get-FileHash -LiteralPath $sourceOutput).Hash) 'published plugin is identical to compiler output'

    $legacy = Join-Path $sourceRoot 'bin'
    New-Item -ItemType Directory -Path $legacy -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $legacy 'old.dll') -Value 'obsolete'
    $stale = Join-Path $release 'obsolete.dll'
    Set-Content -LiteralPath $stale -Value 'obsolete'
    Remove-Item -LiteralPath $published
    $log = Run-Build @('build', $project, '-c', 'Release', '--no-restore')
    Require ((Test-Path -LiteralPath $published) -and -not (Test-Path -LiteralPath $stale) -and
        -not (Test-Path -LiteralPath $legacy)) 'incremental build restores the distribution and cleans scattered outputs'
    Require ((Get-Content -LiteralPath $log -Raw).Contains('Post-build: Release sincronizzata')) 'incremental build executes the post-build event'

    $hash = (Get-FileHash -LiteralPath $published).Hash
    $writeTime = (Get-Item -LiteralPath $published).LastWriteTimeUtc
    $null = Run-Build @('build', $project, '-c', 'Debug')
    Require ((Get-FileHash -LiteralPath $published).Hash -eq $hash -and
        (Get-Item -LiteralPath $published).LastWriteTimeUtc -eq $writeTime -and
        @(Get-ChildItem -LiteralPath (Join-Path $fixture 'bin')).Count -eq 1) 'Debug neither publishes nor modifies the existing Release'

    Set-Content -LiteralPath $code -Value 'public class Broken { syntax error }'
    Set-Content -LiteralPath $stale -Value 'must survive a failed build'
    $log = Run-Build @('build', $project, '-c', 'Release', '--no-restore') $false
    Require ((Get-Content -LiteralPath $log -Raw) -match 'error CS\d+') 'negative test fails in the C# compiler'
    Require ((Test-Path -LiteralPath $stale) -and (Get-FileHash -LiteralPath $published).Hash -eq $hash) 'compiler failure leaves the previous distribution untouched'

    Set-Content -LiteralPath $code -Value $validCode
    $null = Run-Build @('build', $project, '-c', 'Release', '--no-restore', '-t:Rebuild')
    Require ((Test-Path -LiteralPath $published) -and -not (Test-Path -LiteralPath $stale)) 'Rebuild runs publication and cleanup after recovering from a failed build'

    New-Item -ItemType Directory -Path $legacy -Force | Out-Null
    $null = Run-Build @('msbuild', $project, '-t:BridGHePostBuild', '-p:DesignTimeBuild=true')
    Require (Test-Path -LiteralPath $legacy) 'Visual Studio design-time evaluations do not run cleanup'
    Write-Host "POST BUILD TOTAL $checks"
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($fixtureParent + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Percorso del fixture non valido: $resolvedFixture"
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
