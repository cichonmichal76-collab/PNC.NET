param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceName,

    [Parameter(Mandatory = $true)]
    [string]$EventId,

    [string]$RequestedBy = "service-panel",

    [int]$DelaySeconds = 0
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$tmpDir = Join-Path $root "tmp"
$pidFile = Join-Path $tmpDir "pathonet-mock-pids.json"
$healthDir = Join-Path $tmpDir "service-health"
$historyFile = Join-Path $healthDir "restart-history.json"

New-Item -ItemType Directory -Force -Path $healthDir | Out-Null

function Get-JsonArray {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return @()
    }

    $raw = Get-Content -Path $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @()
    }

    $data = $raw | ConvertFrom-Json
    if ($null -eq $data) {
        return @()
    }

    if ($data -is [System.Array]) {
        return @($data)
    }

    return @($data)
}

function Save-JsonArray {
    param(
        [string]$Path,
        [object[]]$Items,
        [int]$Depth = 8
    )

    if ($Items.Count -eq 1) {
        $json = "[`n$($Items[0] | ConvertTo-Json -Depth $Depth)`n]"
    }
    else {
        $json = $Items | ConvertTo-Json -Depth $Depth
    }

    Set-Content -Path $Path -Value $json -Encoding UTF8
}

function Update-RestartHistory {
    param(
        [string]$Status,
        [string]$Summary,
        [Nullable[int]]$PreviousProcessId,
        [Nullable[int]]$CurrentProcessId
    )

    $entries = Get-JsonArray -Path $historyFile
    $updated = @()
    $found = $false

    foreach ($entry in $entries) {
        if ($entry.id -eq $EventId) {
            $entry.status = $Status
            $entry.mode = "local-script"
            $entry.requestedBy = $RequestedBy
            $entry.completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            $entry.previousProcessId = $PreviousProcessId
            $entry.currentProcessId = $CurrentProcessId
            $entry.summary = $Summary
            $updated += $entry
            $found = $true
        }
        else {
            $updated += $entry
        }
    }

    if (-not $found) {
        $updated += [pscustomobject]@{
            id = $EventId
            serviceName = $ServiceName
            status = $Status
            mode = "local-script"
            requestedBy = $RequestedBy
            requestedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            previousProcessId = $PreviousProcessId
            currentProcessId = $CurrentProcessId
            summary = $Summary
        }
    }

    $updated = @($updated | Sort-Object requestedAtUtc -Descending | Select-Object -First 100)
    Save-JsonArray -Path $historyFile -Items $updated
}

try {
    if ($DelaySeconds -gt 0) {
        Start-Sleep -Seconds $DelaySeconds
    }

    if (-not (Test-Path $pidFile)) {
        throw "Nie znaleziono pliku PID mocka: $pidFile"
    }

    $entries = Get-JsonArray -Path $pidFile
    $entry = $entries | Where-Object { $_.name -eq $ServiceName } | Select-Object -First 1

    if ($null -eq $entry) {
        throw "Brak wpisu uslugi $ServiceName w pliku PID."
    }

    $previousPid = $null
    if ($entry.pid) {
        $previousPid = [int]$entry.pid
        Stop-Process -Id $previousPid -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
    }

    $process = Start-Process `
        -FilePath $entry.executable `
        -ArgumentList $entry.arguments `
        -WorkingDirectory $root `
        -RedirectStandardOutput $entry.stdout `
        -RedirectStandardError $entry.stderr `
        -PassThru

    Start-Sleep -Seconds 2

    $updatedEntries = foreach ($item in $entries) {
        if ($item.name -eq $ServiceName) {
            [pscustomobject]@{
                name = $item.name
                pid = $process.Id
                executable = $item.executable
                arguments = $item.arguments
                stdout = $item.stdout
                stderr = $item.stderr
            }
        }
        else {
            $item
        }
    }

    Save-JsonArray -Path $pidFile -Items $updatedEntries -Depth 4

    Update-RestartHistory `
        -Status "completed" `
        -Summary "Restart uslugi $ServiceName zakonczony powodzeniem." `
        -PreviousProcessId $previousPid `
        -CurrentProcessId $process.Id
}
catch {
    Update-RestartHistory `
        -Status "failed" `
        -Summary $_.Exception.Message `
        -PreviousProcessId $previousPid `
        -CurrentProcessId $null
    throw
}
