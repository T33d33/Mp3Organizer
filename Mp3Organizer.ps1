param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-review-scope/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


