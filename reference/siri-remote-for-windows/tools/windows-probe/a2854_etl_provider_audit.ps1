<#
.SYNOPSIS
Count HIDCLASS provider GUIDs in an EXISTING Siri Remote WPP ETL.

.DESCRIPTION
Windows tracerpt can summarize raw ETL provider GUIDs when Get-WinEvent has
blank ProviderName/ProviderId for WPP records. This does NOT decode payloads.
It creates a temporary XML event dump (may contain sensitive input data)
under the user's TEMP, removes it immediately after parsing the summary,
and never prints raw event payloads or local paths.
No new capture, PnP restart, Bluetooth changes or driver changes.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$wppGuid = '47c779cd-4efd-49d7-9b10-9f16e5c25d06'
$etwGuid = '6465da78-e7a0-4f39-b084-8f53c7c30dc6'
$traces = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Filter 'a2854_wpp_*.etl' -File -ErrorAction Stop |
    Sort-Object LastWriteTimeUtc -Descending)
if ($traces.Count -eq 0) { throw 'No existing A2854 WPP ETL in the current TEMP.' }
$trace = $traces[0]
Write-Output ('Existing ETL size: {0} MiB' -f [math]::Round($trace.Length / 1MB, 2))
Write-Output ('ETLs found: {0} (latest selected)' -f $traces.Count)
if (-not (Get-Command tracerpt.exe -ErrorAction SilentlyContinue)) {
    throw 'tracerpt.exe not available.'
}
$work = Join-Path ([IO.Path]::GetTempPath()) ('a2854_guid_audit_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
$summary = Join-Path $work 'summary.txt'
$dump = Join-Path $work 'events-private.xml'
try {
    Write-Output 'Reading ETL headers with tracerpt (NO re-capture).'
    # Redirect any generated full XML dump to a private temporary directory.
    & tracerpt.exe -l $trace.FullName -summary $summary -o $dump -of XML -y *> $null
    $exitCode = $LASTEXITCODE
    Write-Output ('tracerpt exit code: {0}' -f $exitCode)
    if (-not (Test-Path -LiteralPath $summary -PathType Leaf)) {
        Write-Output 'Summary missing: result INCONCLUSIVE.'
    } else {
        $wppRows = 0
        $etwRows = 0
        $wppCount = [long]0
        $etwCount = [long]0
        $allRows = 0
        $unparsed = 0
        $processed = '(unavailable)'
        $lost = '(unavailable)'
        foreach ($line in [IO.File]::ReadLines($summary)) {
            if ($line -match '^\s*Total\s+Events\s+Processed\s+(\d+)') {
                $processed = $Matches[1]
            }
            if ($line -match '^\s*Total\s+Events\s+Lost\s+(\d+)') {
                $lost = $Matches[1]
            }
            if ($line -match '^\s*\|\s*(\d+)\s+.+\{[0-9a-fA-F-]{36}\}') {
                $allRows++
            }
            if ($line.IndexOf($wppGuid,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $wppRows++
                if ($line -match '^\s*\|\s*(\d+)\s+') {
                    $wppCount += [long]$Matches[1]
                } else { $unparsed++ }
            }
            if ($line.IndexOf($etwGuid,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $etwRows++
                if ($line -match '^\s*\|\s*(\d+)\s+') {
                    $etwCount += [long]$Matches[1]
                } else { $unparsed++ }
            }
        }
        Write-Output ('Summary Total Events Processed: {0}' -f $processed)
        Write-Output ('Summary Total Events Lost: {0}' -f $lost)
        Write-Output ('GUID event-table rows recognized: {0}' -f $allRows)
        Write-Output ('HIDCLASS WPP: rows={0}; events={1}' -f $wppRows,$wppCount)
        Write-Output ('HIDCLASS ETW: rows={0}; events={1}' -f $etwRows,$etwCount)
        if ($exitCode -ne 0 -or $allRows -eq 0 -or $unparsed -gt 0) {
            Write-Output 'WARNING: summary parse incomplete; zero counts remain INCONCLUSIVE.'
        } elseif ($wppCount -eq 0 -and $etwCount -eq 0) {
            Write-Output 'Neither HIDCLASS GUID was found in the provider summary.'
            Write-Output 'Investigate trace configuration, not another blind device restart.'
        } else {
            Write-Output 'HIDCLASS event GUID found in raw ETL summary.'
            Write-Output 'If WPA hides WPP Trace, investigate decoding/PDB/TMF availability.'
        }
    }
} finally {
    # Raw tracerpt XML may contain private input-related event payloads.
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Output 'Temporary tracerpt files cleaned up (best-effort); original ETL unchanged.'
}
