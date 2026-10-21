# SPDX-License-Identifier: AGPL-3.0-or-later
# Without -Install: read-only. With -Install: opt-in machine-wide trust and external driver change.
[CmdletBinding()]
param([switch]$Install)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$barrotDevices = @(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object { $_.InstanceId -like 'USB\VID_33FA&PID_0001\*' })
if ($barrotDevices.Count -ne 1) {
    throw "Expected exactly one 33FA:0001 adapter; found $($barrotDevices.Count). No changes made."
}
$id = $barrotDevices[0].InstanceId
$svc = (Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_Service -ErrorAction SilentlyContinue).Data
$compat = @((Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_CompatibleIds -ErrorAction SilentlyContinue).Data)
$hardwareIds = @((Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_HardwareIds -ErrorAction SilentlyContinue).Data)
if ($hardwareIds -notcontains 'USB\VID_33FA&PID_0001') {
    throw 'The selected device did not expose the exact USB\VID_33FA&PID_0001 hardware ID. No changes made.'
}
if ($compat -notcontains 'USB\Class_E0&SubClass_01&Prot_01') {
    throw 'The adapter did not expose the expected USB Bluetooth HCI class. No changes made.'
}
$backupFolder = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'AppleRemoteWin-LocalBackups'
$backups = @(Get-ChildItem -LiteralPath $backupFolder -Directory -Filter 'Barrot-33FA-0001-*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Where-Object {
    $inf = Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.inf' -ErrorAction SilentlyContinue | Select-Object -First 1
    $cat = Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.cat' -ErrorAction SilentlyContinue | Select-Object -First 1
    $sys = Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.sys' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $inf -or -not $cat -or -not $sys) { return $false }
    $infText = Get-Content -LiteralPath $inf.FullName -Raw -ErrorAction SilentlyContinue
    if ($infText -notmatch 'USB\\VID_33FA&PID_0001') { return $false }
    foreach ($file in @($inf, $cat, $sys)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Microsoft Windows Hardware Compatibility Publisher*') {
            return $false
        }
    }
    return $true
})
$intel = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_8087&PID_0029\*' })
Write-Host '=== Barrot USB driver preflight ===' -ForegroundColor Cyan
Write-Host "External adapter: 33FA:0001, current status=$($barrotDevices[0].Status), service=$svc"
Write-Host "Saved vendor INF/CAT/SYS backup: $(if ($backups.Count -gt 0) { 'YES' } else { 'NO' })"
Write-Host "Built-in Intel 8087:0029: $(if ($intel.Count -eq 1) { $intel[0].Status } else { 'not uniquely present' })"
if (-not $Install) {
    Write-Host 'Read-only checks complete. Add -Install only when authorizing driver + certificate changes.'
    return
}
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator PowerShell required. No changes made.'
}
if ($backups.Count -eq 0) { throw 'Verified vendor backup missing; no changes made.' }
if ($svc -eq 'WinUSB') { throw 'Device is already WinUSB; no changes made.' }
if ($intel.Count -ne 1 -or $intel[0].Status -ne 'OK') {
    throw 'Expected exactly one healthy built-in Intel 8087:0029 adapter; no changes made.'
}
Write-Warning 'WinUsbDriver.Install() will add a self-signed certificate to LocalMachine\My, Root and TrustedPublisher.'
Write-Warning 'Root/TrustedPublisher additions affect machine-wide trust. The driver change targets VID 33FA:PID 0001.'
Write-Warning 'The adapter might not work after switching; rollback has not yet been tested on this unit.'
$approval = Read-Host 'Type exactly INSTALL 33FA:0001 to authorize these changes'
if ($approval -cne 'INSTALL 33FA:0001') {
    Write-Host 'Cancelled. No changes made.'
    return
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('AppleRemote-Barrot-' + [guid]::NewGuid().ToString('N'))
New-Item -Path $temp -ItemType Directory | Out-Null
try {
    $projectRef = [Security.SecurityElement]::Escape((Join-Path $repo 'src\AppleRemote.Windows\AppleRemote.Windows.csproj'))
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectRef" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $temp 'Installer.csproj') -Value $project -Encoding UTF8
    $program = @'
using AppleRemote.Windows;

var candidates = WinUsbDriver.List()
    .Where(a => a.Id.Vid == 0x33FA && a.Id.Pid == 0x0001)
    .ToArray();
if (candidates.Length != 1)
    throw new InvalidOperationException($"Expected one 33FA:0001 HCI adapter; got {candidates.Length}.");
Console.WriteLine("Attempting WinUSB binding on external 33FA:0001 only.");
bool reboot = WinUsbDriver.Install(candidates[0].InstanceId);
Console.WriteLine($"Installer completed; reboot required: {reboot}");
'@
    Set-Content -LiteralPath (Join-Path $temp 'Program.cs') -Value $program -Encoding UTF8
    & dotnet run -c Release --project (Join-Path $temp 'Installer.csproj') --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Installer failed with exit code $LASTEXITCODE. Do not retry blindly." }
    $afterDevices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_33FA&PID_0001\*' })
    if ($afterDevices.Count -ne 1) { throw "After install expected exactly one 33FA:0001 adapter; found $($afterDevices.Count). Stop and inspect." }
    $afterService = (Get-PnpDeviceProperty -InstanceId $afterDevices[0].InstanceId -KeyName DEVPKEY_Device_Service -ErrorAction SilentlyContinue).Data
    Write-Host "External adapter now: service=$afterService; PnP=$($afterDevices[0].Status)"
    if ($afterService -ne 'WinUSB' -or $afterDevices[0].Status -ne 'OK') { throw 'WinUSB did not reach PnP OK; do not run HCI probe. Inspect and restore the vendor driver if needed.' }
    $intelAfter = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_8087&PID_0029\*' })
    if ($intel.Count -eq 1 -and ($intelAfter.Count -ne 1 -or $intelAfter[0].Status -ne 'OK')) {
        throw 'Built-in Intel adapter state changed unexpectedly. Stop and inspect before HCI testing.'
    }
}
finally {
    try { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction Stop } catch {
        Write-Warning "Temp build directory remains: $temp"
    }
}
