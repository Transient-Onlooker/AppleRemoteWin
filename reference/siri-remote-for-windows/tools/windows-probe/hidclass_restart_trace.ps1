<#
.SYNOPSIS
Restart only the uniquely identified, Code-10 Siri Remote HID child during
a bounded manifested HIDCLASS ETW trace.

.DESCRIPTION
Explicit -Execute required. Intended for administrator Windows PowerShell.
This *does* restart one PnP HID child device; it does NOT reset the Intel
Bluetooth adapter, unpair, remove a device, install a driver, or reboot.
The trace may contain private device instance IDs and must remain local.

Only sanitized counts/statuses are printed: never InstanceId, BLE address,
event XML/message or descriptor bytes.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\windows-probe\hidclass_restart_trace.ps1 -Execute
#>
[CmdletBinding()]
param(
    [switch]$Execute,
    [ValidateRange(5,60)]
    [int]$ObserveSeconds = 12
)
$ErrorActionPreference = 'Stop'
$providerName = 'Microsoft-Windows-Input-HIDCLASS'
$session = 'SiriHidRestart_' + $PID
$etl = Join-Path ([IO.Path]::GetTempPath()) ('siri_hid_restart_{0}_{1}.etl' -f (Get-Date -Format 'yyyyMMdd_HHmmss'), $PID)

function Get-PnpPropertySafe([string]$instanceId, [string]$name) {
    try {
        $property = Get-PnpDeviceProperty -InstanceId $instanceId -KeyName $name -ErrorAction Stop
        if ($null -ne $property) { return $property.Data }
    } catch {}
    return $null
}

function Is-ChildOf([string]$child, [string]$parent) {
    if ([string]::IsNullOrWhiteSpace($child) -or [string]::IsNullOrWhiteSpace($parent)) {
        return $false
    }
    $visited = @{}
    $current = $child
    for ($depth = 0; $depth -le 10; $depth++) {
        if ($current -ieq $parent) { return $true }
        if ($visited.ContainsKey($current)) { break }
        $visited[$current] = $true
        $current = [string](Get-PnpPropertySafe $current 'DEVPKEY_Device_Parent')
        if ([string]::IsNullOrWhiteSpace($current)) { break }
    }
    return $false
}

function Format-SafeStatus($value) {
    if ($null -eq $value) { return '(unavailable)' }
    $s = ([string]$value).Trim()
    if ($s -match '(?i)^0x([a-f0-9]{1,8})$') {
        return '0x' + $Matches[1].ToUpperInvariant().PadLeft(8,'0')
    }
    try {
        $number = [long]::Parse($s)
        return ('0x{0:X8}' -f [BitConverter]::ToUInt32([BitConverter]::GetBytes($number),0))
    } catch { return '(unavailable)' }
}

function EventDataByName($event) {
    $map = @{}
    try {
        [xml]$x = $event.ToXml()
        foreach ($d in @($x.SelectNodes('//*[local-name()="EventData"]/*[local-name()="Data"]'))) {
            $name = [string]$d.GetAttribute('Name')
            # Read only the minimal allowlisted keys; all remain local.
            if (@('DeviceInstancePath','DeviceInstanceId','DeviceStartStatus','Status','Problem') -contains $name) {
                if (-not $map.ContainsKey($name)) { $map[$name] = [string]$d.InnerText }
            }
        }
    } catch {}
    return $map
}

if (-not $Execute) {
    Write-Output 'Preview only: pass -Execute to restart the unique failed Siri Remote HID child.'
    Write-Output 'No driver, pairing, trace or PnP state has been changed.'
    exit 0
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Open an administrator PowerShell window before using -Execute.'
}

$all = @(Get-PnpDevice -ErrorAction Stop)
$anchors = @($all | Where-Object {
    $_.Class -eq 'Bluetooth' -and $_.FriendlyName -match '(?i)^(DL1)|Siri Remote'
})
if ($anchors.Count -ne 1) { throw 'No unique DL1/Siri Remote Bluetooth anchor. Aborting without changes.' }
$anchor = [string]$anchors[0].InstanceId

$candidates = @(
    $all | Where-Object {
        $_.Class -eq 'HIDClass' -and
        $_.InstanceId -match '(?i)^BTHLEDEVICE' -and
        (Is-ChildOf ([string]$_.InstanceId) $anchor) -and
        [string](Get-PnpPropertySafe ([string]$_.InstanceId) 'DEVPKEY_Device_ProblemCode') -eq '10' -and
        [string](Get-PnpPropertySafe ([string]$_.InstanceId) 'DEVPKEY_Device_Service') -eq 'mshidumdf'
    }
)
Write-Output ("Unique remote anchor found; matching failed HID child nodes: {0}" -f $candidates.Count)
if ($candidates.Count -ne 1) {
    throw 'Expected exactly one mshidumdf Code 10 HID child. No restart attempted.'
}
$target = [string]$candidates[0].InstanceId
$before = Format-SafeStatus (Get-PnpPropertySafe $target 'DEVPKEY_Device_ProblemStatus')
Write-Output ("Before: HID child Code 10; NTSTATUS={0}" -f $before)
if ($before -ne '0xC00000B9') {
    throw 'ProblemStatus is not the previously observed 0xC00000B9. Aborting.'
}

$started = $false
$exitCode = $null
$restartTime = $null
try {
    & logman.exe start $session -p $providerName '0x8000000000000001' '4' -o $etl -ets 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'ETW trace could not start.' }
    $started = $true
    Write-Output 'HIDCLASS ETW started. Restarting ONLY the confirmed Siri Remote HID child.'
    Start-Sleep -Seconds 2
    $restartTime = Get-Date
    # The ID stays in a PowerShell variable; pnputil console output is suppressed.
    & pnputil.exe /restart-device $target 2>&1 | Out-Null
    $exitCode = $LASTEXITCODE
    Write-Output ("Targeted PnP restart command exit code: {0}" -f $exitCode)
    Start-Sleep -Seconds $ObserveSeconds
} finally {
    if ($started) {
        & logman.exe stop $session -ets 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'ETW trace stop failed. Inspect logman query -ets before further tracing.'
        } else {
            Write-Output 'HIDCLASS ETW stopped.'
        }
    }
}

$afterCode = Get-PnpPropertySafe $target 'DEVPKEY_Device_ProblemCode'
$afterStatus = Format-SafeStatus (Get-PnpPropertySafe $target 'DEVPKEY_Device_ProblemStatus')
Write-Output ("After: HID child problem code={0}; NTSTATUS={1}" -f $(if ($null -eq $afterCode) { '(unavailable)' } else { [string]$afterCode }), $afterStatus)

if (Test-Path -LiteralPath $etl) {
    try {
        $events = @(Get-WinEvent -Path $etl -Oldest -ErrorAction Stop |
            Where-Object { $_.ProviderName -eq $providerName })
        Write-Output ("Trace: HIDCLASS event count={0}" -f $events.Count)
        $exact = 0
        $statusList = @()
        foreach ($event in $events) {
            if ($event.Id -ne 3 -or $event.Version -ne 2) { continue }
            $data = EventDataByName $event
            if ($data.ContainsKey('DeviceInstancePath') -and $data['DeviceInstancePath'] -ieq $target) {
                $exact++
                if ($data.ContainsKey('DeviceStartStatus')) {
                    $statusList += Format-SafeStatus $data['DeviceStartStatus']
                }
            }
        }
        Write-Output ("Trace: Event 3 v2 records exactly matching failed HID child={0}" -f $exact)
        if ($statusList.Count -gt 0) {
            $statusList | Group-Object | ForEach-Object {
                Write-Output ("  DeviceStartStatus={0}; events={1}" -f $_.Name, $_.Count)
            }
        }
    } catch {
        Write-Output 'ETW analysis unavailable. This is inconclusive.'
    }
}

if ($null -ne $restartTime) {
    try {
        $evts = @(Get-WinEvent -FilterHashtable @{
            LogName = 'Microsoft-Windows-Kernel-PnP/Configuration'
            Id = 411
            StartTime = $restartTime.AddSeconds(-2)
        } -MaxEvents 200 -ErrorAction Stop)
        $matching = @($evts | Where-Object {
            $d = EventDataByName $_
            $d.ContainsKey('DeviceInstanceId') -and $d['DeviceInstanceId'] -ieq $target
        })
        Write-Output ("Kernel-PnP 411 for exact target during restart window: {0}" -f $matching.Count)
        foreach ($event in $matching | Select-Object -First 3) {
            $d = EventDataByName $event
            Write-Output ("  PnP Status={0}" -f $(if ($d.ContainsKey('Status')) { Format-SafeStatus $d['Status'] } else { '(unavailable)' }))
        }
    } catch {
        Write-Output 'Kernel-PnP 411 query unavailable or no events were found.'
    }
}

Write-Output 'Raw ETL remains ONLY in local TEMP. Do not upload raw ETL, event XML or device identifiers.'
Write-Output 'A PnP restart attempt may fail or leave Code 10 unchanged; neither outcome proves a malformed HID descriptor.'
