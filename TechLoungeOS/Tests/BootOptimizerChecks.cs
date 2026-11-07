using System.Text.Json;
using TechLoungeOS;

static class BootOptimizerChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var mount=Path.Combine(root,"boot-stage");var hook=SetupHooks.ImageHook(mount);Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        var original=System.Text.Encoding.ASCII.GetBytes("@echo inherited setup\r\n");File.WriteAllBytes(hook,original);
        BootOptimizer.Stage(mount,["Discord.Discord","REALiX.HWiNFO"]);
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS\BootOptimizer");
        check(RuntimePack.Hash(Path.Combine(folder,"Optimize-Boot.ps1")).Equals(BootOptimizer.SourceHash,StringComparison.OrdinalIgnoreCase)
            &&JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(folder,"apps.json")))!.SequenceEqual(new[]{RecommendedApps.Discord,"REALiX.HWiNFO"}),
            "Boot Optimizer stages the exact reviewed Tweaks script and canonical selected-app readiness manifest");
        check(File.ReadAllBytes(hook).AsSpan().EndsWith(original)&&File.ReadAllText(hook).Contains("BootOptimizer\\queue.ps1"),"Windows Setup registers Boot Optimizer without replacing the source hook or running tuning before app installation");
        check(File.ReadAllText(Path.Combine(folder,"driver-lock.ps1"))==GraphicsQueue.LockScript,"Boot Optimizer shares the existing graphics installer gate rather than changing GPU services during an installer");
        var script=Provisioning.Script(["Vendor.Example"],false,false,bootOptimizer:true);
        check(script.IndexOf("Invoke-Step 'Vendor.Example'",StringComparison.Ordinal)<script.IndexOf("Invoke-Step 'boot-optimizer'",StringComparison.Ordinal)
            &&script.IndexOf("Invoke-Step 'boot-optimizer'",StringComparison.Ordinal)<script.IndexOf("All selected provisioning steps completed.",StringComparison.Ordinal),
            "automatic Boot Optimizer is ordered after selected apps and before provisioning completion");
        var fixture=Path.Combine(root,"boot-login");Directory.CreateDirectory(fixture);
        var firstLogin="$fixture="+Policy.Ps(fixture)+";\n"+"""
            $env:ProgramData=Join-Path $fixture pd;$env:LOCALAPPDATA=Join-Path $fixture local
            $state=Join-Path $fixture state;New-Item $state -ItemType Directory -Force|Out-Null
            $bootRoot=Join-Path $env:ProgramData 'TechLoungeOS\BootOptimizer';New-Item $bootRoot -ItemType Directory -Force|Out-Null
            Set-Content "$bootRoot\queue-ready" 'owned fixture'
            $global:bootCalls=0;$global:bootErrors=@()
            function Invoke-Step {param($Name,$Action) try{& $Action;Set-Content (Join-Path $state ($Name+'.done')) 'complete'}catch{$global:bootErrors+=$_.Exception.Message}}
            function Start-ScheduledTask {param($TaskName,$ErrorAction)
              if($TaskName -ne 'TechLoungeOS-BootOptimizer'){throw 'Unexpected task'}
              $global:bootCalls++
              $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
              $result=Join-Path $bootRoot ('state\'+$sid);New-Item $result -ItemType Directory -Force|Out-Null
              Set-Content "$result\complete" 'owned fixture'
              Set-Content "$result\rollback.json" '[{"Kind":"Startup","Name":"Discord"}]'
            }
            """+"\n"+BootOptimizer.FirstLogin(["Vendor.Example"])+"\n"+"""
            $earlyCalls=$global:bootCalls;$earlyComplete=Test-Path (Join-Path $state 'boot-optimizer.done')
            Set-Content (Join-Path $state 'Vendor.Example.done') 'installed'
            $machine=Join-Path $env:ProgramData 'TechLoungeOS\MachineSetup';New-Item $machine -ItemType Directory -Force|Out-Null;Set-Content "$machine\complete" 'fixture'
            """+"\n"+BootOptimizer.FirstLogin(["Vendor.Example"])+"\n"+"""
            @{earlyCalls=$earlyCalls;earlyComplete=$earlyComplete;finalCalls=$global:bootCalls;complete=(Test-Path (Join-Path $state 'boot-optimizer.done'));rollback=(Get-Content "$env:LOCALAPPDATA\TechLoungeTweaks\boot-rollback.json" -Raw|ConvertFrom-Json).Name;errors=$global:bootErrors}|ConvertTo-Json -Compress
            """;
        var output=await new ProcessRunner(_=>{}).PowerShell(firstLogin);using var result=JsonDocument.Parse(output);var r=result.RootElement;
        check(r.GetProperty("earlyCalls").GetInt32()==0&&!r.GetProperty("earlyComplete").GetBoolean()&&r.GetProperty("errors")[0].GetString()!.Contains("Vendor.Example"),"failed or missing selected apps defer Boot Optimizer without starting a privileged job");
        check(r.GetProperty("finalCalls").GetInt32()==1&&r.GetProperty("complete").GetBoolean()&&r.GetProperty("rollback").GetString()=="Discord","successful app retry requests only the fixed action, waits for completion and retains Tweaks-compatible rollback data");
        var worker=Path.Combine(folder,"worker.ps1");var profile=Path.Combine(root,"boot-profile");Directory.CreateDirectory(profile);
        var selector=". "+Policy.Ps(worker)+" -Library\n$fixtureProfile="+Policy.Ps(profile)+"\n"+"""
            $env:ProgramData=Join-Path $fixtureProfile 'common'
            $targetRoot=Join-Path $env:ProgramData 'TechLoungeOS\BootOptimizer'
            New-Item $targetRoot -ItemType Directory -Force|Out-Null
            Set-Content (Join-Path $targetRoot apps.json) '["Vendor.Example"]'
            $appState=Join-Path $fixtureProfile 'AppData\Local\TechLoungeOS'
            New-Item $appState -ItemType Directory -Force|Out-Null
            Set-Content (Join-Path $appState 'Vendor.Example.done') 'installed'
            Set-Content (Join-Path $appState 'post-app-BootOptimizer.request') 'S-1-5-21-1-2-3-1001'
            $global:ownerSids=@('S-1-5-21-1-2-3-1001');$global:linked=$false
            function Get-CimInstance {param($ClassName,$Filter)
              if($ClassName -eq 'Win32_Process'){foreach($sid in $global:ownerSids){[pscustomobject]@{Sid=$sid}}}
              elseif($ClassName -eq 'Win32_UserProfile'){[pscustomobject]@{Loaded=$true;Special=$false;LocalPath=if($Filter -match '1002'){$fixtureProfile+'-second'}else{$fixtureProfile}}}
              else{throw 'Unexpected machine inspection'}
            }
            function Invoke-CimMethod {param($InputObject,$MethodName) if($MethodName -ne 'GetOwnerSid'){throw 'Unexpected method'};[pscustomobject]@{ReturnValue=0;Sid=$InputObject.Sid}}
            function Get-Item {param($LiteralPath,[switch]$Force) if($global:linked){[pscustomobject]@{Attributes=[IO.FileAttributes]::ReparsePoint}}else{Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force}}
            function Test-Path {param($LiteralPath) if($LiteralPath -like 'Registry::*'){return $true};Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath}
            $target=Get-PostAppTarget 'BootOptimizer'
            $global:ownerSids+= 'S-1-5-21-1-2-3-1002'
            $withSecond=Get-PostAppTarget 'BootOptimizer'
            $secondState=Join-Path ($fixtureProfile+'-second') 'AppData\Local\TechLoungeOS'
            New-Item $secondState -ItemType Directory -Force|Out-Null
            Set-Content (Join-Path $secondState 'post-app-BootOptimizer.request') 'S-1-5-21-1-2-3-1002'
            $missingAppTarget=Get-PostAppTarget 'BootOptimizer'
            Set-Content (Join-Path $secondState 'Vendor.Example.done') 'installed'
            $firstComplete=Join-Path $targetRoot 'state\S-1-5-21-1-2-3-1001'
            New-Item $firstComplete -ItemType Directory -Force|Out-Null
            Set-Content (Join-Path $firstComplete complete) 'completed'
            $secondTarget=Get-PostAppTarget 'BootOptimizer'
            Set-Content (Join-Path $secondState 'post-app-BootOptimizer.request') 'S-1-5-21-1-2-3-1001'
            $forged=$false;try{Get-PostAppTarget 'BootOptimizer'|Out-Null}catch{$forged=$true}
            Remove-Item -LiteralPath (Join-Path $firstComplete complete)
            $global:ownerSids=@('S-1-5-21-1-2-3-1001');$global:linked=$true;$linkRejected=$false
            try{Get-PostAppTarget 'BootOptimizer'|Out-Null}catch{$linkRejected=$true}
            $global:linked=$false
            $manifest=Join-Path $fixtureProfile apps.json
            Set-Content $manifest '[]';$emptyApps=@(Read-PostAppSelections $manifest)
            Set-Content $manifest '["Vendor.Example"]';$oneApp=@(Read-PostAppSelections $manifest)
            Set-Content $manifest '["Vendor.Example","Other.App"]';$twoApps=@(Read-PostAppSelections $manifest)
            Set-Content $manifest '[["Vendor.Example"]]';$nestedRejected=$false
            try{Read-PostAppSelections $manifest|Out-Null}catch{$nestedRejected=$true}
            $missing=Test-PostAppReceipts $fixtureProfile @('Vendor.Example')
            Set-Content (Join-Path $fixtureProfile 'Vendor.Example.done') 'installed'
            $ready=Test-PostAppReceipts $fixtureProfile @('Vendor.Example');$invalid=$false
            try{Test-PostAppReceipts $fixtureProfile @('..\wrong')|Out-Null}catch{$invalid=$true}
            @{sid=$target.Sid;profile=$target.Profile;withSecond=$withSecond.Sid;missingAppTarget=$missingAppTarget.Sid;secondTarget=$secondTarget.Sid;forged=$forged;linked=$linkRejected;missing=$missing;ready=$ready;invalid=$invalid;empty=$emptyApps.Count;one=$oneApp;two=$twoApps;nestedRejected=$nestedRejected}|ConvertTo-Json -Compress
            """;
        output=await new ProcessRunner(_=>{}).PowerShell(selector);using var selected=JsonDocument.Parse(output);var s=selected.RootElement;
        check(s.GetProperty("sid").GetString()=="S-1-5-21-1-2-3-1001"&&s.GetProperty("profile").GetString()==profile&&s.GetProperty("linked").GetBoolean(),"privileged worker resolves the exact requesting profile and rejects linked profile paths");
        check(s.GetProperty("withSecond").GetString()=="S-1-5-21-1-2-3-1001"&&s.GetProperty("missingAppTarget").GetString()=="S-1-5-21-1-2-3-1001"&&s.GetProperty("secondTarget").GetString()=="S-1-5-21-1-2-3-1002"&&s.GetProperty("forged").GetBoolean(),"multiple signed-in users select only requested app-ready profiles, advance past completed users and reject a forged target SID");
        check(!s.GetProperty("missing").GetBoolean()&&s.GetProperty("ready").GetBoolean()&&s.GetProperty("invalid").GetBoolean(),"worker independently verifies all selected-app receipts and rejects path-bearing app identities");
        check(s.GetProperty("empty").GetInt32()==0&&s.GetProperty("one")[0].GetString()=="Vendor.Example"&&s.GetProperty("two").GetArrayLength()==2&&s.GetProperty("nestedRejected").GetBoolean(),"Windows PowerShell parses empty, single and multiple selected-app JSON arrays without nesting them and rejects nested input");
    }
}
