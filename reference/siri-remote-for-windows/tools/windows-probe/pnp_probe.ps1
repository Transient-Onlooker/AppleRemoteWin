<#
.SYNOPSIS
Fast, read-only inventory of Windows Bluetooth/HID PnP nodes.

.DESCRIPTION
Default uses one Get-PnpDevice call and no per-device property queries.
Optional -Details queries up to -DetailLimit candidate devices, once per
device requesting three keys together. Output suppresses all device IDs,
MAC addresses, serials, hardware IDs, container GUIDs, and unrecognized
friendly names. Never changes pairing, drivers or device settings.
#>

[CmdletBinding()]
param(
    [switch]$Details,
    [ValidateRange(1, 20)]
    [int]$DetailLimit = 8
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue)) {
    Write-Error 'Get-PnpDevice is unavailable; use native Windows PowerShell.'
    exit 2
}

function Get-SafeName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return '(unnamed)' }
    if ($Name -match '(?i)^DL1|Siri Remote') {
        return '(remote-like name, redacted)'
    }
    if ($Name -match '(?i)(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}' -or
        $Name -match '(?i)[0-9a-f]{12}') {
        return '(identifier-bearing name, redacted)'
    }
    return '(other device)'
}

function Get-NodeFamily($Device) {
    if ($Device.InstanceId -match '(?i)BTHLEDEVICE') { return 'BTHLEDEVICE' }
    if ($Device.InstanceId -match '(?i)BTHLE') { return 'BTHLE-other' }
    if ($Device.Class -eq 'HIDClass') { return 'HIDClass-other' }
    return 'Bluetooth-other'
}

function Get-Details([object[]]$Devices) {
    $keys = @('DEVPKEY_Device_Service', 'DEVPKEY_Device_DriverDesc',
              'DEVPKEY_Device_ProblemCode')
    foreach ($device in $Devices) {
        $props = @()
        try {
            # Fetch all three properties in one CIM operation per node.
            $props = @(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName $keys -ErrorAction Stop)
        } catch {
            Write-Output ([pscustomobject]@{
                Class = [string]$device.Class
                Status = [string]$device.Status
                Family = Get-NodeFamily $device
                Service = '(metadata unavailable)'
                Driver = '(metadata unavailable)'
                Problem = '?'
            })
            continue
        }
        $values = @{}
        foreach ($p in $props) { $values[[string]$p.KeyName] = $p.Data }
        $service = $values['DEVPKEY_Device_Service']
        $desc = $values['DEVPKEY_Device_DriverDesc']
        $problem = $values['DEVPKEY_Device_ProblemCode']
        $serviceText = if ([string]::IsNullOrWhiteSpace([string]$service)) {
            '(none)'
        } elseif ([string]$service -match '^[A-Za-z0-9_.-]{1,64}$') {
            [string]$service
        } else {
            '(redacted)'
        }
        $driverClass = if ([string]$desc -match '(?i)HID.*Bluetooth|Bluetooth.*HID') {
            'Bluetooth HID'
        } elseif ([string]$desc -match '(?i)Bluetooth') {
            'Bluetooth (other)'
        } elseif ($null -eq $desc) {
            '(none)'
        } else {
            '(other)'
        }
        Write-Output ([pscustomobject]@{
            Class = [string]$device.Class
            Status = [string]$device.Status
            Family = Get-NodeFamily $device
            Service = $serviceText
            Driver = $driverClass
            Problem = if ($null -eq $problem) { '?' } else { [string]$problem }
        })
    }
}

try {
    $all = @(Get-PnpDevice -ErrorAction Stop)
} catch {
    Write-Error 'Cannot enumerate Windows PnP devices.'
    exit 2
}
$filtered = @($all | Where-Object {
    $_.Class -in @('Bluetooth', 'HIDClass') -or
    $_.FriendlyName -match '(?i)^DL1|Siri Remote' -or
    $_.InstanceId -match '(?i)BTHLE'
})

Write-Output ("All PnP devices: {0}; Bluetooth/HIDClass/BTHLE candidates: {1}" -f $all.Count, $filtered.Count)
Write-Output 'Candidate nodes may be stale or unrelated to the A2854.'
Write-Output 'Device identifiers, addresses and nonstandard names are suppressed.'

$rows = @(
    foreach ($device in $filtered) {
        [pscustomobject]@{
            Class = [string]$device.Class
            Status = [string]$device.Status
            Family = Get-NodeFamily $device
            Name = Get-SafeName ([string]$device.FriendlyName)
        }
    }
)
Write-Output ''
Write-Output 'Quick inventory (no per-device property queries):'
if ($rows.Count -eq 0) {
    Write-Output '(no matching nodes)'
} else {
    $rows | Group-Object Class, Status, Family | Sort-Object Count -Descending |
        Select-Object Count, Name | Format-Table -AutoSize |
        Out-String -Width 160 | Write-Output
}

$interesting = @($rows | Where-Object {
    $_.Family -eq 'BTHLEDEVICE' -or
    $_.Family -eq 'BTHLE-other' -or
    $_.Name -eq '(remote-like name, redacted)'
})
Write-Output ("BLE-related or remote-like nodes: {0}" -f $interesting.Count)
if ($interesting.Count -gt 0) {
    $interesting | Group-Object Class, Status, Family, Name |
        Sort-Object Count -Descending |
        Select-Object Count, Name | Format-Table -AutoSize |
        Out-String -Width 160 | Write-Output
}
Write-Output 'No HID binding conclusion can be drawn from PnP counts alone.'

if ($Details) {
    $priority = @($filtered | Where-Object {
        $_.FriendlyName -match '(?i)^DL1|Siri Remote' -or
        $_.InstanceId -match '(?i)BTHLE'
    } | Select-Object -First $DetailLimit)
    Write-Output ''
    Write-Output ("Detailed property queries: {0} of up to {1} nodes" -f $priority.Count, $DetailLimit)
    if ($priority.Count -eq 0) {
        Write-Output 'No BLE-specific nodes selected.'
    } else {
        $detailRows = @(Get-Details $priority)
        $detailRows | Format-Table -AutoSize |
            Out-String -Width 160 | Write-Output
    }
} else {
    Write-Output 'Optional: -Details -DetailLimit 8 (slower; only if needed).'
}
