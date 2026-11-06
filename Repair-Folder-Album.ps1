param([string]$Folder)
$ErrorActionPreference = 'Stop'
try {
    Write-Host 'Repair album details for ONE source folder. No subfolders. Song edits are kept.'
    if ([string]::IsNullOrWhiteSpace($Folder)) { $Folder = Read-Host 'Paste the full source folder path (under E:\Mp3-Ai-test), or Enter to cancel' }
    if ([string]::IsNullOrWhiteSpace($Folder)) { exit 0 }
    $Folder = $Folder.Trim().Trim('"')
    & dotnet (Join-Path $PSScriptRoot 'artifacts-track-repair\Mp3Organizer.dll') repair-folder $Folder --workspace 'E:\Mp3-Ai-test-workspace'
    exit $LASTEXITCODE
} catch { Write-Host $_.Exception.Message; exit 1 }
