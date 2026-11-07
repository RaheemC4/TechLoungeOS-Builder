param([switch]$Library)
$ErrorActionPreference='Stop'
$env:PSModulePath=(Join-Path $PSHOME 'Modules')+';'+$env:PSModulePath

function Test-DesktopSetupRelative([string]$Path) {
 if(!$Path -or $Path.Length -gt 220 -or $Path -match '[\p{Cc}:/\*?"<>|]'){return $false}
 foreach($part in $Path.Split('\')){
  if(!$part -or $part -in @('.','..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.Split('.')[0].ToUpperInvariant() -cmatch '\A(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9\u00b9\u00b2\u00b3]|LPT[0-9\u00b9\u00b2\u00b3])\z'){return $false}
 }
 return $true
}

function Get-DesktopSetupPath([string]$Root,[string]$Relative) {
 if(!(Test-DesktopSetupRelative $Relative)){throw 'Unsafe desktop setup path'}
 $base=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\'
 $path=[IO.Path]::GetFullPath((Join-Path $base $Relative))
 if(!$path.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Desktop setup path escaped its root'}
 return $path
}

function Assert-DesktopSetupUnlinked([string]$Path) {
 $part=[IO.Path]::GetFullPath($Path)
 while($part){
  if((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked desktop setup paths are not supported'}
  $part=Split-Path -Parent $part
 }
}

function Copy-DesktopSetupInput($Item,[string]$SourceRoot,[string]$CacheRoot) {
 if([long]$Item.Bytes -lt 0 -or [long]$Item.Bytes -gt 4294967296 -or [string]$Item.Sha256 -notmatch '\A[a-fA-F0-9]{64}\z'){throw 'Unsafe desktop setup file metadata'}
 $source=Get-DesktopSetupPath $SourceRoot ([string]$Item.Path)
 $target=Get-DesktopSetupPath $CacheRoot ([string]$Item.Destination)
 Assert-DesktopSetupUnlinked $source
 Assert-DesktopSetupUnlinked $target
 # CacheRoot is created beneath the already protected SYSTEM task directory.
 New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
 $input=[IO.FileStream]::new($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try {
  if($input.Length -ne [long]$Item.Bytes){throw 'Desktop setup input length changed'}
  $sha=[Security.Cryptography.SHA256]::Create()
  try{$actual=([BitConverter]::ToString($sha.ComputeHash($input))).Replace('-','')}finally{$sha.Dispose()}
  if($actual -ine [string]$Item.Sha256){throw 'Desktop setup input checksum changed'}
  $input.Position=0
  # The same handle denies writers/deletion until copying has finished.
  $output=[IO.FileStream]::new($target,[IO.FileMode]::Create,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try {
   $input.CopyTo($output);$output.Flush($true);$output.Position=0
   $verify=[Security.Cryptography.SHA256]::Create()
   try{$copied=([BitConverter]::ToString($verify.ComputeHash($output))).Replace('-','')}finally{$verify.Dispose()}
   if($copied -ine [string]$Item.Sha256){throw 'Desktop setup cache readback checksum changed'}
  }finally{$output.Dispose()}
 }finally{$input.Dispose()}
}

# Unit checks may load the file-copy boundary without registering/running tasks.
if($Library){return}
if([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne 'S-1-5-18'){throw 'Desktop setup requires its fixed SYSTEM task'}
$root=[IO.Path]::GetFullPath($PSScriptRoot)
if($root -ine [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'TechLoungeOS\MachineSetup'))){throw 'Desktop setup must stay in its protected fixed folder'}
Assert-DesktopSetupUnlinked $root

function Write-DesktopSetupStatus([string]$Phase,[string]$Step,[string]$Message) {
 $status=[ordered]@{schema=1;phase=$Phase;step=$Step;message=$Message;updatedUtc=[DateTime]::UtcNow.ToString('o');completed=$script:done;total=$script:total}
 $path=Join-Path $root 'status.json';$temporary=$path+'.tmp'
 [IO.File]::WriteAllText($temporary,($status|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
 if(Test-Path -LiteralPath $path){[IO.File]::Replace($temporary,$path,[NullString]::Value)}else{[IO.File]::Move($temporary,$path)}
}

$gate=[IO.File]::Open((Join-Path $root 'install.lock'),'OpenOrCreate','ReadWrite','None')
$script:done=0;$script:total=0;$current=''
try {
 if(Test-Path -LiteralPath (Join-Path $root 'runtime-policy.ps1')){
  . (Join-Path $root 'runtime-policy.ps1')
  Initialize-TweaksRuntimePolicies
 }
 if(Test-Path -LiteralPath (Join-Path $root 'complete')){exit 0}
 $runningFile=Join-Path $root 'running.json'
 if(Test-Path -LiteralPath $runningFile){
  $prior=[IO.File]::ReadAllText($runningFile,[Text.Encoding]::UTF8)|ConvertFrom-Json
  $active=Get-Process -Id ([int]$prior.pid) -ErrorAction SilentlyContinue
  if($active -and $active.StartTime.ToUniversalTime().ToString('o') -ceq [string]$prior.startedUtc){
   Write-DesktopSetupStatus 'installing' ([string]$prior.id) 'The previous installer is still running. No duplicate was started.';exit 4
  }
 }
 # There is no boot trigger. The ordinary-user launcher also checks actual shell windows.
 if(!@(Get-Process explorer -ErrorAction SilentlyContinue|Where-Object {$_.SessionId -gt 0}).Count){
  Write-DesktopSetupStatus 'waiting' '' 'Waiting for the Windows desktop. No installer was started.';exit 3
 }
 $manifest=Join-Path $root 'steps.json'
 if((Get-Item -LiteralPath $manifest).Length -gt 1048576){throw 'Desktop setup manifest exceeds its budget'}
 $steps=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($manifest,[Text.Encoding]::UTF8))
 $steps=@($steps)
 $order=@('Runtimes','Edge','StoreXbox','HWiNFO','LegacyNetwork','Recovery','Office')
 $previous=-1
 foreach($step in $steps){
  $index=[Array]::IndexOf($order,[string]$step.Id)
  if($index -le $previous -or @($step.Files).Count -lt 1 -or @($step.Files).Count -gt 4096){throw 'Unsupported or unordered desktop setup step'}
  $previous=$index
  $names=@{}
  foreach($file in $step.Files){
   if($file.Root -cnotin @('Windows','Common') -or !(Test-DesktopSetupRelative ([string]$file.Path)) -or !(Test-DesktopSetupRelative ([string]$file.Destination)) -or
      [long]$file.Bytes -lt 0 -or [long]$file.Bytes -gt 4294967296 -or [string]$file.Sha256 -notmatch '\A[a-fA-F0-9]{64}\z' -or $names.ContainsKey([string]$file.Destination)){throw 'Unsafe desktop setup file manifest'}
   $names[[string]$file.Destination]=$true
  }
  if(($step.Files|Measure-Object Bytes -Sum).Sum -gt 10737418240){throw 'Desktop setup step exceeds its byte budget'}
 }
 if(!$steps.Count){throw 'No machine installation steps were staged'}
 $script:total=$steps.Count
 foreach($step in $steps){
  $current=[string]$step.Id;$receipt=Join-Path $root ($current+'.done')
  if(Test-Path -LiteralPath $receipt){$script:done++;continue}
  $cache=Get-DesktopSetupPath $root ('payloads\'+$current)
  Assert-DesktopSetupUnlinked $cache
  New-Item -ItemType Directory -Path $cache -Force|Out-Null
  $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($cache))
  $reserve=if($current -ceq 'Office'){6442450944}else{2147483648}
  if($drive.AvailableFreeSpace -lt (($step.Files|Measure-Object Bytes -Sum).Sum+$reserve)){throw 'Free more space on the Windows drive before retrying setup'}
  Write-DesktopSetupStatus 'preparing' $current 'Preparing and verifying installer files. You can keep using your desktop.'
  $cached=Test-Path -LiteralPath (Join-Path $cache 'cache-ready')
  foreach($file in $step.Files){
   $target=Get-DesktopSetupPath $cache ([string]$file.Destination)
   Assert-DesktopSetupUnlinked $target
   # Office writes its actual local source into this protected working configuration.
   if($cached -and $current -ceq 'Office' -and $file.Destination -ceq 'Configuration.xml' -and (Test-Path -LiteralPath $target)){continue}
   if((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).Length -eq [long]$file.Bytes -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ieq [string]$file.Sha256){continue}
   $sourceRoot=if($file.Root -ceq 'Windows'){$env:SystemRoot}else{$env:ProgramData}
   Copy-DesktopSetupInput $file $sourceRoot $cache
  }
  [IO.File]::WriteAllText((Join-Path $cache 'cache-ready'),'Pinned files copied and read back successfully')
  Write-DesktopSetupStatus 'installing' $current 'Installing quietly. Your Windows desktop remains available.'
  $launcher=Join-Path $env:ProgramData 'TechLoungeOS\FirstLogin.exe'
  $start=[Diagnostics.ProcessStartInfo]::new($launcher,('--machine-installer '+$current))
  $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$cache
  $installer=[Diagnostics.Process]::Start($start)
  try {
   try{[IO.File]::WriteAllText((Join-Path $root 'running.json'),(@{id=$current;pid=$installer.Id;startedUtc=$installer.StartTime.ToUniversalTime().ToString('o')}|ConvertTo-Json -Compress))}
   catch{if(!$installer.HasExited){throw}}
  # Never terminate Windows servicing or a vendor installer to satisfy a timer.
  $installer.WaitForExit()
  if($installer.ExitCode -notin @(0,3010)){throw ('Installer did not complete: '+$current+' (exit '+$installer.ExitCode+')')}
  }finally{$installer.Dispose()}
  $marker=switch($current){
   'StoreXbox'{Join-Path $cache 'packages-ready'}
   'Office'{Join-Path $cache 'TechLoungeOS-Office-State\complete'}
   'LegacyNetwork'{Join-Path $env:ProgramData 'TechLoungeOS\legacy-network\complete'}
   'Recovery'{Join-Path $env:ProgramData 'TechLoungeOS\recovery\complete'}
   default{Join-Path $cache 'complete'}
  }
  if(!(Test-Path -LiteralPath $marker)){throw ('Installer verification is incomplete: '+$current)}
  if((Test-Path -LiteralPath (Join-Path $cache 'restart-required')) -or
     (Test-Path -LiteralPath (Join-Path $cache 'TechLoungeOS-Office-State\restart-required')) -or
     ($current -ceq 'LegacyNetwork' -and (Test-Path -LiteralPath (Join-Path $env:ProgramData 'TechLoungeOS\legacy-network\restart-required')))){
    [IO.File]::WriteAllText((Join-Path $root 'restart-required'),'A normal Windows restart is needed. None was forced.')
  }
  [IO.File]::WriteAllText($receipt,'Installer and completion receipt verified')
  $script:done++
 }
 [IO.File]::WriteAllText((Join-Path $root 'complete'),'All required machine installers completed')
 Write-DesktopSetupStatus 'complete' '' 'Required installers are ready. Selected apps and final settings can continue.'
 Remove-Item -LiteralPath (Join-Path $root 'running.json') -ErrorAction SilentlyContinue
}catch{
 Write-DesktopSetupStatus 'failed' $current $_.Exception.Message
 throw
}finally{$gate.Dispose()}
