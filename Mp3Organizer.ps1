param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-playlist-import/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE

