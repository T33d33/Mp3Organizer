$ErrorActionPreference = 'Stop'
function Read-SavedSetting([string]$Name) {
    foreach ($scope in @('Process','User','Machine')) {
        $value = [Environment]::GetEnvironmentVariable($Name,$scope)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }
    return ''
}
function Save-Setting([string]$Name,[string]$Value) {
    if ([Environment]::GetEnvironmentVariable($Name,'User') -ceq $Value) {
        [Environment]::SetEnvironmentVariable($Name,$Value,'Process')
        Write-Host "$Name already saved; reusing it (value hidden)."
        return
    }
    Write-Host "Saving $Name for future sessions; Windows may take a moment..."
    [Environment]::SetEnvironmentVariable($Name,$Value,'User')
    [Environment]::SetEnvironmentVariable($Name,$Value,'Process')
    Write-Host "$Name saved for future sessions (value hidden)."
}
Write-Host 'One-time online setup. Existing values are reused; keys are never displayed.'
Write-Host 'Answer each question below, then press Enter. Saved settings are skipped automatically.'
Write-Host ''
Write-Host 'Step 1 of 3: AcoustID application key'
$key = Read-SavedSetting 'ACOUSTID_API_KEY'
if (-not $key) {
    Write-Host 'Open https://acoustid.org/my-applications and sign in with your existing account.'
    Write-Host 'Copy the key for your registered application (not the user submission key).'
    Write-Host 'Paste it at the prompt below and press Enter. The characters will be hidden.'
    $secret = Read-Host 'Paste your AcoustID APPLICATION key here' -AsSecureString
    $key = [System.Net.NetworkCredential]::new('', $secret).Password
}
if (-not $key) { throw 'An AcoustID application key is required.' }
Save-Setting 'ACOUSTID_API_KEY' $key
Write-Host ''
Write-Host 'Step 2 of 3: MusicBrainz contact'
$contact = Read-SavedSetting 'MP3ORGANIZER_CONTACT'
if (-not $contact) {
    Write-Host 'Enter your own email address, for example name@example.com, or your contact website URL.'
    Write-Host 'MusicBrainz receives this contact with API requests. This is not a password or API key.'
    $contact = Read-Host 'Type your contact email address or URL, then press Enter'
}
if ([string]::IsNullOrWhiteSpace($contact) -or $contact -match '[\x00-\x1f\x7f]') { throw 'A contact email or URL is required.' }
Save-Setting 'MP3ORGANIZER_CONTACT' $contact
Write-Host ''
Write-Host 'Step 3 of 3: Fingerprint tool'
$fpcalc = Read-SavedSetting 'CHROMAPRINT_FPCALC'
if (-not $fpcalc) {
    $found = Get-Command fpcalc -ErrorAction SilentlyContinue
    if ($found) { $fpcalc = $found.Source }
}
while (-not $fpcalc -or -not (Test-Path -LiteralPath $fpcalc -PathType Leaf)) {
    Write-Host 'The fingerprint tool could not be found automatically.'
    Write-Host 'Enter the full path to the installed fpcalc.exe, for example C:\Tools\Chromaprint\fpcalc.exe.'
    Write-Host 'If it is not installed, download Chromaprint from https://acoustid.org/chromaprint and extract it first.'
    Write-Host 'Paste the path to the executable itself, not just its folder. Blank input stops setup.'
    $fpcalc = (Read-Host 'Full path to fpcalc.exe, then press Enter').Trim('"')
    if (-not $fpcalc) { throw 'Fingerprint tool still needs configuration; earlier settings remain saved.' }
}
Save-Setting 'CHROMAPRINT_FPCALC' $fpcalc
foreach ($name in @('OPENAI_API_KEY','MP3ORGANIZER_CODEX_MODEL','MP3ORGANIZER_CODEX_ENABLED')) {
    $value = Read-SavedSetting $name
    if ($value) { Save-Setting $name $value }
}
Write-Host 'Setup saved. No need to repeat after restarting Windows. Missing optional LLM settings use the existing fallback.'
Write-Host ''
Write-Host 'NEXT: Run the following commands in your PowerShell terminal to continue processing:'
Write-Host ('$dll = ' + "'" + (Join-Path $PSScriptRoot 'artifacts-review-artist-fallback\Mp3Organizer.dll') + "'")
Write-Host "dotnet `$dll run --online --workspace 'E:\Mp3-Ai-test-workspace' --target 'E:\Mp3-Ai-test-organized' --reports 'E:\Mp3-Ai-test-reports'"
Write-Host ''
Write-Host 'If you added new source folders, run this BEFORE the run command:'
Write-Host "dotnet `$dll scan 'E:\Mp3-Ai-test' --workspace 'E:\Mp3-Ai-test-workspace'"
