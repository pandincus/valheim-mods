<#
    Builds a mod and zips it into the layout Thunderstore expects: everything
    flat at the root of the archive, no folders.

        manifest.json
        icon.png
        README.md
        CHANGELOG.md
        <ModName>.dll

    The .pdb is deliberately left out of releases.

    Usage:  .\tools\package.ps1                       # packages FishQualityBonus
            .\tools\package.ps1 -Mod SomeOtherMod
#>
param(
    [string]$Mod = 'FishQualityBonus',
    # Skip the test run. Don't use this for anything you intend to upload.
    [switch]$SkipTests
)

$root = Split-Path $PSScriptRoot -Parent
$modDir = Join-Path $root "src\$Mod"
$csproj = Join-Path $modDir "$Mod.csproj"
$outDir = Join-Path $root 'dist'

if (-not (Test-Path $csproj)) { Write-Host "No such mod: $Mod" -ForegroundColor Red; exit 1 }

# --- Version, from the one place that actually builds the DLL ---------------
$csprojVersion = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version |
                 Where-Object { $_ } | Select-Object -First 1
$manifestPath = Join-Path $modDir 'manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

# Thunderstore versions are immutable, so a mismatch here would publish a
# package whose number disagrees with the DLL inside it. Refuse instead.
if ($manifest.version_number -ne $csprojVersion) {
    Write-Host "Version mismatch - nothing packaged." -ForegroundColor Red
    Write-Host "  $Mod.csproj:   $csprojVersion"
    Write-Host "  manifest.json: $($manifest.version_number)"
    Write-Host "Make them agree (and check PluginVersion in Plugin.cs too) and run again."
    exit 1
}

# --- Build ------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    & dotnet test (Join-Path $root 'ValheimMods.sln') -c Release -p:Deploy=false
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'TESTS FAILED - nothing packaged.' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host 'Building...' -ForegroundColor Cyan
& dotnet build $csproj -c Release -p:Deploy=false
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED.' -ForegroundColor Red; exit $LASTEXITCODE }

# --- Collect ----------------------------------------------------------------
$staging = Join-Path $outDir "_staging_$Mod"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

$required = @(
    (Join-Path $modDir 'manifest.json'),
    (Join-Path $modDir 'icon.png'),
    (Join-Path $modDir 'README.md'),
    (Join-Path $modDir 'CHANGELOG.md'),
    (Join-Path $modDir "bin\Release\$Mod.dll")
)
foreach ($f in $required) {
    if (-not (Test-Path $f)) { Write-Host "Missing required file: $f" -ForegroundColor Red; exit 1 }
    Copy-Item $f $staging
}

# Thunderstore rejects anything that isn't exactly 256x256.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $staging 'icon.png'))
$iconSize = "$($icon.Width)x$($icon.Height)"
$icon.Dispose()
if ($iconSize -ne '256x256') {
    Write-Host "icon.png is $iconSize - Thunderstore requires exactly 256x256." -ForegroundColor Red
    exit 1
}

# --- Zip --------------------------------------------------------------------
$zip = Join-Path $outDir "$Mod-$csprojVersion.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging -Recurse -Force

Write-Host ''
Write-Host "Packaged $zip" -ForegroundColor Green
Write-Host 'Contents:' -ForegroundColor DarkGray
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
$archive.Entries | ForEach-Object { Write-Host ("    {0,-22} {1,8:N0} bytes" -f $_.FullName, $_.Length) }
$archive.Dispose()
Write-Host ''
Write-Host "Upload at https://thunderstore.io/c/valheim/create/ under the pandincus team." -ForegroundColor Cyan
