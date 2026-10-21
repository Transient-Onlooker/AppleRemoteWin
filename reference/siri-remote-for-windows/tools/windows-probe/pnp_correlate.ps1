<#
.SYNOPSIS
Read-only correlation of Bluetooth LE HID PnP nodes to a remote-like parent.

.DESCRIPTION
Uses DEVPKEY_Device_ContainerId and DEVPKEY_Device_Parent internally.
Neither their values nor unique InstanceIds / MACs are printed.
Does not start/stop devices, change pairing, or change drivers.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 12)]
    [int]$MaxDepth = 8
)
$ErrorActionPreference = 'Stop'

function Read-Prop([string]$Id, [string]$Key) {
    try {
        $p = Get-PnpDeviceProperty -InstanceId $Id -KeyName $Key -ErrorAction Stop
        if ($null -ne $p) { return $p.Data }
    } catch {
        # Unavailable properties are possible on historical/non-present nodes.
    }
    return $null
}

function Get-ChainMatch([string]$ChildId, [string]$AnchorId, [int]$Depth) {
    $seen = @{}
    $current = $ChildId
    for ($i = 0; $i -lt $Depth; $i++) {
        if ($current -eq $AnchorId) { return $true }
        if ([string]::IsNullOrWhiteSpace($current) -or $seen.ContainsKey($current)) {
            return $false
        }
        $seen[$current] = $true
        $current = [string](Read-Prop $current 'DEVPKEY_Device_Parent')
    }
    return ($current -eq $AnchorId)
}

$all = @(Get-PnpDevice -ErrorAction Stop)
$remotes = @($all | Where-Object {
    $_.Class -eq 'Bluetooth' -and $_.FriendlyName -match '(?i)^DL1|Siri Remote'
})
$hid = @($all | Where-Object {
    $_.Class -eq 'HIDClass' -and $_.InstanceId -match '(?i)^BTHLEDEVICE'
})

Write-Output ("Remote-like Bluetooth anchors: {0}; BLE HIDClass candidates: {1}" -f $remotes.Count, $hid.Count)
Write-Output 'Raw device IDs, MACs, parent IDs and container GUIDs are intentionally hidden.'
if ($remotes.Count -ne 1) {
    Write-Output 'No unique remote anchor: correlation is inconclusive.'
    exit 1
}
if ($hid.Count -eq 0) {
    Write-Output 'No BLE HIDClass candidates: cannot test driver relationship.'
    exit 1
}
$remote = $remotes[0]
$remoteContainer = Read-Prop ([string]$remote.InstanceId) 'DEVPKEY_Device_ContainerId'
$hasContainer = $null -ne $remoteContainer -and
    [string]$remoteContainer -ne '' -and
    [string]$remoteContainer -ne '00000000-0000-0000-0000-000000000000'

Write-Output ("Remote anchor Status={0}; container metadata available={1}" -f $remote.Status, $hasContainer)
Write-Output 'Relationship: ancestor=yes (PnP parent chain), same-container=yes (shared device container).'
Write-Output 'A shared container is evidence of association, not proof of live HID report access.'

$i = 0
foreach ($node in $hid) {
    $i++
    $id = [string]$node.InstanceId
    $container = Read-Prop $id 'DEVPKEY_Device_ContainerId'
    $sameContainer = $hasContainer -and $null -ne $container -and
        ([string]$container -eq [string]$remoteContainer)
    $ancestor = Get-ChainMatch $id ([string]$remote.InstanceId) $MaxDepth
    $service = [string](Read-Prop $id 'DEVPKEY_Device_Service')
    if ($service -notmatch '^[A-Za-z0-9_.-]{1,64}$') { $service = '(unavailable)' }
    $problem = Read-Prop $id 'DEVPKEY_Device_ProblemCode'
    $problemText = if ($null -eq $problem) { '?' }
                   elseif ([string]$problem -match '^\d{1,5}$') { [string]$problem }
                   else { '(unavailable)' }

    $relationship = if ($ancestor) { 'ancestor' }
                    elseif ($sameContainer) { 'same-container' }
                    elseif (-not $hasContainer -or $null -eq $container) {
                        'unknown (container property missing)'
                    } else { 'no match in tested relationships' }
    Write-Output ("HID candidate #{0}: Status={1}; Service={2}; Problem={3}; Relationship={4}" -f
        $i, $node.Status, $service, $problemText, $relationship)
}
Write-Output 'No drivers were restarted/modified and no BLE connection was requested.'
