param(
    [string]$GamePath,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$payload = Join-Path $PSScriptRoot 'BlueprintWorkspace.dll'
if (-not (Test-Path -LiteralPath $payload -PathType Leaf)) { throw 'BlueprintWorkspace.dll is missing beside Install.ps1.' }
$manifestPath = Join-Path $PSScriptRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'manifest.json is missing beside Install.ps1.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$actualHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $manifest.dll_sha256.ToLowerInvariant()) { throw 'BlueprintWorkspace.dll hash does not match manifest.json; no files changed.' }

function Find-Game {
    param([string]$Explicit)
    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    try {
        $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steam) { $candidates += (Join-Path $steam 'steamapps\common\Spaceflight Simulator') }
    } catch { }
    $candidates += @(
        "${env:ProgramFiles(x86)}\Steam\steamapps\common\Spaceflight Simulator",
        "$env:ProgramFiles\Steam\steamapps\common\Spaceflight Simulator"
    )
    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        $roots = @($candidate, (Join-Path $candidate 'Spaceflight Simulator Game'))
        foreach ($root in $roots) {
            if (Test-Path -LiteralPath (Join-Path $root 'Spaceflight Simulator.exe') -PathType Leaf) { return $root }
        }
    }
    return $null
}

$game = Find-Game $GamePath
if (-not $game) {
    Add-Type -AssemblyName System.Windows.Forms
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    $picker.Description = 'Select the Spaceflight Simulator game folder containing Spaceflight Simulator.exe'
    if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'No game folder selected.' }
    $game = Find-Game $picker.SelectedPath
}
if (-not $game) { throw 'The selected folder does not contain Spaceflight Simulator.exe.' }
$settings = Join-Path $game 'Saving\Settings\ModsSettings.txt'
if (-not (Test-Path -LiteralPath $settings -PathType Leaf)) { throw "ModsSettings.txt not found under $game" }
$targetDir = Join-Path $game 'Mods\BlueprintWorkspace'
$target = Join-Path $targetDir 'BlueprintWorkspace.dll'
$settingsData = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
if (-not $settingsData.modsActive) { throw 'ModsSettings.txt has no modsActive object; no files changed.' }
Write-Host "Game: $game"
Write-Host "Install: $target"
if ($CheckOnly) { Write-Host 'Check only; no files changed.'; exit 0 }

$running = Get-Process -Name 'Spaceflight Simulator' -ErrorAction SilentlyContinue
if ($running) { throw 'Save progress and close Spaceflight Simulator before installing.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination "$target.backup.$stamp" }
Copy-Item -LiteralPath $payload -Destination $target

if (-not ($settingsData.modsActive.PSObject.Properties.Name -contains 'BlueprintWorkspace')) {
    Copy-Item -LiteralPath $settings -Destination "$settings.backup.$stamp"
    $settingsData.modsActive | Add-Member -NotePropertyName 'BlueprintWorkspace' -NotePropertyValue $true
    $settingsData | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settings -Encoding UTF8
} elseif (-not $settingsData.modsActive.BlueprintWorkspace) {
    Copy-Item -LiteralPath $settings -Destination "$settings.backup.$stamp"
    $settingsData.modsActive.BlueprintWorkspace = $true
    $settingsData | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settings -Encoding UTF8
}
Write-Host 'Installed. Open the build scene and click Shared Blueprints (or press F8).'
