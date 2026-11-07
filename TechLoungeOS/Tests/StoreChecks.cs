using System.Text.Json;
using TechLoungeOS;

static class StoreChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var unavailable = new UnavailableStore();
        try { await StoreXbox.Prepare(unavailable,Path.Combine(root,"store-acquisition"),null,CancellationToken.None,download:(_,_,_,_,_)=>Task.FromException(new IOException("Fixture: official download unavailable"))); throw new Exception("Expected acquisition failure"); }
        catch(IOException ex) { check(ex.Message.Contains("No sign-in or licensing bypass") && unavailable.Arguments.Contains("msstore") && unavailable.Arguments.Contains("silent"),"Store acquisition failure remains actionable and uses silent official-source download"); }
        var retained=Path.Combine(root,"store-retained"); var noDownload = new UnavailableStore();
        await StoreXbox.Prepare(noDownload,retained,null,CancellationToken.None,StoreXbox.Products.Select(p=>p.Identity).ToArray());
        var plan=JsonSerializer.Deserialize<StoreXbox.Package[]>(File.ReadAllText(Path.Combine(retained,"packages.json")))!;
        check(noDownload.Arguments.Length==0 && plan.Length==6 && plan.All(p=>p.Main==""),"an image already provisioning Store/Xbox requires no replacement package downloads");
        var payload=Path.Combine(root,"store-fixture");Directory.CreateDirectory(payload);
        var entries=new List<StoreXbox.Package>();
        foreach(var (_,identity) in StoreXbox.Products)
        {
            var file=identity+".appx";File.WriteAllText(Path.Combine(payload,file),"fixture; never deployed");
            entries.Add(new(identity,file,[],new(){{file,RuntimePack.Hash(Path.Combine(payload,file))}}));
        }
        File.WriteAllText(Path.Combine(payload,"packages.json"),JsonSerializer.Serialize(entries));
        var source=StoreXbox.Script.Replace("if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Store/Xbox provisioning requires administrator permission.' }","# fixture-only elevation substitution");
        source=source.Replace(StoreXbox.Deployment,$$$"""
            [void][Windows.Management.Deployment.DeploymentOptions,Windows.Management.Deployment,ContentType=WindowsRuntime]
            function Wait-StoreDeployment($operation) {}
            $manager=New-Object PSObject
            $manager|Add-Member -MemberType ScriptMethod -Name StagePackageAsync -Value {
              param($main,$dependencies,$options)
              Add-Content {{{Policy.Ps(Path.Combine(payload,"staged.txt"))}}} $main.LocalPath
              [pscustomobject]@{Kind='stage'}
            }
            $manager|Add-Member -MemberType ScriptMethod -Name ProvisionPackageForAllUsersAsync -Value {
              param($family)
              Add-Content {{{Policy.Ps(Path.Combine(payload,"installed.txt"))}}} ($family.Split('_')[0])
              [pscustomobject]@{Kind='provision'}
            }
            """);
        source=source.Replace("throw 'Store/Xbox payload hash mismatch.'", "throw ('Store/Xbox fixture mismatch: '+$hash.Name+' expected='+$hash.Value+' actual='+(Get-FileHash -LiteralPath (Join-Path $payload $hash.Name) -Algorithm SHA256).Hash)");
        var path=Path.Combine(payload,"install.ps1");File.WriteAllText(path,source);
        var header=$$"""
            function Get-Service { param($Name,$ErrorAction) [pscustomobject]@{Name=$Name;StartType='Disabled'} }
            function Set-Service { param($Name,$StartupType) Add-Content {{Policy.Ps(Path.Combine(payload,"services.txt"))}} ($Name+'='+$StartupType) }
            function Test-Path { [CmdletBinding()]param([Parameter(Position=0)]$Path,$LiteralPath,$PathType) $target=if($LiteralPath){$LiteralPath}else{$Path}; if($target -like 'HKLM:*'){return $true}; Microsoft.PowerShell.Management\Test-Path @PSBoundParameters }
            function Get-ItemProperty { [pscustomobject]@{RemoveWindowsStore=1;DisableStoreApps=1} }
            function Remove-ItemProperty { param($Path,$Name,$ErrorAction) Add-Content {{Policy.Ps(Path.Combine(payload,"policy.txt"))}} $Name }
            function Get-AppxProvisionedPackage { param([switch]$Online) if(Test-Path {{Policy.Ps(Path.Combine(payload,"installed.txt"))}}){Get-Content {{Policy.Ps(Path.Combine(payload,"installed.txt"))}}|ForEach-Object{[pscustomobject]@{DisplayName=$_ } } } }
            & {{Policy.Ps(path)}}
            """;
        var run=new ProcessRunner(_=>{});await run.PowerShell(header);
        check(File.ReadAllLines(Path.Combine(payload,"installed.txt")).Order().SequenceEqual(StoreXbox.Products.Select(p=>p.Identity).Order()) && File.Exists(Path.Combine(payload,"packages-ready")) && File.Exists(Path.Combine(payload,"complete")),"Store setup provisions all six selected machine packages before declaring completion");
        var services=File.ReadAllLines(Path.Combine(payload,"services.txt"));
        check(services.Contains("wuauserv=Manual") && services.Contains("StateRepository=Automatic") && services.Contains("GamingServices=Automatic") && services.Contains("XblAuthManager=Manual") && File.ReadAllLines(Path.Combine(payload,"policy.txt")).SequenceEqual(new[]{"RemoveWindowsStore","DisableStoreApps"}),"Store setup enables the intended dependency services and removes only the two selected Store block values");
        File.Delete(Path.Combine(payload,"complete"));await run.PowerShell(header);
        check(File.ReadAllLines(Path.Combine(payload,"installed.txt")).Length==6,"Store setup retains existing provisioned packages on recovery rather than reinstalling them");
        File.Delete(Path.Combine(payload,"complete"));
        var deferred=header.Replace("param($Name,$ErrorAction) [pscustomobject]@{Name=$Name;StartType='Disabled'}","param($Name,$ErrorAction) if($Name -in @('GamingServices','GamingServicesNet')){return};[pscustomobject]@{Name=$Name;StartType='Disabled'}");
        await run.PowerShell(deferred);
        check(File.Exists(Path.Combine(payload,"complete")) && File.ReadAllText(Path.Combine(payload,"status.txt")).Contains("verified at first login"),"SYSTEM provisioning defers package-owned Gaming Services until first-user registration instead of failing Setup");
        File.Delete(Path.Combine(payload,"complete"));
        var broken=header.Replace("param($Name,$ErrorAction) [pscustomobject]@{Name=$Name;StartType='Disabled'}","param($Name,$ErrorAction) if($Name -eq 'XblAuthManager'){return};[pscustomobject]@{Name=$Name;StartType='Disabled'}");
        try{await run.PowerShell(broken);throw new Exception("Expected missing infrastructure refusal");}
        catch(IOException ex){check(ex.Message.Contains("Required Xbox infrastructure service is missing") && !File.Exists(Path.Combine(payload,"complete")),"missing base Xbox infrastructure remains a genuine failure and is not treated as deferred Gaming Services");}
    }

    private sealed class UnavailableStore : ICommandRunner
    {
        public string[] Arguments=[];
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default){Arguments=args.ToArray();throw new IOException("Synthetic official package acquisition unavailable");}
        public Task<string> PowerShell(string script,CancellationToken ct=default)=>throw new Exception("No host signature/deployment command should run after acquisition failure");
        public Task<string> Dism(params string[] args)=>throw new Exception("No DISM operation should run after acquisition failure");
    }
}
