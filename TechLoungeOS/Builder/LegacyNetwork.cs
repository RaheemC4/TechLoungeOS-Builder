namespace TechLoungeOS;

public static class LegacyNetwork
{
    public const string Script="$guest=$true;$smb1=$true;$client=$true;$server=$true\n"+Worker;
    public static async Task ValidateSource(ICommandRunner runner,string mount)
    {
        var result=await runner.PowerShell("# TechLoungeOS mandatory SMB1 source check\n$ErrorActionPreference='Stop';$f=Get-WindowsOptionalFeature -Path "+Policy.Ps(mount)+" -FeatureName SMB1Protocol-Client; [string]$f.State");
        if(result.Trim() is not ("Enabled" or "Disabled" or "EnablePending"))
            throw new IOException("Required SMB1 client payload is unavailable in this ISO ("+result.Trim()+"). A matching authorised Windows component source is needed. No incomplete ISO was exported and the original ISO is unchanged.");
    }
    public const string Verify="""

          Invoke-Step 'legacy-network' {
            if(!(Test-Path (Join-Path $env:ProgramData 'TechLoungeOS\legacy-network\complete'))) {
              throw 'Legacy NAS setup did not complete. An administrator can review ProgramData\TechLoungeOS\legacy-network\status.txt and legacy-network.ps1. No automatic elevation was attempted.'
            }
          }
        """;
    public const string Worker="""
        $ErrorActionPreference='Stop'
        $state=Join-Path $env:ProgramData 'TechLoungeOS\legacy-network'
        New-Item $state -ItemType Directory -Force|Out-Null
        $lock=[IO.File]::Open((Join-Path $state 'apply.lock'),'OpenOrCreate','ReadWrite','None')
        try {
          if(Test-Path (Join-Path $state 'complete')) { exit 0 }
          $before=Join-Path $state 'before.xml'
          if(!(Test-Path $before)) {
            $prior=@{}
            if($guest -or $client){$prior.client=Get-SmbClientConfiguration}
            if($server){$prior.server=Get-SmbServerConfiguration}
            if($smb1){$prior.smb1=Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client;$prior.smb1Server=Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Server}
            $prior|Export-Clixml -LiteralPath ($before+'.partial')
            Move-Item -LiteralPath ($before+'.partial') -Destination $before -ErrorAction Stop
          }
          if($guest -or $client) {
            $clientOptions=@{RequireSecuritySignature=$false;Force=$true}
            if($guest){
              $clientOptions.EnableInsecureGuestLogons=$true
              if((Get-SmbClientConfiguration).PSObject.Properties.Name -contains 'RequireEncryption'){$clientOptions.RequireEncryption=$false}
            }
            Set-SmbClientConfiguration @clientOptions
            $read=Get-SmbClientConfiguration
            if($read.RequireSecuritySignature -or ($guest -and (!$read.EnableInsecureGuestLogons -or $read.RequireEncryption))){throw 'SMB client readback failed; check enforced organisation policy.'}
          }
          if($server){
            Set-SmbServerConfiguration -RequireSecuritySignature $false -Force
            if((Get-SmbServerConfiguration).RequireSecuritySignature){throw 'SMB server readback failed; check enforced organisation policy.'}
          }
          $restart=$false
          if($smb1){
            $original=Import-Clixml -LiteralPath $before
            $serverBefore=[string]$original.smb1Server.State
            if($serverBefore -notin @('Enabled','EnablePending','Disabled','DisablePending','DisabledWithPayloadRemoved')){throw 'The original SMB1 server state was not recorded; administrator review is needed.'}
            $feature=Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client
            $restart=$feature.State -eq 'EnablePending'
            try { if($feature.State -notin @('Enabled','EnablePending')){
              $result=Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client -All -LimitAccess -NoRestart
              $restart=[bool]$result.RestartNeeded
            } } finally {
            $serverAfter=[string](Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Server).State
            if($serverBefore -in @('Disabled','DisabledWithPayloadRemoved') -and $serverAfter -in @('Enabled','EnablePending')) {
              $restore=@{Online=$true;FeatureName='SMB1Protocol-Server';NoRestart=$true}
              if($serverBefore -eq 'DisabledWithPayloadRemoved'){$restore.Remove=$true}
              $disabled=Disable-WindowsOptionalFeature @restore
              $restart=$restart -or [bool]$disabled.RestartNeeded
              if((Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Server).State -notin @('Disabled','DisablePending','DisabledWithPayloadRemoved')){throw 'The source SMB1 server state could not be preserved.'}
            }
            }
            if((Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client).State -notin @('Enabled','EnablePending')){throw 'SMB1 client payload is unavailable in this source image; no other stripped components were restored.'}
          }
          Set-Content (Join-Path $state 'status.txt') ('Selected legacy network settings verified. Restart needed: '+$restart)
          if($restart){Set-Content (Join-Path $state 'restart-required') 'Normal restart needed; none was forced.'}
          Set-Content (Join-Path $state 'complete') 'Verified'
        } catch {
          Set-Content (Join-Path $state 'status.txt') ('Legacy network setup needs review: '+$_.Exception.Message)
          throw
        } finally { $lock.Dispose() }
        """;
    public static DesktopSetup.Step Stage(string mount)
    {
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS");Policy.RequireUnlinkedPath(folder);Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"legacy-network.ps1"),Script);
        var file=Path.Combine(folder,"legacy-network.ps1");
        return new("LegacyNetwork",[new("Common",@"TechLoungeOS\legacy-network.ps1","install.ps1",new FileInfo(file).Length,RuntimePack.Hash(file))]);
    }
}
