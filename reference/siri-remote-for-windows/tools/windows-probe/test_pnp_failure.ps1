<# Offline regression test of remote-specific Code 10 failure diagnosis. #>
$ErrorActionPreference = 'Stop'
$anchor = 'BTHLE\PRIVATE_REMOTE_PARENT'
$failed = 'BTHLEDEVICE\PRIVATE_REMOTE_HID'
$healthy = 'BTHLEDEVICE\PRIVATE_KEYBOARD'
$unrelated = 'BTHLEDEVICE\PRIVATE_OTHER_HID'

function Get-PnpDevice {
    [CmdletBinding()]
    param()
    @(
        [pscustomobject]@{
            Class='Bluetooth'; FriendlyName='DL1PRIVATE_SERIAL'
            InstanceId=$anchor; Status='OK'
        }
        [pscustomobject]@{
            Class='HIDClass'; FriendlyName='BLE HID'
            InstanceId=$failed; Status='Error'
        }
        [pscustomobject]@{
            Class='HIDClass'; FriendlyName='Other HID'
            InstanceId=$healthy; Status='OK'
        }
        [pscustomobject]@{
            Class='HIDClass'; FriendlyName='Another HID'
            InstanceId=$unrelated; Status='Error'
        }
    )
}
function Get-PnpDeviceProperty {
    [CmdletBinding()]
    param([string]$InstanceId, [string]$KeyName)
    $data = switch ("$InstanceId|$KeyName") {
        "$failed|DEVPKEY_Device_ProblemCode" { 10 }
        "$healthy|DEVPKEY_Device_ProblemCode" { 0 }
        "$unrelated|DEVPKEY_Device_ProblemCode" { 10 }
        "$failed|DEVPKEY_Device_Parent" { $anchor }
        "$healthy|DEVPKEY_Device_Parent" { 'BTHLE\PRIVATE_OTHER_PARENT' }
        "$unrelated|DEVPKEY_Device_Parent" { 'BTHLE\PRIVATE_OTHER_PARENT' }
        "$failed|DEVPKEY_Device_ProblemStatus" { [int64]-1073741811 }
        "$failed|DEVPKEY_Device_Service" { 'mshidumdf' }
        default { $null }
    }
    if ($null -ne $data) {
        return [pscustomobject]@{ KeyName=$KeyName; Data=$data }
    }
}
function New-FakeEvent($id, $problem, $status) {
    $element = [System.Security.SecurityElement]::Escape($id)
    $xmlText = '<Event><EventData>' +
        '<Data Name="DeviceInstanceId">' + $element + '</Data>' +
        '<Data Name="Problem">' + $problem + '</Data>' +
        '<Data Name="Status">' + $status + '</Data>' +
        '</EventData></Event>'
    $item = [pscustomobject]@{
        TimeCreated = [datetime]'2026-10-17T14:00:00'
        XmlText = $xmlText
    }
    $item | Add-Member -MemberType ScriptMethod -Name ToXml -Value {
        return $this.XmlText
    }
    return $item
}
function Get-WinEvent {
    [CmdletBinding()]
    param([hashtable]$FilterHashtable, [int]$MaxEvents)
    @(
        (New-FakeEvent $unrelated 10 '0xC000000E')
        (New-FakeEvent $failed 10 '0xC000000D')
    )
}

$output = @(& "$PSScriptRoot\pnp_failure.ps1")
$text = $output -join [environment]::NewLine
if ($text -notmatch 'BLE HID problem-code-10 nodes descended from remote: 1') {
    throw 'Failed to isolate the remote-owned HID node.'
}
if ($text -notmatch 'DEVPKEY_Device_ProblemStatus \(NTSTATUS\): 0xC000000D') {
    throw 'Failed to format the signed negative NTSTATUS property.'
}
if ($text -notmatch 'Matching Kernel-PnP event 411 entries: 1') {
    throw 'Failed to match the correct event or ignored the unrelated event.'
}
if ($text -match 'PRIVATE|DL1PRIVATE|SERIAL|C000000E') {
    throw 'Sensitive identifier or unrelated event appeared in output.'
}
Write-Output 'Remote-specific Code 10 and sanitized event 411 tests passed.'
