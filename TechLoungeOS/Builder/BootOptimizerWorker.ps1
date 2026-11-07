param([switch]$Library)
$ErrorActionPreference='Stop'

. (Join-Path $PSScriptRoot 'app-readiness.ps1')
if($Library){return}
if([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne 'S-1-5-18'){throw 'Run only through the fixed Windows Setup task'}
$bootRoot=$PSScriptRoot
$bootLease=[IO.File]::Open((Join-Path $bootRoot 'worker.lock'),'OpenOrCreate','ReadWrite','None')
$bootState=$null;$bootTranscript=$false;$bootOldHive=$null;$bootDriverLease=$null
try{
  $bootTarget=Get-PostAppTarget 'BootOptimizer'
  $bootState=Join-Path $bootRoot ('state\'+$bootTarget.Sid)
  New-Item $bootState -ItemType Directory -Force|Out-Null
  if(Test-Path "$bootState\complete"){return}
  if(Test-Path "$bootState\failed"){Remove-Item -LiteralPath "$bootState\failed"}
  if(!(Test-Path -LiteralPath "$env:ProgramData\TechLoungeOS\MachineSetup\complete")){throw 'Required machine installers have not completed; Boot Optimizer was deferred.'}
  $bootApps=Read-PostAppSelections (Join-Path $bootRoot 'apps.json')
  if(!(Test-PostAppReceipts $bootTarget.AppState $bootApps)){throw 'Selected apps are not all installed; Boot Optimizer made no changes'}
  $bootSource=Join-Path $bootRoot 'Optimize-Boot.ps1'
  if((Get-FileHash $bootSource -Algorithm SHA256).Hash -ne '059461812d10f91e8c3fed05f6142606346656bf1f311a0256564c679dde6d07'){throw 'The reviewed Boot Optimizer script failed its integrity check'}
  Start-Transcript "$bootState\apply.log" -Append|Out-Null;$bootTranscript=$true
  . (Join-Path $bootRoot 'driver-lock.ps1')
  $bootDriverLease=Get-GraphicsInstallLock (Join-Path $env:SystemRoot 'Setup\TechLoungeOS-graphics-install.lock') (Join-Path $bootState 'status.txt') 180
  # HKCU is process-local here. SYSTEM must tune the actual installed user's hive.
  $bootOldHive=(Get-PSDrive HKCU).Root
  Remove-PSDrive HKCU -Scope Global
  New-PSDrive -Name HKCU -PSProvider Registry -Root ('HKEY_USERS\'+$bootTarget.Sid) -Scope Global|Out-Null
  if((Get-PSDrive HKCU).Root -ne 'HKEY_USERS\'+$bootTarget.Sid){throw 'Boot Optimizer could not bind the actual user hive'}
  $env:LOCALAPPDATA=Join-Path $bootTarget.Profile 'AppData\Local'
  . $bootSource -RollbackFilePath (Join-Path $bootState 'rollback.json')
  $ErrorActionPreference='Stop'
  if($script:Failed.Count){throw ('Boot Optimizer reported failed actions: '+($script:Failed -join '; '))}
  [pscustomobject]@{sid=$bootTarget.Sid;appsVerified=$true;apps=$bootApps;changes=@($script:Changes);warnings=@($script:Warnings);utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 5|Set-Content "$bootState\receipt.json" -Encoding UTF8
  Set-Content "$bootState\status.txt" 'Boot Optimizer completed after all selected apps. Rollback data retained. No restart was forced.'
  Set-Content "$bootState\complete" 'Completed'
}catch{
  Set-Content (Join-Path $bootRoot 'status.txt') $_.Exception.Message
  if($bootState){Set-Content "$bootState\status.txt" $_.Exception.Message;Set-Content "$bootState\failed" $_.Exception.Message}
  throw
}finally{
  if($bootOldHive){Remove-PSDrive HKCU -Scope Global -ErrorAction SilentlyContinue;New-PSDrive -Name HKCU -PSProvider Registry -Root $bootOldHive -Scope Global|Out-Null}
  if($bootDriverLease){$bootDriverLease.Dispose()}
  if($bootTranscript){Stop-Transcript|Out-Null}
  $bootLease.Dispose()
}
