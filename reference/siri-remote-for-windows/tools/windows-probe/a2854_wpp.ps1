<#
.SYNOPSIS
A2854 HIDCLASS WPP experiment, using Microsoft's InputOnlyWithVerboseWppProfile.

.DESCRIPTION
Modes:
  Check   - read-only system and target diagnostics; does not download/trace.
  Prepare - download ONLY Microsoft's .wprp XML from a pinned upstream commit
            to local TEMP, and validate the named HIDCLASS WPP profile.
  Capture - EXPLICIT consent to restart exactly one already-failed Siri Remote
            HID child under a bounded Windows Performance Recorder WPP session.
            No pairing changes, driver replacement, system policy changes, or
            reboot. Raw trace is SENSITIVE and remains ONLY in local TEMP.

Windows Performance Recorder can conflict with a pre-existing WPR session.
If starting WPR fails, the script DOES NOT restart the HID child.
The 12 second trace uses the profile's Memory logging mode (no -filemode).
If a stop fails, the user must inspect and stop the session manually.

This script NEVER runs BusesTrace.cmd, which also changes UMDF registry
diagnostic settings. Only its upstream, declarative .wprp XML is used.

The Windows image might not support the official upstream .wprp unchanged;
a failure is a negative environment-preflight, not an A2854 diagnosis.

Learn more: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues
Upstream XML: https://github.com/microsoft/busiotools/tree/master/usb/tracing

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_wpp.ps1" -Mode Check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_wpp.ps1" -Mode Prepare
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_wpp.ps1" -Mode Capture
#>
[CmdletBinding()]
param(
    [ValidateSet('Check','Prepare','Capture')]
    [string]$Mode = 'Check',
    [ValidateRange(5,30)]
    [int]$ObserveSeconds = 12
)
$ErrorActionPreference = 'Stop'
$upstreamRevision = 'f9c6f70775276c1b82c3541317820b489e59a9f9'
$upstreamUri = 'https://raw.githubusercontent.com/microsoft/busiotools/' +
    $upstreamRevision + '/usb/tracing/BusesAllProfile.wprp'
$profilePath = Join-Path ([IO.Path]::GetTempPath()) 'A2854_Input_Wpp_Microsoft.wprp'
$profileName = 'InputOnlyWithVerboseWppProfile'
$expectedStatus = '0xC00000B9'
$tracePattern = 'a2854_wpp_*.etl'

function Get-PnpPropSafe([string]$id,[string]$key) {
    try {
        $p = Get-PnpDeviceProperty -InstanceId $id -KeyName $key -ErrorAction Stop
        if ($null -ne $p) { return $p.Data }
    } catch {}
    return $null
}
function Get-Hex32($value) {
    if ($null -eq $value) { return '(unknown)' }
    $v = ([string]$value).Trim()
    if ($v -match '(?i)^0x([a-f0-9]{1,8})$') {
        return '0x' + $Matches[1].ToUpperInvariant().PadLeft(8,'0')
    }
    try {
        $n = [long]::Parse($v)
        return ('0x{0:X8}' -f [BitConverter]::ToUInt32([BitConverter]::GetBytes($n), 0))
    } catch { return '(unknown)' }
}
function Has-Ancestor([string]$child,[string]$ancestor) {
    if ([string]::IsNullOrWhiteSpace($child) -or [string]::IsNullOrWhiteSpace($ancestor)) {
        return $false
    }
    $seen = @{}
    for ($i = 0; $i -le 12; $i++) {
        if ($child -ieq $ancestor) { return $true }
        if ($seen.ContainsKey($child)) { break }
        $seen[$child] = $true
        $child = [string](Get-PnpPropSafe $child 'DEVPKEY_Device_Parent')
        if ([string]::IsNullOrWhiteSpace($child)) { break }
    }
    return $false
}
function Get-FailedRemoteTarget {
    $devices = @(Get-PnpDevice -ErrorAction Stop)
    $anchors = @($devices | Where-Object {
        $_.Class -eq 'Bluetooth' -and
        $_.FriendlyName -match '(?i)^(DL1)|Siri Remote'
    })
    if ($anchors.Count -ne 1) {
        throw 'Safety check: paired Siri Remote Bluetooth anchor is not unique.'
    }
    $anchorId = [string]$anchors[0].InstanceId
    $children = @($devices | Where-Object {
        $_.Class -eq 'HIDClass' -and
        $_.InstanceId -match '(?i)^BTHLEDEVICE' -and
        (Has-Ancestor ([string]$_.InstanceId) $anchorId) -and
        [string](Get-PnpPropSafe ([string]$_.InstanceId) 'DEVPKEY_Device_Service') -eq 'mshidumdf' -and
        [string](Get-PnpPropSafe ([string]$_.InstanceId) 'DEVPKEY_Device_ProblemCode') -eq '10' -and
        (Get-Hex32 (Get-PnpPropSafe ([string]$_.InstanceId) 'DEVPKEY_Device_ProblemStatus')) -eq $expectedStatus
    })
    if ($children.Count -ne 1) {
        throw 'Safety check: expected exactly one remote mshidumdf Code-10/0xC00000B9 child.'
    }
    return [string]$children[0].InstanceId
}
function Assert-Profile([string]$file) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw 'Official WPRP is missing; run -Mode Prepare before Capture.'
    }
    $size = (Get-Item -LiteralPath $file).Length
    if ($size -lt 20000 -or $size -gt 200000) {
        throw 'Unexpected official WPRP size; refuse capture.'
    }
    [xml]$doc = Get-Content -LiteralPath $file -Raw -Encoding UTF8
    $profiles = @($doc.SelectNodes('//*[local-name()="Profile"]'))
    $match = @($profiles | Where-Object {
        $_.GetAttribute('Name') -eq $profileName -and
        $_.GetAttribute('DetailLevel') -eq 'Verbose' -and
        $_.GetAttribute('LoggingMode') -eq 'Memory'
    })
    if ($match.Count -ne 1) {
        throw 'Official verbose memory WPP profile missing or ambiguous.'
    }
    $providerIds = @($match[0].SelectNodes('.//*[local-name()="EventProviderId"]') |
        ForEach-Object { $_.GetAttribute('Value') })
    if ('HidClassWppVerbose' -notin $providerIds -or
        'HidClassEtw' -notin $providerIds) {
        throw 'Selected Microsoft profile does not include both HIDCLASS WPP and manifested ETW.'
    }
    Write-Output 'Microsoft XML validated: verbose HIDCLASS WPP + HIDCLASS ETW; memory mode.'
}
function Is-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent()
    )
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Print-Preflight {
    $build = [Environment]::OSVersion.Version.Build
    Write-Output ('Windows build: {0}' -f $build)
    Write-Output ('Administrator: {0}' -f (Is-Administrator))
    foreach ($name in @('wpr.exe','wpa.exe','logman.exe','pnputil.exe','python.exe')) {
        $found = [bool](Get-Command $name -ErrorAction SilentlyContinue)
        Write-Output ('Tool {0}: {1}' -f $name, $(if ($found) { 'found' } else { 'not on PATH' }))
    }
    Write-Output ('Official WPRP present: {0}' -f (Test-Path -LiteralPath $profilePath -PathType Leaf))
    try {
        $id = Get-FailedRemoteTarget
        $status = Get-Hex32 (Get-PnpPropSafe $id 'DEVPKEY_Device_ProblemStatus')
        Write-Output ('Exactly one matching failed Siri Remote HID child: yes; NTSTATUS={0}' -f $status)
    } catch {
        Write-Output 'Exactly one matching failed Siri Remote HID child: no / unavailable.'
        Write-Output 'Capture is blocked until uniqueness and exact Code 10 conditions hold.'
    }
    Write-Output ('Local WPP traces already present: {0}' -f @(
        Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Filter $tracePattern -File -ErrorAction SilentlyContinue
    ).Count)
    Write-Output 'No system modifications performed in Check mode.'
}

if ($Mode -eq 'Check') {
    Print-Preflight
    exit 0
}
if ($Mode -eq 'Prepare') {
    $tempDownload = $profilePath + '.tmp_' + $PID
    try {
        if (Test-Path -LiteralPath $profilePath -PathType Leaf) {
            Assert-Profile $profilePath
            Write-Output 'Existing local XML is valid; not downloading again.'
        } else {
            Write-Output 'Downloading ONLY the pinned official Microsoft WPRP XML to local TEMP.'
            Invoke-WebRequest -Uri $upstreamUri -OutFile $tempDownload -UseBasicParsing -TimeoutSec 40
            Assert-Profile $tempDownload
            Move-Item -LiteralPath $tempDownload -Destination $profilePath -ErrorAction Stop
            Write-Output 'WPP profile prepared locally. No command scripts executed.'
        }
    } finally {
        if (Test-Path -LiteralPath $tempDownload) {
            Remove-Item -LiteralPath $tempDownload -Force -ErrorAction SilentlyContinue
        }
    }
    exit 0
}

# Capture is an explicit state-changing operation and requires elevation.
if (-not (Is-Administrator)) { throw 'Capture requires an administrator PowerShell.' }
if (-not (Get-Command wpr.exe -ErrorAction SilentlyContinue)) {
    throw 'wpr.exe unavailable on this Windows image.'
}
Assert-Profile $profilePath
$target = Get-FailedRemoteTarget
Write-Output 'Safety checks passed: exactly one failed A2854 HID child; other devices untouched.'

# A fresh filename prevents silently overwriting older trace evidence.
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$etlPath = Join-Path ([IO.Path]::GetTempPath()) ('a2854_wpp_{0}_{1}.etl' -f $stamp, $PID)
if (Test-Path -LiteralPath $etlPath) { throw 'Trace destination already exists; abort.' }
$profileArg = $profilePath + '!' + $profileName

# WPR is global. Never cancel/stop a recording started by someone else.
$started = $false
$stopped = $false
$restartCode = $null
try {
    Write-Output 'Starting official HIDCLASS verbose WPP profile in bounded memory mode.'
    & wpr.exe -start $profileArg 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'WPR start failed; NO PnP restart was attempted.' }
    $started = $true
    Start-Sleep -Seconds 2
    Write-Output 'Restarting ONLY the validated failed A2854 HID child once.'
    & pnputil.exe /restart-device $target 2>&1 | Out-Null
    $restartCode = $LASTEXITCODE
    Write-Output ('Target-only pnputil exit code: {0}' -f $restartCode)
    Start-Sleep -Seconds $ObserveSeconds
} finally {
    if ($started) {
        Write-Output 'Stopping the WPR session and writing LOCAL trace.'
        & wpr.exe -stop $etlPath 2>&1 | Out-Null
        $stopped = ($LASTEXITCODE -eq 0)
        if (-not $stopped) {
            Write-Warning 'WPR stop failed. Inspect wpr -status and use wpr -stop <local file> manually; do not start another recording.'
        }
    }
}

$finalStatus = Get-Hex32 (Get-PnpPropSafe $target 'DEVPKEY_Device_ProblemStatus')
$code = Get-PnpPropSafe $target 'DEVPKEY_Device_ProblemCode'
Write-Output ("After target restart: problem code={0}, NTSTATUS={1}" -f $code, $finalStatus)
if ($stopped -and (Test-Path -LiteralPath $etlPath)) {
    $file = Get-Item -LiteralPath $etlPath
    Write-Output ('Private WPP ETL saved in local TEMP: yes; size={0} MiB' -f
        [math]::Round($file.Length / 1MB, 2))
    Write-Output 'Open latest a2854_wpp_*.etl locally in Windows Performance Analyzer.'
    Write-Output 'In WPA: System Activity > WPP Trace, Load Symbols, filter GUID Name HIDCLASS and Error.'
    Write-Output 'Look for descriptor parse error and byte offset; do NOT paste raw paths/addresses/WPP messages.'
} else {
    Write-Output 'WPP trace not confirmed saved; preserve WPR status for troubleshooting.'
}
Write-Output 'This is a real targeted PnP restart, not a dry-run; no re-pair/driver install was performed.'
