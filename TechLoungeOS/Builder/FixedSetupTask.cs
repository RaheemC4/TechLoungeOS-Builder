namespace TechLoungeOS;

// Setup owns the immutable action. Users may request it without changing its code.
internal static class FixedSetupTask
{
    public static string Queue(string task)
    {
        if(task is not ("TechLoungeOS-BootOptimizer" or "TechLoungeOS-MachineSetup" or "TechLoungeOS-PowerDefaults"))throw new ArgumentException("Unknown fixed setup task.");
        return $$"""
        $ErrorActionPreference='Stop'
        if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Post-install setup registration requires Windows Setup or administrator permission'}
        $owner=if([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -eq 'S-1-5-18'){'S-1-5-18'}else{'S-1-5-32-544'}
        $sharedRoot=Split-Path $PSScriptRoot
        if([IO.Path]::GetFullPath($sharedRoot).TrimEnd('\') -ine [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'TechLoungeOS')).TrimEnd('\')){throw 'Post-install tasks must stay in their fixed ProgramData folder'}
        $part=$PSScriptRoot
        while($part){
          if((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked Post-install setup paths are not supported'}
          $part=Split-Path $part
        }
        # Protect the shared parent too: DELETE_CHILD there can replace a protected task folder.
        foreach($item in @((Get-Item -LiteralPath $sharedRoot -Force))+@((Get-Item -LiteralPath $PSScriptRoot -Force))+@(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -Force){{(task=="TechLoungeOS-MachineSetup"?"+@((Get-Item -LiteralPath (Join-Path $sharedRoot 'FirstLogin.exe') -Force))":"")}}){
          if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked Post-install setup payloads are not supported'}
          $acl=if($item.PSIsContainer){New-Object Security.AccessControl.DirectorySecurity}else{New-Object Security.AccessControl.FileSecurity}
          $inherit=if($item.PSIsContainer){'ContainerInherit,ObjectInherit'}else{'None'}
          $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier($owner)))
          $acl.SetAccessRuleProtection($true,$false)
          foreach($sid in @('S-1-5-18','S-1-5-32-544')){
            $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)),'FullControl',$inherit,'None','Allow')))
          }
          $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')),'ReadAndExecute',$inherit,'None','Allow')))
          Set-Acl -LiteralPath $item.FullName -AclObject $acl
        }
        $exe={{(task=="TechLoungeOS-MachineSetup"?"Join-Path $sharedRoot 'FirstLogin.exe'":"Join-Path $env:SystemRoot 'System32\\WindowsPowerShell\\v1.0\\powershell.exe'")}}
        $arguments={{(task=="TechLoungeOS-MachineSetup"?"'--machine-setup'":"'-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"'+$PSScriptRoot+'\\worker.ps1\"'")}}
        $task={{Policy.Ps(task)}}
        $existing=Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
        if($existing -and (@($existing.Actions).Count -ne 1 -or $existing.Actions[0].Execute -ne $exe -or $existing.Actions[0].Arguments -ne $arguments -or $existing.Principal.UserId -notin @('SYSTEM','S-1-5-18'))){throw 'A different Post-install setup task already exists; it was retained.'}
        {{(task=="TechLoungeOS-MachineSetup"?"if($existing -and (@($existing.Triggers | Where-Object {$null -ne $_}).Count -gt 0 -or [Xml.XmlConvert]::ToTimeSpan($existing.Settings.ExecutionTimeLimit) -ne [TimeSpan]::Zero)){throw 'A pre-desktop trigger or hard installer timeout exists on this task; it was retained for administrator review.'}":"")}}
        {{(task=="TechLoungeOS-PowerDefaults"?"if($existing -and @($existing.Triggers | Where-Object {$null -ne $_}).Count -gt 0){throw 'An automatic trigger already exists on the power-default task; it was retained for administrator review.'}":"")}}
        if(!$existing){
          $action=New-ScheduledTaskAction -Execute $exe -Argument $arguments
          $principal=New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
          # Machine installers may service Windows: never impose a scheduler kill deadline.
          $settings=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::FromMinutes({{(task=="TechLoungeOS-MachineSetup"?0:5)}}))
          Register-ScheduledTask -TaskName $task -Action $action -Principal $principal -Settings $settings|Out-Null
        }
        # Users may request this fixed action, but cannot replace its code or task.
        $scheduler=New-Object -ComObject Schedule.Service;$scheduler.Connect()
        $registered=$scheduler.GetFolder('\').GetTask($task)
        $registered.SetSecurityDescriptor('D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;BU)',0)
        Set-Content (Join-Path $PSScriptRoot 'queue-ready') 'Fixed post-install setup action registered. No background watcher.'
        """;
    }
}
