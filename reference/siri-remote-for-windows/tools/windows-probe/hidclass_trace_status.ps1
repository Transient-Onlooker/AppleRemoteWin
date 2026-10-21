<#
.SYNOPSIS
Inspect the captured HIDCLASS Event 3 v2 safely without revealing device IDs.

.DESCRIPTION
Read-only. Reuses the newest local siri_hidclass_*.etl; does not start
tracing, re-pair, restart PnP nodes, or alter Windows configuration.

Reads Event 3 v2's DeviceStartStatus, ReportDescriptorLength and
DeviceInstancePath in memory only if a trustworthy per-event field mapping exists. Prints ONLY numeric statuses/lengths and
the result of an exact Windows PnP-node/parent-ancestry correlation.

Never prints event Message, XML, DeviceInstancePath, MAC, VID/PID,
ReportDescriptor bytes, user/computer names or raw ETL filename.

NOTE: An unknown device association is not evidence that an event
belongs to the Siri Remote.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 500)]
    [int]$MaxEvents = 200,
    [ValidateRange(1, 15)]
    [int]$MaxDepth = 10
)

$ErrorActionPreference = 'Stop'
$providerName = 'Microsoft-Windows-Input-HIDCLASS'

function Get-NumericStatus($value) {
    if ($null -eq $value) { return '(missing)' }
    $s = ([string]$value).Trim()
    if ($s -match '(?i)^0x([0-9a-f]{1,8})$') {
        return ('0x{0}' -f $Matches[1].ToUpperInvariant().PadLeft(8, '0'))
    }
    try {
        $number = [long]::Parse($s)
        $bits = [BitConverter]::ToUInt32([BitConverter]::GetBytes($number), 0)
        return ('0x{0:X8}' -f $bits)
    } catch {
        return '(unavailable)'
    }
}
function Safe-Length($value) {
    if ($null -eq $value) { return '(missing)' }
    try {
        $v = ([string]$value).Trim()
        if ($v -match '(?i)^0x([0-9a-f]{1,8})$') {
            $n = [Convert]::ToInt64($Matches[1], 16)
        } else {
            $n = [long]::Parse($v)
        }
        if ($n -ge 0 -and $n -le 1048576) { return [string]$n }
    } catch {}
    return '(unavailable)'
}
function Get-Parent($id) {
    if ([string]::IsNullOrWhiteSpace($id)) { return $null }
    try {
        $p = Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_Parent' -ErrorAction Stop
        if ($null -ne $p -and -not [string]::IsNullOrWhiteSpace([string]$p.Data)) {
            return [string]$p.Data
        }
    } catch {}
    return $null
}
function Is-Descendant($child, $ancestor) {
    if ([string]::IsNullOrWhiteSpace($ancestor) -or
        [string]::IsNullOrWhiteSpace($child)) { return $false }
    $seen = @{}
    $current = [string]$child
    for ($i = 0; $i -le $MaxDepth; $i++) {
        if ($current -ieq $ancestor) { return $true }
        if ($seen.ContainsKey($current)) { break }
        $seen[$current] = $true
        $current = Get-Parent $current
        if ([string]::IsNullOrWhiteSpace($current)) { break }
    }
    return $false
}

$etl = Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -File -Filter 'siri_hidclass_*.etl' |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($null -eq $etl) {
    Write-Output 'No local HIDCLASS ETL trace found in TEMP for this Windows account.'
    exit 1
}

$metadata = Get-WinEvent -ListProvider $providerName -ErrorAction Stop
$template = @($metadata.Events | Where-Object { $_.Id -eq 3 -and $_.Version -eq 2 })
if ($template.Count -ne 1) {
    Write-Output 'HIDCLASS event 3 version 2 schema unavailable; no unsafe assumptions.'
    exit 1
}

[xml]$xml = [string]$template[0].Template
$fields = @($xml.SelectNodes('//*[local-name()="data"]'))
$indices = @{}
for ($i = 0; $i -lt $fields.Count; $i++) {
    $name = [string]$fields[$i].GetAttribute('name')
    if (-not $indices.ContainsKey($name)) { $indices[$name] = $i }
}
$needed = @('DeviceStartStatus', 'DeviceInstancePath', 'ReportDescriptorLength')
foreach ($name in $needed) {
    if (-not $indices.ContainsKey($name)) {
        Write-Output 'Expected event schema field absent; stopping without guessing offsets.'
        exit 1
    }
}

$events = @(
    Get-WinEvent -Path $etl.FullName -Oldest -ErrorAction Stop |
        Where-Object { $_.ProviderName -eq $providerName -and $_.Id -eq 3 -and $_.Version -eq 2 } |
        Select-Object -First $MaxEvents
)
Write-Output ("Event 3 v2 records found (up to {0}): {1}" -f $MaxEvents, $events.Count)
if ($events.Count -eq 0) { exit 0 }

# The PnP lookup is confined to local machine memory; IDs/paths never leave
# this process. A match requires EXACT InstanceId and then parent ancestry.
$anchor = $null
$pnp = @()
try { $pnp = @(Get-PnpDevice -ErrorAction Stop) } catch {}
$remoteCandidates = @(
    $pnp | Where-Object {
        $_.Class -eq 'Bluetooth' -and
        $_.FriendlyName -match '(?i)^(DL1)|Siri Remote'
    }
)
if ($remoteCandidates.Count -eq 1) {
    $anchor = [string]$remoteCandidates[0].InstanceId
    Write-Output 'Unique paired remote Bluetooth PnP anchor: yes.'
} else {
    Write-Output 'Unique remote anchor: unavailable; device correlation is inconclusive.'
}
$knownIds = @{}
foreach ($device in $pnp) {
    if (-not [string]::IsNullOrWhiteSpace([string]$device.InstanceId)) {
        $knownIds[[string]$device.InstanceId] = $true
    }
}

# The Windows ETL event's Properties array is NOT guaranteed to contain
# the same number of fields as the current provider manifest template. Prefer
# the actual event's NAMED EventData fields when present. Never apply partial
# positional maps to an incomplete event.
Write-Output ("Provider template fields: {0}" -f $fields.Count)
$rows = @()
$i = 0
foreach ($e in $events) {
    $i++
    $fieldValues = @{}
    $xmlFieldCount = 0
    $source = 'Unavailable'
    $parseProblem = $false
    try {
        [xml]$recordXml = $e.ToXml()
        $nodes = @($recordXml.SelectNodes('//*[local-name()="EventData"]/*[local-name()="Data"]'))
        $xmlFieldCount = $nodes.Count
        foreach ($node in $nodes) {
            $key = [string]$node.GetAttribute('Name')
            if ($needed -contains $key -and -not $fieldValues.ContainsKey($key)) {
                # The value stays in private process memory. Do not print it.
                $fieldValues[$key] = [string]$node.InnerText
            }
        }
        if (@($needed | Where-Object { $fieldValues.ContainsKey($_) }).Count -eq $needed.Count) {
            $source = 'NamedXML'
        }
    } catch {
        $parseProblem = $true
    }
    if ($source -ne 'NamedXML') {
        $fieldValues = @{}
        if ($e.Properties.Count -eq $fields.Count) {
            # Positional access is only defensible for an *exact* field count.
            # No ambiguous truncated-layout indexing.
            $source = 'FullTemplate'
            foreach ($key in $needed) {
                $fieldValues[$key] = $e.Properties[$indices[$key]].Value
            }
        } elseif ($parseProblem) {
            $source = 'XMLParseError'
        } else {
            $source = 'LayoutUnresolved'
        }
    }

    $association = 'Unknown'
    $startStatus = '(unavailable)'
    $descBytes = '(unavailable)'
    if ($source -eq 'NamedXML' -or $source -eq 'FullTemplate') {
        $instanceId = [string]$fieldValues['DeviceInstancePath']
        $association = 'UnmatchedPath'
        if ($knownIds.ContainsKey($instanceId)) {
            $association = 'OtherPnP'
            if ($null -ne $anchor -and (Is-Descendant $instanceId $anchor)) {
                $association = 'RemoteDescendant'
            }
        }
        $startStatus = Get-NumericStatus $fieldValues['DeviceStartStatus']
        $descBytes = Safe-Length $fieldValues['ReportDescriptorLength']
    }
    $rows += [pscustomobject]@{
        No = $i
        Source = $source
        PropertyCount = [int]$e.Properties.Count
        XmlFieldCount = [int]$xmlFieldCount
        Attribution = $association
        StartStatus = $startStatus
        DescriptorBytes = $descBytes
    }
}
$rows | Format-Table -AutoSize | Out-String -Width 160 | Write-Output
Write-Output 'PropertyCount is the actual event property count; XmlFieldCount counts event XML data nodes.'
Write-Output 'LayoutUnresolved means there was no trustworthy field mapping; NOT a remote/driver diagnosis.'
Write-Output 'Device paths/identifiers and descriptor bytes were not printed.'
Write-Output 'UnmatchedPath means NOT attributable, not that it is unrelated.'
Write-Output 'StartStatus is an event-field value, not proof of a new device-start attempt.'
