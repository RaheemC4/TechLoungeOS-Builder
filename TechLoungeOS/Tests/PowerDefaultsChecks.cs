using System.Text.Json;
using TechLoungeOS;

static class PowerDefaultsChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"power O'Brien 测试");Directory.CreateDirectory(folder);
        var worker=Path.Combine(folder,"worker.ps1");File.WriteAllText(worker,PowerDefaults.Worker);
        var script=". "+Policy.Ps(worker)+" -Library\n$fixture="+Policy.Ps(folder)+"\n"+"""
            # Only enumerate/read the host's real API. All write/refresh calls below are fixtures.
            $nativePlans=@(Get-IdlePowerPlans)
            if(!$nativePlans.Count){throw 'Host native power API returned no plans'}
            $nativeRead=Get-IdlePowerTimeout $nativePlans[0] '7516b95f-f776-4464-8c53-06167f40cc99' '3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e' $true
            $nativeSleep=Get-IdlePowerTimeout $nativePlans[0] '238c9fa8-0aad-41ed-83f4-97be242c8f20' '29f6c1db-86da-48c5-9fdb-f2b67b1f44da' $true
            $nativeHibernate=Get-IdlePowerTimeout $nativePlans[0] '238c9fa8-0aad-41ed-83f4-97be242c8f20' '9d7815a6-7ee4-497e-8888-515a05f02364' $true
            $script:plans=@('381b4222-f694-41f0-9685-ff5bb260df2e','8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c')
            $script:values=@{};$script:writes=0;$script:refresh=0;$script:reject=$false;$script:readback=$false;$script:policy=$false;$script:addPlan=$false;$script:enumerations=0
            function Get-IdlePowerPlans {
              $script:enumerations++
              if($script:addPlan -and $script:enumerations -eq 2){$script:plans+='a1841308-3541-4fab-bc81-f71556f20b4a'}
              $script:plans
            }
            function Get-IdlePowerTimeout($Scheme,$Group,$Setting,[bool]$Ac){
              if($script:readback -and $script:writes -gt 0){return 71}
              $key="$Scheme|$Group|$Setting|$Ac"
              if(!$script:values.ContainsKey($key)){$script:values[$key]=if($Ac){1800}else{300}}
              return $script:values[$key]
            }
            function Set-IdlePowerTimeout($Scheme,$Group,$Setting,[bool]$Ac){
              if($script:reject){throw 'Inert access-denied fixture'}
              $script:writes++;$script:values["$Scheme|$Group|$Setting|$Ac"]=0
            }
            function Update-IdlePowerPlan {$script:refresh++}
            function Assert-IdlePowerPolicy($Settings){if($script:policy){throw 'Inert organisation policy fixture'}}
            $state=Join-Path $fixture success
            Set-IdlePowerDefaults $state $false
            $initialWrites=$script:writes;$initialApplied=(Test-Path "$state\initial-complete") -and !(Test-Path "$state\complete")
            $backup=[IO.File]::ReadAllText("$state\before.json")
            $script:plans+='a1841308-3541-4fab-bc81-f71556f20b4a'
            Set-IdlePowerDefaults $state $true
            $final=[IO.File]::ReadAllText("$state\verified.json")|ConvertFrom-Json
            $finalWrites=$script:writes
            $changedKey=$script:plans[0]+'|7516b95f-f776-4464-8c53-06167f40cc99|3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e|True'
            $script:values[$changedKey]=1200
            Set-IdlePowerDefaults $state $true
            $respected=$script:values[$changedKey] -eq 1200 -and $script:writes -eq $finalWrites
            $backupRetained=[IO.File]::ReadAllText("$state\before.json") -ceq $backup
            $script:values=@{};$script:writes=0;$script:reject=$true
            $retry=Join-Path $fixture retry;$failure=$false
            try{Set-IdlePowerDefaults $retry $true}catch{$failure=$_.Exception.Message -like '*access-denied*'}
            $failedComplete=Test-Path "$retry\complete";$retryBackup=[IO.File]::ReadAllText("$retry\before.json")
            $script:reject=$false;Set-IdlePowerDefaults $retry $true
            $retried=(Test-Path "$retry\complete") -and [IO.File]::ReadAllText("$retry\before.json") -ceq $retryBackup
            $script:values=@{};$script:writes=0;$script:readback=$true
            $wrong=Join-Path $fixture readback;$wrongRejected=$false
            try{Set-IdlePowerDefaults $wrong $true}catch{$wrongRejected=$_.Exception.Message -like '*readback failed*'}
            $script:readback=$false;$script:values=@{};$script:writes=0;$script:policy=$true
            $policyState=Join-Path $fixture policy;$policyRejected=$false
            try{Set-IdlePowerDefaults $policyState $true}catch{$policyRejected=$_.Exception.Message -like '*organisation policy*'}
            $policyWrites=$script:writes;$script:policy=$false
            $script:values=@{};$script:writes=0;$script:plans=@('381b4222-f694-41f0-9685-ff5bb260df2e');$script:enumerations=0;$script:addPlan=$true
            $changing=Join-Path $fixture changing;$changedRejected=$false
            try{Set-IdlePowerDefaults $changing $true}catch{$changedRejected=$_.Exception.Message -like '*Power plans changed*'}
            @{nativeReadOnlyPlans=$nativePlans.Count;nativeScreenAc=$nativeRead;initial=$initialApplied;initialWrites=$initialWrites;final=$final;finalWrites=$finalWrites;respected=$respected;backupRetained=$backupRetained;failure=$failure;failedComplete=$failedComplete;retried=$retried;wrongRejected=$wrongRejected;wrongComplete=(Test-Path "$wrong\complete");policyRejected=$policyRejected;policyWrites=$policyWrites;changedRejected=$changedRejected;changedComplete=(Test-Path "$changing\complete")}|ConvertTo-Json -Depth 6 -Compress
            """;
        var output=await new ProcessRunner(_=>{}).PowerShell(script);
        using var json=JsonDocument.Parse(output);var r=json.RootElement;
        check(r.GetProperty("nativeReadOnlyPlans").GetInt32()>0,"actual Windows power API enumerates schemes and reads a timer without changing host power settings");
        check(r.GetProperty("initial").GetBoolean()&&r.GetProperty("initialWrites").GetInt32()==12,
            "setup applies all six AC/DC idle timers to two independent power plans before app installation");
        var final=r.GetProperty("final");
        check(r.GetProperty("finalWrites").GetInt32()==18&&final.GetProperty("plans").GetArrayLength()==3&&final.GetProperty("finalized").GetBoolean()&&
            new[]{"screenAc","screenDc","sleepAc","sleepDc","hibernateAc","hibernateDc"}.All(name=>final.GetProperty(name).GetInt32()==0),
            "post-app finalisation includes a newly created power plan and records all six timers verified Never");
        check(r.GetProperty("respected").GetBoolean()&&r.GetProperty("backupRetained").GetBoolean(),
            "completed power setup preserves its original backup and lets users change their preferences afterwards");
        check(r.GetProperty("failure").GetBoolean()&&!r.GetProperty("failedComplete").GetBoolean()&&r.GetProperty("retried").GetBoolean(),
            "power API failures remain incomplete and retry safely without replacing the original backup");
        check(r.GetProperty("wrongRejected").GetBoolean()&&!r.GetProperty("wrongComplete").GetBoolean(),
            "nonzero timer readback cannot produce a completed power-default receipt");
        check(r.GetProperty("policyRejected").GetBoolean()&&r.GetProperty("policyWrites").GetInt32()==0,
            "enforced organisation policy is reported before changing any plan");
        check(r.GetProperty("changedRejected").GetBoolean()&&!r.GetProperty("changedComplete").GetBoolean(),
            "a concurrent power-plan addition cannot be silently omitted from final verification");
        var mount=Path.Combine(folder,"mount");PowerDefaults.Stage(mount,RecommendedApps.PresetIds);
        var staged=Path.Combine(mount,@"ProgramData\TechLoungeOS\PowerDefaults");
        check(File.ReadAllText(Path.Combine(staged,"worker.ps1"))==PowerDefaults.Worker&&File.Exists(Path.Combine(staged,"initial.ps1"))&&
            File.ReadAllText(SetupHooks.ImageHook(mount)).Contains(@"PowerDefaults\initial.ps1"),
            "shared ISO setup stages the exact power worker, early application and protected final task without a selectable opt-out");
        var login=Provisioning.Script([],false,false,desktopDefaults:true,bootOptimizer:true);
        check(login.IndexOf("Invoke-Step 'power-defaults'",StringComparison.Ordinal)>login.IndexOf("Invoke-Step 'boot-optimizer'",StringComparison.Ordinal)&&
            login.IndexOf("Invoke-Step 'power-defaults'",StringComparison.Ordinal)<login.IndexOf("All selected provisioning steps completed.",StringComparison.Ordinal),
            "final idle timeouts run after app and tweak processing and before first-login completion");
        var parse=await new ProcessRunner(_=>{}).PowerShell("$t=$null;$e=$null;[void][Management.Automation.Language.Parser]::ParseFile("+Policy.Ps(Path.Combine(staged,"queue.ps1"))+",[ref]$t,[ref]$e);if($e){throw ($e|Out-String)};'parsed'");
        check(parse.Trim()=="parsed","fixed SYSTEM power-task registration parses in Windows PowerShell without registering a host task");
        var iso=Path.Combine(folder,"source.iso");File.WriteAllText(iso,"unchanged source fixture");
        var tool=Path.Combine(folder,"oscdimg.exe");File.WriteAllText(tool,"inert tool fixture");
        var workspace=Path.Combine(folder,"missing power prerequisite");
        var options=new BuildOptions(iso,2,workspace,Path.Combine(folder,"must-not-export.iso"),tool,[],[],false,false,null)
        {IncludeOffice=false,IncludeEdge=false,IncludeRuntimePack=false,IncludeStoreXbox=false,IncludeNvidiaSetup=false,IncludeAmdIntelGraphics=false};
        var runner=new FixtureRunner(workspace,afterDism:args=>{
            if(args.Contains("/Mount-Image"))File.Delete(Path.Combine(workspace,@"mount\Windows\System32\powrprof.dll"));
        });
        try{await new BuildEngine(_=>{},_=>runner,_=>new Dictionary<string,string>()).Build(options,CancellationToken.None);throw new Exception("A stripped power-management prerequisite was accepted");}
        catch(IOException error) when(error.Message.Contains("power-management library",StringComparison.Ordinal))
        {
            check(!File.Exists(options.Output)&&runner.Commands.Any(c=>c.Contains("/Discard"))&&!runner.Commands.Any(c=>c.Contains("/Commit"))&&File.ReadAllText(iso)=="unchanged source fixture",
                "a source missing its native power library fails before export, discards the private mount and leaves the original input untouched");
        }
    }
}
