<# Synthetic PnP relation test; no BLE hardware required. #>
$ErrorActionPreference = 'Stop'
$remoteId = 'BTHLE\PRIVATE_ANCHOR'
$okId = 'BTHLEDEVICE\PRIVATE_OTHER'
$errorId = 'BTHLEDEVICE\PRIVATE_ERROR'
$remoteContainer = 'aabbccdd-0000-0000-0000-000000000001'
$otherContainer = 'aabbccdd-0000-0000-0000-000000000002'

function Get-PnpDevice {
    [CmdletBinding()]
    param()
    @(
        [pscustomobject]@{
            Class = 'Bluetooth'; FriendlyName = 'DL1PRIVATE-SERIAL-567'
            InstanceId = $remoteId; Status = 'OK'
        }
        [pscustomobject]@{
            Class = 'HIDClass'; FriendlyName = 'Unknown Other Keyboard'
            InstanceId = $okId; Status = 'OK'
        }
        [pscustomobject]@{
            Class = 'HIDClass'; FriendlyName = 'Remote-like HID'
            InstanceId = $errorId; Status = 'Error'
        }
    )
}

function Get-PnpDeviceProperty {
    [CmdletBinding()]
    param([string]$InstanceId, [string]$KeyName)
    $data = switch ("$InstanceId|$KeyName") {
        "$remoteId|DEVPKEY_Device_ContainerId" { $remoteContainer }
        "$okId|DEVPKEY_Device_ContainerId" { $otherContainer }
        "$errorId|DEVPKEY_Device_ContainerId" { $remoteContainer }
        "$errorId|DEVPKEY_Device_Parent" { $remoteId }
        "$okId|DEVPKEY_Device_Parent" { 'BTHLE\PRIVATE_DIFFERENT' }
        "$okId|DEVPKEY_Device_Service" { 'mshidumdf' }
        "$errorId|DEVPKEY_Device_Service" { 'mshidumdf' }
        "$okId|DEVPKEY_Device_ProblemCode" { 0 }
        "$errorId|DEVPKEY_Device_ProblemCode" { 10 }
        default { $null }
    }
    if ($null -ne $data) {
        return [pscustomobject]@{ KeyName = $KeyName; Data = $data }
    }
}

$output = @(& "$PSScriptRoot\pnp_correlate.ps1")
$text = $output -join [environment]::NewLine
if ($text -notmatch 'Remote-like Bluetooth anchors: 1; BLE HIDClass candidates: 2') {
    throw 'PnP anchor/candidate selection failed.'
}
if ($text -notmatch 'HID candidate #2: Status=Error; Service=mshidumdf; Problem=10; Relationship=ancestor') {
    throw 'Expected failed HID candidate to match the remote parent chain.'
}
if ($text -notmatch 'HID candidate #1: Status=OK; Service=mshidumdf; Problem=0; Relationship=no match') {
    throw 'Other keyboard must not be attributed to the remote.'
}
if ($text -match 'PRIVATE|aabbccdd|SERIAL') {
    throw 'PnP correlation output leaked raw device identifiers.'
}
Write-Output 'PnP relationship correlation fixture passed.'
