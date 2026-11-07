using System.Diagnostics;
using System.Text.Json;
using TechLoungeOS;

// Real child processes and durable receipts, using an inert installer substitute.
// No task registration, SYSTEM token, Windows servicing or vendor installer is used.
internal static class DesktopWorkerChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var area=Path.Combine(root,"worker O'Brien 测试");
        var common=Path.Combine(area,"ProgramData");
        var queue=DesktopSetup.Folder(area);
        Directory.CreateDirectory(queue);
        var source=Path.Combine(common,"Fixture");Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source,"install.ps1"),"inert fixture, never executed");
        var runtime=DesktopSetup.Snapshot("Runtimes","Common","Fixture",source);
        var edge=runtime with{Id="Edge"};
        File.WriteAllText(Path.Combine(queue,"steps.json"),JsonSerializer.Serialize(new[]{runtime,edge}));
        var fixtureSource=Path.Combine(area,"Installer.cs");
        File.WriteAllText(fixtureSource,"""
            using System;using System.IO;using System.Threading;
            class Installer {
              static int Main(string[] args){
                if(args.Length!=2 || args[0]!="--machine-installer" || (args[1]!="Runtimes" && args[1]!="Edge"))return 9;
                string root=Path.Combine(Environment.GetEnvironmentVariable("ProgramData"),"TechLoungeOS","MachineSetup");
                string cache=Path.Combine(root,"payloads",args[1]);
                if(File.Exists(Path.Combine(cache,"complete")))return 0;
                File.AppendAllText(Path.Combine(root,"calls.txt"),args[1]+"\n");
                if(File.Exists(Path.Combine(root,args[1]+".fail")))return 7;
                for(int n=0;File.Exists(Path.Combine(root,args[1]+".hold"));n++){
                  if(n>200)return 8;Thread.Sleep(50);
                }
                File.WriteAllText(Path.Combine(cache,"complete"),"inert child completed");
                if(args[1]=="Edge")File.WriteAllText(Path.Combine(cache,"restart-required"),"inert fixture reminder; no restart requested");return 0;
              }
            }
            """);
        var launcher=Path.Combine(common,@"TechLoungeOS\FirstLogin.exe");
        var run=new ProcessRunner(_=>{});
        await run.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),["/nologo","/target:exe","/out:"+launcher,fixtureSource]);
        var header=$$"""
            $env:ProgramData={{Policy.Ps(common)}}
            function Get-Process {
              param($Name,$Id,$ErrorAction)
              if($Name -eq 'explorer'){[pscustomobject]@{SessionId=1};return}
              Microsoft.PowerShell.Management\Get-Process -Id $Id -ErrorAction SilentlyContinue
            }
            """;
        const string identity="if([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne 'S-1-5-18'){throw 'Desktop setup requires its fixed SYSTEM task'}";
        if(!DesktopSetup.Worker.Contains(identity,StringComparison.Ordinal))throw new Exception("Fixture identity substitution no longer matches the production boundary.");
        var worker=Path.Combine(queue,"worker.ps1");
        File.WriteAllText(worker,DesktopSetup.Worker.Replace(identity,"# Inert fixture only: no SYSTEM token or real installer.",StringComparison.Ordinal)
            .Replace("param([switch]$Library)","param([switch]$Library)\n"+header,StringComparison.Ordinal),new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(queue,"Edge.fail"),"fixture failure");
        string lastError="";
        async Task<bool> Invoke(){try{await run.PowerShell("& "+Policy.Ps(worker));return true;}catch(IOException error){lastError=error.Message;return false;}}
        var failed=!await Invoke();
        using(var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(queue,"status.json"))))
            failed=failed && File.Exists(Path.Combine(queue,"Runtimes.done")) && !File.Exists(Path.Combine(queue,"Edge.done")) &&
                status.RootElement.GetProperty("phase").GetString()=="failed";
        check(failed,"real desktop worker retains a successful step and reports the inert second installer's failure without global completion"+(failed?"":": "+lastError));
        File.Delete(Path.Combine(queue,"Edge.fail"));
        check(await Invoke() && File.Exists(Path.Combine(queue,"complete")) && File.Exists(Path.Combine(queue,"restart-required")) && File.ReadAllLines(Path.Combine(queue,"calls.txt")).SequenceEqual(new[]{"Runtimes","Edge","Edge"}),
            "retry skips the successful runtime step, retries the failed inert installer and retains its restart-needed receipt without requesting a reboot");
        check(await Invoke() && File.ReadAllLines(Path.Combine(queue,"calls.txt")).Length==3,"a completed machine queue performs no further installer work");

        // Interrupt only this disposable PowerShell fixture. Its inert child must survive.
        foreach(var name in new[]{"complete","Edge.done",@"payloads\Edge\complete"})File.Delete(Path.Combine(queue,name));
        File.WriteAllText(Path.Combine(queue,"Edge.hold"),"fixture latch");
        var parentScript=Path.Combine(area,"parent.ps1");var parentError=Path.Combine(area,"parent-error.txt");
        File.WriteAllText(parentScript,"try { & "+Policy.Ps(worker)+" } catch { [IO.File]::WriteAllText("+Policy.Ps(parentError)+",($_|Out-String));exit 1 }",new System.Text.UTF8Encoding(true));
        var powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        var info=new ProcessStartInfo(powershell){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",parentScript})info.ArgumentList.Add(arg);
        using var parent=Process.Start(info)??throw new Exception("Fixture worker did not start.");
        Process? child=null;
        try {
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed.TotalSeconds<8){
                var status=Path.Combine(queue,"running.json");
                if(File.Exists(status)){
                    using var json=JsonDocument.Parse(File.ReadAllText(status));
                    if(json.RootElement.GetProperty("id").GetString()=="Edge"){
                        try{child=Process.GetProcessById(json.RootElement.GetProperty("pid").GetInt32());if(!child.HasExited)break;child.Dispose();child=null;}catch(ArgumentException){}
                    }
                }
                await Task.Delay(50);
            }
            if(child==null)throw new Exception("Inert child did not reach its interruption latch. "+(File.Exists(parentError)?File.ReadAllText(parentError):"No fixture error report.")+" Status: "+File.ReadAllText(Path.Combine(queue,"status.json")));
            parent.Kill();await parent.WaitForExitAsync();
            check(!child.HasExited && !await Invoke() && !File.Exists(Path.Combine(queue,"complete")) && File.ReadAllLines(Path.Combine(queue,"calls.txt")).Length==4,
                "an interrupted observer does not kill its inert installer; retry detects the exact surviving process and starts no duplicate");
            File.Delete(Path.Combine(queue,"Edge.hold"));
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(await Invoke() && File.Exists(Path.Combine(queue,"complete")) && File.ReadAllLines(Path.Combine(queue,"calls.txt")).Length==4,
                "after interrupted work finishes, retry reuses its completion marker without reinstalling the successful payload");
        }finally{
            File.Delete(Path.Combine(queue,"Edge.hold"));
            if(!parent.HasExited){parent.Kill();await parent.WaitForExitAsync();}
            if(child!=null){if(!child.HasExited)await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));child.Dispose();}
        }
    }
}
