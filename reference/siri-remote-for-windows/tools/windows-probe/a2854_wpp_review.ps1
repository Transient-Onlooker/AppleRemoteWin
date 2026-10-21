<#
.SYNOPSIS
Locate an existing A2854 HIDCLASS WPP trace and optionally open it in WPA.

.DESCRIPTION
Read-only: no new capture, WPR start/stop, Bluetooth connection, PnP
restart or registry edits. Does not print local paths, device identifiers,
WPP messages or report descriptor bytes. Only opens the local trace
if -OpenWpa is explicitly provided.
#>
[CmdletBinding()]
param([switch]$OpenWpa)

$ErrorActionPreference = 'Stop'
$traces = @(
    Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -File -Filter 'a2854_wpp_*.etl' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending
)
if ($traces.Count -eq 0) {
    Write-Output 'No A2854 WPP ETLs found in this Windows account TEMP.'
    Write-Output 'Do not restart the remote merely for this viewer.'
    exit 0
}
$trace = $traces[0]
Write-Output ('Local A2854 WPP traces found: {0}' -f $traces.Count)
Write-Output ('Latest trace size: {0} MiB' -f [math]::Round($trace.Length / 1MB, 2))
Write-Output 'Existing trace preserved; no new collection or device action.'
$wpa = Get-Command wpa.exe -ErrorAction SilentlyContinue
if (-not $wpa) {
    Write-Output 'wpa.exe not on PATH: open newest a2854_wpp_*.etl manually in WPA.'
} elseif ($OpenWpa) {
    Start-Process -FilePath $wpa.Source -ArgumentList ('"' + $trace.FullName + '"')
    Write-Output 'WPA launched with existing local trace.'
} else {
    Write-Output 'WPA available: use -OpenWpa to open the latest ETL.'
}
Write-Output 'WPA steps (keep all raw traces and full event messages private):'
Write-Output '1. Graph Explorer -> System Activity -> WPP Trace -> drag into Analysis.'
Write-Output '2. Trace -> Load Symbols; optionally Trace -> Configure Symbol Paths.'
Write-Output '3. View Editor: add GUID Name, Level and Message columns.'
Write-Output '4. Select HIDCLASS / WPP provider GUID 47c779cd-4efd-49d7-9b10-9f16e5c25d06.'
Write-Output '5. Inspect Error rows around the failed remote PnP restart.'
Write-Output '6. Report ONLY redacted error type, numeric descriptor offset, and status.'
Write-Output '7. No WPP messages? Report that fact; do not blindly repeat captures.'
Write-Output 'The manifested HIDCLASS Event 3 v2 alone may only show generic error status.'
