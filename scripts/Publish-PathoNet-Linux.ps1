param(
    [string]$Configuration = "Release",
    [string]$Runtime = "linux-arm64",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root ("publish\" + $Runtime)
}

$publishMap = @(
    @{ Name = "api"; Project = "src\PathoNet.Api\PathoNet.Api.csproj" },
    @{ Name = "collector"; Project = "src\PathoNet.Collector\PathoNet.Collector.csproj" },
    @{ Name = "hub"; Project = "src\PathoNet.Hub\PathoNet.Hub.csproj" },
    @{ Name = "apisender"; Project = "src\PathoNet.ApiSender\PathoNet.ApiSender.csproj" }
)

foreach ($entry in $publishMap) {
    $projectPath = Join-Path $root $entry.Project
    $outputPath = Join-Path $OutputRoot $entry.Name

    Write-Host "Publishing $($entry.Name) -> $outputPath"
    dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -o $outputPath
}

$systemdSource = Join-Path $root "deploy\systemd"
$systemdOutput = Join-Path $OutputRoot "systemd"
$kioskSource = Join-Path $root "deploy\kiosk"
$kioskOutput = Join-Path $OutputRoot "kiosk"

if (Test-Path $systemdOutput) {
    Remove-Item -Recurse -Force $systemdOutput
}

Copy-Item -Path $systemdSource -Destination $systemdOutput -Recurse

if (Test-Path $kioskOutput) {
    Remove-Item -Recurse -Force $kioskOutput
}

Copy-Item -Path $kioskSource -Destination $kioskOutput -Recurse

Write-Host ""
Write-Host "Linux publish ready in: $OutputRoot"
Write-Host "Systemd units copied to: $systemdOutput"
Write-Host "Kiosk assets copied to: $kioskOutput"
