using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using TechLoungeOS;

static class EdgeChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        check(RecommendedApps.IncludedInIso("microsoft.edge") && RecommendedApps.IncludedInIso("Microsoft.Office")
            && !RecommendedApps.IncludedInIso("Microsoft.Edge.Beta") && !RecommendedApps.IncludedInIso("Microsoft.EdgeWebView2Runtime"),
            "only bundled Edge Stable and Office are marked included; alternative channels and WebView2 remain selectable");
        var merged=EdgePack.Associations("<DefaultAssociations><Association Identifier='HTTPS' ProgId='IE.HTTPS' ApplicationName='old'/><Association Identifier='https' ProgId='oldDuplicate'/><Association Identifier='.pdf' ProgId='CustomPDF'/><Association Identifier='.txt' ProgId='CustomText'/></DefaultAssociations>");
        check(merged.Root!.Elements("Association").Count()==6 && merged.Root.Elements("Association").Single(x=>(string?)x.Attribute("Identifier")=="https").Attribute("ProgId")!.Value=="MSEdgeHTM","Edge defaults replace case-insensitive duplicate browser entries only");
        check(merged.Root.Elements("Association").Single(x=>(string?)x.Attribute("Identifier")==".pdf").Attribute("ProgId")!.Value=="CustomPDF" && merged.Root.Elements("Association").Single(x=>(string?)x.Attribute("Identifier")==".txt").Attribute("ProgId")!.Value=="CustomText","unrelated PDF and text defaults are retained");
        check(EdgePack.Associations(null).Root!.Elements().Select(x=>(string)x.Attribute("Identifier")!).SequenceEqual(new[]{"http","https",".htm",".html"}),"new defaults cover just the four browser associations");
        foreach(var xml in new[]{"<Other/>","<!DOCTYPE DefaultAssociations [<!ENTITY x SYSTEM 'file:///C:/unread'>]><DefaultAssociations>&x;</DefaultAssociations>"})
        {
            bool rejected=false;try{EdgePack.Associations(xml);}catch(Exception ex)when(ex is XmlException or IOException){rejected=true;}
            check(rejected,"unsupported or entity-bearing source association XML is rejected");
        }
        var payload=Path.Combine(root,"edge-script");Directory.CreateDirectory(payload);
        var msi=Path.Combine(payload,"MicrosoftEdge.msi");File.WriteAllText(msi,"inert MSI fixture; not executed");
        File.WriteAllText(Path.Combine(payload,"installer.json"),JsonSerializer.Serialize(new EdgePack.Installer("156.0.4314.50",RuntimePack.Hash(msi))));
        var fakeProgramFiles=Path.Combine(payload,"program-files");
        var registryPath=@"HKEY_CURRENT_USER\Software\TechLoungeOS-Tests\Edge-"+Guid.NewGuid().ToString("N");
        var policyPath="HKCU:"+registryPath["HKEY_CURRENT_USER".Length..];
        var exe=Path.Combine(fakeProgramFiles,@"Microsoft\Edge\Application\msedge.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe)!);File.WriteAllText(exe,"inert browser fixture");
        var script=EdgePack.Script.Replace("if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Edge setup requires administrator permission.' }","# fixture-only privilege substitution")
            .Replace("${env:ProgramFiles(x86)}",Policy.Ps(fakeProgramFiles)).Replace("$env:ProgramFiles",Policy.Ps(fakeProgramFiles))
            .Replace("HKLM:\\SOFTWARE\\Policies\\Microsoft\\Edge",policyPath);
        var path=Path.Combine(payload,"install.ps1");File.WriteAllText(path,script);
        string Header(string version,int exit)=>$$"""
            $global:tlosEdgeFixtureVersion={{Policy.Ps(version)}}
            function Get-Item { param($LiteralPath,$Path) [pscustomobject]@{VersionInfo=[pscustomobject]@{ProductVersion=$global:tlosEdgeFixtureVersion} } }
            function Get-AuthenticodeSignature { param($FilePath) [pscustomobject]@{Status='Valid';SignerCertificate=[pscustomobject]@{Subject='CN=Microsoft Corporation, O=Microsoft Corporation, C=US'} } }
            function Start-Process { param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru)
              Add-Content {{Policy.Ps(Path.Combine(payload,"calls.txt"))}} ($ArgumentList -join '|')
              if({{exit}} -eq 0){$global:tlosEdgeFixtureVersion='156.0.4314.50'}
              if($Wait){throw 'Do not wait for persistent updater descendants'}
              $p=[pscustomobject]@{ExitCode={{exit}} }; $p|Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {}; $p
            }
            & {{Policy.Ps(path)}}
            """;
        var run=new ProcessRunner(_=>{});
        try
        {
        await run.PowerShell(Header("155.0.1.0",0));
        check(Equals(Microsoft.Win32.Registry.GetValue(registryPath,"HideFirstRunExperience",null),1),"Edge setup creates the quiet first-run default before browser installation");
        Microsoft.Win32.Registry.SetValue(registryPath,"UnrelatedSourceChoice","keep");
        Microsoft.Win32.Registry.SetValue(registryPath,"HideFirstRunExperience",0);
        check(File.Exists(Path.Combine(payload,"complete")) && File.ReadAllText(Path.Combine(payload,"calls.txt")).Contains("/qn|/norestart"),"offline Edge installer is quiet, requests no restart and verifies the installed version");
        File.Delete(Path.Combine(payload,"calls.txt"));
        await run.PowerShell(Header("157.0.1.0",0));
        check(Equals(Microsoft.Win32.Registry.GetValue(registryPath,"HideFirstRunExperience",null),0)
            && Equals(Microsoft.Win32.Registry.GetValue(registryPath,"UnrelatedSourceChoice",null),"keep"),"Edge recovery retains explicit source policy and unrelated settings");
        check(!File.Exists(Path.Combine(payload,"calls.txt")),"a newer installed Edge is retained without reinstall or downgrade");
        bool failed=false;try{await run.PowerShell(Header("155.0.1.0",1603));}catch(IOException){failed=true;}
        check(failed && !File.Exists(Path.Combine(payload,"complete")) && File.ReadAllText(Path.Combine(payload,"status.txt")).Contains("1603"),"failed Edge recovery clears a stale success marker and retains an actionable error");
        var mount=Path.Combine(root,"edge-stage");
        var original=Path.Combine(mount,@"Windows\System32\OEMDefaultAssociations.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        var originalXml="<DefaultAssociations><Association Identifier='.pdf' ProgId='SourcePDF'/></DefaultAssociations>";
        File.WriteAllText(original,originalXml);
        var hook=SetupHooks.ImageHook(mount);Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        var hookBytes=System.Text.Encoding.ASCII.GetBytes("@echo inherited-hook\r\n");File.WriteAllBytes(hook,hookBytes);
        var commands=new StageCommands();
        var step=await EdgePack.Stage(commands,mount,payload,CancellationToken.None);
        check(commands.Arguments.SequenceEqual(new[]{"/Image:"+mount,"/Import-DefaultAppAssociations:"+Path.Combine(payload,"associations.xml")})
            && File.ReadAllText(Path.Combine(mount,@"ProgramData\TechLoungeOS\source-hooks\Edge-DefaultAssociations.xml"))==originalXml,
            "Edge staging imports merged defaults through DISM and preserves the exact original associations");
        var staged=Path.Combine(mount,"Windows","Setup","TechLoungeOS-Edge");
        check(File.Exists(Path.Combine(staged,"MicrosoftEdge.msi")) && File.ReadAllBytes(Path.Combine(staged,"source-SetupComplete.cmd")).SequenceEqual(hookBytes)
            && step.Id=="Edge" && step.Files.Any(f=>f.Destination=="MicrosoftEdge.msi") && File.ReadAllBytes(hook).SequenceEqual(hookBytes),
            "Edge staging pins its offline installer for the desktop queue and leaves the inherited hook unchanged");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath["HKEY_CURRENT_USER\\".Length..],false); }
    }

    private sealed class StageCommands : ICommandRunner
    {
        public string[] Arguments=[];
        public Task<string> Dism(params string[] args){Arguments=args;return Task.FromResult("");}
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)=>throw new Exception("Unexpected host operation");
        public Task<string> PowerShell(string script,CancellationToken ct=default)=>throw new Exception("Unexpected host operation");
    }
}
