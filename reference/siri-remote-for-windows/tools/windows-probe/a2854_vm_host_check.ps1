<#
.SYNOPSIS
Read-only suitability check for a separate A2854 Windows 11 driver-test VM.
.DESCRIPTION
Reports Windows edition, current hypervisor, virtual-machine features, CPU
virtualization flags and FIXED drive free space. No hostname/user path/serial
details are printed. Absolutely no Windows/BIOS/boot/driver/VM changes.
SLAT reported false under a running hypervisor is NOT a proof that the
physical processor cannot run a guest VM.
#>
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Write-Output '--- A2854 isolated Windows HID VM feasibility audit ---'
Write-Output ('Windows build: {0}' -f [Environment]::OSVersion.Version.Build)
try {
    $edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop).EditionID
    Write-Output ('Windows edition: {0}' -f $edition)
    if ([string]$edition -match '(?i)^Core') {
        Write-Output 'Client Hyper-V management is not included with Windows Home; consider another supported hypervisor.'
    }
} catch { Write-Output 'Windows edition: unavailable' }
$hypervisor = $null
try {
    $sys=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    $hypervisor = $sys.HypervisorPresent
    Write-Output ('Hypervisor already running: {0}' -f $hypervisor)
    Write-Output ('Total physical memory (GiB): {0}' -f [math]::Round(([double]$sys.TotalPhysicalMemory)/1GB,1))
} catch { Write-Output 'Computer system info: unavailable' }
try {
    $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
    Write-Output ('CPU: {0}' -f $cpu.Name)
    Write-Output ('Firmware virtualization enabled: {0}' -f $cpu.VirtualizationFirmwareEnabled)
    Write-Output ('Reported SLAT support: {0}' -f $cpu.SecondLevelAddressTranslationExtensions)
    if ($cpu.SecondLevelAddressTranslationExtensions -eq $false) {
        Write-Output 'NOTE: A False SLAT flag must be reconciled with CPU model, hypervisor status and working WSL2 before interpreting as unsupported.'
    }
} catch { Write-Output 'Processor virtualization properties: unavailable' }
foreach ($featureName in @('Microsoft-Hyper-V-All', 'VirtualMachinePlatform','HypervisorPlatform')) {
    try {
        $feature=Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Stop
        if ($null -eq $feature -or [string]::IsNullOrWhiteSpace([string]$feature.State)) {
            Write-Output ('Optional feature {0}: unavailable / not returned' -f $featureName)
        } else {
            Write-Output ('Optional feature {0}: {1}' -f $featureName,$feature.State)
        }
    } catch {
        Write-Output ('Optional feature {0}: unavailable (edition, permissions or unknown feature)' -f $featureName)
    }
}
Write-Output ('Hyper-V management cmdlets: {0}' -f $(if (Get-Command Get-VM -ErrorAction SilentlyContinue) { 'available' } else { 'not on PATH' }))
Write-Output ('VMware vmrun: {0}' -f $(if (Get-Command vmrun.exe -ErrorAction SilentlyContinue) { 'on PATH' } else { 'not on PATH (not proof of absence)' }))
Write-Output 'Fixed drive free space (GiB):'
try {
    $disks = @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -ErrorAction Stop |
        Sort-Object DeviceID)
    foreach ($disk in $disks) {
        Write-Output ('  {0} free={1} total={2}' -f $disk.DeviceID,
            [math]::Round(([double]$disk.FreeSpace)/1GB,1),
            [math]::Round(([double]$disk.Size)/1GB,1))
    }
    if ($disks.Count -eq 0) { Write-Output '  (none reported)' }
} catch { Write-Output '  unavailable' }
Write-Output 'Windows 11 guest requires >=64GB provisioned virtual storage, >=4GB guest RAM, >=2 vCPUs, and vTPM; driver work adds overhead.'
Write-Output 'No Windows optional feature, driver, boot setting, virtualization or disk was modified.'
