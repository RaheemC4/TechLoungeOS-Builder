param([switch]$Library)
$ErrorActionPreference='Stop'

function Get-GraphicsDevices {
  $adapters=@(Get-CimInstance Win32_VideoController -ErrorAction Stop)
  if(!$adapters.Count){throw 'Windows has not reported a display-device inventory yet. No graphics driver work was queued.'}
  $selected=@($adapters | Where-Object {$_.PNPDeviceID -match '^PCI\\VEN_(1002|8086)&DEV_[0-9A-F]{4}(&|$)'})
  if(!$selected.Count){return}
  $entities=@(Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Display'" -ErrorAction Stop)
  $drivers=@(Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceClass='DISPLAY'" -ErrorAction Stop)
  foreach($adapter in $selected){
    $entity=@($entities | Where-Object {$_.DeviceID -eq $adapter.PNPDeviceID -and $_.Present -ne $false})
    if($entity.Count -ne 1){throw 'The installed graphics hardware identity could not be verified.'}
    $vendor=if($adapter.PNPDeviceID -match '^PCI\\VEN_1002&'){'AMD'}else{'Intel'}
    $ids=@($entity[0].HardwareID | Where-Object {$_ -match '^PCI\\VEN_(1002|8086)&DEV_[0-9A-F]{4}(&|$)'})
    if(!$ids.Count){throw "No hardware IDs were reported for $($adapter.Name). No driver family was guessed."}
    $driver=@($drivers | Where-Object {$_.DeviceID -eq $adapter.PNPDeviceID})
    [pscustomobject]@{Vendor=$vendor;Name=$adapter.Name;PNPDeviceID=$adapter.PNPDeviceID;HardwareIds=$ids;DriverVersion=if($driver.Count -eq 1){$driver[0].DriverVersion}else{$null};Provider=if($driver.Count -eq 1){$driver[0].DriverProviderName}else{$null};Signed=($driver.Count -eq 1 -and $driver[0].IsSigned)}
  }
}
function Test-GraphicsProvider([string]$Vendor,[string]$Provider){
  if($Vendor -eq 'AMD'){return $Provider -match '^(Advanced Micro Devices|AMD|ATI Technologies)(\W|$)'}
  if($Vendor -eq 'Intel'){return $Provider -match '^Intel(\W|$)'}
  return $false
}
function Test-GraphicsReady($Device,[string]$MinimumVersion){
  $current=$null;$minimum=$null
  $Device.Signed -and (Test-GraphicsProvider $Device.Vendor $Device.Provider) -and [version]::TryParse($Device.DriverVersion,[ref]$current) -and [version]::TryParse($MinimumVersion,[ref]$minimum) -and $current -ge $minimum
}
function Select-GraphicsUpdates($Devices,$Updates){
  $chosen=@()
  foreach($device in $Devices){
    $candidates=@()
    foreach($update in $Updates){
      if($update.DriverClass -ne 'Display' -or !(Test-GraphicsProvider $device.Vendor $update.DriverProvider) -or $update.DriverHardwareID -notin $device.HardwareIds){continue}
      # WUA exposes no structured version field. Accept only its established
      # four-part title suffix; a changed format requires review, not guessing.
      if($update.Title -notmatch '(?:^| - )(?<version>\d+\.\d+\.\d+\.\d+)$'){throw 'A matching graphics update has no verifiable version. It was not installed.'}
      $version=[version]$Matches.version
      if(Test-GraphicsReady $device $version.ToString()){continue}
      $candidates += [pscustomobject]@{DeviceId=$device.PNPDeviceID;Vendor=$device.Vendor;Version=$version;Update=$update}
    }
    if($candidates.Count){$chosen += $candidates | Sort-Object Version -Descending | Select-Object -First 1}
    elseif(!(Test-GraphicsReady $device '0.0.0.0')){throw "No signed, compatible $($device.Vendor) driver is available for $($device.Name). Check Windows Update or the machine's OEM support page."}
  }
  $chosen
}
function Invoke-GraphicsVendor([string]$Vendor,$Devices,[string]$State,[string]$Boot){
  $done=Join-Path $State ($Vendor+'.done')
  if(Test-Path $done){return}
  $pending=Join-Path $State ($Vendor+'.pending.json')
  if(Test-Path $pending){
    $receipt=Get-Content -LiteralPath $pending -Raw|ConvertFrom-Json
    if($receipt.Boot -eq $Boot){throw "$Vendor graphics setup is waiting for a normal Windows restart."}
    $fresh=@(Get-GraphicsDevices)
    foreach($expected in $receipt.Expected){
      $actual=@($fresh|Where-Object {$_.PNPDeviceID -eq $expected.DeviceId})
      if($actual.Count -ne 1 -or !(Test-GraphicsReady $actual[0] $expected.Version)){throw "$Vendor driver readiness could not be verified after restart."}
    }
    Set-Content $done 'Installed and verified after restart';return
  }
  $session=New-Object -ComObject Microsoft.Update.Session
  $search=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
  if($search.ResultCode -ne 2){throw "$Vendor graphics update search did not complete."}
  $choices=@(Select-GraphicsUpdates $Devices @($search.Updates))
  if(!$choices.Count){Set-Content $done 'Existing signed driver retained; no newer hardware-matched Windows Update package offered.';return}
  $updates=New-Object -ComObject Microsoft.Update.UpdateColl
  $seen=@{}
  foreach($choice in $choices){
    $id=$choice.Update.Identity.UpdateID+'|'+$choice.Update.Identity.RevisionNumber
    if($seen.ContainsKey($id)){continue}
    if(!$choice.Update.EulaAccepted){$choice.Update.AcceptEula()}
    [void]$updates.Add($choice.Update);$seen[$id]=$true
  }
  $downloader=$session.CreateUpdateDownloader();$downloader.Updates=$updates
  if($downloader.Download().ResultCode -ne 2){throw "$Vendor graphics driver download failed."}
  $installer=$session.CreateUpdateInstaller();$installer.Updates=$updates;$installer.ForceQuiet=$true
  $result=$installer.Install()
  # Record individual results before handling partial success; do not pretend
  # an overall success means every package installed.
  $results=@(for($i=0;$i -lt $updates.Count;$i++){$r=$result.GetUpdateResult($i);[pscustomobject]@{UpdateId=$updates.Item($i).Identity.UpdateID;ResultCode=$r.ResultCode;HResult=$r.HResult}})
  $expected=@($choices|ForEach-Object {[pscustomobject]@{DeviceId=$_.DeviceId;Version=$_.Version.ToString()}})
  [pscustomobject]@{Source='Windows Update hardware-matched display drivers';Vendor=$Vendor;Boot=$Boot;Expected=$expected;Results=$results;RebootRequired=$result.RebootRequired} | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $State ($Vendor+'.receipt.json'))
  if($result.ResultCode -ne 2 -or @($results|Where-Object {$_.ResultCode -ne 2}).Count){throw "$Vendor driver installation is incomplete. Successful package work is retained for the next retry."}
  if(!$result.RebootRequired){
    $fresh=@(Get-GraphicsDevices)
    $unready=@($expected|Where-Object {$e=$_;$a=@($fresh|Where-Object {$_.PNPDeviceID -eq $e.DeviceId});$a.Count -ne 1 -or !(Test-GraphicsReady $a[0] $e.Version)})
    if(!$unready.Count){Set-Content $done 'Installed and verified';return}
  }
  [pscustomobject]@{Boot=$Boot;Expected=$expected}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 $pending
  throw "$Vendor graphics driver installation finished; restart Windows normally to verify activation. No restart was forced."
}
function Invoke-GraphicsProvisioning([string]$State){
  $devices=@(Get-GraphicsDevices)
  if(!$devices.Count){Set-Content "$State\status.txt" 'No AMD/Intel graphics adapters present. Nothing downloaded.';Set-Content "$State\complete" 'Not applicable';return}
  $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')
  $errors=New-Object 'Collections.Generic.List[string]'
  foreach($vendor in @('AMD','Intel')){
    $matching=@($devices|Where-Object {$_.Vendor -eq $vendor})
    if(!$matching.Count){continue}
    try{Invoke-GraphicsVendor $vendor $matching $State $boot}catch{$errors.Add($_.Exception.Message)}
  }
  if($errors.Count){Set-Content "$State\status.txt" $errors.ToArray();throw ($errors -join '; ')}
  Set-Content "$State\status.txt" 'Detected AMD/Intel adapters completed hardware-matched Windows Update checks. This does not guarantee the absolute newest vendor reference release.'
  Set-Content "$State\complete" 'Completed'
}
if(!$Library){
  $state=Join-Path $PSScriptRoot 'state'
  if(!(Test-Path -LiteralPath "$env:ProgramData\TechLoungeOS\MachineSetup\complete")){Write-Output 'Waiting for desktop machine setup before graphics installation.';exit 0}
  if(!(Test-Path $state)){throw 'Graphics setup state was not prepared by Windows setup.'}
  if(Test-Path "$state\complete"){Unregister-ScheduledTask -TaskName 'TechLoungeOS-Graphics' -Confirm:$false -ErrorAction SilentlyContinue;exit 0}
  $lock=[IO.File]::Open("$state\worker.lock",'OpenOrCreate','ReadWrite','None')
  $transcript=$false
  $driverLease=$null
  try{
    Start-Transcript "$state\setup.log" -Append|Out-Null;$transcript=$true
    $manifest=Get-Content (Join-Path $PSScriptRoot 'payload-sha256.json') -Raw|ConvertFrom-Json
    foreach($item in $manifest.PSObject.Properties){
      $path=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $item.Name))
      if(!$path.StartsWith($PSScriptRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Value){throw 'Graphics payload integrity check failed.'}
    }
    . (Join-Path $PSScriptRoot 'driver-lock.ps1')
    if(!(Test-GraphicsAppsReady)){Set-Content "$state\status.txt" 'Waiting for the selected apps and a signed-in desktop before graphics installation.';exit 0}
    $driverLease=Get-GraphicsInstallLock (Join-Path $env:SystemRoot 'Setup\TechLoungeOS-graphics-install.lock') (Join-Path $state 'status.txt')
    Invoke-GraphicsProvisioning $state
    Unregister-ScheduledTask -TaskName 'TechLoungeOS-Graphics' -Confirm:$false
  }catch{Set-Content "$state\status.txt" $_.Exception.Message;Write-Warning $_.Exception.Message;exit 1}
  finally{if($driverLease){$driverLease.Dispose()};if($transcript){Stop-Transcript|Out-Null};$lock.Dispose()}
}
