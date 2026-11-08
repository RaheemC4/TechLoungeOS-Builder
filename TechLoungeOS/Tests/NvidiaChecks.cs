using TechLoungeOS;

static class NvidiaChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        await NvidiaAppChecks.Run(Path.Combine(root,"nvidia-app"),check);
        var folder=Path.Combine(root,"NVIDIA O'Brien 测试"); Directory.CreateDirectory(folder);
        var worker=Path.Combine(folder,"worker.ps1"); File.WriteAllText(worker,NvidiaSetup.Script,new System.Text.UTF8Encoding(true));
        var run=new ProcessRunner(_=>{});
        var prefix=". "+Policy.Ps(worker)+" -Library\n";
        foreach(var script in new[]{NvidiaSetup.Script,NvidiaSetup.Queue,Provisioning.Script([],false,false,nvidia:true)})
            await run.PowerShell("$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseInput("+Policy.Ps(script)+",[ref]$t,[ref]$e);if($e.Count){throw ($e|Out-String)}");
        check(true,"NVIDIA worker, queue and first-login integration parse in Windows PowerShell 5.1 without registering tasks");
        foreach(var (name,id,expected) in new[]{("Intel Arc",@"PCI\VEN_8086&DEV_56A0",0),("AMD Radeon",@"PCI\VEN_1002&DEV_744C",0),("NVIDIA misleading name",@"PCI\VEN_1002&DEV_744C",0),("Microsoft Basic Display Adapter",@"PCI\VEN_10DE&DEV_2684",1)})
        {
            var result=await run.PowerShell(prefix+"function Get-CimInstance { [pscustomobject]@{Name="+Policy.Ps(name)+";PNPDeviceID="+Policy.Ps(id)+"} }; @(Get-NvidiaDevices).Count");
            check(result.Trim()==expected.ToString(),"NVIDIA hardware gate uses PCI vendor ID: "+name);
        }
        var skip=Path.Combine(folder,"skip"); Directory.CreateDirectory(skip);
        await run.PowerShell(prefix+"function Get-NvidiaDevices { @() }; function Get-NvidiaRelease { throw 'Must not query NVIDIA' }; function Invoke-NvidiaProcess {throw 'Must not launch tools'}; Invoke-NvidiaProvisioning "+Policy.Ps(folder)+" "+Policy.Ps(skip));
        check(File.ReadAllText(Path.Combine(skip,"complete")).Trim()=="Not applicable","non-NVIDIA setup completes without network, driver installation or profile tool execution");
        var catalog="""
            function Get-NvidiaLookup($Type,$Parent){switch($Type){1{[pscustomobject]@{Name='GeForce';Value=1}}2{[pscustomobject]@{Name='RTX 40 Series';Value=127}}3{[pscustomobject]@{Name='NVIDIA GeForce RTX 4090';Value=995}}}}
            function Invoke-RestMethod {param($Uri,$TimeoutSec) [pscustomobject]@{IDS=@(
              [pscustomobject]@{downloadInfo=[pscustomobject]@{Version='560.09';Success='1';IsBeta='0';IsWHQL='1';DownloadURL='https://us.download.nvidia.com/old.exe'}},
              [pscustomobject]@{downloadInfo=[pscustomobject]@{Version='560.10';Success='1';IsBeta='0';IsWHQL='1';DownloadURL='https://us.download.nvidia.com/current.exe'}},
              [pscustomobject]@{downloadInfo=[pscustomobject]@{Version='999.99';Success='1';IsBeta='1';IsWHQL='1';DownloadURL='https://us.download.nvidia.com/beta.exe'}})}}
            """;
        var latest=await run.PowerShell(prefix+catalog+"\n(Get-NvidiaRelease 'NVIDIA GeForce RTX 4090').Version");
        check(latest.Trim()=="560.10","NVIDIA lookup uses an exact live catalogue model and numeric newest WHQL version, excluding beta metadata");
        try{await run.PowerShell(prefix+catalog+"\nGet-NvidiaRelease 'NVIDIA Unknown Future GPU'");throw new Exception("Expected unknown GPU rejection");}
        catch(IOException ex) when(ex.Message.Contains("No unambiguous")){check(true,"unknown or unsupported NVIDIA model does not fall back to a guessed family");}
        foreach(var url in new[]{"http://us.download.nvidia.com/a.exe","https://nvidia.com.attacker.test/a.exe","https://user@us.download.nvidia.com/a.exe","https://us.download.nvidia.com:444/a.exe"})
        {
            try{await run.PowerShell(prefix+"Assert-NvidiaUrl "+Policy.Ps(url));throw new Exception("Expected URL rejection");}
            catch(IOException ex) when(ex.Message.Contains("untrusted")){check(true,"NVIDIA download trust boundary rejects "+url);}
        }
        var profile=Path.Combine(folder,"TechLoungeProfile.nip");
        File.WriteAllText(profile,"<?xml version=\"1.0\" encoding=\"utf-16\"?><ArrayOfProfile><Profile><Executeables/><Settings><ProfileSetting><SettingID>123</SettingID><SettingValue>4</SettingValue><ValueType>Dword</ValueType></ProfileSetting></Settings></Profile></ArrayOfProfile>",new System.Text.UTF8Encoding(true));
        var original=File.ReadAllBytes(profile); var normalized=NvidiaSetup.Profile(profile); normalized.Save(Path.Combine(folder,"normalized.nip"));
        check(normalized.Declaration!.Encoding=="utf-8" && normalized.Root!.Element("Profile")!.Element("ProfileName")!.Value=="_GLOBAL_DRIVER_PROFILE (Base Profile)" && File.ReadAllBytes(profile).SequenceEqual(original),"private profile normalization corrects mismatched encoding/global name and preserves the original supplied profile");
        var ready=Path.Combine(folder,"ready");Directory.CreateDirectory(ready); normalized.Save(profile);
        var model="""
            function Ensure-NvidiaApp {param($State) Set-Content (Join-Path $State 'app-fixture-ready') 'inert verified App'}
            function Get-NvidiaDevices { [pscustomobject]@{Name='NVIDIA GeForce RTX 4090';PNPDeviceID='PCI\VEN_10DE&DEV_2684';DriverVersion='32.0.15.6094'} }
            function Get-NvidiaRelease { [pscustomobject]@{Version='560.00';DownloadURL='https://us.download.nvidia.com/current.exe'} }
            function Get-CimInstance($ClassName){if($ClassName -eq 'Win32_OperatingSystem'){[pscustomobject]@{LastBootUpTime=[datetime]'2026-10-25T00:00:00Z'}}else{[pscustomobject]@{DeviceClass='DISPLAY';DeviceID='PCI\VEN_10DE&DEV_2684';IsSigned=$true;DriverProviderName='NVIDIA';DriverVersion='32.0.15.6094'}}}
            function Invoke-WebRequest { throw 'A newer installed driver must not download an older package' }
            """;
        foreach(var (label,signed) in new[]{("wrong-device",model.Replace("DeviceClass='DISPLAY';DeviceID='PCI\\VEN_10DE&DEV_2684'","DeviceClass='DISPLAY';DeviceID='PCI\\VEN_10DE&DEV_9999'")),("stale-signed-version",model.Replace("DriverProviderName='NVIDIA';DriverVersion='32.0.15.6094'","DriverProviderName='NVIDIA';DriverVersion='32.0.15.0000'"))}) {
            var rejected=Path.Combine(folder,label);Directory.CreateDirectory(rejected);
            try{await run.PowerShell(prefix+signed+"\nInvoke-NvidiaProvisioning "+Policy.Ps(folder)+" "+Policy.Ps(rejected));throw new Exception("Expected per-device driver rejection");}
            catch(IOException ex)when(ex.Message.Contains("for every detected")){check(!File.Exists(Path.Combine(rejected,"app-fixture-ready"))&&!File.Exists(Path.Combine(rejected,"complete")),label+": an unrelated or stale signed driver cannot unlock NVIDIA App/profile installation");}
        }
        var import=prefix+model+"\nfunction Export-NvidiaProfile($Payload,$Destination){if(!(Test-Path (Join-Path (Split-Path $Destination) 'app-fixture-ready'))){throw 'Profile ran before NVIDIA App readiness'};Copy-Item -LiteralPath (Join-Path $Payload 'TechLoungeProfile.nip') -Destination $Destination}; function Invoke-NvidiaProcess($Path,$Arguments){if($Arguments -notlike '-silentImport*'){throw 'Unexpected tool operation'};0}; Invoke-NvidiaProvisioning "+Policy.Ps(folder)+" "+Policy.Ps(ready);
        await run.PowerShell(import);
        check(File.Exists(Path.Combine(ready,"complete")) && File.Exists(Path.Combine(ready,"before.nip")) && File.Exists(Path.Combine(ready,"after.nip")),"newer installed NVIDIA driver is retained and profile completion requires backup and exact settings readback");
        var mismatch=Path.Combine(folder,"mismatch");Directory.CreateDirectory(mismatch);
        try{await run.PowerShell(prefix+model+"\nfunction Export-NvidiaProfile($Payload,$Destination){Set-Content -LiteralPath $Destination '<ArrayOfProfile/>'}; function Invoke-NvidiaProcess {0}; Invoke-NvidiaProvisioning "+Policy.Ps(folder)+" "+Policy.Ps(mismatch));throw new Exception("Expected readback failure");}
        catch(IOException ex)when(ex.Message.Contains("did not read back")){check(!File.Exists(Path.Combine(mismatch,"complete")),"successful Inspector exit alone cannot mark mismatched profile settings complete");}
        var reboot=Path.Combine(folder,"reboot");Directory.CreateDirectory(reboot);File.WriteAllText(Path.Combine(reboot,"restart-boot.txt"),"2026-10-25T00:00:00.0000000Z");
        try{await run.PowerShell(prefix+model+"\nfunction Get-NvidiaRelease {throw 'Must wait for reboot before querying/downloading again'}; Invoke-NvidiaProvisioning "+Policy.Ps(folder)+" "+Policy.Ps(reboot));throw new Exception("Expected reboot deferral");}
        catch(IOException ex) when(ex.Message.Contains("waiting for a normal")){check(!File.Exists(Path.Combine(reboot,"complete")),"NVIDIA setup waits for a real restart without repeating driver downloads on the same boot");}
        var downloadState=Path.Combine(folder,"download");Directory.CreateDirectory(downloadState);
        var download=prefix+model+"""

            function Get-NvidiaRelease { [pscustomobject]@{Version='999.10';DownloadURL='https://us.download.nvidia.com/new.exe'} }
            function Assert-NvidiaSpace { }
            function Invoke-WebRequest {param($Uri,[switch]$UseBasicParsing,$MaximumRedirection,$TimeoutSec,$OutFile) Set-Content -LiteralPath $OutFile 'inert driver fixture'}
            function Assert-NvidiaSignature { }
            function Invoke-NvidiaProcess($Path,$Arguments) { if($Path -like '*7zr.exe'){Set-Content -LiteralPath (Join-Path $script:downloadState 'driver-files\setup.exe') 'inert setup fixture';return 0};if($Path -like '*setup.exe' -and $Arguments -eq '-s -n Display.Driver'){return 1};throw 'Unexpected profile invocation before restart' }
            """+"\n$script:downloadState="+Policy.Ps(downloadState)+"; Invoke-NvidiaProvisioning "+Policy.Ps(folder)+" "+Policy.Ps(downloadState);
        try{await run.PowerShell(download);throw new Exception("Expected restart after successful install");}
        catch(IOException ex)when(ex.Message.Contains("driver installation completed")){check(File.Exists(Path.Combine(downloadState,"restart-boot.txt")) && File.ReadAllText(Path.Combine(downloadState,"driver-receipt.json")).Contains("999.10") && !File.Exists(Path.Combine(downloadState,"complete")),"post-install download/extraction uses quiet driver-only setup and records success-with-reboot before deferring profile import");}
        try{await run.PowerShell(prefix+"function Get-AuthenticodeSignature { [pscustomobject]@{Status='Valid';SignerCertificate=[pscustomobject]@{Subject='CN=Other, O=Other Corp'}} }; Assert-NvidiaSignature 'inert.exe'");throw new Exception("Expected publisher rejection");}
        catch(IOException ex)when(ex.Message.Contains("not signed by a trusted NVIDIA")){check(true,"a valid signature from another publisher is rejected before driver execution");}
        var baseDriver=prefix+"""
            function New-Object {param($ComObject)
              if($ComObject -eq 'Microsoft.Update.UpdateColl'){return ,([Collections.ArrayList]::new())}
              if($ComObject -ne 'Microsoft.Update.Session'){throw 'Unexpected COM object'}
              $session=[pscustomobject]@{}
              $session|Add-Member ScriptMethod CreateUpdateSearcher {
                $searcher=[pscustomobject]@{};$searcher|Add-Member ScriptMethod Search {param($query)
                  [pscustomobject]@{ResultCode=2;Updates=@(
                    [pscustomobject]@{DriverClass='Display';DriverProvider='NVIDIA';DriverHardwareID='PCI\VEN_10DE&DEV_2684';EulaAccepted=$true},
                    [pscustomobject]@{DriverClass='Display';DriverProvider='AMD';DriverHardwareID='PCI\VEN_1002&DEV_744C';EulaAccepted=$true},
                    [pscustomobject]@{DriverClass='Display';DriverProvider='NVIDIA';DriverHardwareID='PCI\VEN_10DE&DEV_0000';EulaAccepted=$true})}
                };$searcher
              }
              $session|Add-Member ScriptMethod CreateUpdateDownloader {
                $d=[pscustomobject]@{Updates=$null};$d|Add-Member ScriptMethod Download {[pscustomobject]@{ResultCode=2}};$d
              }
              $session|Add-Member ScriptMethod CreateUpdateInstaller {
                $i=[pscustomobject]@{Updates=$null;ForceQuiet=$false};$i|Add-Member ScriptMethod Install {
                  if(!$this.ForceQuiet -or $this.Updates.Count -ne 1 -or $this.Updates[0].DriverHardwareID -ne 'PCI\VEN_10DE&DEV_2684'){throw 'Wrong update selected'}
                  [pscustomobject]@{ResultCode=2}
                };$i
              };$session
            }
            Install-NvidiaBaseDriver @([pscustomobject]@{PNPDeviceID='PCI\VEN_10DE&DEV_2684&SUBSYS_12345678'})
            'base-driver-selected'
            """;
        check((await run.PowerShell(baseDriver)).Contains("base-driver-selected"),"base-driver bootstrap selects only the matching NVIDIA PCI display update and excludes AMD and other NVIDIA device IDs");
        var queueFolder=Path.Combine(folder,"queue");Directory.CreateDirectory(queueFolder);
        File.WriteAllText(Path.Combine(queueFolder,"install.ps1"),NvidiaSetup.Script);
        var queueFile=Path.Combine(queueFolder,"queue.ps1");File.WriteAllText(queueFile,NvidiaSetup.Queue,new System.Text.UTF8Encoding(true));
        var queueMocks="""
            function Get-CimInstance { [pscustomobject]@{PNPDeviceID="PCI\VEN_10DE&DEV_2684";Name="NVIDIA fixture"} }
            function Set-Acl {param($LiteralPath,$AclObject) }
            function Get-ScheduledTask {param($TaskName,$ErrorAction) if(Test-Path $global:tlosFixtureTaskFile){Get-Content $global:tlosFixtureTaskFile -Raw|ConvertFrom-Json} }
            function New-ScheduledTaskAction {param($Execute,$Argument) [pscustomobject]@{Execute=$Execute;Arguments=$Argument} }
            function New-ScheduledTaskTrigger {param([switch]$AtLogOn,[switch]$AtStartup) [pscustomobject]@{Fixture=$true} }
            function New-ScheduledTaskPrincipal {param($UserId,$LogonType,$RunLevel) [pscustomobject]@{UserId=$UserId} }
            function New-ScheduledTaskSettingsSet {param($MultipleInstances,[switch]$StartWhenAvailable,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries,$ExecutionTimeLimit) [pscustomobject]@{Fixture=$true} }
            function Register-ScheduledTask {param($TaskName,$Action,$Trigger,$Principal,$Settings)
              if($global:tlosFixtureDenyRegister){throw 'Synthetic task registration failure'}
              [pscustomobject]@{Actions=@($Action);Principal=$Principal}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 $global:tlosFixtureTaskFile
            }
            """;
        var queueHeader="$global:tlosFixtureTaskFile="+Policy.Ps(Path.Combine(queueFolder,"fake-task.json"))+"\n"+queueMocks+"\n";
        try{await run.PowerShell(queueHeader+"$global:tlosFixtureDenyRegister=$true; & "+Policy.Ps(queueFile));throw new Exception("Expected synthetic queue failure");}
        catch(IOException ex)when(ex.Message.Contains("Synthetic task registration failure")){}
        await run.PowerShell(queueHeader+"& "+Policy.Ps(queueFile)+"; & "+Policy.Ps(queueFile));
        check(File.Exists(Path.Combine(queueFolder,"fake-task.json")) && File.Exists(Path.Combine(queueFolder,"state","owner.txt")),"queue registration can recover from failure and reuse its own existing task without creating real tasks or changing host ACLs");
        check(BootableDrive.RequiredUsbBytes(12_075_616_027)==12_712_951_260L && BootableDrive.RequiredUsbBytes(14_500_000_000L)<BootableDrive.Target16GbBytes && BootableDrive.RequiredUsbBytes(15_000_000_000L)>BootableDrive.Target16GbBytes,
            "16 GB budget includes actual prior media bytes, filesystem reserve and five percent headroom");
    }
}
