# SPDX-License-Identifier: AGPL-3.0-or-later
<#
.SYNOPSIS
Final, targeted A2854 Windows HID Code-10 restart experiment.

.DESCRIPTION
-Check (default): read-only; find exactly one paired DL1/Siri Remote
Bluetooth PnP anchor and the descendant HIDClass node with mshidumdf,
Code 10, NTSTATUS 0xC00000B9. Never displays unique device identifiers.
-Restart: elevated action to restart ONLY that verified failed HID child.
Does not touch Intel AX200/BTHUSB, pairing, INF packages, USB/IP or WinUSB.
No reboot, driver install, or registry changes.

WARNING: A targeted PnP restart still changes state and may temporarily
disconnect the Siri Remote; it may not recover Code 10.
#>
[CmdletBinding()]
param(
    [switch]$Restart
)

$ErrorActionPreference = 'Stop'
$expectedStatus = '0xC00000B9'

function Read-Property([string]$instanceId, [string]$key) {
    try {
        $result = Get-PnpDeviceProperty -InstanceId $instanceId -KeyName $key -ErrorAction Stop
        if ($null -ne $result) { return $result.Data }
    } catch {}
    return $null
}

function Format-NtStatus($value) {
    if ($null -eq $value) { return '(unavailable)' }
    $s = ([string]$value).Trim()
    if ($s -match '^(?i:0x)([0-9a-fA-F]{1,8})$') {
        return '0x' + $Matches[1].ToUpperInvariant().PadLeft(8, '0')
    }
    try {
        $number = [long]::Parse($s)
        return ('0x{0:X8}' -f [BitConverter]::ToUInt32([BitConverter]::GetBytes($number), 0))
    } catch { return '(unavailable)' }
}

function Test-IsDescendant([string]$child, [string]$parent) {
    $seen = @{}
    for ($i = 0; $i -lt 12; $i++) {
        if ($child -ieq $parent) { return $true }
        if ([string]::IsNullOrWhiteSpace($child) -or $seen.ContainsKey($child)) {
            return $false
        }
        $seen[$child] = $true
        $child = [string](Read-Property $child 'DEVPKEY_Device_Parent')
    }
    return $false
}

# The earlier scripts used output-bearing PnP functions; keep only one
# target object so the unique instance path never appears in console output.
$target = $null
try {
    $all = @(Get-PnpDevice -ErrorAction Stop)
    $remotes = @($all | Where-Object {
        $_.Class -eq 'Bluetooth' -and
        $_.FriendlyName -match '(?i)^(DL1|Siri Remote)'
    })
    Write-Output ('Remote anchor candidates: {0}' -f $remotes.Count)
    if ($remotes.Count -ne 1) { throw 'No unique remote anchor; stopping.' }
    $remoteId = [string]$remotes[0].InstanceId
    $matches = @(
        foreach ($node in $all) {
            $id = [string]$node.InstanceId
            if ($node.Class -ne 'HIDClass' -or
                $id -notmatch '(?i)^BTHLEDEVICE\\' -or
                -not (Test-IsDescendant $id $remoteId)) { continue }
            if ([string](Read-Property $id 'DEVPKEY_Device_Service') -ne 'mshidumdf') { continue }
            $problem = [string](Read-Property $id 'DEVPKEY_Device_ProblemCode')
            $status = Format-NtStatus (Read-Property $id 'DEVPKEY_Device_ProblemStatus')
            if ($problem -eq '10' -and $status -eq $expectedStatus) { $node }
        }
    )
    Write-Output ('Unique failed HID candidates: {0}' -f $matches.Count)
    if ($matches.Count -ne 1) { throw 'No unique matching failed HID child; stopping.' }
    $target = $matches[0]
} catch {
    Write-Output ('Preflight: STOP ({0}). No changes made.' -f $_.Exception.GetType().Name)
    Write-Output 'Run the earlier read-only PnP diagnostics to investigate.'
    exit 2
}

Write-Output 'Target checks passed: one remote descendant, HIDClass, mshidumdf, Code 10, NTSTATUS 0xC00000B9.'
if (-not $Restart) {
    Write-Output 'CHECK ONLY: no system modification. Run -Restart once from elevated PowerShell to perform the last experiment.'
    exit 0
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Output 'Administrator PowerShell required for targeted restart. No change made.'
    exit 2
}

$id = [string]$target.InstanceId
Write-Output 'Restarting only the verified FAILED A2854 HID child. The Intel AX200/BTHUSB adapter is not being restarted.'
& pnputil.exe /restart-device $id | Out-Null
$code = $LASTEXITCODE
Write-Output ('PnP restart exit code: {0}' -f $code)
Start-Sleep -Seconds 5

$after = Get-PnpDevice -InstanceId $id -ErrorAction SilentlyContinue
if ($null -eq $after) {
    Write-Output 'After restart: remote HID node not currently enumerated.'
    exit 1
}
$problemAfter = Read-Property $id 'DEVPKEY_Device_ProblemCode'
$statusAfter = Format-NtStatus (Read-Property $id 'DEVPKEY_Device_ProblemStatus')
Write-Output ('After restart: device status={0}; problem code={1}; NTSTATUS={2}' -f
    ([string]$after.Status), ([string]$problemAfter), $statusAfter)
if ($code -eq 0 -and $after.Status -eq 'OK' -and
    ([string]$problemAfter -eq '0' -or $null -eq $problemAfter)) {
    Write-Output 'HID device initialization RECOVERED. Next, test an actual HID input interface; no report capture was performed here.'
    exit 0
}
Write-Output 'HID not recovered (or not proven recovered). Do not repeatedly restart or change the Intel driver.'
exit 1
