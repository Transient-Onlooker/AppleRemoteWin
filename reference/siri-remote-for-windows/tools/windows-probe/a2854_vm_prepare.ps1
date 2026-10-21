<#
.SYNOPSIS
Prepare a PRIVATE, OFFLINE 4-case HID parser experiment source matrix.

.DESCRIPTION
Runs on user's existing Windows HOST without installing any drivers.
Requires git, Python 3 and original A2854 38-byte Report Descriptor from
prior failed HIDCLASS trace in the current user's TEMP.

Downloads ONLY a single .c source file from a pinned official Microsoft
Windows-driver-samples commit, to temporary storage. Fetches the user's
own already-committed Python experiment scripts with git show origin/main.
Refuses source mismatch, descriptor mismatch or existing output paths.
Does NOT enable Windows test signing, create a VM, install drivers, touch
the registry, restart devices, or modify the local Git working tree.

Output is *private, generated driver source code* which contains embedded
A2854 Report Descriptor bytes. NEVER push the generated output to GitHub.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Get-Location).Path
)
$ErrorActionPreference = 'Stop'
$commit = '2dc3fd3a0cc84a2933f2194e7ec0871584979071'
$url = 'https://raw.githubusercontent.com/microsoft/Windows-driver-samples/' +
    $commit + '/hid/vhidmini2/driver/vhidmini.c'
$root = [IO.Path]::GetTempPath()
$bin = Join-Path $root 'a2854_hidclass_report_map.bin'
$expectedHash = '6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49'
$source = Join-Path $root ('a2854_official_vhidmini_{0}.c' -f $PID)
$generator = Join-Path $root 'a2854_vhidmini2_source_matrix.py'
$matrix = Join-Path $root 'byte_alignment_matrix.py'
$variants = Join-Path $root 'descriptor_variants.py'
$out = Join-Path $root ('a2854_vm_source_{0}_{1}' -f (Get-Date -Format 'yyyyMMdd_HHmmss'), $PID)

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot '.git'))) {
    throw 'Run from siri-remote-for-windows repository root.'
}
if (-not (Test-Path -LiteralPath $bin -PathType Leaf)) {
    throw 'Private A2854 38-byte descriptor not found in the current TEMP.'
}
if ((Get-Item -LiteralPath $bin).Length -ne 38) {
    throw 'Private A2854 descriptor is not 38 bytes.'
}
if ((Get-FileHash -LiteralPath $bin -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
    throw 'Private A2854 report hash mismatch. No source generation attempted.'
}
Write-Output 'Private original A2854 38-byte descriptor hash verified.'
if (-not (Get-Command py.exe -ErrorAction SilentlyContinue)) {
    throw 'Python 3 launcher py.exe required.'
}
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows required.'
}

try {
    Write-Output 'Downloading only a pinned Microsoft vhidmini.c SOURCE FILE.'
    Invoke-WebRequest -Uri $url -OutFile $source -TimeoutSec 45 -UseBasicParsing
    if ((Get-Item -LiteralPath $source).Length -lt 20000) {
        throw 'Unexpected upstream file size.'
    }
    foreach ($entry in @(
        @{ path='tools/protocol/descriptor_variants.py'; target=$variants },
        @{ path='tools/protocol/byte_alignment_matrix.py'; target=$matrix },
        @{ path='tools/protocol/vhidmini2_source_matrix.py'; target=$generator }
    )) {
        $gitObject = 'origin/main:' + $entry.path
        $lines = @(& git.exe -C $RepoRoot show $gitObject)
        if ($LASTEXITCODE -ne 0 -or $lines.Count -lt 10) {
            throw ('Missing committed generator: {0}' -f $entry.path)
        }
        $lines | Set-Content -LiteralPath $entry.target -Encoding UTF8
    }
    & py.exe -3 $generator --input $bin --sample-c $source --out-dir $out
    if ($LASTEXITCODE -ne 0) {
        throw 'Strict generator failed; no driver operation attempted.'
    }
    Write-Output 'Four private, experimental UMDF2 C source variants generated.'
    Write-Output ('Generated local folder: {0}' -f $out)
    Write-Output 'Do not upload this folder: generated C files embed the original descriptor.'
    Write-Output 'Next: configure an isolated WINDOWS VM, snapshot it, and build the OFFICIAL unmodified UMDF2 sample as baseline.'
} finally {
    if (Test-Path -LiteralPath $source) {
        Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
    }
}
