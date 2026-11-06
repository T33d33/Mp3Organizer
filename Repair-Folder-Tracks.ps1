param([string]$Folder)
$ErrorActionPreference = 'Stop'
try {
    Write-Host 'Repair track artists, titles and numbers for ONE source folder. No subfolders. N suggests from filename; M edits a track.'
    if ([string]::IsNullOrWhiteSpace($Folder)) { $Folder = Read-Host 'Paste the full source folder path (under E:\Mp3-Ai-test), or Enter to cancel' }
    if ([string]::IsNullOrWhiteSpace($Folder)) { exit 0 }
    $Folder = $Folder.Trim().Trim('"')
    & dotnet (Join-Path $PSScriptRoot 'artifacts-track-repair\Mp3Organizer.dll') repair-tracks $Folder --workspace 'E:\Mp3-Ai-test-workspace'
    exit $LASTEXITCODE
} catch { Write-Host $_.Exception.Message; exit 1 }
