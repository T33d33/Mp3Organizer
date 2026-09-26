param([string]$TagLibDll = "$env:USERPROFILE\.nuget\packages\taglibsharp\2.3.0\lib\netstandard2.0\TagLibSharp.dll", [string]$OutputDirectory = "artifacts")
$ErrorActionPreference = 'Stop'
$dotnetRoot = Split-Path (Get-Command dotnet).Source
$sdk = Get-ChildItem (Join-Path $dotnetRoot 'sdk') -Directory | Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$refPack = Get-ChildItem (Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk -or !$refPack -or !(Test-Path -LiteralPath $TagLibDll)) { throw 'Requires .NET 10 SDK/reference pack and TagLibSharp 2.3.0. Supply -TagLibDll when not cached.' }
$out = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $out | Out-Null
$globals = Join-Path $out 'GlobalUsings.cs'
Set-Content -LiteralPath $globals -Encoding utf8 -Value 'global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic; global using System.Threading; global using System.Threading.Tasks;'
$refs = @(Get-ChildItem (Join-Path $refPack.FullName 'ref\net10.0\*.dll') | ForEach-Object { '/reference:' + $_.FullName })
$compiler = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$app = Join-Path $out 'Mp3Organizer.dll'
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src\Mp3Organizer\*.cs') | ForEach-Object FullName)
& dotnet $compiler /nologo /target:exe /langversion:latest /nullable:enable /warnaserror+ /optimize+ /deterministic+ "/out:$app" @refs "/reference:$TagLibDll" $globals @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath $TagLibDll -Destination (Join-Path $out 'TagLibSharp.dll') -Force
$testSources = @(Get-ChildItem (Join-Path $PSScriptRoot 'tests\Mp3Organizer.Tests\*.cs') | ForEach-Object FullName)
& dotnet $compiler /nologo /target:exe /langversion:latest /nullable:enable /warnaserror+ /optimize+ /deterministic+ "/out:$out\Mp3Organizer.Tests.dll" @refs "/reference:$app" "/reference:$TagLibDll" $globals @testSources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
$runtime = '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'
Set-Content -LiteralPath (Join-Path $out 'Mp3Organizer.runtimeconfig.json') -Encoding utf8 -Value $runtime
Set-Content -LiteralPath (Join-Path $out 'Mp3Organizer.Tests.runtimeconfig.json') -Encoding utf8 -Value $runtime
Write-Output 'BUILD PASSED: application and test suite; warnings treated as errors.'
