param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-numbered-review-titles/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


