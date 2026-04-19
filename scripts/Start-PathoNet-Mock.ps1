param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$tmpDir = Join-Path $root "tmp"
$pidFile = Join-Path $tmpDir "pathonet-mock-pids.json"

New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

& (Join-Path $PSScriptRoot "Stop-PathoNet-Mock.ps1") -Quiet

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

dotnet build (Join-Path $root "PathoNet.sln") --configfile (Join-Path $root "NuGet.Config") -v minimal | Out-Host

$apps = @(
    @{
        Name = "PathoNet.Api"
        Executable = "dotnet"
        Arguments = "`"$((Join-Path $root "src\PathoNet.Api\bin\$Configuration\net8.0\PathoNet.Api.dll"))`""
        StdOut = Join-Path $tmpDir "pathonet-api.log"
        StdErr = Join-Path $tmpDir "pathonet-api.err.log"
        DelaySeconds = 2
    },
    @{
        Name = "PathoNet.Hub"
        Executable = "dotnet"
        Arguments = "`"$((Join-Path $root "src\PathoNet.Hub\bin\$Configuration\net8.0\PathoNet.Hub.dll"))`""
        StdOut = Join-Path $tmpDir "pathonet-hub.log"
        StdErr = Join-Path $tmpDir "pathonet-hub.err.log"
        DelaySeconds = 2
    },
    @{
        Name = "PathoNet.ApiSender"
        Executable = "dotnet"
        Arguments = "`"$((Join-Path $root "src\PathoNet.ApiSender\bin\$Configuration\net8.0\PathoNet.ApiSender.dll"))`""
        StdOut = Join-Path $tmpDir "pathonet-apisender.log"
        StdErr = Join-Path $tmpDir "pathonet-apisender.err.log"
        DelaySeconds = 2
    },
    @{
        Name = "PathoNet.Collector"
        Executable = "dotnet"
        Arguments = "`"$((Join-Path $root "src\PathoNet.Collector\bin\$Configuration\net8.0\PathoNet.Collector.dll"))`""
        StdOut = Join-Path $tmpDir "pathonet-collector.log"
        StdErr = Join-Path $tmpDir "pathonet-collector.err.log"
        DelaySeconds = 5
    }
)

$started = @()

foreach ($app in $apps) {
    $dllPath = $app.Arguments.Trim('"')
    if (-not (Test-Path $dllPath)) {
        throw "Missing dll: $dllPath"
    }

    $process = Start-Process `
        -FilePath $app.Executable `
        -ArgumentList $app.Arguments `
        -WorkingDirectory $root `
        -RedirectStandardOutput $app.StdOut `
        -RedirectStandardError $app.StdErr `
        -PassThru

    Start-Sleep -Seconds $app.DelaySeconds

    $started += [pscustomobject]@{
        name = $app.Name
        pid = $process.Id
        executable = $app.Executable
        arguments = $app.Arguments
        stdout = $app.StdOut
        stderr = $app.StdErr
    }
}

$started | ConvertTo-Json -Depth 4 | Set-Content -Path $pidFile -Encoding UTF8

Write-Host ""
Write-Host "Started PathoNet mock pipeline:"
$started | Format-Table -AutoSize | Out-Host
Write-Host ""
Write-Host "Snapshot:"
Invoke-RestMethod -Uri "http://localhost:5080/api/diagnostics/snapshot" | ConvertTo-Json -Depth 4 | Out-Host
