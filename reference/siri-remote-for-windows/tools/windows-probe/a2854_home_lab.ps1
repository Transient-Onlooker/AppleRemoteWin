<#
.SYNOPSIS
A2854 home investigation: safe diagnostics or explicit bounded HIDCLASS WPP trace.

.DESCRIPTION
Use elevated PowerShell from the siri-remote-for-windows repository root.
Your local working tree and uncommitted changes are NEVER modified.
Scripts are read from origin/main via git show into LOCAL TEMP. Run git fetch
origin main first.

Stages:
  Safe        (default) offline descriptor matrix + third-party parser when
              installed + local read-only Windows/WPP preflight.
  PrepareWpp  download ONLY pinned Microsoft .wprp XML into local TEMP.
  CaptureWpp  explicitly restart ONE uniquely matched failed HID child while
              capturing Microsoft's verbose HIDCLASS WPP profile with WPR.
              Will not run if the target/profile prerequisites fail.

No stage re-pairs devices, edits drivers, patches the registry or reboots.
Raw ETL and private descriptor binaries stay in LOCAL TEMP.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_home_lab.ps1" -Stage Safe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_home_lab.ps1" -Stage PrepareWpp
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_home_lab.ps1" -Stage CaptureWpp
#>
[CmdletBinding()]
param(
    [ValidateSet('Safe','PrepareWpp','CaptureWpp')]
    [string]$Stage = 'Safe',
    [string]$RepoRoot = (Get-Location).Path,
    [switch]$WriteVariants
)
$ErrorActionPreference = 'Stop'
$requiredRemote = 'Transient-Onlooker/siri-remote-for-windows'
$privateInput = Join-Path ([IO.Path]::GetTempPath()) 'a2854_hidclass_report_map.bin'
$workDir = Join-Path ([IO.Path]::GetTempPath()) 'A2854HomeLabScripts'

function Assert-Repository {
    if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
        throw 'git.exe is unavailable; install Git for Windows.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot '.git'))) {
        throw 'Run from the root of the siri-remote-for-windows Git repository.'
    }
    $remote = (& git.exe -C $RepoRoot config --get remote.origin.url 2>$null) -join ''
    if ($LASTEXITCODE -ne 0 -or
        $remote -notmatch '^(https://github\.com/|git@github\.com:|ssh://git@github\.com/|git://github\.com/)(?i:Transient-Onlooker/siri-remote-for-windows)(\.git)?/?$') {
        throw 'Git origin is not the expected siri-remote-for-windows repository.'
    }
    $head = (& git.exe -C $RepoRoot rev-parse --verify origin/main 2>$null) -join ''
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        throw 'No origin/main reference; run git fetch origin main first.'
    }
    Write-Output ('Using origin/main commit {0} (local files untouched)' -f $head.Substring(0, 12))
}
function Get-Tool([string]$repoPath,[string]$targetName) {
    if (-not (Test-Path -LiteralPath $workDir)) {
        New-Item -ItemType Directory -Path $workDir -Force | Out-Null
    }
    $dest = Join-Path $workDir $targetName
    # No arbitrary URL execution; this file is fetched from the user's
    # selected Git remote ref and written only under TEMP.
    $relative = 'origin/main:' + $repoPath
    $lines = @(& git.exe -C $RepoRoot show $relative 2>$null)
    if ($LASTEXITCODE -ne 0 -or $lines.Count -lt 4) {
        throw ('Cannot read file from Git origin/main: {0}' -f $repoPath)
    }
    $lines | Set-Content -LiteralPath $dest -Encoding UTF8
    return $dest
}
function Need-Python {
    $cmd = Get-Command py.exe -ErrorAction SilentlyContinue
    if (-not $cmd) {
        throw 'Python launcher py.exe missing; install Python 3.11+ on Windows.'
    }
}
Assert-Repository
$wpp = Get-Tool 'tools/windows-probe/a2854_wpp.ps1' 'a2854_wpp.ps1'

switch ($Stage) {
    'PrepareWpp' {
        Write-Output '--- PHASE: Microsoft XML preparation (no system changes) ---'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $wpp -Mode Prepare
        if ($LASTEXITCODE -ne 0) { throw 'WPP preparation failed.' }
    }
    'CaptureWpp' {
        Write-Output '--- PHASE: TARGETED DRIVER RESTART + WPP CAPTURE ---'
        Write-Output 'WARNING: this stage actually restarts the one confirmed Code-10 remote HID child.'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $wpp -Mode Capture
        if ($LASTEXITCODE -ne 0) { throw 'WPP capture failed; see sanitized output above.' }
    }
    'Safe' {
        Write-Output '--- PHASE 1: local Windows and WPP preflight (read-only) ---'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $wpp -Mode Check
        if ($LASTEXITCODE -ne 0) { throw 'Preflight failed.' }
        Need-Python
        if (-not (Test-Path -LiteralPath $privateInput -PathType Leaf)) {
            Write-Output 'Local private 38-byte descriptor not found under this Windows account TEMP.'
            Write-Output 'Run previous validated ETL extraction before offline testing.'
            exit 2
        }
        $len = (Get-Item -LiteralPath $privateInput).Length
        if ($len -ne 38) {
            throw 'Descriptor is not exactly 38 bytes; refusing speculative tests.'
        }
        $expectedHash = '6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49'
        $actualHash = (Get-FileHash -LiteralPath $privateInput -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw 'Descriptor SHA-256 differs from the previously confirmed A2854 ETL capture.'
        }
        Write-Output 'Original 38-byte descriptor SHA-256 matches confirmed ETL capture.'

        $common = Get-Tool 'tools/protocol/descriptor_variants.py' 'descriptor_variants.py'
        $matrix = Get-Tool 'tools/protocol/byte_alignment_matrix.py' 'byte_alignment_matrix.py'
        $independent = Get-Tool 'tools/protocol/third_party_variant_check.py' 'third_party_variant_check.py'
        Write-Output '--- PHASE 2: alignment and local Usage experiment (offline) ---'
        $args = @($matrix, '--input', $privateInput)
        if ($WriteVariants) {
            $out = Join-Path ([IO.Path]::GetTempPath()) ('a2854_matrix_{0}_{1}' -f (Get-Date -Format 'yyyyMMdd_HHmmss'), $PID)
            $args += @('--out-dir', $out)
        }
        & py.exe -3 @args
        if ($LASTEXITCODE -ne 0) { throw 'Offline alignment experiment failed.' }

        Write-Output '--- PHASE 3: independent Python HID parser (offline when installed) ---'
        # Windows PowerShell 5.1 native -c strips the inner Python string
        # quotation marks. Use pip's plain-text installed package listing
        # instead; do not pass nested -c expressions through PowerShell.
        $packages = @(& py.exe -3 -m pip list --format=freeze)
        $pipOk = ($LASTEXITCODE -eq 0)
        $hasParser = $pipOk -and (@(
            $packages | Where-Object { $_ -match '(?i)^hid-parser==0\.1\.0$' }
        ).Count -gt 0)
        if ($hasParser) {
            & py.exe -3 $independent --input $privateInput
            if ($LASTEXITCODE -ne 0) { throw 'Independent HID parser failed.' }
        } else {
            Write-Output 'Optional hid-parser 0.1.0 is not installed.'
            Write-Output 'To enable: py -3 -m pip install hid-parser==0.1.0'
        }
        Write-Output '--- SAFE LAB COMPLETE ---'
        Write-Output 'No system state modified. Nothing uploaded; optional .bin variants remain in TEMP.'
        Write-Output 'Follow up in this order: PrepareWpp, then (explicitly) CaptureWpp.'
    }
}
