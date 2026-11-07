[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory=$true)][string]$Plan, [switch]$Apply)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath('D:\TechLoungeOS-validation')
$protected=@('live-114','live-120','ghost-vm-95','export-252','fresh-full-254')
if((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Validation root is linked; cleanup stopped.'}
# Preview works without elevation. Applying requires an administrator and a
# successful servicing-ownership query before any file can be removed.
$mounted=@()
if($Apply){
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if(!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run PowerShell as administrator to apply this reviewed cleanup plan.'}
 $mounted=@(Get-WindowsImage -Mounted -ErrorAction Stop)
}
foreach($image in $mounted){
 foreach($path in @($image.Path,$image.ImagePath)){
  if($path -and [IO.Path]::GetFullPath($path).StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'A host WIM under the validation root is mounted. Unmount its owned image normally before cleanup.'}
 }
}
$before=[IO.DriveInfo]::new('D:\').AvailableFreeSpace
$deleted=0;$skipped=0;$logical=[long]0
$records=Import-Csv -LiteralPath $Plan
foreach($record in $records){
 $path=[IO.Path]::GetFullPath($record.Path)
 if(!$path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'A planned path escapes the validation root.'}
 $relative=$path.Substring($root.Length+1)
 if(($relative.Split('\')[0]) -in $protected){throw 'The plan includes a retained active/backing/export directory.'}
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){continue}
 $file=Get-Item -LiteralPath $path -Force
 $parent=$file.Directory
 while($parent -and $parent.FullName.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){
  if($parent.Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('A parent is linked: '+$path)}
  $parent=$parent.Parent
 }
 if(($file.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System)) -or $file.Length -ne [long]$record.Bytes -or $file.LastWriteTimeUtc.Ticks -ne [long]$record.LastWriteUtcTicks){$skipped++;continue}
 if(!$Apply){$logical+=$file.Length;continue}
 try{
  if($file.Extension -in @('.iso','.vhd','.vhdx')){
   if((Get-DiskImage -ImagePath $path -ErrorAction Stop).Attached){$skipped++;continue}
  }
  # An in-use file is left alone. Never force-close a VM or servicing process.
  $handle=[IO.File]::Open($path,'Open','Read','None');$handle.Dispose()
  if($PSCmdlet.ShouldProcess($path,'Delete obsolete generated validation file')){
   Remove-Item -LiteralPath $path -ErrorAction Stop
   $deleted++;$logical+=$file.Length
  }
 }catch{Write-Warning ($path+': '+$_.Exception.Message);$skipped++}
}
$after=[IO.DriveInfo]::new('D:\').AvailableFreeSpace
[pscustomobject]@{Applied=[bool]$Apply;FilesDeleted=$deleted;Skipped=$skipped;CandidateLogicalGiB=[math]::Round($logical/1GB,1);NetFreeSpaceChangeGiB=[math]::Round(($after-$before)/1GB,1);FreeGiB=[math]::Round($after/1GB,1)}
