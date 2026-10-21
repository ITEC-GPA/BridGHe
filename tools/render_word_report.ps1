param([Parameter(Mandatory=$true)][string]$InputPath,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
# QA fallback on Windows when LibreOffice is absent. The plugin itself does not require Office.
$source = (Resolve-Path -LiteralPath $InputPath).Path
$destination = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($destination) -ne '.pdf') { throw 'OutputPath must be a PDF.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
$application = $null
$document = $null
$owned = $false
try {
    $application = New-Object -ComObject Word.Application
    # Do not alter or close any pre-existing interactive document.
    if ($application.Documents.Count -ne 0) { throw 'Word returned an instance containing user documents; QA export stopped.' }
    $owned = $true
    $application.Visible = $false
    $application.DisplayAlerts = 0
    $application.AutomationSecurity = 3
    $document = $application.Documents.Open($source, $false, $true, $false)
    $document.Fields.Update() | Out-Null
    $document.Repaginate()
    $pages = $document.ComputeStatistics(2)
    $document.ExportAsFixedFormat($destination, 17)
    Write-Output "WORD RENDER PASS: $pages pages -> $destination"
} finally {
    $closeMode = 0
    if ($null -ne $document) { $document.Close([ref]$closeMode); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($document) | Out-Null }
    if ($null -ne $application) {
        if ($owned) { $application.Quit([ref]$closeMode) }
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($application) | Out-Null
    }
}
