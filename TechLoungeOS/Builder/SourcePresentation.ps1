param([switch]$Library)
$ErrorActionPreference='Stop'

function Convert-SourcePresentation([string]$Text,[string]$Kind,[string]$Wallpaper) {
 $lines=$Text -split "`r?`n"
 if($Kind -ceq 'visual'){
  $rules=@(
   '^Reg add "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce" /v "Complete" ',
   '^start %ProgramData%\\Microsoft\\DRM\\autorun\.exe ',
   '^taskkill /IM "autorun\.exe" ',
   '^powershell Stop-Process -Name "autorun"$',
   '^shutdown\.exe -r -t 15 ',
   '^%WinDir%\\System32\\restart\.vbs$',
   '^del "%~f0"$'
  )
  foreach($pattern in $rules){
   $matches=@($lines|Where-Object {$_ -cmatch $pattern})
   if($matches.Count -ne 1){throw 'The checked presentation command differs; no script was published.'}
   $lines=@($lines|ForEach-Object {if($_ -cmatch $pattern){'rem TechLoungeOS replaces this presentation/restart command: '+$_}else{$_}})
  }
  $text=($lines -join "`r`n").Replace('/v "TaskbarAl" /t REG_DWORD /d "1"','/v "TaskbarAl" /t REG_DWORD /d "0"')
  # The original completion script restores networking and task-manager access.
  # Run it before releasing the app queue, so its temp cleanup cannot interrupt Office.
  return $text+"`r`ncscript.exe //nologo `"%SystemRoot%\ico\complete.vbs`" >>`"%ProgramData%\TechLoungeOS\SourcePresentation\finalise.log`" 2>&1`r`nif errorlevel 1 exit /b 1`r`necho Source setup completed>`"%ProgramData%\TechLoungeOS\SourcePresentation\ready`"`r`n"
 }
 if($Kind -cne 'oobe'){throw 'Unsupported presentation script'}
 if(!$Wallpaper){return $Text}
 if($Wallpaper -cnotmatch '\A(?:Wallpapers\\[a-z0-9-]+\.png|wallpaper\.(?:png|jpg|jpeg|bmp))\z'){throw 'Unsupported selected wallpaper path'}
 $theme='%Windir%\Resources\Themes\GHOSTV5_AI.01.theme';$image='%Windir%\Resources\Themes\GHOSTV5\GHOST-PDX02.jpg'
 if(([regex]::Matches($Text,[regex]::Escape($theme))).Count -ne 2 -or ([regex]::Matches($Text,[regex]::Escape($image))).Count -ne 1){throw 'The checked source theme commands differ'}
 return $Text.Replace($theme,'%ProgramData%\TechLoungeOS\SourcePresentation\TechLoungeOS.theme').Replace($image,('%ProgramData%\TechLoungeOS\'+$Wallpaper))
}

if($Library){return}
$root=[IO.Path]::GetFullPath($PSScriptRoot)
if($root -ine [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'TechLoungeOS\SourcePresentation'))){throw 'Source handoff is outside its fixed setup folder'}
$transcribing=$false
try{
 Start-Transcript -LiteralPath (Join-Path $root 'prepare.log') -Append|Out-Null;$transcribing=$true
 $wallpaperFile=Join-Path $root 'wallpaper.txt';$wallpaper=if(Test-Path -LiteralPath $wallpaperFile){[IO.File]::ReadAllText($wallpaperFile)}else{''}
 $inputs=@(
  @{path=(Join-Path $env:SystemRoot 'System32\oobe.cmd');sha='6C4B5A6DA345A993A4E98A676DDD98311C8987E4119EABE65617FCCF3381EB77';kind='oobe'},
  @{path=(Join-Path $env:SystemRoot 'System32\visual\visual.cmd');sha='F4C57F7E2C8E6E71EB64465DFB73F5B6D854714829C41DD29A3961D3D63EAADB';kind='visual'}
 )
 # Validate all inputs and transformations before publishing either script.
 $changes=@(foreach($item in $inputs){
  if((Get-Item -LiteralPath $item.path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked source setup script rejected'}
  if((Get-FileHash -LiteralPath $item.path -Algorithm SHA256).Hash -cne $item.sha){throw 'The extracted source setup script does not match the inspected version'}
  $text=[IO.File]::ReadAllText($item.path,[Text.UTF8Encoding]::new($false,$true))
  @{item=$item;text=(Convert-SourcePresentation $text $item.kind $wallpaper)}
 })
 foreach($change in $changes){
  $item=$change.item;[IO.File]::Copy($item.path,(Join-Path $root ('original-'+$item.kind+'.cmd')),$false)
  if($item.kind -ceq 'visual'){
   [IO.File]::WriteAllText((Join-Path $root 'visual-source.cmd'),$change.text,[Text.UTF8Encoding]::new($false,$true))
   [IO.File]::WriteAllText($item.path,"@echo off`r`ncall `"%ProgramData%\TechLoungeOS\SourcePresentation\visual-source.cmd`"`r`nexit /b %errorlevel%`r`n",[Text.Encoding]::ASCII)
  }else{[IO.File]::WriteAllText($item.path,$change.text,[Text.UTF8Encoding]::new($false,$true))}
 }
 Write-Output 'Retained source configuration; replaced only the recognised intro, forced restart and selected-theme paths.'
}catch{
 [IO.File]::WriteAllText((Join-Path $root 'failed'),$_.Exception.Message)
 throw
}finally{if($transcribing){Stop-Transcript|Out-Null}}
