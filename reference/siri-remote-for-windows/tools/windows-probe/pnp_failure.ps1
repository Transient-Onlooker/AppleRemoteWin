<#
.SYNOPSIS
Read-only diagnostics of a BLE HIDClass problem-code-10 node belonging to
the unique remote-like Bluetooth PnP ancestor.

.DESCRIPTION
Prints only sanitized failure properties and related Kernel-PnP event 411
status fields. Never prints InstanceId, BLE address, ContainerId, event XML,
event messages, user/computer names or other unique identifiers.
Never changes Bluetooth pairing, driver configuration or device state.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 30)]
    [int]$Days = 14,
    [ValidateRange(1, 500)]
    [int]$MaxEvents = 200,
    [ValidateRange(1, 12)]
    [int]$MaxDepth = 8
)
$ErrorActionPreference = 'Stop'

function Read-Prop([string]$Id, [string]$Key) {
    try {
        $p = Get-PnpDeviceProperty -InstanceId $Id -KeyName $Key -ErrorAction Stop
        if ($null -ne $p) { return $p.Data }
    } catch {
        # Unavailable PnP properties are common for non-present nodes.
    }
    return $null
}

function Test-IsDescendant([string]$Child, [string]$Anchor, [int]$Depth) {
    $visited = @{}
    $current = $Child
    for ($i = 0; $i -le $Depth; $i++) {
        if ($current -ieq $Anchor) { return $true }
        if ([string]::IsNullOrWhiteSpace($current) -or $visited.ContainsKey($current)) {
            return $false
        }
        $visited[$current] = $true
        $current = [string](Read-Prop $current 'DEVPKEY_Device_Parent')
    }
    return $false
}

function Format-NTStatus($Value) {
    if ($null -eq $Value) { return '(unavailable)' }
    $s = [string]$Value
    if ($s -match '(?i)^0x([0-9a-f]{1,8})$') {
        return ('0x{0}' -f $Matches[1].ToUpperInvariant().PadLeft(8,'0'))
    }
    if ($s -match '^-?[0-9]{1,20}$') {
        try {
            $number = [long]::Parse($s)
            $bytes = [BitConverter]::GetBytes($number)
            $bits = [BitConverter]::ToUInt32($bytes, 0)
            return ('0x{0:X8}' -f $bits)
        } catch {}
    }
    return '(unrecognized status)'
}

function Get-SafeCode($Text) {
    $s = [string]$Text
    if ($s -match '(?i)^0x[0-9a-f]{1,8}$' -or
        $s -match '^[0-9]{1,10}$') { return $s }
    return '(unavailable)'
}

function Get-EventData($Event) {
    try {
        [xml]$xml = $Event.ToXml()
        $result = @{}
        if ($null -eq $xml.Event.EventData) { return $result }
        foreach ($item in $xml.Event.EventData.Data) {
            if (-not [string]::IsNullOrWhiteSpace([string]$item.Name)) {
                $result[[string]$item.Name] = [string]$item.'#text'
            }
        }
        return $result
    } catch {
        return @{}
    }
}

$all = @(Get-PnpDevice -ErrorAction Stop)
$remotes = @($all | Where-Object {
    $_.Class -eq 'Bluetooth' -and $_.FriendlyName -match '(?i)^DL1|Siri Remote'
})
$hid = @($all | Where-Object {
    $_.Class -eq 'HIDClass' -and $_.InstanceId -match '(?i)^BTHLEDEVICE'
})
Write-Output ("Remote anchors={0}; BLE HIDClass nodes={1}" -f $remotes.Count, $hid.Count)
if ($remotes.Count -ne 1) {
    Write-Output 'No unique remote anchor; cannot attribute the error. No changes made.'
    exit 1
}
$anchor = [string]$remotes[0].InstanceId
$failures = @(
    foreach ($device in $hid) {
        $problem = Read-Prop ([string]$device.InstanceId) 'DEVPKEY_Device_ProblemCode'
        if ([string]$problem -eq '10' -and
            (Test-IsDescendant ([string]$device.InstanceId) $anchor $MaxDepth)) {
            $device
        }
    }
)
Write-Output ("BLE HID problem-code-10 nodes descended from remote: {0}" -f $failures.Count)
if ($failures.Count -ne 1) {
    Write-Output 'No unique failed HID child found; do not infer a driver cause.'
    exit 1
}
$id = [string]$failures[0].InstanceId
$status = Read-Prop $id 'DEVPKEY_Device_ProblemStatus'
$service = [string](Read-Prop $id 'DEVPKEY_Device_Service')
if ($service -notmatch '^[A-Za-z0-9_.-]{1,64}$') { $service = '(unavailable)' }
Write-Output ("HID problem code: 10; service: {0}" -f $service)
Write-Output ("DEVPKEY_Device_ProblemStatus (NTSTATUS): {0}" -f (Format-NTStatus $status))

$path = 'Microsoft-Windows-Kernel-PnP/Configuration'
Write-Output ("Looking for Kernel-PnP event 411 for this exact HID node (last {0} days, max {1} events)..." -f $Days, $MaxEvents)
try {
    $events = @(Get-WinEvent -FilterHashtable @{
        LogName = $path
        Id = 411
        StartTime = (Get-Date).AddDays(-$Days)
    } -MaxEvents $MaxEvents -ErrorAction Stop)
} catch {
    Write-Output 'Event 411 unavailable (log access, no matching events, or log missing).'
    Write-Output 'The PnP problem code and ProblemStatus above remain valid.'
    exit 0
}
$matched = @(
    foreach ($e in $events) {
        $data = Get-EventData $e
        # Match exact ID only, all within process. NEVER print raw event fields.
        if ($data.ContainsKey('DeviceInstanceId') -and
            $data['DeviceInstanceId'] -ieq $id) {
            $eventStatus = if ($data.ContainsKey('Status')) {
                Format-NTStatus $data['Status']
            } elseif ($data.ContainsKey('ProblemStatus')) {
                Format-NTStatus $data['ProblemStatus']
            } else { '(unavailable)' }
            $eventProblem = if ($data.ContainsKey('Problem')) {
                Get-SafeCode $data['Problem']
            } else { '(unavailable)' }
            [pscustomobject]@{
                Time = $e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')
                EventId = 411
                Problem = $eventProblem
                Status = $eventStatus
            }
        }
    }
)
Write-Output ("Matching Kernel-PnP event 411 entries: {0}" -f $matched.Count)
if ($matched.Count -gt 0) {
    $matched | Select-Object -First 8 |
        Format-Table Time, EventId, Problem, Status -AutoSize |
        Out-String -Width 140 | Write-Output
} else {
    Write-Output 'No matching events found in search window; event logs may have rolled over.'
}
Write-Output 'Read-only diagnosis complete. No PnP settings or BLE connection were modified.'
