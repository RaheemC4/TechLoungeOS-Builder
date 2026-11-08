param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
$metadata=@(Get-ChildItem -LiteralPath $sdk -Directory | Where-Object {$_.Name -match '^\d+\.\d+\.\d+\.\d+$'} |
 Sort-Object {[version]$_.Name} -Descending | ForEach-Object {Join-Path $_.FullName 'Windows.winmd'} | Where-Object {Test-Path -LiteralPath $_})
if(!$metadata.Count){throw 'Building the native lock-screen helper requires Windows SDK metadata on the development PC. It is bundled in the app for users.'}
$runtime=Join-Path $env:SystemRoot 'Microsoft.Net\assembly\GAC_MSIL\System.Runtime.WindowsRuntime\v4.0_4.0.0.0__b77a5c561934e089\System.Runtime.WindowsRuntime.dll'
$facade=Join-Path $env:SystemRoot 'Microsoft.Net\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll'
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:'+ $Output) ('/reference:'+ $metadata[0]) ('/reference:'+ $runtime) ('/reference:'+ $facade) (Join-Path $PSScriptRoot 'LockScreenBridge.cs')
if($LASTEXITCODE -ne 0){throw 'The native lock-screen helper did not compile.'}
