param([switch]$Library)
$ErrorActionPreference = 'Stop'

function Get-NvidiaDevices {
  # PCI vendor ID works before the driver has named a card. Never infer hardware
  # from the builder host, an old registry key or a marketing-name substring.
  $adapters=@(Get-CimInstance Win32_VideoController -ErrorAction Stop)
  if(!$adapters.Count){throw 'Windows has not reported a display-device inventory yet. No NVIDIA driver work was queued.'}
  @($adapters | Where-Object { $_.PNPDeviceID -match '^PCI\\VEN_10DE&DEV_[0-9A-F]{4}(&|$)' })
}
function Normalize-NvidiaName([string]$Name) {
  (($Name -replace '\(R\)|\(TM\)| with Max-Q Design','') -replace '\s+',' ').Trim().ToUpperInvariant()
}
function Get-NvidiaLookup([int]$Type, [int]$Parent) {
  [xml]$xml = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 30 -Uri "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=$Type&ParentID=$Parent").Content
  @($xml.LookupValueSearch.LookupValues.LookupValue)
}
function Assert-NvidiaUrl([string]$Url) {
  $uri = [uri]$Url
  if ($uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Port -ne 443 -or $uri.DnsSafeHost -notin @('us.download.nvidia.com','international.download.nvidia.com','download.nvidia.com')) { throw 'NVIDIA returned an untrusted download address.' }
}
function Get-NvidiaRelease([string]$Name) {
  $productsMatched = @()
  # Query product types and series from NVIDIA, not a stale GPU-family table.
  foreach ($type in @(Get-NvidiaLookup 1 0)) {
    if ($type.Name -notmatch 'GeForce|TITAN|Quadro|RTX PRO|NVS') { continue }
    foreach ($series in @(Get-NvidiaLookup 2 ([int]$type.Value))) {
      foreach ($product in @(Get-NvidiaLookup 3 ([int]$series.Value))) {
        if ((Normalize-NvidiaName $product.Name) -eq (Normalize-NvidiaName $Name)) { $productsMatched += [int]$product.Value }
      }
    }
  }
  $productsMatched = @($productsMatched | Select-Object -Unique)
  if ($productsMatched.Count -ne 1) { throw "No unambiguous NVIDIA catalogue match for $Name. Install its supported OEM/NVIDIA driver manually; no family was guessed." }
  $uri = 'https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&pfid=' + $productsMatched[0] + '&osID=135&languageCode=1033&isWHQL=1&dch=1&upCRD=0&numberOfResults=10'
  $data = Invoke-RestMethod -Uri $uri -TimeoutSec 45
  $releases = @($data.IDS | ForEach-Object { $_.downloadInfo } | Where-Object { $_.IsWHQL -eq '1' -and $_.Version -match '^\d{3}\.\d{2}$' -and $_.DownloadURL })
  if (!$releases.Count) { throw "No compatible Windows 11 WHQL driver is currently offered for $Name." }
  $release = $releases | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
  Assert-NvidiaUrl $release.DownloadURL
  $release
}
function Get-NvidiaVersion([string]$DriverVersion) {
  if ($DriverVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { return $null }
  $digits = $DriverVersion -replace '\D',''
  if ($digits.Length -lt 5) { return $null }
  $tail = $digits.Substring($digits.Length - 5)
  [version]($tail.Substring(0,3) + '.' + $tail.Substring(3))
}
function Assert-NvidiaSignature([string]$Path) {
  $signature = Get-AuthenticodeSignature -LiteralPath $Path
  if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=NVIDIA Corporation(?:,|$)') { throw 'The driver is not signed by a trusted NVIDIA publisher.' }
}
function Invoke-NvidiaProcess([string]$Path, [string]$Arguments) {
  $process = Start-Process -FilePath $Path -WorkingDirectory (Split-Path $Path) -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
  $process.ExitCode
}
function Assert-NvidiaSpace([string]$State) {
  $drive=New-Object IO.DriveInfo([IO.Path]::GetPathRoot($State))
  if($drive.AvailableFreeSpace -lt 8GB){throw 'NVIDIA driver download/extraction needs at least 8 GB free on the installed Windows drive.'}
}
function Export-NvidiaProfile([string]$Payload, [string]$Destination) {
  $before=@(Get-ChildItem -LiteralPath $Payload -Filter 'CustomProfiles_*.nip' | ForEach-Object FullName)
  $code=Invoke-NvidiaProcess (Join-Path $Payload 'nvidiaProfileInspector.exe') '-exportCustomized'
  $new=@(Get-ChildItem -LiteralPath $Payload -Filter 'CustomProfiles_*.nip' | Where-Object { $_.FullName -notin $before })
  if ($code -ne 0 -or $new.Count -ne 1) { throw 'Could not export NVIDIA profile state. The operation remains incomplete.' }
  Move-Item -LiteralPath $new[0].FullName -Destination $Destination -Force
}
function Install-NvidiaBaseDriver($Devices) {
  $session=New-Object -ComObject Microsoft.Update.Session
  $search=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
  if ($search.ResultCode -ne 2) { throw 'Windows Update did not complete its compatible-driver search.' }
  $ids=@($Devices | ForEach-Object { if($_.PNPDeviceID -match '^(PCI\\VEN_10DE&DEV_[0-9A-F]{4})'){$Matches[1]} })
  $updates=New-Object -ComObject Microsoft.Update.UpdateColl
  foreach($update in $search.Updates) {
    if($update.DriverClass -ne 'Display' -or $update.DriverProvider -notmatch '^NVIDIA' -or $update.DriverHardwareID -notmatch '^(PCI\\VEN_10DE&DEV_[0-9A-F]{4})' -or $Matches[1] -notin $ids){continue}
    if(!$update.EulaAccepted){$update.AcceptEula()}
    [void]$updates.Add($update)
  }
  if(!$updates.Count){throw 'Windows Update has no matching NVIDIA base driver. Install the supported OEM/NVIDIA driver manually, then retry. No GPU family was guessed.'}
  $downloader=$session.CreateUpdateDownloader();$downloader.Updates=$updates
  if($downloader.Download().ResultCode -ne 2){throw 'The compatible NVIDIA base-driver download failed.'}
  $installer=$session.CreateUpdateInstaller();$installer.Updates=$updates;$installer.ForceQuiet=$true
  if($installer.Install().ResultCode -ne 2){throw 'The compatible NVIDIA base-driver installation did not complete.'}
}
function Invoke-NvidiaProvisioning([string]$Payload, [string]$State) {
  $devices = @(Get-NvidiaDevices)
  if (!$devices.Count) { Set-Content "$State\status.txt" 'No NVIDIA PCI display adapter: NVIDIA setup skipped.'; Set-Content "$State\complete" 'Not applicable'; return }
  $boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')
  if ((Test-Path "$State\restart-boot.txt") -and (Get-Content "$State\restart-boot.txt" -Raw).Trim() -eq $boot) { throw 'NVIDIA profile setup is waiting for a normal Windows restart.' }
  # An unknown/basic adapter cannot be matched by name. Do not guess a modern
  # family for a legacy card, nor use the development machine's GPU identity.
  if (@($devices | Where-Object { $_.Name -match 'Basic Display' -or $_.Name -notmatch 'NVIDIA' }).Count) {
    Install-NvidiaBaseDriver $devices
    Set-Content "$State\restart-boot.txt" $boot
    throw 'The matching NVIDIA base driver was installed through Windows Update. Restart Windows normally; current NVIDIA driver/profile setup will continue afterwards.'
  }
  $releases = @($devices | ForEach-Object { Get-NvidiaRelease $_.Name })
  $urls = @($releases.DownloadURL | Select-Object -Unique)
  if ($urls.Count -ne 1) { throw 'These NVIDIA adapters require different driver packages. Automatic installation was skipped to avoid replacing an incompatible driver branch.' }
  $release = $releases[0]
  $needsDriver = @($devices | Where-Object { !(Get-NvidiaVersion $_.DriverVersion) -or (Get-NvidiaVersion $_.DriverVersion) -lt [version]$release.Version }).Count -gt 0
  if ($needsDriver) {
    Assert-NvidiaSpace $State
    $download = Join-Path $State 'driver.exe'
    Assert-NvidiaUrl $release.DownloadURL
    # No redirects to unreviewed hosts. Download bytes are validated before use.
    Invoke-WebRequest -Uri $release.DownloadURL -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 1800 -OutFile $download
    Assert-NvidiaSignature $download
    $extracted = Join-Path $State 'driver-files'
    if (!(Test-Path $extracted)) { New-Item $extracted -ItemType Directory | Out-Null }
    $extract = Invoke-NvidiaProcess (Join-Path $Payload '7zr.exe') ('x "' + $download + '" -o"' + $extracted + '" -y')
    if ($extract -ne 0) { throw "Driver extraction failed ($extract)." }
    $setup = Join-Path $extracted 'setup.exe'
    Assert-NvidiaSignature $setup
    $code = Invoke-NvidiaProcess $setup '-s -n Display.Driver'
    if ($code -notin @(0,1)) { throw "NVIDIA driver installation failed ($code)." }
    [pscustomobject]@{Version=$release.Version;Url=$release.DownloadURL;Sha256=(Get-FileHash $download -Algorithm SHA256).Hash;ExitCode=$code} | ConvertTo-Json | Set-Content "$State\driver-receipt.json"
    # Do not force a restart or apply a profile to a driver awaiting activation.
    Set-Content "$State\restart-boot.txt" (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')
    throw 'The driver installation completed. Restart Windows normally to activate it, then NVIDIA profile setup will retry.'
  }
  $ready = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceClass -eq 'DISPLAY' -and $_.DeviceID -match '^PCI\\VEN_10DE&' -and $_.IsSigned -and $_.DriverProviderName -match '^NVIDIA' })
  if ($ready.Count -lt $devices.Count) { throw 'A signed NVIDIA display driver is not ready for every detected NVIDIA adapter.' }
  $tool = Join-Path $Payload 'nvidiaProfileInspector.exe'
  $profile = Join-Path $Payload 'TechLoungeProfile.nip'
  if (!(Test-Path "$State\before.nip")) {
    Export-NvidiaProfile $Payload (Join-Path $State 'before.nip')
  }
  $code = Invoke-NvidiaProcess $tool ('-silentImport "' + $profile + '"')
  if ($code -ne 0) { throw "NVIDIA profile import failed ($code)." }
  Export-NvidiaProfile $Payload (Join-Path $State 'after.nip')
  [xml]$expected=Get-Content -LiteralPath $profile -Raw
  [xml]$actual=Get-Content -LiteralPath "$State\after.nip" -Raw
  $global=@($actual.ArrayOfProfile.Profile | Where-Object { $_.ProfileName -eq '_GLOBAL_DRIVER_PROFILE (Base Profile)' })
  if($global.Count -ne 1){throw 'NVIDIA profile import did not read back a global profile.'}
  foreach($setting in $expected.ArrayOfProfile.Profile.Settings.ProfileSetting){
    $found=@($global[0].Settings.ProfileSetting | Where-Object { $_.SettingID -eq $setting.SettingID -and $_.SettingValue -eq $setting.SettingValue -and $_.ValueType -eq $setting.ValueType })
    if($found.Count -ne 1){throw ('NVIDIA setting did not read back: '+$setting.SettingID)}
  }
  Set-Content "$State\status.txt" ('TechLounge NVIDIA profile imported after compatible driver check. Driver catalogue version: ' + $release.Version)
  # Only our own cache directories may be removed; the original ISO is unrelated.
  if (Test-Path "$State\driver.exe") { Remove-Item -LiteralPath "$State\driver.exe" -Force }
  if (Test-Path "$State\driver-files") { Remove-Item -LiteralPath "$State\driver-files" -Recurse -Force }
  Set-Content "$State\complete" 'Completed'
}
if (!$Library) {
  $state = Join-Path $PSScriptRoot 'state'
  if(!(Test-Path -LiteralPath "$env:ProgramData\TechLoungeOS\MachineSetup\complete")){Write-Output 'Waiting for desktop machine setup before graphics installation.';exit 0}
  if (!(Test-Path $state)) { throw 'NVIDIA setup state was not prepared by Windows setup.' }
  if (Test-Path "$state\complete") { Unregister-ScheduledTask -TaskName 'TechLoungeOS-Nvidia' -Confirm:$false -ErrorAction SilentlyContinue; exit 0 }
  $lock = [IO.File]::Open("$state\worker.lock",'OpenOrCreate','ReadWrite','None')
  $transcript = $false
  $driverLease=$null
  try {
    Start-Transcript "$state\setup.log" -Append | Out-Null; $transcript=$true
    $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-sha256.json') -Raw | ConvertFrom-Json
    foreach($item in $manifest.PSObject.Properties){
      $path=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $item.Name))
      if(!$path.StartsWith($PSScriptRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Value){throw 'NVIDIA setup payload integrity check failed.'}
    }
    . (Join-Path $PSScriptRoot 'driver-lock.ps1')
    if(!(Test-GraphicsAppsReady)){Set-Content "$state\status.txt" 'Waiting for the selected apps and a signed-in desktop before graphics installation.';exit 0}
    $driverLease=Get-GraphicsInstallLock (Join-Path $env:SystemRoot 'Setup\TechLoungeOS-graphics-install.lock') (Join-Path $state 'status.txt')
    Invoke-NvidiaProvisioning $PSScriptRoot $state
    Unregister-ScheduledTask -TaskName 'TechLoungeOS-Nvidia' -Confirm:$false -ErrorAction SilentlyContinue
  } catch { Set-Content "$state\status.txt" $_.Exception.Message; Write-Warning $_.Exception.Message; exit 1 }
  finally { if($driverLease){$driverLease.Dispose()}; if($transcript){Stop-Transcript|Out-Null}; $lock.Dispose() }
}
