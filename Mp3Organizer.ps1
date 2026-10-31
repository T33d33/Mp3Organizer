param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-explicit-title/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


