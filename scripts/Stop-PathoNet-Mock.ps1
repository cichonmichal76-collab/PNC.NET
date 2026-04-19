param(
    [switch]$Quiet
)

$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$tmpDir = Join-Path $root "tmp"
$pidFile = Join-Path $tmpDir "pathonet-mock-pids.json"

if (Test-Path $pidFile) {
    $entries = Get-Content -Path $pidFile -Raw | ConvertFrom-Json
    foreach ($entry in $entries) {
        Stop-Process -Id $entry.pid -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
}

$knownNames = @("PathoNet.Api", "PathoNet.Hub", "PathoNet.ApiSender", "PathoNet.Collector")
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $knownNames -contains $_.ProcessName } |
    Stop-Process -Force -ErrorAction SilentlyContinue

Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -eq "dotnet.exe" -and
        $_.CommandLine -match "PathoNet\.(Api|Hub|ApiSender|Collector)"
    } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

if (-not $Quiet) {
    Write-Host "Stopped PathoNet mock processes."
}
