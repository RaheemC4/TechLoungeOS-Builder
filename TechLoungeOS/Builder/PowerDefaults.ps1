param([switch]$Library,[switch]$Initial)
$ErrorActionPreference='Stop'

function Initialize-IdlePowerApi {
 if('TechLoungeOS.IdlePower' -as [type]){return}
 Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
[assembly:DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
namespace TechLoungeOS {
 public static class IdlePower {
  [DllImport("powrprof.dll",ExactSpelling=true)] static extern uint PowerEnumerate(IntPtr root,IntPtr scheme,IntPtr subgroup,uint flags,uint index,byte[] buffer,ref uint size);
  [DllImport("powrprof.dll",ExactSpelling=true)] static extern uint PowerReadACValueIndex(IntPtr root,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
  [DllImport("powrprof.dll",ExactSpelling=true)] static extern uint PowerReadDCValueIndex(IntPtr root,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
  [DllImport("powrprof.dll",ExactSpelling=true)] static extern uint PowerWriteACValueIndex(IntPtr root,ref Guid scheme,ref Guid subgroup,ref Guid setting,uint value);
  [DllImport("powrprof.dll",ExactSpelling=true)] static extern uint PowerWriteDCValueIndex(IntPtr root,ref Guid scheme,ref Guid subgroup,ref Guid setting,uint value);
  [DllImport("powrprof.dll",ExactSpelling=true)] static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
  [DllImport("powrprof.dll",ExactSpelling=true)] static extern uint PowerSetActiveScheme(IntPtr root,ref Guid scheme);
  [DllImport("kernel32.dll",ExactSpelling=true)] static extern IntPtr LocalFree(IntPtr memory);
  static void Check(uint result){if(result!=0)throw new Win32Exception((int)result);}
  public static Guid[] Plans(){
   var plans=new List<Guid>();
   for(uint index=0;index<256;index++){
    var buffer=new byte[16];uint size=16;
    uint result=PowerEnumerate(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,16,index,buffer,ref size);
    if(result==259)return plans.ToArray();
    Check(result);if(size!=16)throw new InvalidOperationException("Invalid Windows power scheme identity");
    plans.Add(new Guid(buffer));
   }
   throw new InvalidOperationException("Too many Windows power schemes to verify safely");
  }
  public static uint Read(Guid scheme,Guid subgroup,Guid setting,bool ac){
   uint value;Check(ac?PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,out value):PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,out value));return value;
  }
  public static void Never(Guid scheme,Guid subgroup,Guid setting,bool ac){
   Check(ac?PowerWriteACValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,0):PowerWriteDCValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,0));
  }
  public static void Refresh(){
   IntPtr memory;Check(PowerGetActiveScheme(IntPtr.Zero,out memory));
   try{var active=(Guid)Marshal.PtrToStructure(memory,typeof(Guid));Check(PowerSetActiveScheme(IntPtr.Zero,ref active));}
   finally{LocalFree(memory);}
  }
 }
}
'@
}
function Get-IdlePowerPlans {Initialize-IdlePowerApi;[TechLoungeOS.IdlePower]::Plans()}
function Get-IdlePowerTimeout($Scheme,$Group,$Setting,[bool]$Ac){Initialize-IdlePowerApi;[TechLoungeOS.IdlePower]::Read([Guid]$Scheme,[Guid]$Group,[Guid]$Setting,$Ac)}
function Set-IdlePowerTimeout($Scheme,$Group,$Setting,[bool]$Ac){Initialize-IdlePowerApi;[TechLoungeOS.IdlePower]::Never([Guid]$Scheme,[Guid]$Group,[Guid]$Setting,$Ac)}
function Update-IdlePowerPlan {Initialize-IdlePowerApi;[TechLoungeOS.IdlePower]::Refresh()}
function Assert-IdlePowerPolicy($Settings){
 foreach($setting in $Settings){
  $path='HKLM:\SOFTWARE\Policies\Microsoft\Power\PowerSettings\'+$setting.Setting
  if(Test-Path -LiteralPath $path){
   $key=Get-Item -LiteralPath $path
   try{foreach($name in 'ACSettingIndex','DCSettingIndex'){
    if($key.GetValueNames() -contains $name -and [uint32]$key.GetValue($name) -ne 0){throw 'An organisation policy controls idle power timeouts. It was not overridden.'}
   }}finally{$key.Close()}
  }
 }
}

function Set-IdlePowerDefaults([string]$State,[bool]$Finalize){
 New-Item -ItemType Directory -Path $State -Force|Out-Null
 $gate=[IO.File]::Open((Join-Path $State 'apply.lock'),'OpenOrCreate','ReadWrite','None')
 try{
  if(Test-Path -LiteralPath (Join-Path $State 'complete')){return}
  $settings=@(
   @{Name='screen';Group='7516b95f-f776-4464-8c53-06167f40cc99';Setting='3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e'},
   @{Name='sleep';Group='238c9fa8-0aad-41ed-83f4-97be242c8f20';Setting='29f6c1db-86da-48c5-9fdb-f2b67b1f44da'},
   @{Name='hibernate';Group='238c9fa8-0aad-41ed-83f4-97be242c8f20';Setting='9d7815a6-7ee4-497e-8888-515a05f02364'}
  )
  Assert-IdlePowerPolicy $settings
  $plans=@(Get-IdlePowerPlans|ForEach-Object {([Guid]$_).ToString()}|Sort-Object -Unique)
  if(!$plans.Count){throw 'Windows did not provide a power plan to configure'}
  $before=@(foreach($scheme in $plans){foreach($setting in $settings){foreach($ac in $true,$false){
   @{Scheme=$scheme;Name=$setting.Name;Group=$setting.Group;Setting=$setting.Setting;Ac=$ac;Seconds=[uint32](Get-IdlePowerTimeout $scheme $setting.Group $setting.Setting $ac)}
  }}})
  $backup=Join-Path $State 'before.json'
  if(!(Test-Path -LiteralPath $backup)){[IO.File]::WriteAllText($backup,($before|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))}
  foreach($item in $before){if($item.Seconds -ne 0){Set-IdlePowerTimeout $item.Scheme $item.Group $item.Setting $item.Ac}}
  # Refresh the currently selected plan without selecting a different performance plan.
  Update-IdlePowerPlan
  foreach($item in $before){
   if((Get-IdlePowerTimeout $item.Scheme $item.Group $item.Setting $item.Ac) -ne 0){throw ('Idle timeout readback failed: '+$item.Name)}
  }
  $after=@(Get-IdlePowerPlans|ForEach-Object {([Guid]$_).ToString()}|Sort-Object -Unique)
  if(($plans -join ',') -cne ($after -join ',')){throw 'Power plans changed during setup. Retry to include the new plan.'}
  $receipt=@{utc=[DateTime]::UtcNow.ToString('o');plans=$plans;screenAc=0;screenDc=0;sleepAc=0;sleepDc=0;hibernateAc=0;hibernateDc=0;finalized=$Finalize}
  [IO.File]::WriteAllText((Join-Path $State 'verified.json'),($receipt|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
  Set-Content -LiteralPath (Join-Path $State $(if($Finalize){'complete'}else{'initial-complete'})) 'Screen, sleep and hibernate idle timeouts verified Never on AC and battery for every available plan.'
  if(Test-Path -LiteralPath (Join-Path $State 'failed')){Remove-Item -LiteralPath (Join-Path $State 'failed')}
 }finally{$gate.Dispose()}
}

if($Library){return}
$root=[IO.Path]::GetFullPath($PSScriptRoot)
if($root -ine [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'TechLoungeOS\PowerDefaults'))){throw 'Power defaults must run from their fixed setup folder'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if($Initial){
 if(!([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Initial power defaults require Windows Setup permission'}
}elseif($identity.User.Value -ne 'S-1-5-18'){throw 'Final power defaults require their fixed SYSTEM task'}
try{
 if(!(Test-Path -LiteralPath (Join-Path $root 'complete')) -and !$Initial){
  . (Join-Path $root 'app-readiness.ps1')
  $target=Get-PostAppTarget 'PowerDefaults'
  if(!(Test-PostAppReceipts $target.AppState (Read-PostAppSelections (Join-Path $root 'apps.json'))) -or
     !(Test-Path -LiteralPath "$env:ProgramData\TechLoungeOS\MachineSetup\complete")){
   throw 'Final power defaults are waiting for the required installers and selected apps.'
  }
 }
 Set-IdlePowerDefaults $root (!$Initial)
}catch{
 Set-Content -LiteralPath (Join-Path $root 'failed') $_.Exception.Message
 throw
}finally{$identity.Dispose()}
