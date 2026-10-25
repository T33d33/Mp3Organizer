param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
& dotnet (Join-Path $PSScriptRoot 'artifacts-review-artist-fallback/Mp3Organizer.dll') @Arguments
exit $LASTEXITCODE


