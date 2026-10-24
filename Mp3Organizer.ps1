param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-finish-folder/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


