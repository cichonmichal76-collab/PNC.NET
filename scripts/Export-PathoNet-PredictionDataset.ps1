param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$UserName = "admin",
    [string]$Password = "123",
    [ValidateSet("csv", "jsonl")]
    [string]$Format = "csv",
    [string]$ResolvedOnly = "true",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$resolvedOnlyFlag = switch ($ResolvedOnly.ToString().Trim().ToLowerInvariant()) {
    "1" { $true; break }
    "true" { $true; break }
    '$true' { $true; break }
    "0" { $false; break }
    "false" { $false; break }
    '$false' { $false; break }
    default { throw "ResolvedOnly must be one of: true, false, 1, 0." }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $extension = if ($Format -eq "jsonl") { "jsonl" } else { "csv" }
    $OutputPath = Join-Path $PSScriptRoot "..\\tmp\\ml\\pathonet-prediction-dataset-$timestamp.$extension"
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Path $resolvedOutputPath -Parent
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$manifestPath = Join-Path $outputDirectory "pathonet-prediction-manifest.json"
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$loginPayload = @{
    userName = $UserName
    password = $Password
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "$BaseUrl/api/identity/login" `
    -Method Post `
    -ContentType "application/json" `
    -Body $loginPayload `
    -WebSession $session | Out-Null

$resolvedQuery = if ($resolvedOnlyFlag) { "true" } else { "false" }
$datasetUri = "$BaseUrl/api/prediction/dataset/export?format=$Format&resolvedOnly=$resolvedQuery"
$manifestUri = "$BaseUrl/api/prediction/dataset/manifest"

Invoke-WebRequest `
    -Uri $datasetUri `
    -WebSession $session `
    -OutFile $resolvedOutputPath | Out-Null

$manifest = Invoke-RestMethod `
    -Uri $manifestUri `
    -WebSession $session

$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

[pscustomobject]@{
    DatasetPath  = $resolvedOutputPath
    ManifestPath = [System.IO.Path]::GetFullPath($manifestPath)
    Format       = $Format
    ResolvedOnly = $resolvedOnlyFlag
} | Format-List
