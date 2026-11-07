using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TechLoungeOS;

internal static class WindowsFeatureUpdateChecks
{
    private static readonly Version Source=new(10,0,26200,9457),Baseline=new(10,0,26200,9725),Target=new(10,0,26300,9725);
    private static readonly WindowsReleaseCatalog.Release BaseRelease=new("25H2",Baseline,new(2026,10,26),"2026-10 OOB","KB5134133");
    private static readonly WindowsReleaseCatalog.Release TargetRelease=new("26H2",Target,new(2026,10,26),"2026-10 OOB","KB5134133");
    private static readonly WindowsReleaseCatalog.Release Available=new("26H2",new(10,0,26300,9457),new(2026,9,29),"GA","");
    private const string CuFile="windows11.0-kb5134133-x64_0e59d2f988a5422dfb489e97faed3b3835fc7568.msu";
    private static readonly byte[] CuBytes=Encoding.UTF8.GetBytes("Inert cumulative fixture, never sent to DISM.");
    public static async Task Run(string root,string signedPackage,Action<bool,string> check)
    {
        var metadata=System.Xml.Linq.XElement.Parse("<IMAGE INDEX='1'><NAME>Ghost custom name</NAME><FILECOUNT>123</FILECOUNT><WINDOWS><ARCH>9</ARCH><EDITIONID>IoTEnterprise</EDITIONID><VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26200</BUILD><SPBUILD>9457</SPBUILD><SPLEVEL>0</SPLEVEL></VERSION></WINDOWS></IMAGE>");
        var refreshed=WimVersionMetadata.WithVersion(metadata,Source,Target);
        check((string?)refreshed.Element("WINDOWS")?.Element("VERSION")?.Element("BUILD")=="26300" &&
            (string?)refreshed.Element("WINDOWS")?.Element("VERSION")?.Element("SPBUILD")=="9725" &&
            (string?)refreshed.Element("NAME")=="Ghost custom name" && (string?)refreshed.Element("FILECOUNT")=="123" &&
            (string?)metadata.Element("WINDOWS")?.Element("VERSION")?.Element("BUILD")=="26200",
            "version metadata refresh retains custom identity/resource counts and leaves the original XML untouched");
        var rule=WindowsUpgradeRules.Get("win11-26h2-x64");var bytes=File.ReadAllBytes(signedPackage);
        check(Convert.ToHexString(SHA256.HashData(bytes))=="BF43EB13B7EBDE920C2201473BFD5C531B45E0F46ACD8E0843F27F9744FB4B43" && bytes.Length==177535,
            "downloaded enablement fixture is the exact independently Microsoft-signature-verified x64 package");
        var today=new DateOnly(2026,11,1);var offer=WindowsUpgradeRules.Find([BaseRelease,TargetRelease,Available],Source,today)!;
        check(offer is not null && offer.Baseline.Version==Baseline && offer.Target.Version==Target,"25H2 resolves to 26H2 only with a matching stable prerequisite revision");
        check(WindowsUpgradeRules.Find([BaseRelease,TargetRelease],Source,today)==null &&
            WindowsUpgradeRules.Find([BaseRelease,TargetRelease,Available],Source,new(2026,8,1))==null,"unreleased or missing general-availability targets are not offered");
        foreach(var source in new[]{new Version(10,0,22631,9457),new Version(10,0,28000,10000),new Version(10,0,26200,9818)})
            check(WindowsUpgradeRules.Find([BaseRelease,TargetRelease,Available],source,today)==null,"unrelated core versions and source previews newer than the stable target are not converted");
        var future=new WindowsReleaseCatalog.Release("27H2",new(10,0,29000,100),new(2026,10,30),"GA","");
        var report=WindowsReleaseCatalog.Summarise([BaseRelease,TargetRelease,Available,future],Source,new(2026,11,1,0,0,0,TimeSpan.Zero));
        check(report.NewestFeature==future && report.Upgrade?.Target.Name=="26H2","future releases are detected but cannot silently create an unreviewed upgrade route");
        foreach(var edition in new[]{"EnterpriseS","IoTEnterpriseS","ServerStandard","unknown"})
        {try{WindowsUpgradeRules.RequireEdition(rule.Id,edition);throw new Exception("Unsupported edition accepted");}catch(IOException){check(true,"LTSC, server and unknown editions are not silently converted: "+edition);}}
        var feature=new WindowsImageUpdates.Payload(rule.File,rule.Bytes,rule.Sha256);
        var cumulative=new WindowsImageUpdates.Payload(CuFile,CuBytes.Length,Convert.ToHexString(SHA256.HashData(CuBytes)));
        var plan=new WindowsImageUpdates.Plan(2,Source.ToString(),Target.ToString(),[cumulative,feature],new(rule.Id,Baseline.ToString()));
        foreach(var bad in new[]{plan with{Schema=1},plan with{Feature=null},plan with{Feature=new("unreviewed-27h2",Baseline.ToString())},
            plan with{TargetVersion="10.0.28000.9725"},plan with{Feature=new(rule.Id,"10.0.26200.9457")},plan with{Packages=[feature]},
            plan with{Packages=[cumulative,feature with{Sha256=new string('0',64)}]},plan with{Packages=[feature,cumulative]}})
        {try{WindowsImageUpdates.ValidatePlan(bad);throw new Exception("Unsafe feature plan accepted");}catch(IOException){check(true,"invalid route, prerequisite, order, schema or enablement identity fails before servicing");}}
        var sourceFolder=Path.Combine(root,"source packages");Directory.CreateDirectory(sourceFolder);
        File.WriteAllBytes(Path.Combine(sourceFolder,rule.File),bytes);File.WriteAllBytes(Path.Combine(sourceFolder,CuFile),CuBytes);
        foreach(var mode in new[]{"success","features","services","edition","release","source-pending","defender","system-app","wrong-baseline","wrong-target","health","source-health","cancel"})
        {
            var work=Path.Combine(root,mode);Directory.CreateDirectory(work);var runner=new Commands{Mode=mode};using var cancel=new CancellationTokenSource();
            var mount=Path.Combine(work,"mount");Directory.CreateDirectory(mount);
            if(mode=="cancel")runner.AfterAdd=cancel.Cancel;
            if(mode=="source-pending"){Directory.CreateDirectory(Path.Combine(mount,@"Windows\WinSxS"));File.WriteAllText(Path.Combine(mount,@"Windows\WinSxS\pending.xml"),"inherited pending actions fixture");}
            if(mode=="defender")runner.AfterAdd=()=>Directory.CreateDirectory(Path.Combine(mount,@"Program Files\Windows Defender"));
            if(mode=="system-app")runner.AfterAdd=()=>Directory.CreateDirectory(Path.Combine(mount,@"Windows\SystemApps\Restored.Bloat"));
            bool success=false;
            try
            {
                var actual=await WindowsImageUpdates.Apply(runner,mount,work,sourceFolder,plan,Source,_=>{},cancel.Token,
                    _=>new Dictionary<string,string>{{"existing-tweak",runner.Adds>0&&mode=="services"?"changed":"original"}},
                    _=>runner.Adds==0?Source:runner.Adds==1?(mode=="wrong-baseline"?Source:Baseline):(mode=="wrong-target"?Baseline:Target),_=>mode=="edition"&&runner.Adds>0?"Enterprise":"IoTEnterprise",_=>mode=="release"?"25H2":"26H2");
                success=actual==Target;
            }
            catch(Exception error) when(error is IOException or OperationCanceledException){ }
            using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(work,"windows-update","result.json")));
            check(success==(mode=="success") && evidence.RootElement.GetProperty("preservationPassed").GetBoolean()==(mode=="success"),
                "only exact-version updates retaining source settings and removed features pass: "+mode);
            if(mode=="success")check(runner.Paths.SequenceEqual(new[]{CuFile,rule.File}) && runner.Folders.SequenceEqual(new[]{"cumulative","enablement"}),"required cumulative package precedes enablement; there is no stock-image replacement or ignore-check command");
            if(mode=="source-pending")check(runner.Adds==0,"inherited pending actions cannot be folded into a new feature upgrade");
            if(mode=="source-health")check(runner.Adds==0 && !evidence.RootElement.GetProperty("packageOperationStarted").GetBoolean() &&
                File.ReadAllText(Path.Combine(work,"windows-update","before.json")).Contains("Kept.App_1.0_x64") &&
                File.ReadAllText(Path.Combine(work,"windows-update","before.json")).Contains("existing-tweak"),
                "a failed fresh source scan retains the source app/configuration baseline and applies no update");
            if(mode=="wrong-baseline" || mode=="cancel")check(runner.Adds==1,"prerequisite failure/cancellation prevents enablement");
        }
        var currentPlan=plan with{SourceVersion=Baseline.ToString(),Packages=[feature]};var readyWork=Path.Combine(root,"already current");Directory.CreateDirectory(readyWork);
        var readyRunner=new Commands();
        var readyVersion=await WindowsImageUpdates.Apply(readyRunner,Path.Combine(readyWork,"mount"),readyWork,sourceFolder,currentPlan,Baseline,_=>{},CancellationToken.None,
            _=>new Dictionary<string,string>(),_=>readyRunner.Adds==0?Baseline:Target,_=>"IoTEnterprise",_=>"26H2");
        check(readyVersion==Target && readyRunner.Adds==1 && readyRunner.Folders.SequenceEqual(new[]{"enablement"}),"an image already containing prerequisites receives only the small enablement package");
        var untouched=new Commands();
        try{await WindowsImageUpdates.Apply(untouched,"fixture mount",Path.Combine(root,"lt-sc"),sourceFolder,plan,Source,_=>{},CancellationToken.None,editionReader:_=>"IoTEnterpriseS");throw new Exception("LTSC serviced");}
        catch(IOException){check(untouched.Adds==0 && !Directory.Exists(Path.Combine(root,"lt-sc")),"ineligible offline edition is rejected before staging or any DISM command");}
        using(var client=new HttpClient(new Downloads(bytes)))
        {
            var folder=Path.Combine(root,"downloaded plan");var prepared=await WindowsFeatureUpdateDownload.Prepare(folder,Source,offer!,_=>{},CancellationToken.None,client);
            var loaded=WindowsImageUpdates.ReadPlan(folder);
            check(loaded.Schema==2 && loaded.SourceVersion=="10.0.26200.9457" && loaded.TargetVersion=="10.0.26300.9725" &&
                loaded.Feature?.RuleId=="win11-26h2-x64" && loaded.Feature.BaselineVersion=="10.0.26200.9725",
                "complete upgrade plan records the exact source, prerequisite, route and target after verified downloads");
            check(prepared.Packages.Select(p=>p.File).SequenceEqual(new[]{CuFile,rule.File}) && File.ReadAllBytes(Path.Combine(folder,rule.File)).SequenceEqual(bytes),"prepared bytes retain their package identity and intended servicing order");
        }
        foreach(var mode in new[]{"corrupt","redirect","oversize"})
        {
            using var client=new HttpClient(new Downloads(bytes,mode));var folder=Path.Combine(root,"bad download "+mode);
            var current=new WindowsUpgradeRules.Offer(rule.Id,BaseRelease,TargetRelease);
            try{await WindowsFeatureUpdateDownload.Prepare(folder,Baseline,current,_=>{},CancellationToken.None,client);throw new Exception("Unsafe download accepted");}
            catch(IOException){check(!File.Exists(Path.Combine(folder,"update-plan.json")),"unsafe enablement transfer cannot publish a usable plan: "+mode);}
        }
    }
    private sealed class Commands:ICommandRunner
    {
        public string Mode="";public int Adds;public List<string> Paths=[];public List<string> Folders=[];public Action? AfterAdd;
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)=>throw new Exception("No executable is run in this fixture");
        public Task<string> PowerShell(string script,CancellationToken ct=default)
        {if(!script.Contains("Get-AuthenticodeSignature"))throw new Exception("Unexpected PowerShell fixture");return Task.FromResult("");}
        public Task<string> Dism(params string[] args)
        {
            if(args.Contains("/Add-Package"))
            {
                if(args.Any(a=>a.Contains("IgnoreCheck")||a.Contains("Online")||a.Contains("RestoreHealth")))throw new Exception("Unsafe servicing command");
                Adds++;var package=args.Single(a=>a.StartsWith("/PackagePath:"))[13..];Paths.Add(Path.GetFileName(package));Folders.Add(Path.GetFileName(Path.GetDirectoryName(package))!);AfterAdd?.Invoke();return Task.FromResult("");
            }
            if(args.Contains("/ScanHealth"))return Task.FromResult((Mode=="source-health" || Mode=="health"&&Adds>0)?"The component store is repairable.":"No component store corruption detected.");
            if(args.Contains("/Get-ProvisionedAppxPackages"))return Task.FromResult("DisplayName : Kept.App\nPackageName : Kept.App_1.0_x64\nThe operation completed successfully.");
            if(args.Contains("/Get-Features"))return Task.FromResult("Feature Name : RemovedFeature\nState : Disabled with Payload Removed\n"+(Mode=="features"&&Adds>0?"Feature Name : RestoredDefender\nState : Enabled\n":"")+"The operation completed successfully.");
            throw new Exception("Unexpected DISM fixture");
        }
    }
    private sealed class Downloads(byte[] enablement,string mode=""):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            const string id="62c21efa-416e-430c-84ee-bba3c8d583c2";var uri=request.RequestUri!;HttpContent content;
            if(uri.Host=="www.catalog.update.microsoft.com")
                content=new StringContent(uri.AbsolutePath=="/Search.aspx"?$"<a id='{id}_link'>2026-10 Cumulative Update for Windows 11, version 25H2 for x64-based Systems (KB5134133) (26200.9725)</a>":
                    $"downloadInformation[0].updateID ='{id}';\ndownloadInformation[0].files[0].url = 'https://download.windowsupdate.com/test/{CuFile}';\ndownloadInformation[0].files[0].fileName = '{CuFile}';\ndownloadInformation[0].files[0].sha256 = '{Convert.ToBase64String(SHA256.HashData(CuBytes))}';");
            else if(uri.Host=="download.windowsupdate.com")content=new ByteArrayContent(CuBytes);
            else if(uri.Host=="catalog.sf.dl.delivery.mp.microsoft.com")
            {content=new ByteArrayContent(mode=="corrupt"?new byte[enablement.Length]:enablement);if(mode=="oversize")content.Headers.ContentLength=99999999;}
            else throw new Exception("Real network is prohibited in this fixture");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content,RequestMessage=mode=="redirect"?new HttpRequestMessage(HttpMethod.Get,"https://untrusted.invalid"):request});
        }
    }
}
