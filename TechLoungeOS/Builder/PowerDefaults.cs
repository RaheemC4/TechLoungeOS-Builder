using System.Text.Json;

namespace TechLoungeOS;

public static class PowerDefaults
{
    private static string Resource(string name)
    {
        using var stream=typeof(PowerDefaults).Assembly.GetManifestResourceStream(name)??throw new IOException("Power defaults were not packaged.");
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public static string Worker=>Resource("TechLoungeOS.PowerDefaults.ps1");
    public static void ValidateSource(string mount)
    {
        if(!File.Exists(Path.Combine(mount,@"Windows\System32\powrprof.dll")))
            throw new IOException("The source's Windows power-management library is unavailable. Required idle-timeout defaults cannot be applied safely to this image.");
    }
    public static void Stage(string mount,string[] apps)
    {
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS\PowerDefaults");Policy.RequireUnlinkedPath(folder);
        if(Directory.Exists(folder))throw new IOException("The source already contains Builder power-default state.");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"worker.ps1"),Worker);
        File.WriteAllText(Path.Combine(folder,"initial.ps1"),"$ErrorActionPreference='Stop'\n& (Join-Path $PSScriptRoot 'worker.ps1') -Initial\n");
        File.WriteAllText(Path.Combine(folder,"app-readiness.ps1"),Resource("TechLoungeOS.PostAppReadiness.ps1"));
        File.WriteAllText(Path.Combine(folder,"apps.json"),JsonSerializer.Serialize(RecommendedApps.NormalizeIds(apps)));
        File.WriteAllText(Path.Combine(folder,"queue.ps1"),FixedSetupTask.Queue("TechLoungeOS-PowerDefaults"));
        SetupHooks.Prefix(mount,"@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%ProgramData%\\TechLoungeOS\\PowerDefaults\\queue.ps1\"\r\n"+
            "@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%ProgramData%\\TechLoungeOS\\PowerDefaults\\initial.ps1\"\r\n");
    }
    public const string FirstLogin="""
        Invoke-Step 'power-defaults' {
          $powerRoot=Join-Path $env:ProgramData 'TechLoungeOS\PowerDefaults'
          if(!(Test-Path -LiteralPath "$powerRoot\queue-ready")){throw 'Power defaults were not registered by Windows Setup. No elevation was requested.'}
          if(!(Test-Path -LiteralPath "$powerRoot\complete")){
            $requested=[DateTime]::UtcNow
            $powerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            [IO.File]::WriteAllText((Join-Path $state 'post-app-PowerDefaults.request'),$powerSid,[Text.UTF8Encoding]::new($false))
            Start-ScheduledTask -TaskName 'TechLoungeOS-PowerDefaults' -ErrorAction Stop
            $timer=[Diagnostics.Stopwatch]::StartNew()
            $nextPowerRequest=5
            while(!(Test-Path -LiteralPath "$powerRoot\complete") -and $timer.Elapsed.TotalSeconds -lt 60){
              if((Test-Path -LiteralPath "$powerRoot\failed") -and (Get-Item -LiteralPath "$powerRoot\failed").LastWriteTimeUtc -ge $requested){break}
              if($timer.Elapsed.TotalSeconds -ge $nextPowerRequest){
                if((Get-ScheduledTask -TaskName 'TechLoungeOS-PowerDefaults' -ErrorAction Stop).State -eq 'Ready'){Start-ScheduledTask -TaskName 'TechLoungeOS-PowerDefaults' -ErrorAction Stop}
                $nextPowerRequest=$timer.Elapsed.TotalSeconds+5
              }
              Start-Sleep -Milliseconds 250
            }
            if(!(Test-Path -LiteralPath "$powerRoot\complete")){throw 'Idle power defaults need attention. Review ProgramData\TechLoungeOS\PowerDefaults\failed and retry setup.'}
          }
        }
        """;
}
