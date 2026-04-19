param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$linuxPublishScript = Join-Path $scriptDir "Publish-PathoNet-Linux.ps1"

& $linuxPublishScript `
    -Configuration $Configuration `
    -Runtime "linux-arm64" `
    -OutputRoot $OutputRoot
