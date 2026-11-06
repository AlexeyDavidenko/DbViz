# DbViz: installs notes and graph settings into an EXISTING Obsidian vault (Windows PowerShell 5.1+ / PowerShell 7).
# Usage: the "(Windows).cmd" launcher, or  powershell -ExecutionPolicy Bypass -File install.ps1 [-Vault <path>] [-Notes|-NoNotes]
# Changes only: .obsidian\graph.json (own color groups + arrows), bookmarks.json (own bookmark group),
# core-plugins.json (enables graph/bookmarks/...). Backups *.dbviz-backup-* are made before any change.
param([string]$Vault, [switch]$Notes, [switch]$NoNotes)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$utf8 = New-Object System.Text.UTF8Encoding($false) # no BOM, otherwise Obsidian cannot parse the JSON

function Read-Json([string]$path, $fallback) {
    if (-not (Test-Path -LiteralPath $path)) { return $fallback }
    $text = [IO.File]::ReadAllText($path, $utf8).TrimStart([char]0xFEFF)
    if ([string]::IsNullOrWhiteSpace($text)) { return $fallback }
    return ConvertFrom-Json -InputObject $text
}
function Save-Json([string]$path, $obj) {
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination ($path + '.dbviz-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $obj -Depth 50), $utf8)
}
function Set-Prop($obj, [string]$name, $value) { $obj | Add-Member -NotePropertyName $name -NotePropertyValue $value -Force }

$payload = Read-Json (Join-Path $here 'dbviz-obsidian-settings.json') $null
if (-not $payload) { throw 'dbviz-obsidian-settings.json not found next to the script' }

[void][System.Windows.Forms.MessageBox]::Show("QUIT Obsidian before installing, otherwise it will overwrite the settings.`n`nNext, choose your vault folder.", 'DbViz')
if (-not $Vault) {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Your Obsidian vault folder (the one containing .obsidian)'
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
    $Vault = $dlg.SelectedPath
}
$cfg = Join-Path $Vault '.obsidian'
if (-not (Test-Path -LiteralPath $cfg)) { throw "No .obsidian folder here, this is not an Obsidian vault: $Vault" }

# notes
$src = Join-Path (Split-Path -Parent $here) $payload.notesFolder
$dst = Join-Path $Vault $payload.notesFolder
$notesInfo = 'notes were not copied'
if (Test-Path -LiteralPath $dst) {
    $notesInfo = "folder $($payload.notesFolder) already exists, notes were not overwritten"
} else {
    $copy = $Notes -or (-not $NoNotes -and [System.Windows.Forms.MessageBox]::Show("Copy the schema notes (folder '$($payload.notesFolder)') into the vault?", 'DbViz', 'YesNo') -eq 'Yes')
    if ($copy) { Copy-Item -LiteralPath $src -Destination $dst -Recurse; $notesInfo = "notes copied to $($payload.notesFolder)" }
}

# graph.json: replace our color groups, keep everyone else's
$graphPath = Join-Path $cfg 'graph.json'
$graph = Read-Json $graphPath ([pscustomobject]@{})
$groups = @()
foreach ($g in @($graph.colorGroups)) {
    if ($null -eq $g) { continue }
    $q = [string]$g.query
    $ours = $false
    foreach ($m in $payload.groupMarkers) { if ($q.Contains([string]$m)) { $ours = $true } }
    if (-not $ours) { $groups += $g }
}
$groups += @($payload.colorGroups)
Set-Prop $graph 'colorGroups' $groups
Set-Prop $graph 'showArrow' $true
Save-Json $graphPath $graph

# bookmarks.json: our group is recreated
$bmPath = Join-Path $cfg 'bookmarks.json'
$bm = Read-Json $bmPath ([pscustomobject]@{})
$items = @()
foreach ($i in @($bm.items)) {
    if ($null -eq $i) { continue }
    if (-not ($i.type -eq 'group' -and $i.title -eq $payload.bookmarkGroup.title)) { $items += $i }
}
$items += $payload.bookmarkGroup
Set-Prop $bm 'items' $items
Save-Json $bmPath $bm

# core-plugins.json: legacy format is an array, current format is an object
$corePath = Join-Path $cfg 'core-plugins.json'
$core = Read-Json $corePath ([pscustomobject]@{})
if ($core -is [array]) {
    $list = [System.Collections.ArrayList]@($core)
    foreach ($p in $payload.corePlugins) { if (-not $list.Contains($p)) { [void]$list.Add($p) } }
    Save-Json $corePath @($list)
} else {
    foreach ($p in $payload.corePlugins) { Set-Prop $core $p $true }
    Save-Json $corePath $core
}

[void][System.Windows.Forms.MessageBox]::Show("Done: $notesInfo; added layer colors and the '$($payload.bookmarkGroup.title)' bookmarks.`nOpen Obsidian: Bookmarks -> DBVisualization group.", 'DbViz')
