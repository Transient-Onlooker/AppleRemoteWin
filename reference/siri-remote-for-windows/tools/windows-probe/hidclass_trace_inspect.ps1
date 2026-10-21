<#
.SYNOPSIS
Inspect metadata of the most recent SiriHidClass ETW trace without printing payloads.

.DESCRIPTION
Read-only. Does not start ETW, change drivers, or touch pairing.
Shows declared event templates (field NAMES/types, never field VALUES)
and observed ID/version timeline. Does not emit raw XML, message text,
device addresses/paths, user/computer identifiers, or trace filename.

The presence of a HIDCLASS event does not associate it with the Siri
Remote; the OS handles multiple HID devices.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 1000)]
    [int]$MaxEvents = 200
)
$ErrorActionPreference = 'Stop'
$provider = 'Microsoft-Windows-Input-HIDCLASS'
$folder = [System.IO.Path]::GetTempPath()
$etl = Get-ChildItem -LiteralPath $folder -Filter 'siri_hidclass_*.etl' -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $etl) {
    throw 'No siri_hidclass_*.etl in the Windows TEMP folder for this account.'
}

# This accesses only provider metadata from the local Windows manifest.
$catalog = Get-WinEvent -ListProvider $provider -ErrorAction Stop
Write-Output 'HIDCLASS event definitions (event payload values are NOT shown):'
foreach ($definition in $catalog.Events | Sort-Object Id, Version) {
    $fields = @()
    if (-not [string]::IsNullOrWhiteSpace([string]$definition.Template)) {
        try {
            [xml]$template = [string]$definition.Template
            # Use LocalName so Windows build-specific template namespaces do not matter.
            $elements = @($template.SelectNodes('//*[local-name()="data"]'))
            foreach ($el in $elements) {
                $name = [string]$el.GetAttribute('name')
                $type = [string]$el.GetAttribute('inType')
                # Manifest identifiers only; guard against any unexpected private text.
                if ($name -match '^[A-Za-z_][A-Za-z0-9_]{0,80}$') {
                    if ($type -notmatch '^[A-Za-z0-9_:]{1,48}$') {
                        $type = 'unspecified'
                    }
                    $fields += ('{0}:{1}' -f $name, $type)
                }
            }
        } catch {
            $fields = @('template-parse-unavailable')
        }
    }
    [pscustomobject]@{
        EventId = [int]$definition.Id
        Version = [int]$definition.Version
        Fields = if ($fields.Count -gt 0) { $fields -join ', ' } else { '(none declared)' }
    } | Format-List | Out-String -Width 160 | Write-Output
}

# Filter and truncate locally so that only non-identifying record metadata prints.
$observed = @(
    Get-WinEvent -Path $etl.FullName -Oldest -ErrorAction Stop |
        Where-Object { $_.ProviderName -eq $provider } |
        Select-Object -First $MaxEvents
)
Write-Output ('HIDCLASS events observed (up to {0}): {1}' -f $MaxEvents, $observed.Count)
if ($observed.Count -eq 0) {
    Write-Output 'No HIDCLASS events in this file. Inconclusive.'
    exit 0
}
$firstTime = $observed[0].TimeCreated
$observed |
    ForEach-Object {
        [pscustomobject]@{
            SecondsFromFirst = [math]::Round(($_.TimeCreated - $firstTime).TotalSeconds, 3)
            EventId = [int]$_.Id
            Version = [int]$_.Version
            Level = [int]$_.Level
            Opcode = [int]$_.Opcode
            Task = [int]$_.Task
        }
    } |
    Format-Table -AutoSize |
    Out-String -Width 120 |
    Write-Output
Write-Output 'Only event metadata and declared field names were printed.'
Write-Output 'These events are not yet proven to belong to the Siri Remote.'
