param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-repair-any-folder/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


