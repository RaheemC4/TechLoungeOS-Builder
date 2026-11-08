function Get-PostAppTarget([string]$Purpose) {
  if($Purpose -notin @('BootOptimizer','PowerDefaults','Graphics')){throw 'Unknown fixed post-install action'}
  $root=Join-Path $env:ProgramData ('TechLoungeOS\'+$(if($Purpose -eq 'Graphics'){'BootOptimizer'}else{$Purpose}))
  $apps=@(Read-PostAppSelections (Join-Path $root 'apps.json'))
  $owners=@(foreach($process in @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'")){
    $owner=Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid
    if($owner.ReturnValue -eq 0 -and $owner.Sid){$owner.Sid}
  })
  $owners=@($owners|Select-Object -Unique)
  if($owners.Count -gt 32){throw 'Too many signed-in profiles to inspect safely'}
  $ready=@(foreach($ownerSid in $owners){
  $sid=[string]$ownerSid
  if($sid -notmatch '^S-1-(?:5-21|12-1)-\d+(?:-\d+)+$'){throw 'Unexpected interactive user SID'}
  $result=Join-Path $root ('state\'+$sid)
  if($Purpose -ne 'Graphics' -and (Test-Path -LiteralPath (Join-Path $result 'complete'))){continue}
  $profiles=@(Get-CimInstance Win32_UserProfile -Filter "SID='$sid'"|Where-Object {$_.Loaded -and !$_.Special})
  if($profiles.Count -ne 1 -or !$profiles[0].LocalPath){continue}
  $profile=[IO.Path]::GetFullPath($profiles[0].LocalPath)
  $state=Join-Path $profile 'AppData\Local\TechLoungeOS'
  $request=Join-Path $state ('post-app-'+$Purpose+'.request')
  if($Purpose -eq 'Graphics'){if(!(Test-Path -LiteralPath $state)){continue}}
  elseif(!(Test-Path -LiteralPath $request)){continue}
  $part=$state
  while($part){
    if((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked user profile paths are not supported by automatic Post-install setup'}
    $part=Split-Path $part
  }
  if(!(Test-Path -LiteralPath ('Registry::HKEY_USERS\'+$sid))){throw 'The interactive user registry hive is not loaded'}
  if($Purpose -eq 'Graphics'){
    if(Test-PostAppReceipts $state $apps){[pscustomobject]@{Sid=$sid;Profile=$profile;AppState=$state;RequestedUtc=[DateTime]::MinValue}}
    continue
  }
  $info=Get-Item -LiteralPath $request -Force
  if($info.PSIsContainer -or ($info.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked or invalid post-install request'}
  $input=[IO.File]::Open($request,'Open','Read','Read')
  try{
    if($input.Length -gt 128){throw 'Post-install request is too large'}
    $reader=[IO.StreamReader]::new($input,[Text.Encoding]::UTF8)
    try{$requestedSid=$reader.ReadToEnd().Trim()}finally{$reader.Dispose()}
  }finally{$input.Dispose()}
  if($requestedSid -cne $sid){throw 'Post-install request does not match its Windows profile'}
  if(!(Test-PostAppReceipts $state $apps)){continue}
  if($Purpose -eq 'PowerDefaults' -and !(Test-Path -LiteralPath (Join-Path $env:ProgramData ('TechLoungeOS\BootOptimizer\state\'+$sid+'\complete')))){continue}
  $failed=Join-Path $result 'failed'
  if((Test-Path -LiteralPath $failed) -and (Get-Item -LiteralPath $failed).LastWriteTimeUtc -ge $info.LastWriteTimeUtc){continue}
  # The SID comes from the process token/profile service, never from a caller-supplied target.
  [pscustomobject]@{Sid=$sid;Profile=$profile;AppState=$state;RequestedUtc=$info.LastWriteTimeUtc}
  })
  if(!$ready.Count){if($Purpose -eq 'Graphics'){return};throw 'No safely identified signed-in user has requested this action after completing their apps.'}
  $ready|Sort-Object RequestedUtc,Sid|Select-Object -First 1
}
function Read-PostAppSelections([string]$Path){
  $text=[IO.File]::ReadAllText($Path).Trim()
  if(!$text.StartsWith('[') -or !$text.EndsWith(']')){throw 'The protected selected-app manifest must be a JSON array'}
  $parsed=ConvertFrom-Json -InputObject $text
  foreach($id in $parsed){
    if($id -isnot [string] -or $id.Length -gt 200 -or $id -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*(?:\.[A-Za-z0-9][A-Za-z0-9_-]*)+$'){throw 'Invalid selected app identity in the protected Post-install setup manifest'}
  }
  return $parsed
}
function Test-PostAppReceipts([string]$State,$Apps){
  foreach($id in $Apps){
    if($id -isnot [string] -or $id.Length -gt 200 -or $id -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*(?:\.[A-Za-z0-9][A-Za-z0-9_-]*)+$'){throw 'Invalid selected app identity in the protected Post-install setup manifest'}
    if(!(Test-Path -LiteralPath (Join-Path $State ($id+'.done')))){return $false}
  }
  return $true
}
