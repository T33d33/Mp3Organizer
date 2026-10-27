param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-output-validation/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


