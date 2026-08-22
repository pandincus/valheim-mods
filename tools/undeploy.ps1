<#
    Removes the locally-built copy of a mod from the BepInEx profile, so you can
    install the published Thunderstore package into the same profile and be sure
    which one you are actually running.

    Why this is needed: the build deploys to plugins\<ModName>, while Thunderstore
    Mod Manager installs to plugins\<team>-<ModName>. Different folders, so neither
    overwrites the other - and BepInEx then finds two DLLs claiming the same plugin
    GUID, loads one, and silently skips the other. Which one wins is load order.

    Afterwards it lists every remaining copy it can find, so you can see exactly
    what BepInEx will load.

    Usage:  .\tools\undeploy.ps1
            .\tools\undeploy.ps1 -Mod SomeOtherMod
#>
param(
    [string]$Mod = 'FishQualityBonus'
)

$plugins = Join-Path $env:APPDATA 'Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx\plugins'

if (-not (Test-Path $plugins)) {
    Write-Host "No BepInEx plugins folder at $plugins" -ForegroundColor Red
    exit 1
}

$devCopy = Join-Path $plugins $Mod
if (Test-Path $devCopy) {
    Remove-Item $devCopy -Recurse -Force
    Write-Host "Removed the locally-built copy: $devCopy" -ForegroundColor Green
} else {
    Write-Host "No locally-built copy to remove ($devCopy)" -ForegroundColor DarkGray
}

# Whatever is left is what BepInEx will actually load.
Write-Host ''
Write-Host "Copies of $Mod.dll still in the profile:" -ForegroundColor Cyan
$found = Get-ChildItem $plugins -Recurse -Filter "$Mod.dll" -ErrorAction SilentlyContinue
if (-not $found) {
    Write-Host '    (none - the mod is not installed at all right now)' -ForegroundColor DarkGray
} else {
    foreach ($f in $found) {
        $version = try { [Reflection.AssemblyName]::GetAssemblyName($f.FullName).Version } catch { 'unknown' }
        Write-Host ("    {0}  (v{1})" -f $f.Directory.Name, $version)
    }
    if ($found.Count -gt 1) {
        Write-Host ''
        Write-Host "  $($found.Count) copies found. BepInEx will load one and skip the rest," -ForegroundColor Yellow
        Write-Host '  because they all declare the same plugin GUID.' -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host 'Note: the next build will deploy the local copy again.' -ForegroundColor DarkGray
Write-Host 'Use the "build only (no deploy)" task while testing the Thunderstore version.' -ForegroundColor DarkGray
