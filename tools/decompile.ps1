# Decompiles Valheim game classes for reference. Requires ilspycmd:
#   dotnet tool install -g ilspycmd --version 8.2.0.7535
#
# Usage: .\tools\decompile.ps1 Recipe InventoryGui Player
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Types)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
$asm = Join-Path $managed "assembly_valheim.dll"
$out = Join-Path $PSScriptRoot "..\decompiled"
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($t in $Types) {
    $dest = Join-Path $out "$t.cs"
    & ilspycmd $asm -r $managed -t $t | Out-File -Encoding utf8 $dest
    Write-Host "$t -> $dest"
}
