param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-fieldmerge/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE