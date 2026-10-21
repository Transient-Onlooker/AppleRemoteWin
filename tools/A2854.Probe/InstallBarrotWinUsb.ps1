# SPDX-License-Identifier: AGPL-3.0-or-later
# Without -Install: read-only. With -Install: opt-in machine-wide trust and external driver change.
[CmdletBinding()]
param([switch]$Install)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$matches = @(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object { $_.InstanceId -like 'USB\VID_33FA&PID_0001\*' })
if ($matches.Count -ne 1) {
    throw "Expected exactly one 33FA:0001 adapter; found $($matches.Count). No changes made."
}
$id = $matches[0].InstanceId
$svc = (Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_Service -ErrorAction SilentlyContinue).Data
$compat = @((Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_CompatibleIds -ErrorAction SilentlyContinue).Data)
if ($compat -notcontains 'USB\Class_E0&SubClass_01&Prot_01') {
    throw 'The adapter did not expose the expected USB Bluetooth HCI class. No changes made.'
}
$backupFolder = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'AppleRemoteWin-LocalBackups'
$backups = @(Get-ChildItem -LiteralPath $backupFolder -Directory -Filter 'Barrot-33FA-0001-*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Where-Object {
    (Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.inf' -ErrorAction SilentlyContinue) -and
    (Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.cat' -ErrorAction SilentlyContinue) -and
    (Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.sys' -ErrorAction SilentlyContinue)
})
$intel = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_8087&PID_0029\*' })
Write-Host '=== Barrot USB driver preflight ===' -ForegroundColor Cyan
Write-Host "External adapter: 33FA:0001, current status=$($matches[0].Status), service=$svc"
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
    $afterService = (Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_Service -ErrorAction SilentlyContinue).Data
    $afterStatus = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -eq $id })
    Write-Host "External adapter now: service=$afterService; PnP=$($afterStatus[0].Status)"
    if ($afterService -ne 'WinUSB') { Write-Warning 'WinUSB not confirmed; do not run HCI probe yet.' }
    $intelAfter = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_8087&PID_0029\*' })
    if ($intel.Count -eq 1 -and ($intelAfter.Count -ne 1 -or $intelAfter[0].Status -ne 'OK')) {
        Write-Warning 'Built-in Intel adapter state changed unexpectedly. Stop and inspect.'
    }
}
finally {
    try { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction Stop } catch {
        Write-Warning "Temp build directory remains: $temp"
    }
}
