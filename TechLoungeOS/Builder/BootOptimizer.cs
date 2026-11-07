using System.Security.Cryptography;
using System.Text.Json;

namespace TechLoungeOS;

public static class BootOptimizer
{
    public const string SourceHash="059461812d10f91e8c3fed05f6142606346656bf1f311a0256564c679dde6d07";
    private static byte[] Resource(string name)
    {
        using var input=typeof(BootOptimizer).Assembly.GetManifestResourceStream(name)??throw new IOException("Boot Optimizer was not packaged.");
        using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
    }
    public static void Stage(string mount,string[] apps)
    {
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS\BootOptimizer");Policy.RequireUnlinkedPath(folder);
        if(Directory.Exists(folder))throw new IOException("The source already contains Builder Boot Optimizer state.");
        var source=Resource("TechLoungeOS.OptimizeBootSource.ps1");
        if(!Convert.ToHexString(SHA256.HashData(source)).Equals(SourceHash,StringComparison.OrdinalIgnoreCase))throw new IOException("The reviewed Boot Optimizer payload changed.");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"Optimize-Boot.ps1"),source);
        File.WriteAllBytes(Path.Combine(folder,"worker.ps1"),Resource("TechLoungeOS.BootOptimizerWorker.ps1"));
        File.WriteAllBytes(Path.Combine(folder,"app-readiness.ps1"),Resource("TechLoungeOS.PostAppReadiness.ps1"));
        File.WriteAllText(Path.Combine(folder,"driver-lock.ps1"),GraphicsQueue.LockScript);
        File.WriteAllText(Path.Combine(folder,"apps.json"),JsonSerializer.Serialize(RecommendedApps.NormalizeIds(apps)));
        File.WriteAllText(Path.Combine(folder,"queue.ps1"),Queue);
        SetupHooks.Prefix(mount,"@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"%ProgramData%\\TechLoungeOS\\BootOptimizer\\queue.ps1\"\r\n");
    }
    public static string Queue=>FixedSetupTask.Queue("TechLoungeOS-BootOptimizer");

    public static string FirstLogin(string[] apps)=>"""
          Invoke-Step 'boot-optimizer' {
        """+"\n            $bootApps=ConvertFrom-Json "+Policy.Ps(JsonSerializer.Serialize(RecommendedApps.NormalizeIds(apps)))+"\n"+"""
            foreach($id in @($bootApps)){if(!(Test-Path -LiteralPath (Join-Path $state ($id+'.done')))){throw ('Boot Optimizer is waiting for the selected app: '+$id)}}
            if(!(Test-Path -LiteralPath "$env:ProgramData\TechLoungeOS\MachineSetup\complete")){throw 'Boot Optimizer is waiting for required machine installers, including Office.'}
            $bootRoot=Join-Path $env:ProgramData 'TechLoungeOS\BootOptimizer'
            if(!(Test-Path "$bootRoot\queue-ready")){throw 'Boot Optimizer setup was skipped. An administrator can retry its queue.ps1.'}
            $bootSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            $bootResult=Join-Path $bootRoot ('state\'+$bootSid)
            $bootRequested=[DateTime]::UtcNow
            [IO.File]::WriteAllText((Join-Path $state 'post-app-BootOptimizer.request'),$bootSid,[Text.UTF8Encoding]::new($false))
            Start-ScheduledTask -TaskName 'TechLoungeOS-BootOptimizer' -ErrorAction Stop
            $bootTimer=[Diagnostics.Stopwatch]::StartNew()
            $nextBootRequest=5
            while(!(Test-Path "$bootResult\complete") -and $bootTimer.Elapsed.TotalSeconds -lt 300){
              if((Test-Path "$bootResult\failed") -and (Get-Item "$bootResult\failed").LastWriteTimeUtc -ge $bootRequested){break}
              if($bootTimer.Elapsed.TotalSeconds -ge $nextBootRequest){
                if((Get-ScheduledTask -TaskName 'TechLoungeOS-BootOptimizer' -ErrorAction Stop).State -eq 'Ready'){Start-ScheduledTask -TaskName 'TechLoungeOS-BootOptimizer' -ErrorAction Stop}
                $nextBootRequest=$bootTimer.Elapsed.TotalSeconds+5
              }
              Start-Sleep -Milliseconds 500
            }
            if(!(Test-Path "$bootResult\complete")){throw ('Boot Optimizer needs attention. Review '+$bootResult+'\status.txt. It retries after apps complete at the next sign-in.')}
            $undoFolder=Join-Path $env:LOCALAPPDATA 'TechLoungeTweaks'
            if(!(Test-Path "$undoFolder\boot-rollback.json") -and (Test-Path "$bootResult\rollback.json")){
              New-Item $undoFolder -ItemType Directory -Force|Out-Null
              Copy-Item "$bootResult\rollback.json" "$undoFolder\boot-rollback.json"
            }
          }
        """;
}
