<# Offline regression tests for PnP summary and bounded metadata mode. #>
$ErrorActionPreference = 'Stop'
$global:PnpTestQueryCount = 0

function Get-PnpDevice {
    [CmdletBinding()]
    param()
    @(
        [pscustomobject]@{
            Class = 'Bluetooth'
            Status = 'OK'
            FriendlyName = 'DL1PRIVATE-SERIAL-123'
            InstanceId = 'BTHLEDEVICE\PRIVATE_ID_A'
        }
        [pscustomobject]@{
            Class = 'Bluetooth'
            Status = 'Unknown'
            FriendlyName = 'Bluetooth 10:b9:c4:2d:07:8e'
            InstanceId = 'BTHLE\PRIVATE_ID_B'
        }
        [pscustomobject]@{
            Class = 'HIDClass'
            Status = 'OK'
            FriendlyName = 'Keyboard'
            InstanceId = 'HID\PRIVATE_ID_C'
        }
    )
}

function Get-PnpDeviceProperty {
    [CmdletBinding()]
    param([string[]]$InstanceId, [string[]]$KeyName)
    $global:PnpTestQueryCount++
    @(
        [pscustomobject]@{ KeyName = 'DEVPKEY_Device_Service'; Data = 'HidBthLE' }
        [pscustomobject]@{ KeyName = 'DEVPKEY_Device_DriverDesc'; Data = 'Bluetooth HID Device' }
        [pscustomobject]@{ KeyName = 'DEVPKEY_Device_ProblemCode'; Data = 0 }
    )
}

try {
    $summary = @(& "$PSScriptRoot\pnp_probe.ps1")
    $s = $summary -join [environment]::NewLine
    if ($global:PnpTestQueryCount -ne 0) {
        throw "Quick inventory queried PnP properties $global:PnpTestQueryCount times"
    }
    if ($s -notmatch 'All PnP devices: 3' -or $s -notmatch 'BLE-related or remote-like nodes: 2') {
        throw 'Quick inventory did not summarize synthetic BLE candidates'
    }
    if ($s -match 'PRIVATE_ID|PRIVATE-SERIAL|10:b9:c4:2d:07:8e') {
        throw 'Quick inventory exposed a private device identifier'
    }

    $detail = @(& "$PSScriptRoot\pnp_probe.ps1" -Details -DetailLimit 2)
    $d = $detail -join [environment]::NewLine
    if ($global:PnpTestQueryCount -ne 2) {
        throw "Expected exactly two bounded property calls, got $global:PnpTestQueryCount"
    }
    if ($d -notmatch 'HidBthLE' -or $d -match 'PRIVATE_ID|PRIVATE-SERIAL') {
        throw 'Detailed mode failed or exposed sensitive fields'
    }
    Write-Output 'PnP quick and bounded-detail mock tests passed.'
} finally {
    Remove-Variable -Name PnpTestQueryCount -Scope Global -ErrorAction SilentlyContinue
}
