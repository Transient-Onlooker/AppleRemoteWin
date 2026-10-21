<#
.SYNOPSIS
Capture a short, bounded HIDCLASS manifested ETW trace and print safe metadata.

.DESCRIPTION
Admin PowerShell required. Starts and stops only its uniquely named trace
session; NEVER modifies pairing, adapters, PnP devices, or drivers.
The local ETL is SENSITIVE and must not be shared publicly. This script never
prints event messages, event payloads, device paths/IDs, or BLE addresses.
Only the provider's event IDs, versions, counts and relative event times are
printed. An empty trace does NOT prove successful HID initialization.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\windows-probe\hidclass_trace.ps1 -Seconds 20
#>
[CmdletBinding()]
param(
    [ValidateRange(5, 60)]
    [int]$Seconds = 20
)

$ErrorActionPreference = 'Stop'
$provider = 'Microsoft-Windows-Input-HIDCLASS'
$session = 'SiriHidClass_' + $PID
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$etl = Join-Path $env:TEMP ("siri_hidclass_{0}_{1}.etl" -f $stamp, $PID)
$started = $false

try {
    $null = Get-WinEvent -ListProvider $provider -ErrorAction Stop
    # 0x1 = Default, high bit = HIDCLASS analytic; level 4=Informational.
    & logman.exe start $session -p $provider '0x8000000000000001' '4' -o $etl -ets | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'logman start failed. Try an elevated PowerShell.'
    }
    $started = $true
    Write-Output ("HIDCLASS ETW started for up to {0} seconds." -f $Seconds)
    Write-Output 'Interact with the Siri Remote now (Back/TV/touchpad).'
    Write-Output 'This does not force a broken HID driver node to restart.'
    Start-Sleep -Seconds $Seconds
} finally {
    if ($started) {
        & logman.exe stop $session -ets | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'ETW session stop failed. Run logman query -ets and stop your SiriHidClass session manually.'
        }
    }
}

if (-not (Test-Path -LiteralPath $etl)) {
    Write-Output 'ETL file was not created; no event conclusions.'
    exit 1
}

Write-Output 'Trace stopped. Raw ETL stored ONLY on this PC under TEMP.'
Write-Output 'Do NOT upload the ETL file or raw event Message/XML.'
try {
    $allEvents = @(Get-WinEvent -Path $etl -Oldest -ErrorAction Stop)
} catch {
    Write-Output 'No readable ETW records. This is inconclusive.'
    exit 0
}
$events = @($allEvents | Where-Object { $_.ProviderName -eq $provider })
Write-Output ("HIDCLASS events found: {0}" -f $events.Count)
if ($events.Count -eq 0) {
    Write-Output 'No provider events were emitted during this window. A failing node may not have restarted.'
    exit 0
}
$events |
    Group-Object Id, Version |
    ForEach-Object {
        $sample = $_.Group[0]
        [pscustomobject]@{
            EventId = [int]$sample.Id
            Version = [int]$sample.Version
            Count = [int]$_.Count
        }
    } |
    Sort-Object EventId, Version |
    Format-Table -AutoSize |
    Out-String -Width 100 |
    Write-Output
Write-Output 'Summary only. Event payloads and device identifiers were not displayed.'
