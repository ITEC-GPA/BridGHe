param([string]$Directory = (Join-Path $PSScriptRoot '../lib/GPC'), [switch]$AllowCandidate)
$ErrorActionPreference = 'Stop'
$gpcDirectory = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content -LiteralPath (Join-Path $gpcDirectory 'release-set.json') -Raw | ConvertFrom-Json
if ($manifest.candidate -ne $false -and -not ($AllowCandidate -and $manifest.candidate -eq $true)) { throw 'BridGHe richiede un set GPC rilasciato; usare GpcAllowCandidate=true soltanto per collaudare un candidato esplicito.' }
# The closure of the references of BridGHe.Common and BridGHe.Grasshopper (build/GpcReferences.props): every DLL the plug-in ships.
$names = @('GPCUtilities.dll','GPCGeometry.dll','DelaunayMesh.dll','GPCModel.dll','GPCModelData.dll','GPC.Converter.dll','GPC.Model.Persistence.dll','GPC.Archive.dll','GPCChecker.Concrete.dll','GPCChecker.Steel.dll','GPCChecker.CompositeBridge.dll')
$extra = @(Get-ChildItem -LiteralPath $gpcDirectory -Filter '*.dll' -File | Where-Object { $names -notcontains $_.Name } | ForEach-Object Name)
if ($extra.Count) { throw "DLL non previste in lib/GPC: $($extra -join ', ')" }
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
# T/C/Angle are now pure Model sections: the older Converter silently falls back to BXS.
$converterVersion = [version]($manifest.files | Where-Object file -eq 'GPC.Converter.dll').assemblyVersion
if ($converterVersion -lt [version]'2.23.0.0') { throw 'T/C/Angle GPC richiedono GPC.Converter >= 2.23.0.0 in un set coerente; AW non contiene il mapping nativo. Per il 190 usare il set CX e GpcAllowCandidate=true.' }
if ($manifest.candidate) { Write-Warning "GPC: set CANDIDATO, $($names.Count) DLL verificate. Non e un rilascio ufficiale delle librerie." }
else { Write-Host "GPC: $($names.Count) DLL verificate contro il manifest del set rilasciato." }
