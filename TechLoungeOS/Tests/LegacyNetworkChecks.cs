using System.Text.Json;
using TechLoungeOS;

static class LegacyNetworkChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var mount=Path.Combine(root,"legacy-mandatory");var step=LegacyNetwork.Stage(mount);
        check(File.ReadAllText(Path.Combine(mount,@"ProgramData\TechLoungeOS\legacy-network.ps1")).StartsWith("$guest=$true;$smb1=$true;$client=$true;$server=$true"),"every generated ISO stages all mandatory SMB compatibility settings without selectable flags");
        foreach(var value in new[]{"Enabled","Disabled","EnablePending","Enable Pending","Disabled with Payload Removed","DisabledWithPayloadRemoved","Unknown",""}) {
            var accepted=true;try{await LegacyNetwork.ValidateSource(new Source(value),mount);}catch(IOException){accepted=false;}
            check(accepted==(value is "Enabled" or "Disabled" or "EnablePending" or "Enable Pending"),"mandatory SMB1 source preflight accepts only retained payload states: "+value);
        }
        foreach(var scenario in new[]{"guest","client","server","smb1","missing","policy","all"}) {
            var folder=Path.Combine(root,"legacy-"+scenario);Directory.CreateDirectory(folder);
            string B(bool value)=>value?"$true":"$false";
            var flags="$guest="+B(scenario is "guest" or "policy" or "all")+";$client="+B(scenario is "client" or "all")+";$server="+B(scenario is "server" or "all")+";$smb1="+B(scenario is "smb1" or "missing" or "all")+";";
            var script="$env:ProgramData="+Policy.Ps(folder)+";$scenario="+Policy.Ps(scenario)+";"+"""
                $script:clientConfig=[pscustomobject]@{RequireSecuritySignature=$true;EnableInsecureGuestLogons=$false;RequireEncryption=$true}
                $script:serverConfig=[pscustomobject]@{RequireSecuritySignature=$true}
                $script:feature='Disabled';$script:serverFeature='Disabled';$script:featureNames=@();$script:clientWrites=0;$script:serverWrites=0
                function Get-SmbClientConfiguration {$script:clientConfig}
                function Get-SmbServerConfiguration {$script:serverConfig}
                function Set-SmbClientConfiguration {param($RequireSecuritySignature,$EnableInsecureGuestLogons,$RequireEncryption,[switch]$Force)
                  $script:clientWrites++
                  if($scenario -eq 'policy'){return}
                  foreach($name in @('RequireSecuritySignature','EnableInsecureGuestLogons','RequireEncryption')){if($PSBoundParameters.ContainsKey($name)){$script:clientConfig.$name=$PSBoundParameters[$name]}}
                }
                function Set-SmbServerConfiguration {param($RequireSecuritySignature,[switch]$Force) $script:serverWrites++;$script:serverConfig.RequireSecuritySignature=$RequireSecuritySignature}
                function Get-WindowsOptionalFeature {param([switch]$Online,$FeatureName) [pscustomobject]@{State=$(if($FeatureName -eq 'SMB1Protocol-Server'){$script:serverFeature}else{$script:feature})}}
                function Disable-WindowsOptionalFeature {param([switch]$Online,$FeatureName,[switch]$NoRestart,[switch]$Remove)
                  if($FeatureName -ne 'SMB1Protocol-Server' -or !$NoRestart){throw 'Unexpected feature disable'}
                  $script:serverFeature='Disabled';[pscustomobject]@{RestartNeeded=$true}
                }
                function Enable-WindowsOptionalFeature {param([switch]$Online,$FeatureName,[switch]$All,[switch]$LimitAccess,[switch]$NoRestart)
                  if(!$LimitAccess -or !$NoRestart){throw 'Unexpected download/reboot'}
                  $script:featureNames+= $FeatureName
                  if($scenario -eq 'missing'){throw 'Source payload unavailable'}
                  $script:feature='EnablePending';$script:serverFeature='Enabled';[pscustomobject]@{RestartNeeded=$true}
                }
                """+"\ntry { & {"+flags+LegacyNetwork.Worker+"\n} } catch { $failure=$_.Exception.Message };"+"""
                @{failed=[bool]$failure;client=$script:clientConfig;server=$script:serverConfig;serverFeature=$script:serverFeature;clientWrites=$script:clientWrites;serverWrites=$script:serverWrites;features=$script:featureNames;complete=(Test-Path (Join-Path $env:ProgramData 'TechLoungeOS\legacy-network\complete'));backup=(Test-Path (Join-Path $env:ProgramData 'TechLoungeOS\legacy-network\before.xml'))}|ConvertTo-Json -Depth 4
                """;
            var text=await new ProcessRunner(_=>{}).PowerShell(script);using var json=JsonDocument.Parse(text);var r=json.RootElement;
            var failed=scenario is "missing" or "policy";
            check(r.GetProperty("failed").GetBoolean()==failed&&r.GetProperty("complete").GetBoolean()!=failed&&r.GetProperty("backup").GetBoolean(),"legacy network "+scenario+" preserves a backup, requires readback and never reports failed configuration complete");
            check(r.GetProperty("serverWrites").GetInt32()==(scenario is "server" or "all"?1:0)&&r.GetProperty("clientWrites").GetInt32()==(scenario is "guest" or "client" or "policy" or "all"?1:0),"legacy network "+scenario+" applies expected client/server configuration");
            check(r.GetProperty("features").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(scenario is "smb1" or "missing" or "all"?new[]{"SMB1Protocol-Client"}:Array.Empty<string>()),"legacy network "+scenario+" never enables SMB1 server, fetches stripped features or forces a reboot");
            check(r.GetProperty("serverFeature").GetString()=="Disabled","legacy network "+scenario+" retains the disabled source SMB1 server even if parent feature activation enables it");
            if(scenario=="guest")check(!r.GetProperty("client").GetProperty("RequireSecuritySignature").GetBoolean()&&!r.GetProperty("client").GetProperty("RequireEncryption").GetBoolean()&&r.GetProperty("client").GetProperty("EnableInsecureGuestLogons").GetBoolean(),"guest option handles signing/encryption requirements rather than setting an ineffective guest-only flag");
        }
        check(File.Exists(Path.Combine(mount,@"ProgramData\TechLoungeOS\legacy-network.ps1"))&&step.Id=="LegacyNetwork" && step.Files.Single().Destination=="install.ps1" && !File.Exists(SetupHooks.ImageHook(mount)),"mandatory legacy settings are pinned for deferred setup without pre-desktop execution");
    }
    private sealed class Source(string value):ICommandRunner {
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)=>throw new Exception("No target execution in preflight");
        public Task<string> PowerShell(string script,CancellationToken ct=default)=>throw new Exception("The offline check must use native DISM, not the failing PowerShell API");
        public Task<string> Dism(params string[] args){if(!args.Any(a=>a.StartsWith("/Image:"))||!args.Contains("/Get-FeatureInfo")||!args.Contains("/FeatureName:SMB1Protocol-Client")||args.Any(a=>a.StartsWith("/Enable")))throw new Exception("Expected only native read-only source inventory");return Task.FromResult("Feature Name : SMB1Protocol-Client\r\nState : "+value+"\r\nThe operation completed successfully.");}
    }
}
