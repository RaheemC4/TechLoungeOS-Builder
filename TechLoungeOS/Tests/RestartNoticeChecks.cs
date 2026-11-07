using System.Text.Json;
using TechLoungeOS;

internal static class RestartNoticeChecks
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var generated=Provisioning.Script([],false,false);
        foreach(var (name,marker,bootFails,expected) in new[]{
            ("missing",(string?)null,false,"complete"),
            ("already-restarted","2026-11-04T21:56:00Z",false,"complete"),
            ("current-boot","2026-11-06T13:00:00Z",false,"restart-needed"),
            ("unknown-boot","2026-11-04T21:56:00Z",true,"restart-needed")})
        {
            var folder=Path.Combine(root,"restart-notice-"+name);Directory.CreateDirectory(folder);
            var common=Path.Combine(folder,"common");var local=Path.Combine(folder,"local");
            var markerPath=Path.Combine(common,@"TechLoungeOS\MachineSetup\restart-required");
            if(marker is not null){Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);File.WriteAllText(markerPath,"Inert installer receipt");File.SetLastWriteTimeUtc(markerPath,DateTimeOffset.Parse(marker).UtcDateTime);}
            var preamble="""
                $ErrorActionPreference='Stop'
                function Get-CimInstance {
                  param([string]$ClassName,[object]$ErrorAction)
                  if($ClassName -ne 'Win32_OperatingSystem'){throw 'Unexpected host query in inert notice fixture'}
                """+(bootFails?"throw 'Fixture boot-time lookup unavailable'":"[pscustomobject]@{LastBootUpTime=([datetime]'2026-11-06T11:17:00Z').ToUniversalTime()}")+"\n}\n"+
                "$env:LOCALAPPDATA="+Policy.Ps(local)+"\n$env:ProgramData="+Policy.Ps(common)+"\n";
            var script=Path.Combine(folder,"provision.ps1");File.WriteAllText(script,preamble+generated);
            var run=new ProcessRunner(_=>{});
            await run.PowerShell("& "+Policy.Ps(script));
            var state=Path.Combine(local,"TechLoungeOS");
            using var progress=JsonDocument.Parse(File.ReadAllText(Path.Combine(state,"progress.json")));
            check(File.Exists(Path.Combine(state,"complete"))&&progress.RootElement.GetProperty("phase").GetString()==expected,
                "inert generated first-login flow reports "+expected+" for "+name+" without installing apps or changing host settings");
        }
    }
}
