param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-track-repair/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


