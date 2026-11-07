using System.Text.Json;
using TechLoungeOS;

internal static class DesktopSetupChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var area=Path.Combine(root,"desktop O'Brien 测试");Directory.CreateDirectory(area);
        var source=Path.Combine(area,@"source\Setup\Payload");Directory.CreateDirectory(Path.Combine(source,"Data"));
        File.WriteAllText(Path.Combine(source,@"Data\payload.dat"),"hello");
        File.WriteAllBytes(Path.Combine(source,"empty.dat"),[]);
        var step=DesktopSetup.Snapshot("Office","Windows",@"Setup\Payload",source);
        check(step.Files.Length==2 && step.Files.Single(f=>f.Destination==@"Data\payload.dat") is
            {Bytes:5,Sha256:"2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824"},
            "desktop setup snapshots actual bytes with a pinned independent checksum");
        var rejected=new[]{@"..\escape",@"C:\Windows",@"file:stream",@"NUL.txt",@"data\conin$",@"data\CONOUT$",@"data\COM¹",@"trailing.\x","bad\u007f",@"\\server\share",@"data\\x"};
        foreach(var path in rejected)
            check(!DesktopSetup.Relative(path),"desktop setup rejects unsafe or device paths: "+JsonSerializer.Serialize(path));
        var cache=Path.Combine(area,"cache");var worker=Path.Combine(area,"worker.ps1");
        File.WriteAllText(worker,DesktopSetup.Worker);
        var metadata=JsonSerializer.Serialize(step.Files);
        var script=". "+Policy.Ps(worker)+" -Library\n$items=ConvertFrom-Json "+Policy.Ps(metadata)+"\n"+$$"""
            foreach($path in (ConvertFrom-Json {{Policy.Ps(JsonSerializer.Serialize(rejected))}})){if(Test-DesktopSetupRelative $path){throw 'Unsafe PowerShell cache path accepted'} }
            $source={{Policy.Ps(Path.Combine(area,"source"))}}
            $cache={{Policy.Ps(cache)}}
            foreach($item in $items){Copy-DesktopSetupInput $item $source $cache}
            """;
        var run=new ProcessRunner(_=>{});
        await run.PowerShell(script);
        check(File.ReadAllText(Path.Combine(cache,@"Data\payload.dat"))=="hello" && new FileInfo(Path.Combine(cache,"empty.dat")).Length==0,
            "real PowerShell cache copy preserves exact data and empty files in quoted Unicode paths without running an installer");
        var bad=step.Files.Single(f=>f.Bytes==5) with{Destination="must-not-exist.dat",Sha256=new string('0',64)};
        try {await run.PowerShell(". "+Policy.Ps(worker)+" -Library\n$item=ConvertFrom-Json "+Policy.Ps(JsonSerializer.Serialize(bad))+"\nCopy-DesktopSetupInput $item "+Policy.Ps(Path.Combine(area,"source"))+" "+Policy.Ps(cache));throw new Exception("Corrupt input accepted");}
        catch(IOException ex) when(ex.Message.Contains("checksum changed"))
        {check(!File.Exists(Path.Combine(cache,"must-not-exist.dat")),"a changed input cannot create an executable cache file");}
        var held=Path.Combine(source,@"Data\payload.dat");
        using(var writer=new FileStream(held,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))
        {
            var refused=false;
            try{await run.PowerShell(script);throw new Exception("Concurrent writer accepted");}
            catch(IOException){refused=true;}
            check(refused && File.ReadAllText(Path.Combine(cache,@"Data\payload.dat"))=="hello","an existing writer prevents trusted copying and retains the previous cache");
        }
        await run.PowerShell(script);
        check(File.ReadAllText(Path.Combine(cache,@"Data\payload.dat"))=="hello","trusted copying resumes after the conflicting writer releases its handle");
        var malformed=step with{Files=[step.Files[0],step.Files[0] with{Destination=step.Files[0].Destination.ToUpperInvariant()}]};
        try{DesktopSetup.Validate(malformed);throw new Exception("Aliased cache names accepted");}
        catch(IOException){check(true,"case aliases cannot overwrite another protected cache entry");}
        var mount=Path.Combine(area,"mount");var shared=Path.Combine(mount,@"ProgramData\TechLoungeOS");Directory.CreateDirectory(shared);
        File.Copy(Path.Combine(AppContext.BaseDirectory,"FirstLogin.exe"),Path.Combine(shared,"FirstLogin.exe"));
        var hook=SetupHooks.ImageHook(mount);Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        File.WriteAllText(hook,"@echo preserved source hook\r\n");
        DesktopSetup.Stage(mount,[step]);
        var queued=JsonSerializer.Deserialize<DesktopSetup.Step[]>(File.ReadAllText(Path.Combine(DesktopSetup.Folder(mount),"steps.json")))!;
        check(queued is [{Id:"Office"}] && queued[0].Files.SequenceEqual(step.Files) &&
            File.ReadAllText(hook).EndsWith("@echo preserved source hook\r\n",StringComparison.Ordinal),
            "queue staging retains exact pinned inputs and the original source hook");
        var queue=Path.Combine(DesktopSetup.Folder(mount),"queue.ps1");
        var commands=await run.PowerShell("$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile("+Policy.Ps(queue)+",[ref]$t,[ref]$e);if($e){throw ($e|Out-String)};@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true)|ForEach-Object {$_.GetCommandName()})|ConvertTo-Json -Compress");
        var names=JsonSerializer.Deserialize<string[]>(commands)!;
        check(names.Contains("Register-ScheduledTask") && !names.Contains("Start-ScheduledTask") && !names.Contains("Start-Process"),
            "generated registration parses in real PowerShell and contains no installer launch; actual scheduler execution remains a VM gate");
        var conditions=await run.PowerShell($$"""
            $t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile({{Policy.Ps(queue)}},[ref]$t,[ref]$e)
            $guard=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.IfStatementAst] -and $n.Extent.Text.Contains('hard installer timeout')},$true))
            if($guard.Count -ne 1){throw 'Expected one machine task timing guard'}
            $test=[scriptblock]::Create($guard[0].Clauses[0].Item1.Extent.Text)
            $results=@()
            foreach($case in @(@{triggers=$null;limit='PT0S'},@{triggers=@();limit='PT0S'},@{triggers=@('inert fixture');limit='PT0S'},@{triggers=$null;limit='PT1H'})){
              $existing=[pscustomobject]@{Triggers=$case.triggers;Settings=[pscustomobject]@{ExecutionTimeLimit=$case.limit} }
              $results+= [bool](& $test)
            }
            ConvertTo-Json -InputObject $results -Compress
            """);
        check(JsonSerializer.Deserialize<bool[]>(conditions)!.SequenceEqual(new[]{false,false,true,true}),
            "actual PowerShell timing guard permits null/empty triggers for idempotent registration and rejects a boot trigger or hard installer timeout");
        var rejectedMount=Path.Combine(area,"duplicate-mount");
        try{DesktopSetup.Stage(rejectedMount,[step,step]);throw new Exception("Duplicate installer accepted");}
        catch(IOException){check(!Directory.Exists(DesktopSetup.Folder(rejectedMount)),"duplicate steps fail before creating a privileged queue folder");}
        var outside=Path.Combine(area,"outside");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"private.dat"),"fixture only");
        var link=Path.Combine(source,"linked");
        await run.PowerShell("New-Item -ItemType Junction -Path "+Policy.Ps(link)+" -Target "+Policy.Ps(outside)+"|Out-Null");
        try{
            try{DesktopSetup.Snapshot("Office","Windows",@"Setup\Payload",source);throw new Exception("Linked input tree accepted");}
            catch(IOException){check(File.ReadAllText(Path.Combine(outside,"private.dat"))=="fixture only","nested linked input folders are rejected before following their target");}
        }finally{Directory.Delete(link);}
        await DesktopWorkerChecks.Run(root,check);
    }
}
