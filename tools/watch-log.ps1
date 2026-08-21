<#
    Live-tails the BepInEx log.

    Unlike `Get-Content -Wait`, this survives the game restarting: BepInEx
    truncates LogOutput.log on every launch, which makes a plain tail go silent
    at exactly the wrong moment. This reattaches instead.

    Lines from our own mod are highlighted, and errors are shown in red.
#>
param(
    [string]$LogPath = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx\LogOutput.log",
    [string]$Highlight = 'FishQualityBonus',
    # By default we start at the end of the file. Pass -All to replay it first.
    [switch]$All
)

Write-Host "Watching $LogPath" -ForegroundColor Cyan
Write-Host "Highlighting '$Highlight'. Press Ctrl+C to stop." -ForegroundColor DarkGray

while ($true) {
    if (-not (Test-Path $LogPath)) {
        Write-Host "-- waiting for log file to appear --" -ForegroundColor DarkGray
        while (-not (Test-Path $LogPath)) { Start-Sleep -Milliseconds 500 }
    }

    $fs = $null
    $reader = $null
    try {
        # Share ReadWrite+Delete so we never block the game from writing or
        # replacing the file underneath us.
        $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
        $fs = [System.IO.File]::Open($LogPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
        if (-not $All) { [void]$fs.Seek(0, [System.IO.SeekOrigin]::End) }
        $reader = New-Object System.IO.StreamReader($fs)
        Write-Host "-- attached --" -ForegroundColor Green

        while ($true) {
            $line = $reader.ReadLine()
            if ($null -ne $line) {
                if ($line -match [regex]::Escape($Highlight)) { Write-Host $line -ForegroundColor Yellow }
                elseif ($line -match '\[(Error|Fatal)')       { Write-Host $line -ForegroundColor Red }
                elseif ($line -match '\[Warning')             { Write-Host $line -ForegroundColor DarkYellow }
                else                                          { Write-Host $line }
                continue
            }

            Start-Sleep -Milliseconds 200

            # Truncated (new game session) or deleted? Drop out and reattach.
            if (-not (Test-Path $LogPath)) { break }
            if ($fs.Length -lt $fs.Position) {
                Write-Host "-- log truncated, new session --" -ForegroundColor Cyan
                break
            }
        }
    }
    catch {
        Write-Host "-- reattaching: $($_.Exception.Message) --" -ForegroundColor DarkGray
        Start-Sleep -Milliseconds 500
    }
    finally {
        if ($reader) { $reader.Dispose() }
        if ($fs) { $fs.Dispose() }
    }
    # Only replay the whole file on the first attach.
    $All = $false
}
