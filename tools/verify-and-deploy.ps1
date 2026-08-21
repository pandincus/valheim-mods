<#
    Runs the tests, and only deploys to the BepInEx profile if they pass.

    The test pass uses -p:Deploy=false deliberately: without it, building the
    solution to run the tests would already have copied the DLL into the game,
    which rather defeats the point of gating on the result.
#>
param(
    # Escape hatch for a fast inner loop when you already know the tests pass.
    [switch]$SkipTests
)

$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'ValheimMods.sln'

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    & dotnet test $solution -c Release -p:Deploy=false
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'TESTS FAILED - nothing was deployed. The game still has the last good build.' -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host ''
}

Write-Host 'Building and deploying...' -ForegroundColor Cyan
& dotnet build $solution -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host 'BUILD FAILED - nothing was deployed.' -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ''
Write-Host 'Deployed.' -ForegroundColor Green
