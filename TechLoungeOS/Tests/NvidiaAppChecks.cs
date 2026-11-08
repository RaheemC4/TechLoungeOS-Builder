using System.Text.Json;
using TechLoungeOS;

static class NvidiaAppChecks
{
    internal static async Task Run(string folder,Action<bool,string> check)
    {
        Directory.CreateDirectory(folder);
        var script=Path.Combine(folder,"app-worker.ps1");File.WriteAllText(script,NvidiaSetup.Script);
        var prefix=". "+Policy.Ps(script)+" -Library\n";
        var run=new ProcessRunner(_=>{});
        const string url="https://us.download.nvidia.com/nvapp/client/11.0.9.585/x64/NVIDIA_app_v11.0.9.585_x64.exe";
        var mockPage="function Invoke-WebRequest {param($Uri,[switch]$UseBasicParsing,$TimeoutSec) [pscustomobject]@{Content="+
            Policy.Ps("<a href=\""+url+"\">Windows</a><a href=\""+url.Replace("x64","arm64")+"\">ARM</a>")+"} }\n";
        var resolved=await run.PowerShell(prefix+mockPage+"Get-NvidiaAppRelease|ConvertTo-Json -Compress");
        using(var parsed=JsonDocument.Parse(resolved))check(parsed.RootElement.GetProperty("Version").GetString()=="11.0.9.585"&&
            parsed.RootElement.GetProperty("DownloadURL").GetString()==url,"NVIDIA App resolves the unique x64 version from the vendor page and excludes its ARM installer");
        try{await run.PowerShell(prefix+"function Invoke-WebRequest {[pscustomobject]@{Content='https://nvidia.com.attacker.test/app.exe'}};Get-NvidiaAppRelease");throw new Exception("Expected untrusted page rejection");}
        catch(IOException error)when(error.Message.Contains("unambiguous")){check(true,"changed or unrecognised NVIDIA App links do not become an invented installer");}
        var state=Path.Combine(folder,"state");Directory.CreateDirectory(state);
        var model=$$$$"""
            $script:state={{{{Policy.Ps(state)}}}};$script:calls=[Collections.Generic.List[string]]::new();$script:version=$null;$script:result=0;$script:installVisible=$true
            function Get-NvidiaAppRelease {[pscustomobject]@{Version='11.0.9.585';DownloadURL='{{{{url}}}}'}}
            function Get-NvidiaAppVersion {if($script:version){[version]$script:version}}
            function Assert-NvidiaSpace {}
            function Assert-NvidiaSignature {param($Path)$script:calls.Add('signature')}
            function Invoke-WebRequest {param($Uri,[switch]$UseBasicParsing,$MaximumRedirection,$TimeoutSec,$OutFile)
              if($Uri -ne '{{{{url}}}}' -or $MaximumRedirection -ne 0){throw 'Wrong vendor download boundary'}
              $script:calls.Add('download');Set-Content -LiteralPath $OutFile 'inert fixture installer; never executed'
            }
            function Invoke-NvidiaProcess {param($Path,$Arguments)
              if($Arguments -ne '-s -noreboot'){throw 'Interactive or forced-restart App arguments'}
              $script:calls.Add('install');if($script:installVisible){$script:version='11.0.9.585'};$script:result
            }
            function Get-CimInstance {param($ClassName)[pscustomobject]@{LastBootUpTime=[datetime]'2026-11-08T00:00:00Z'}}
            """;
        var install=await run.PowerShell(prefix+model+"\nEnsure-NvidiaApp $script:state;ConvertTo-Json -InputObject @($script:calls) -Compress");
        check((JsonSerializer.Deserialize<string[]>(install.Trim())??[]).SequenceEqual(new[]{"download","signature","install"})&&File.Exists(Path.Combine(state,"app-receipt.json")),
            "NVIDIA App downloads from the resolved vendor URL, verifies its signature, installs silently and records verified version readback");
        var retain=await run.PowerShell(prefix+model+"\n$script:version='12.0.0.0';Ensure-NvidiaApp $script:state;$script:calls.Count");
        check(retain.Trim()=="0","a newer verified NVIDIA App is retained without a download or downgrade");
        File.Delete(Path.Combine(state,"app-receipt.json"));
        try{await run.PowerShell(prefix+model+"\n$script:installVisible=$false;Ensure-NvidiaApp $script:state");throw new Exception("Expected missing installed App rejection");}
        catch(IOException error)when(error.Message.Contains("not installed")){check(!File.Exists(Path.Combine(state,"app-receipt.json")),"installer exit success cannot masquerade as a detectable signed NVIDIA App/version");}
        try{await run.PowerShell(prefix+model+"\n$script:result=1;Ensure-NvidiaApp $script:state");throw new Exception("Expected normal-restart deferral");}
        catch(IOException error)when(error.Message.Contains("normal Windows restart")){check(File.Exists(Path.Combine(state,"restart-boot.txt"))&&!File.Exists(Path.Combine(state,"app-receipt.json")),
            "NVIDIA App restart requirements defer final profile setup without restarting Windows or claiming App completion");}
    }
}
