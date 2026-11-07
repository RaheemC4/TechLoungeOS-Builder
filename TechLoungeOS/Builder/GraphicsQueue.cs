namespace TechLoungeOS;

// Shared registration for the two isolated workers. Hardware is detected on the
// installed Windows machine before registering any download/install task.
public static class GraphicsQueue
{
    public const string LockScript="""
        function Get-GraphicsInstallLock([string]$Path,[string]$Status,[int]$TimeoutSeconds=3600){
          $timer=[Diagnostics.Stopwatch]::StartNew()
          while($true){
            try{return [IO.File]::Open($Path,'OpenOrCreate','ReadWrite','None')}
            catch [IO.IOException]{
              if(($_.Exception.GetBaseException().HResult -band 65535) -notin @(32,33)){throw}
              Set-Content -LiteralPath $Status 'Waiting for the other graphics setup job; no second installer was started.'
              if($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds){throw 'Another graphics setup is still running. This job will retry at next startup/sign-in; it did not interrupt the installer.'}
              Start-Sleep -Milliseconds 500
            }
          }
        }
        """+"\n"+AppReadiness;
    public const string AppReadiness="""
        function Test-GraphicsAppsReady([string]$UserState){
          $appsRoot=Join-Path $env:ProgramData 'TechLoungeOS\BootOptimizer'
          . (Join-Path $appsRoot 'app-readiness.ps1')
          if(!$UserState){$UserState=(Get-PostAppTarget).AppState}
          $apps=Read-PostAppSelections (Join-Path $appsRoot 'apps.json')
          return (Test-PostAppReceipts $UserState $apps)
        }
        """;
    public static string FirstLogin(bool nvidia)
    {
        var name=nvidia?"nvidia-setup":"amd-intel-graphics";
        var task=nvidia?"TechLoungeOS-Nvidia":"TechLoungeOS-Graphics";
        return "Invoke-Step '"+name+"' {\n"+AppReadiness+"\n"+$$"""
          if(!(Test-GraphicsAppsReady $state)){throw 'Graphics setup is waiting for the selected apps to finish.'}
          $graphicsState=Join-Path $env:SystemRoot 'Setup\{{task}}\state'
          if(!(Test-Path "$graphicsState\complete")){
            if(!(Get-ScheduledTask -TaskName '{{task}}' -ErrorAction SilentlyContinue)){throw 'Graphics task registration is incomplete. Ask an administrator to review its queue.ps1.'}
            Start-ScheduledTask -TaskName '{{task}}' -ErrorAction Stop
            throw ('Graphics setup has been queued after your apps. It may need internet or a normal restart. Review '+$graphicsState+'\status.txt; no restart was forced.')
          }
        }
        """;
    }
    public static string Script(bool nvidia)
    {
        var task = nvidia ? "TechLoungeOS-Nvidia" : "TechLoungeOS-Graphics";
        var owner = nvidia ? "TechLoungeOS.Nvidia.v1" : "TechLoungeOS.Graphics.v1";
        var detect = nvidia ? "Get-NvidiaDevices" : "Get-GraphicsDevices";
        return $$"""
            $ErrorActionPreference='Stop'
            $state=Join-Path $PSScriptRoot 'state'
            if ((Test-Path $state) -and (!(Test-Path "$state\owner.txt") -or (Get-Content "$state\owner.txt" -Raw).Trim() -ne '{{owner}}')) { throw 'Unrecognised graphics setup state already exists; it was not overwritten.' }
            $acl=New-Object Security.AccessControl.DirectorySecurity
            $acl.SetAccessRuleProtection($true,$false)
            foreach($sid in @('S-1-5-18','S-1-5-32-544')) {
              $identity=New-Object Security.Principal.SecurityIdentifier($sid)
              $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
            }
            $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')),'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
            Set-Acl -LiteralPath $PSScriptRoot -AclObject $acl
            if (!(Test-Path $state)) { New-Item $state -ItemType Directory | Out-Null; Set-Content "$state\owner.txt" '{{owner}}' }
            $exe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $arguments='-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$PSScriptRoot+'\install.ps1"'
            $existing=Get-ScheduledTask -TaskName '{{task}}' -ErrorAction SilentlyContinue
            if($existing -and (@($existing.Actions).Count -ne 1 -or $existing.Actions[0].Execute -ne $exe -or $existing.Actions[0].Arguments -ne $arguments -or $existing.Principal.UserId -notin @('SYSTEM','S-1-5-18'))){throw 'A different task with the graphics setup name already exists; it was not overwritten.'}
            try {
              . (Join-Path $PSScriptRoot 'install.ps1') -Library
              $devices=@({{detect}})
              ConvertTo-Json -InputObject @($devices | Select-Object Name,PNPDeviceID) | Set-Content -Encoding UTF8 "$state\detected-devices.json"
            } catch { Set-Content "$state\status.txt" ('Graphics hardware detection failed; nothing was queued: '+$_.Exception.Message); throw }
            if(!$devices.Count){
              if($existing){Unregister-ScheduledTask -TaskName '{{task}}' -Confirm:$false}
              Set-Content "$state\status.txt" 'No matching installed graphics hardware. No driver work was queued.'
              Set-Content "$state\complete" 'Not applicable'
              return
            }
            if($existing -or (Test-Path "$state\complete")){return}
            $action=New-ScheduledTaskAction -Execute $exe -Argument $arguments
            $triggers=@((New-ScheduledTaskTrigger -AtLogOn),(New-ScheduledTaskTrigger -AtStartup))
            $principal=New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
            $settings=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
            Register-ScheduledTask -TaskName '{{task}}' -Action $action -Trigger $triggers -Principal $principal -Settings $settings | Out-Null
            Set-Content "$state\status.txt" 'Matching graphics hardware detected. Driver setup queued for first sign-in; internet is required.'
            """;
    }
}
