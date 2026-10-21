<#
.SYNOPSIS
Safely inspect the Siri Remote HID Report Descriptor in an existing HIDCLASS ETL.

.DESCRIPTION
Read-only ETL and PnP inspection; NO PnP restart, pairing, or ETW session start.
Only the newest siri_hid_restart_*.etl in current user's TEMP is inspected.
Correlates event ID 3 version 2 to the exact Code-10 mshidumdf child beneath
the unique DL1/Siri Remote Bluetooth device.

Optional -SaveBinary writes the validated, length-matched ReportDescriptor
bytes ONLY to %TEMP%\a2854_hidclass_report_map.bin (create-new, no overwrite).
No raw ETL data, descriptor bytes, MACs, PnP paths or other identifiers print.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\windows-probe\hidclass_extract_descriptor.ps1 -SaveBinary
#>
[CmdletBinding()]
param(
    [switch]$SaveBinary,
    [ValidateRange(1, 500)]
    [int]$MaxEvents = 200
)
$ErrorActionPreference = 'Stop'
$providerName = 'Microsoft-Windows-Input-HIDCLASS'
$binaryName = 'a2854_hidclass_report_map.bin'

function Read-Prop([string]$id,[string]$key) {
    try {
        $v = Get-PnpDeviceProperty -InstanceId $id -KeyName $key -ErrorAction Stop
        if ($null -ne $v) { return $v.Data }
    } catch {}
    return $null
}
function Is-Descendant([string]$child,[string]$ancestor) {
    if ([string]::IsNullOrWhiteSpace($child) -or [string]::IsNullOrWhiteSpace($ancestor)) {
        return $false
    }
    $seen = @{}
    $current = $child
    for ($i = 0; $i -le 10; $i++) {
        if ($current -ieq $ancestor) { return $true }
        if ($seen.ContainsKey($current)) { return $false }
        $seen[$current] = $true
        $current = [string](Read-Prop $current 'DEVPKEY_Device_Parent')
        if ([string]::IsNullOrWhiteSpace($current)) { return $false }
    }
    return $false
}
function StatusHex($value) {
    if ($null -eq $value) { return '(missing)' }
    $s = ([string]$value).Trim()
    if ($s -match '(?i)^0x([0-9a-f]{1,8})$') {
        return '0x' + $Matches[1].ToUpperInvariant().PadLeft(8,'0')
    }
    try {
        $n = [long]::Parse($s)
        return ('0x{0:X8}' -f [BitConverter]::ToUInt32([BitConverter]::GetBytes($n), 0))
    } catch { return '(unavailable)' }
}
function Parse-Count($value) {
    if ($null -eq $value) { return $null }
    $s = ([string]$value).Trim()
    try {
        if ($s -match '(?i)^0x([a-f0-9]{1,8})$') {
            $n = [Convert]::ToInt64($Matches[1],16)
        } else {
            $n = [long]::Parse($s)
        }
        if ($n -ge 0 -and $n -le 16384) { return [int]$n }
    } catch {}
    return $null
}
function From-Hex($value) {
    if ($null -eq $value) { return $null }
    $s = ([string]$value).Trim()
    if ($s -match '(?i)^0x') { $s = $s.Substring(2) }
    $s = [regex]::Replace($s, '\s+', '')
    if ($s.Length -eq 0 -or $s.Length -gt 32768 -or ($s.Length % 2) -ne 0) {
        return $null
    }
    if ($s -notmatch '\A[0-9a-fA-F]+\z') { return $null }
    $bytes = New-Object byte[] ($s.Length / 2)
    for ($i=0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($s.Substring($i*2,2),16)
    }
    # PowerShell must not enumerate the byte array into multiple results.
    return ,$bytes
}
function EventFields($record) {
    [xml]$xml = $record.ToXml()
    $nodes = @($xml.SelectNodes('//*[local-name()="EventData"]/*[local-name()="Data"]'))
    $wanted = @('DeviceInstancePath','DeviceStartStatus','ReportDescriptorLength','ReportDescriptor')
    $found = @{}
    $ordinal = @{}
    for ($i=0; $i -lt $nodes.Count; $i++) {
        $name = [string]$nodes[$i].GetAttribute('Name')
        if ($wanted -contains $name -and -not $found.ContainsKey($name)) {
            $found[$name] = [string]$nodes[$i].InnerText
            $ordinal[$name] = $i
        }
    }
    return [pscustomobject]@{
        Values = $found
        Indices = $ordinal
        NodeCount = $nodes.Count
    }
}

$etl = Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -File -Filter 'siri_hid_restart_*.etl' |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($null -eq $etl) {
    Write-Output 'No targeted restart ETL exists in TEMP for this Windows account.'
    exit 1
}

$devices = @(Get-PnpDevice -ErrorAction Stop)
$anchors = @($devices | Where-Object {
    $_.Class -eq 'Bluetooth' -and $_.FriendlyName -match '(?i)^(DL1)|Siri Remote'
})
if ($anchors.Count -ne 1) {
    Write-Output 'Cannot identify exactly one Siri Remote Bluetooth ancestor; stopping.'
    exit 1
}
$anchor = [string]$anchors[0].InstanceId
$targets = @(
    $devices | Where-Object {
        $_.Class -eq 'HIDClass' -and
        $_.InstanceId -match '(?i)^BTHLEDEVICE' -and
        (Is-Descendant ([string]$_.InstanceId) $anchor) -and
        [string](Read-Prop ([string]$_.InstanceId) 'DEVPKEY_Device_Service') -eq 'mshidumdf'
    }
)
if ($targets.Count -ne 1) {
    Write-Output 'Cannot identify exactly one Siri Remote mshidumdf HID child; stopping.'
    exit 1
}
$target = [string]$targets[0].InstanceId
Write-Output 'Targeted restart ETL and unique Siri Remote HID child found.'

$events = @(Get-WinEvent -Path $etl.FullName -Oldest -ErrorAction Stop |
    Where-Object { $_.ProviderName -eq $providerName -and $_.Id -eq 3 -and $_.Version -eq 2 } |
    Select-Object -First $MaxEvents)
$matches = @()
foreach ($event in $events) {
    try {
        $data = EventFields $event
        if ($data.Values.ContainsKey('DeviceInstancePath') -and
            $data.Values['DeviceInstancePath'] -ieq $target) {
            $matches += [pscustomobject]@{ Event = $event; Data = $data }
        }
    } catch {}
}
Write-Output ("Exactly matching HIDCLASS Event 3 v2 records: {0}" -f $matches.Count)
if ($matches.Count -ne 1) {
    Write-Output 'Need exactly one matching event to inspect safely; no descriptor will be written.'
    exit 0
}

$match = $matches[0]
$data = $match.Data
$start = StatusHex $data.Values['DeviceStartStatus']
$length = Parse-Count $data.Values['ReportDescriptorLength']
Write-Output ("HIDCLASS DeviceStartStatus: {0}" -f $start)
Write-Output ("HIDCLASS declared ReportDescriptorLength: {0}" -f $(if ($null -eq $length) { '(unavailable)' } else { $length }))
if ($start -ne '0xC00000B9') {
    Write-Output 'Different start status; refusing to extract a descriptor from an unexpected event.'
    exit 0
}
if ($null -eq $length -or $length -le 0) {
    Write-Output 'No positive bounded descriptor length in this event. This is inconclusive.'
    exit 0
}
if (-not $data.Values.ContainsKey('ReportDescriptor')) {
    Write-Output 'No ReportDescriptor field in the matched event.'
    exit 0
}

$bytes = $null
$source = '(none)'
# When actual event XML has N named Data elements and Properties has N
# entries, derive the binary field's index from *event XML*, not the provider
# template (the latter has 32 fields; actual event has 21).
if ($match.Event.Properties.Count -eq $data.NodeCount) {
    $value = $match.Event.Properties[$data.Indices['ReportDescriptor']].Value
    if ($value -is [byte[]]) {
        $bytes = [byte[]]$value
        $source = 'EventBinary'
    } elseif ($value -is [string]) {
        $bytes = From-Hex $value
        if ($null -ne $bytes) { $source = 'EventHexString' }
    }
}
if ($null -eq $bytes) {
    $bytes = From-Hex $data.Values['ReportDescriptor']
    if ($null -ne $bytes) { $source = 'XMLHex' }
}

if ($null -eq $bytes) {
    Write-Output 'ReportDescriptor encoding is not a supported byte array/hex string.'
    Write-Output 'No binary was written. Do not share raw event XML or ETL.'
    exit 0
}
Write-Output ("ReportDescriptor binary source: {0}; actual bytes={1}" -f $source,$bytes.Length)
if ($bytes.Length -ne $length) {
    Write-Output 'WARNING: Descriptor size does not match declared length. No binary written.'
    exit 0
}
Write-Output 'Descriptor length and actual bytes MATCH for the exact failed Siri Remote HID node.'
if (-not $SaveBinary) {
    Write-Output 'Dry run: pass -SaveBinary to save local report descriptor as a .bin file.'
    exit 0
}
$destination = Join-Path ([IO.Path]::GetTempPath()) $binaryName
try {
    $stream = [System.IO.File]::Open(
        $destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None
    )
    try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
    Write-Output ("Saved validated local binary under TEMP as {0}" -f $binaryName)
    Write-Output 'Next command from repository root:'
    Write-Output ('py -3 tools\protocol\report_map.py --inspect "$env:TEMP\{0}"' -f $binaryName)
} catch [System.IO.IOException] {
    Write-Output 'Local binary already exists or could not be written. Existing file was NOT replaced.'
}
Write-Output 'Keep ETL and binary local; do not post device-instance paths or raw trace data.'
