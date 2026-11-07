using System.Text.Json;
using TechLoungeOS;

static class GraphicsChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"graphics O'Brien 测试");Directory.CreateDirectory(folder);
        var worker=Path.Combine(folder,"install.ps1");File.WriteAllText(worker,GraphicsSetup.Script,new System.Text.UTF8Encoding(true));
        var run=new ProcessRunner(_=>{});var load=". "+Policy.Ps(worker)+" -Library\n";
        var fixtureMount=Path.Combine(folder,"app-readiness");BootOptimizer.Stage(fixtureMount,["Vendor.Example"]);
        var common=Path.Combine(fixtureMount,"ProgramData");var appState=Path.Combine(folder,"user-state");Directory.CreateDirectory(appState);
        var guard=Path.Combine(folder,"driver-guard.ps1");File.WriteAllText(guard,GraphicsQueue.LockScript);
        var ordering=$$"""
            $env:ProgramData={{Policy.Ps(common)}};$state={{Policy.Ps(appState)}}
            . {{Policy.Ps(guard)}}
            $before=Test-GraphicsAppsReady $state
            Set-Content (Join-Path $state 'Vendor.Example.done') 'inert fixture installed'
            $after=Test-GraphicsAppsReady $state
            $script:requests=0;$script:errors=@();$script:driverComplete=$false
            function Invoke-Step {param($Name,$Action)try{& $Action}catch{$script:errors+=$_.Exception.Message} }
            function Get-ScheduledTask {param($TaskName,$ErrorAction)[pscustomobject]@{TaskName=$TaskName} }
            function Start-ScheduledTask {param($TaskName,$ErrorAction)$script:requests++}
            function Test-Path {param($LiteralPath)
              if($LiteralPath -like '*\Setup\TechLoungeOS-Nvidia\state\complete'){return $script:driverComplete}
              Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
            }
            Remove-Item (Join-Path $state 'Vendor.Example.done')
            {{NvidiaSetup.FirstLogin}}
            $early=$script:requests
            Set-Content (Join-Path $state 'Vendor.Example.done') 'inert fixture installed'
            {{NvidiaSetup.FirstLogin}}
            $queued=$script:requests;$script:driverComplete=$true
            {{NvidiaSetup.FirstLogin}}
            @{before=$before;after=$after;early=$early;queued=$queued;final=$script:requests;errors=$script:errors}|ConvertTo-Json -Compress
            """;
        using(var result=JsonDocument.Parse(await run.PowerShell(ordering))){var value=result.RootElement;
            check(!value.GetProperty("before").GetBoolean()&&value.GetProperty("after").GetBoolean()&&value.GetProperty("early").GetInt32()==0,
                "graphics readiness reads the selected-app manifest and prevents an early driver request before actual app receipts exist");
            check(value.GetProperty("queued").GetInt32()==1&&value.GetProperty("final").GetInt32()==1,
                "after apps complete, first-login requests only the fixed driver task and reuses an existing completion without another request");
        }
        var firstStart=Provisioning.Script(["7zip.7zip"],false,false,nvidia:true,amdIntelGraphics:true);
        check(firstStart.IndexOf("Install-WinGetApp '7zip.7zip'",StringComparison.Ordinal)<firstStart.IndexOf("Invoke-Step 'nvidia-setup'",StringComparison.Ordinal)&&
            firstStart.IndexOf("Install-WinGetApp '7zip.7zip'",StringComparison.Ordinal)<firstStart.IndexOf("Invoke-Step 'amd-intel-graphics'",StringComparison.Ordinal),
            "both display-driver checks occur after selected app installation in the desktop-first flow");
        foreach(var script in new[]{GraphicsSetup.Script,GraphicsQueue.Script(false),GraphicsQueue.Script(true),Provisioning.Script([],false,false,amdIntelGraphics:true)})
            await run.PowerShell("$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseInput("+Policy.Ps(script)+",[ref]$t,[ref]$e);if($e.Count){throw ($e|Out-String)}");
        check(true,"AMD/Intel worker, both hardware-gated queues and first-login code parse under Windows PowerShell 5.1");
        var hardware="""
            function Get-CimInstance {param($ClassName,$Filter,$ErrorAction)
              switch($ClassName){
                'Win32_VideoController'{$global:tlosAdapters}
                'Win32_PnPEntity'{foreach($a in $global:tlosAdapters){[pscustomobject]@{DeviceID=$a.PNPDeviceID;Present=$true;HardwareID=@($a.PNPDeviceID)}}}
                'Win32_PnPSignedDriver'{foreach($a in $global:tlosAdapters){[pscustomobject]@{DeviceID=$a.PNPDeviceID;DriverVersion='31.0.1.0';DriverProviderName=if($a.PNPDeviceID -like '*VEN_1002*'){'Advanced Micro Devices, Inc.'}else{'Intel Corporation'};IsSigned=$true}}}
                'Win32_OperatingSystem'{[pscustomobject]@{LastBootUpTime=[datetime]'2026-10-25T00:00:00Z'}}
              }
            }
            """;
        var cases=new[]{("Intel",new[]{"8086"},new[]{"Intel"}),("AMD",new[]{"1002"},new[]{"AMD"}),("NVIDIA only",new[]{"10DE"},Array.Empty<string>()),("Intel + NVIDIA",new[]{"8086","10DE"},new[]{"Intel"}),("AMD + NVIDIA",new[]{"1002","10DE"},new[]{"AMD"}),("AMD + Intel",new[]{"1002","8086"},new[]{"AMD","Intel"}),("unknown",new[]{"1234"},Array.Empty<string>())};
        foreach(var (name,ids,expected) in cases)
        {
            var data=ids.Select(id=>new{Name="Microsoft Basic Display Adapter",PNPDeviceID=@"PCI\VEN_"+id+@"&DEV_1234&SUBSYS_01020304"}).ToArray();
            var prefix="$global:tlosAdapters=(ConvertFrom-Json "+Policy.Ps(JsonSerializer.Serialize(data))+")\n"+hardware+load;
            var result=await run.PowerShell(prefix+"ConvertTo-Json -InputObject @(Get-GraphicsDevices | ForEach-Object Vendor)");
            check((JsonSerializer.Deserialize<string[]>(result.Trim())??[]).SequenceEqual(expected),"target display PCI identities route "+name+" without inferring graphics from CPU/vendor names");
        }
        var queueMocks="""
            function Set-Acl {param($LiteralPath,$AclObject)}
            function Get-ScheduledTask {param($TaskName,$ErrorAction)}
            function Unregister-ScheduledTask {param($TaskName,$Confirm,$ErrorAction) throw 'No existing task was provided by this fixture'}
            function New-ScheduledTaskAction {param($Execute,$Argument) 1}
            function New-ScheduledTaskTrigger {param([switch]$AtLogOn,[switch]$AtStartup) 1}
            function New-ScheduledTaskPrincipal {param($UserId,$LogonType,$RunLevel) 1}
            function New-ScheduledTaskSettingsSet {param($MultipleInstances,[switch]$StartWhenAvailable,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries,$ExecutionTimeLimit) 1}
            function Register-ScheduledTask {param($TaskName,$Action,$Trigger,$Principal,$Settings) Set-Content -LiteralPath $global:tlosQueueLog $TaskName}
            """;
        foreach(var nvidia in new[]{false,true})foreach(var (name,ids,_) in cases){
            var queueFolder=Path.Combine(folder,"queue-"+(nvidia?"nv-":"ai-")+name);Directory.CreateDirectory(queueFolder);
            File.WriteAllText(Path.Combine(queueFolder,"install.ps1"),nvidia?NvidiaSetup.Script:GraphicsSetup.Script,new System.Text.UTF8Encoding(true));
            var queueFile=Path.Combine(queueFolder,"queue.ps1");File.WriteAllText(queueFile,GraphicsQueue.Script(nvidia),new System.Text.UTF8Encoding(true));
            var log=Path.Combine(queueFolder,"fake-task.txt");
            var adapters=ids.Select(id=>new{Name="Basic display fixture",PNPDeviceID=@"PCI\VEN_"+id+@"&DEV_1234&SUBSYS_01020304"}).ToArray();
            var header="$global:tlosQueueLog="+Policy.Ps(log)+";$global:tlosAdapters=(ConvertFrom-Json "+Policy.Ps(JsonSerializer.Serialize(adapters))+")\n";
            await run.PowerShell(header+hardware+queueMocks+"\n& "+Policy.Ps(queueFile));
            var shouldQueue=nvidia?ids.Contains("10DE"):ids.Any(id=>id is "1002" or "8086");
            check(File.Exists(log)==shouldQueue && (shouldQueue||File.ReadAllText(Path.Combine(queueFolder,"state","complete")).Trim()=="Not applicable"),
                (nvidia?"NVIDIA":"AMD/Intel")+" task registers only for matching installed adapters: "+name);
        }
        try{await run.PowerShell(load+"function Get-CimInstance {throw 'Synthetic device inventory unavailable'};Get-GraphicsDevices");throw new Exception("Expected inventory failure");}
        catch(IOException ex)when(ex.Message.Contains("Synthetic device inventory unavailable")){check(true,"failed hardware detection never becomes a false no-GPU success");}
        var device="""
            $device=[pscustomobject]@{Vendor='Intel';Name='Intel fixture';PNPDeviceID='PCI\VEN_8086&DEV_1234';HardwareIds=@('PCI\VEN_8086&DEV_1234&SUBSYS_01020304','PCI\VEN_8086&DEV_1234');DriverVersion='31.0.1.0';Provider='Intel Corporation';Signed=$true}
            function Update($id,$provider,$title,$class='Display'){[pscustomobject]@{DriverHardwareID=$id;DriverProvider=$provider;Title=$title;DriverClass=$class}}
            """;
        var selected=await run.PowerShell(load+device+"""

            $updates=@(
              (Update 'PCI\VEN_8086&DEV_1234' 'Intel Corporation' 'Intel - Display - 31.0.2.0'),
              (Update 'PCI\VEN_8086&DEV_1234&SUBSYS_01020304' 'Intel Corporation' 'Intel - Display - 31.0.10.0'),
              (Update 'PCI\VEN_8086&DEV_1234&SUBSYS_99999999' 'Intel Corporation' 'Intel - Display - 99.0.0.0'),
              (Update 'PCI\VEN_1002&DEV_1234' 'Advanced Micro Devices, Inc.' 'AMD - Display - 99.0.0.0'),
              (Update 'PCI\VEN_8086&DEV_1234' 'Intel Corporation' 'Intel - Network - 99.0.0.0' 'Net'))
            (Select-GraphicsUpdates @($device) $updates).Version.ToString()
            """);
        check(selected.Trim()=="31.0.10.0","graphics update selection matches actual hardware/subsystem IDs, excludes other vendors/classes and sorts numeric versions");
        var retain=await run.PowerShell(load+device+"\n$device.DriverVersion='32.0.1.0'; @(Select-GraphicsUpdates @($device) @((Update 'PCI\\VEN_8086&DEV_1234' 'Intel Corporation' 'Intel - Display - 31.0.10.0'))).Count");
        check(retain.Trim()=="0","an already newer signed vendor driver is never deliberately downgraded");
        try{await run.PowerShell(load+device+"\nSelect-GraphicsUpdates @($device) @((Update 'PCI\\VEN_8086&DEV_1234' 'Intel Corporation' 'Unknown version format'))");throw new Exception("Expected version rejection");}
        catch(IOException ex)when(ex.Message.Contains("no verifiable version")){check(true,"unknown update version format stops installation rather than guessing");}
        var state=Path.Combine(folder,"hybrid");Directory.CreateDirectory(state);
        var hybrid=load+hardware+"""

            function Get-GraphicsDevices { [pscustomobject]@{Vendor='AMD'};[pscustomobject]@{Vendor='Intel'} }
            function Invoke-GraphicsVendor($Vendor,$Devices,$State,$Boot){if($Vendor -eq 'AMD'){throw 'Synthetic AMD failure'};Set-Content (Join-Path $State 'Intel.done') 'Completed'}
            """;
        try{await run.PowerShell(hybrid+"\nInvoke-GraphicsProvisioning "+Policy.Ps(state));throw new Exception("Expected hybrid partial failure");}
        catch(IOException ex)when(ex.Message.Contains("Synthetic AMD failure")){check(File.Exists(Path.Combine(state,"Intel.done"))&&!File.Exists(Path.Combine(state,"complete")),"a failed AMD phase does not block Intel work or falsely complete a hybrid PC");}
        var skip=Path.Combine(folder,"skip");Directory.CreateDirectory(skip);
        await run.PowerShell(load+"function Get-GraphicsDevices {@()};function New-Object {throw 'No update API must run'};Invoke-GraphicsProvisioning "+Policy.Ps(skip));
        check(File.ReadAllText(Path.Combine(skip,"complete")).Trim()=="Not applicable","PCs without AMD/Intel display hardware skip without update APIs or downloads");
        var installState=Path.Combine(folder,"installed");Directory.CreateDirectory(installState);
        var installModel=load+"""
            $global:tlosInstalled=$false
            function Get-GraphicsDevices {[pscustomobject]@{Vendor='Intel';Name='fixture';PNPDeviceID='PCI\VEN_8086&DEV_1234';HardwareIds=@('PCI\VEN_8086&DEV_1234');DriverVersion=if($global:tlosInstalled){'31.0.10.0'}else{'31.0.1.0'};Provider='Intel Corporation';Signed=$true}}
            function New-Object {param($ComObject)
              if($ComObject -eq 'Microsoft.Update.UpdateColl'){
                $c=[pscustomobject]@{Values=[Collections.ArrayList]::new()};$c|Add-Member ScriptProperty Count {$this.Values.Count};$c|Add-Member ScriptMethod Add {param($v)$this.Values.Add($v)};$c|Add-Member ScriptMethod Item {param($i)$this.Values[$i]};return $c
              }
              if($ComObject -ne 'Microsoft.Update.Session'){throw 'Unexpected COM object'}
              $s=[pscustomobject]@{}
              $s|Add-Member ScriptMethod CreateUpdateSearcher {
                $q=[pscustomobject]@{};$q|Add-Member ScriptMethod Search {param($criteria)
                  [pscustomobject]@{ResultCode=2;Updates=@([pscustomobject]@{Title='Intel - Display - 31.0.10.0';DriverClass='Display';DriverProvider='Intel Corporation';DriverHardwareID='PCI\VEN_8086&DEV_1234';EulaAccepted=$true;Identity=[pscustomobject]@{UpdateID='fixture-id';RevisionNumber=1}})}
                };$q
              }
              $s|Add-Member ScriptMethod CreateUpdateDownloader {$d=[pscustomobject]@{Updates=$null};$d|Add-Member ScriptMethod Download {[pscustomobject]@{ResultCode=2}};$d}
              $s|Add-Member ScriptMethod CreateUpdateInstaller {
                $i=[pscustomobject]@{Updates=$null;ForceQuiet=$false};$i|Add-Member ScriptMethod Install {
                  if(!$this.ForceQuiet -or $this.Updates.Count -ne 1){throw 'Incorrect install request'}
                  $global:tlosInstalled=$true
                  $r=[pscustomobject]@{ResultCode=2;RebootRequired=[bool]$global:tlosRequireReboot};$r|Add-Member ScriptMethod GetUpdateResult {param($index)[pscustomobject]@{ResultCode=if($global:tlosPartial){3}else{2};HResult=0}};$r
                };$i
              };$s
            }
            """;
        await run.PowerShell(installModel+"\nInvoke-GraphicsVendor 'Intel' @(Get-GraphicsDevices) "+Policy.Ps(installState)+" 'boot1'");
        check(File.Exists(Path.Combine(installState,"Intel.done"))&&File.ReadAllText(Path.Combine(installState,"Intel.receipt.json")).Contains("31.0.10.0"),"hardware-matched download/install records per-package receipts and verifies the signed active driver before completing");
        var partial=Path.Combine(folder,"partial");Directory.CreateDirectory(partial);
        try{await run.PowerShell(installModel+"\n$global:tlosPartial=$true;Invoke-GraphicsVendor 'Intel' @(Get-GraphicsDevices) "+Policy.Ps(partial)+" 'boot1'");throw new Exception("Expected per-package failure");}
        catch(IOException ex)when(ex.Message.Contains("installation is incomplete")){check(!File.Exists(Path.Combine(partial,"Intel.done"))&&File.Exists(Path.Combine(partial,"Intel.receipt.json")),"an overall success cannot mask a failed per-package install result");}
        var pending=Path.Combine(folder,"pending");Directory.CreateDirectory(pending);
        try{await run.PowerShell(installModel+"\n$global:tlosRequireReboot=$true;Invoke-GraphicsVendor 'Intel' @(Get-GraphicsDevices) "+Policy.Ps(pending)+" 'boot1'");throw new Exception("Expected reboot deferral");}
        catch(IOException ex)when(ex.Message.Contains("restart Windows normally")){check(File.Exists(Path.Combine(pending,"Intel.pending.json"))&&!File.Exists(Path.Combine(pending,"Intel.done")),"a required graphics restart is recorded and never forced");}
        var resume=load+"""
            function Get-GraphicsDevices {[pscustomobject]@{Vendor='Intel';PNPDeviceID='PCI\VEN_8086&DEV_1234';DriverVersion='31.0.10.0';Provider='Intel Corporation';Signed=$true}}
            function New-Object {throw 'No download/install is allowed during restart verification'}
            """;
        try{await run.PowerShell(resume+"\nInvoke-GraphicsVendor 'Intel' @(Get-GraphicsDevices) "+Policy.Ps(pending)+" 'boot1'");throw new Exception("Expected same-boot deferral");}
        catch(IOException ex)when(ex.Message.Contains("waiting for a normal")){check(true,"same-boot retries do not repeat a graphics driver installation");}
        await run.PowerShell(resume+"\nInvoke-GraphicsVendor 'Intel' @(Get-GraphicsDevices) "+Policy.Ps(pending)+" 'boot2'");
        check(File.Exists(Path.Combine(pending,"Intel.done")),"after restart, signed driver readiness completes without another update query/download");
        var lockFile=Path.Combine(folder,"installer.lock");var lockStatus=Path.Combine(folder,"lock-status.txt");
        var lockCode=GraphicsQueue.LockScript+"\n$lease=Get-GraphicsInstallLock "+Policy.Ps(lockFile)+" "+Policy.Ps(lockStatus)+" 1;try{'acquired'}finally{$lease.Dispose()}";
        using(var held=new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
          try{await run.PowerShell(lockCode);throw new Exception("Expected shared graphics lock deferral");}
          catch(IOException ex)when(ex.Message.Contains("Another graphics setup is still running")){check(File.ReadAllText(lockStatus).Contains("no second installer"),"NVIDIA and AMD/Intel workers share an exclusive installer gate without interrupting a running installer");}
        }
        check((await run.PowerShell(lockCode)).Trim()=="acquired","graphics installer gate becomes available after the first worker releases it");
        var mount=Path.Combine(folder,"mount");GraphicsSetup.Stage(mount);
        check(File.Exists(Path.Combine(mount,GraphicsSetup.RelativeFolder,"payload-sha256.json"))&&File.ReadAllText(SetupHooks.ImageHook(mount)).Contains("TechLoungeOS-Graphics\\queue.ps1")&&Directory.EnumerateFiles(Path.Combine(mount,GraphicsSetup.RelativeFolder),"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length)<30_000,
            "AMD/Intel queue stages scripts/manifests only, keeps the ISO small and chains the setup hook");
    }
}
