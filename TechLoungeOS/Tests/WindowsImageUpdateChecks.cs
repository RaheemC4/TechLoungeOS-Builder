using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

static class WindowsImageUpdateChecks
{
    private static readonly Version Source = new(10,0,26200,9457), Target = new(10,0,26200,9725);
    public static async Task Run(string root, Action<bool,string> check)
    {
        var folder=Path.Combine(root,"update source O'Brien 测试");Directory.CreateDirectory(folder);
        var packages=new[]{1,2}.Select(n=>{
            var name="windows11.0-kb"+(n==1?"5043080":"5134133")+"-x64_"+new string((char)('a'+n),40)+".msu";
            var bytes=System.Text.Encoding.UTF8.GetBytes("INERT MSU fixture "+n);File.WriteAllBytes(Path.Combine(folder,name),bytes);
            return new WindowsImageUpdates.Payload(name,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
        }).ToArray();
        var plan=new WindowsImageUpdates.Plan(1,Source.ToString(),Target.ToString(),packages);
        var options=new BuildOptions("unused.iso",1,Path.Combine(root,"work"),"unused-output.iso","unused.exe",[],[],false,false,null);
        check(!options.UpdateWindows,"ordinary builds keep the source Windows version unless updating is explicitly requested");
        try{Policy.Validate(options with{UpdateWindows=true,WindowsUpdateFolder=folder});throw new Exception("Ambiguous Windows update sources accepted");}
        catch(InvalidOperationException error){check(error.Message.Contains("not both"),"automatic discovery and a prepared offline plan cannot be silently mixed");}
        File.WriteAllText(Path.Combine(folder,"update-plan.json"),JsonSerializer.Serialize(plan));
        check(WindowsImageUpdates.ReadPlan(folder).Packages.Length==2,"offline update plan retains checkpoint and target package order with Unicode source paths");
        foreach(var bad in new[]{
            plan with{Schema=2}, plan with{TargetVersion="10.0.26300.9725"},plan with{TargetVersion=Source.ToString()},
            plan with{TargetVersion="10.0.26200"},plan with{SourceVersion="10.0.19045.1"},
            plan with{Packages=[]},plan with{Packages=[packages[0],packages[0]]},
            plan with{Packages=[packages[0] with{File="..\\payload.msu"}]},
            plan with{Packages=[packages[0] with{File="windows11.0-kb5043080-arm64_"+new string('a',40)+".msu"}]},
            plan with{Packages=[packages[0] with{Bytes=0}]},plan with{Packages=[packages[0] with{Bytes=8589934593}]},
            plan with{Packages=[packages[0] with{Sha256="not a checksum"}]}})
        {
            try{WindowsImageUpdates.ValidatePlan(bad);throw new Exception("Unsafe update plan accepted");}
            catch(IOException){check(true,"invalid schema, version, architecture, identity, budget or checksum rejected before servicing");}
        }
        var runner=new UpdateRunner();var work=Path.Combine(root,"update success");Directory.CreateDirectory(work);
        bool locked=false;
        runner.OnAdd=()=>{
            try{File.WriteAllText(Path.Combine(work,"windows-update",packages[0].File),"must not alter pinned checkpoint");}
            catch(IOException){locked=true;}
        };
        WindowsImageUpdates.VerifiedHealthScan? reusableHealth=null;
        var actual=await Apply(work,runner,plan,onVerified:health=>reusableHealth=health);
        check(reusableHealth?.Take(Path.Combine(work,"mount"))=="No component store corruption detected.","only a successful update/preservation transaction hands its actual final full-scan result to the next servicing stage");
        check(actual==Target && runner.Signatures==2 && locked && ReadResult(work).GetProperty("preservationPassed").GetBoolean(),
            "verified payloads remain locked against mutation through update and readback, then an exact-version preservation pass is recorded");
        check(runner.AddArguments!.SequenceEqual(new[]{"/Image:"+Path.Combine(work,"mount"),"/Add-Package","/PackagePath:"+Path.Combine(work,"windows-update",packages[^1].File),"/PreventPending","/NoRestart"}),
            "DISM receives the final target in its isolated verified checkpoint folder with normal applicability checks and no forced restart");
        check(ReadDifferences(work).GetArrayLength()==0,"unchanged update readback records an empty preservation-difference report");
        File.WriteAllText(Path.Combine(work,"windows-update",packages[0].File),"lease released after verification");
        check(File.ReadAllText(Path.Combine(folder,packages[0].File))=="INERT MSU fixture 1","private update staging leaves original package bytes untouched");
        foreach(var mode in new[]{"checksum","signature","health-before","health-after","features","services","combined","wrong-version","pending","cancel"})
        {
            var failing=new UpdateRunner{Failure=mode};var destination=Path.Combine(root,"update failure "+mode);Directory.CreateDirectory(destination);
            using var cancellation=new CancellationTokenSource();
            if(mode=="cancel")failing.OnAdd=cancellation.Cancel;
            var selected=mode=="checksum"?plan with{Packages=[packages[0] with{Sha256=new string('0',64)},packages[1]]}:plan;
            bool publishedHealth=false;
            try{await Apply(destination,failing,selected,cancellation.Token,_=>publishedHealth=true);throw new Exception("Expected failed update: "+mode);}
            catch(Exception error) when(error is IOException or OperationCanceledException)
            {
                var report=ReadResult(destination);
                bool expectedApplied=mode is not "checksum" and not "signature" and not "health-before";
                check(!publishedHealth && failing.Applied==expectedApplied && !report.GetProperty("preservationPassed").GetBoolean() && report.GetProperty("error").GetString()!.Length>0,
                    "update failure retained diagnostic evidence and never claimed preservation success: "+mode);
                if(mode=="combined")
                {
                    var differences=ReadDifferences(destination).EnumerateArray().ToArray();
                    check(differences.Length==2 && differences.Any(d=>d.GetProperty("Kind").GetString()=="feature" && d.GetProperty("Name").GetString()=="UnexpectedCamera" && d.GetProperty("Before").ValueKind==JsonValueKind.Null && d.GetProperty("After").GetString()=="Enabled") &&
                        differences.Any(d=>d.GetProperty("Kind").GetString()=="configuration" && d.GetProperty("Name").GetString()=="service:Kept" && d.GetProperty("Before").GetString()=="disabled" && d.GetProperty("After").GetString()=="automatic"),
                        "feature rejection still retains the independent changed startup setting, with concrete old and new values");
                }
                if(expectedApplied)File.WriteAllText(Path.Combine(destination,"windows-update",packages[0].File),"lease released after failure");
            }
        }
        var mismatch=Path.Combine(root,"update wrong source");Directory.CreateDirectory(mismatch);
        try{await WindowsImageUpdates.Apply(new UpdateRunner(),"unused",mismatch,folder,plan,new Version(10,0,26200,9000),_=>{},CancellationToken.None);throw new Exception("Mismatched source accepted");}
        catch(IOException){check(!Directory.Exists(Path.Combine(mismatch,"windows-update")),"a stale update plan is rejected before allocating a staging folder or invoking servicing");}
        var ordinary=Path.Combine(root,"update-plan.json");
        File.WriteAllText(ordinary,JsonSerializer.Serialize(plan).TrimEnd('}')+",\"UnreviewedCommand\":\"must not run\"}");
        try{WindowsImageUpdates.ReadPlan(root);throw new Exception("Unknown plan property accepted");}
        catch(JsonException){check(true,"unknown update-plan instructions are rejected rather than ignored or executed");}

        Task<Version> Apply(string workspace,UpdateRunner commands,WindowsImageUpdates.Plan selected,CancellationToken ct=default,Action<WindowsImageUpdates.VerifiedHealthScan>? onVerified=null) =>
            WindowsImageUpdates.Apply(commands,Path.Combine(workspace,"mount"),workspace,folder,selected,Source,_=>{},ct,
                _=>new Dictionary<string,string>{{"service:Kept",commands.Applied&&(commands.Failure is "services" or "combined")?"automatic":"disabled"}},
                _=>commands.Applied&&commands.Failure is not "wrong-version" and not "pending"?Target:Source,verifiedHealth:onVerified);
    }
    private static JsonElement ReadResult(string work)
    {
        using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(work,"windows-update","result.json")));return report.RootElement.Clone();
    }
    private static JsonElement ReadDifferences(string work)
    {
        using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(work,"windows-update","preservation-differences.json")));return report.RootElement.Clone();
    }

    // Inert command boundary: no fixture payload is signed, installed or sent to DISM.
    private sealed class UpdateRunner : ICommandRunner
    {
        public string Failure="";
        public bool Applied;
        public int Signatures;
        public string[]? AddArguments;
        public Action? OnAdd;
        public Task<string> PowerShell(string script,CancellationToken ct=default)
        {
            if(!script.Contains("Get-AuthenticodeSignature")||!script.Contains("Microsoft Corporation"))throw new Exception("Unexpected fixture PowerShell");
            Signatures++;if(Failure=="signature")throw new IOException("Fixture signature is invalid");return Task.FromResult("");
        }
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default) => throw new Exception("No executable may run in this fixture");
        public Task<string> Dism(params string[] args)
        {
            if(args.Contains("/Get-ProvisionedAppxPackages"))return Task.FromResult("DisplayName : Kept.App\nPackageName : Kept.App_1.0_x64\nThe operation completed successfully.");
            if(args.Contains("/Get-Features"))return Task.FromResult("Feature Name : KeptFeature\nState : Disabled with Payload Removed\n"+(Applied&&(Failure is "features" or "combined")?"Feature Name : UnexpectedCamera\nState : Enabled\n":"")+"The operation completed successfully.");
            if(args.Contains("/ScanHealth"))return Task.FromResult((Failure=="health-before"||Applied&&Failure=="health-after")?"The component store is repairable.":"No component store corruption detected.");
            if(args.Contains("/Add-Package")){Applied=true;AddArguments=args;OnAdd?.Invoke();return Task.FromResult("The operation completed successfully.");}
            throw new Exception("Unexpected fixture DISM command");
        }
    }
}
