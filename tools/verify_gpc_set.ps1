param([string]$Directory = (Join-Path $PSScriptRoot '../lib/GPC'), [switch]$AllowCandidate)
$ErrorActionPreference = 'Stop'
$gpcDirectory = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content -LiteralPath (Join-Path $gpcDirectory 'release-set.json') -Raw | ConvertFrom-Json
if ($manifest.candidate -ne $false -and -not ($AllowCandidate -and $manifest.candidate -eq $true)) { throw 'BridGHe richiede un set GPC rilasciato; usare GpcAllowCandidate=true soltanto per collaudare un candidato esplicito.' }
$names = @('GPCUtilities.dll','GPCGeometry.dll','DelaunayMesh.dll','GPCModel.dll','GPCChecker.Concrete.dll','GPCChecker.Steel.dll','GPCChecker.CompositeBridge.dll')
foreach ($name in $names) {
    $entries = @($manifest.files | Where-Object file -eq $name)
    if ($entries.Count -ne 1) { throw "Manifest GPC incompleto o ambiguo: $name" }
    $file = Join-Path $gpcDirectory $name
    $stream = [IO.File]::OpenRead($file)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
    if ($digest -ne $entries[0].sha256) { throw "Hash GPC diverso dal set: $name" }
    if ([Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString() -ne $entries[0].assemblyVersion) { throw "Versione GPC diversa dal manifest: $name" }
}
if ($manifest.candidate) { Write-Warning 'GPC: set CANDIDATO, 7 DLL verificate. Non e un rilascio ufficiale delle librerie.' }
else { Write-Host 'GPC: 7 DLL verificate contro il manifest del set rilasciato.' }
