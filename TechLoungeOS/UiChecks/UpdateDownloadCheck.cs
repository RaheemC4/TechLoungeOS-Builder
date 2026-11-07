using System.Net;
using System.Net.Http;
using System.Text.Json;
using TechLoungeOS;
using TechLoungeOS.Updates;

// Small failure-only fixture on the new disposable installation. Never applies an update.
internal static class UpdateDownloadCheck
{
    internal static async Task Run()
    {
        await new ProcessRunner(Console.WriteLine).PowerShell("""
            if((Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU' -or @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Owned fresh1263 required'}
            """);
        const string fixture=@"C:\TLOS-Update1266";
        var manifest=Path.Combine(AppContext.BaseDirectory,"update1266","builder-update.json");
        if(Directory.Exists(fixture))throw new IOException("Review prior fixture evidence before retrying.");
        Directory.CreateDirectory(fixture);
        var bad=Path.Combine(fixture,"partial.zip");File.WriteAllBytes(bad,new byte[64]);
        var root=UpdateStorage.Root();var sibling=UpdateStorage.Stage();
        var note=Path.Combine(sibling,"retain.txt");File.WriteAllText(note,"Retain another staged update.");
        string[] Stages()=>Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var before=Stages();var failures=0;
        try
        {
            for(var i=0;i<3;i++)
            {
                try{AppUpdates.StageLocal(manifest,bad,new Version(0,1,17,0));throw new Exception("A partial package was accepted.");}
                catch(InvalidDataException ex)when(ex.Message.Contains("package failed verification",StringComparison.Ordinal)){failures++;}
                if(!before.SequenceEqual(Stages())||File.ReadAllText(note)!="Retain another staged update.")throw new IOException("Failed download retained a stage or changed another update.");
            }
            using(var locked=new FileStream(bad,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                try{AppUpdates.StageLocal(manifest,bad,new Version(0,1,17,0));throw new Exception("An exclusively locked source was copied.");}
                catch(IOException){failures++;}
            }
            if(failures!=4||!before.SequenceEqual(Stages())||File.ReadAllText(note)!="Retain another staged update.")throw new IOException("Failed copy cleanup did not preserve the baseline.");
            var package=Path.Combine(AppContext.BaseDirectory,"app-updates","Builder-0.1.18.zip");
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var download=new DownloadFixture(manifest,package,false);
            using(var http=new HttpClient(download))
            {
                var ready=await new AppUpdates(http).Check(new Version(0,1,17,0),timeout.Token)??throw new IOException("Signed newer download was not staged.");
                try
                {
                    if(ready.Manifest.Version!="0.1.18.0"||download.Requests.Count!=4||!download.Requests[^1].StartsWith("https://release-assets.githubusercontent.com/",StringComparison.Ordinal)||
                        UpdatePackage.Hash(Path.Combine(ready.Folder,"verified","FirstLogin.exe"))!="50AD4786D872BF83BA8765DA28A3C5FF951EA99B853D13EE10A80260D02276AF")throw new IOException("Signed download/redirect/readback failed.");
                }
                finally{UpdateStorage.DiscardStage(ready.Folder,root);}
            }
            var partial=new DownloadFixture(manifest,package,true);
            using(var http=new HttpClient(partial))
            {
                try{await new AppUpdates(http).Check(new Version(0,1,17,0),timeout.Token);throw new Exception("Truncated download was accepted.");}
                catch(IOException ex)when(ex.Message.Contains("download was incomplete",StringComparison.Ordinal)){failures++;}
            }
            if(failures!=5||!before.SequenceEqual(Stages())||File.ReadAllText(note)!="Retain another staged update.")throw new IOException("Download cleanup retained a stage or changed another update.");
            File.WriteAllText(Path.Combine(fixture,"result.json"),JsonSerializer.Serialize(new{passed=true,failedAttempts=failures,stagesBefore=before.Length,stagesAfter=Stages().Length,otherStagePreserved=true,signedDownloadAndRedirectVerified=true,httpTransportFixture=true,realNetwork=false,updateApplied=false,version=AppUpdates.CurrentVersion.ToString()}));
        }
        finally{UpdateStorage.DiscardStage(sibling,root);}
        Console.WriteLine("Four failed local attempts and one truncated download left no additional stages. Signed HTTP-fixture download/redirect verified. No real network or update application.");
    }
    // Exercises the production downloader with real signed bytes, without a hosted release or TLS interception.
    private sealed class DownloadFixture(string manifest,string package,bool truncated):HttpMessageHandler
    {
        internal List<string> Requests {get;}=[];
        private readonly string release="https://github.com/"+UpdateTrust.Repository+"/releases/download/v0.1.18.0/";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var url=request.RequestUri!.AbsoluteUri;Requests.Add(url);
            HttpResponseMessage response;
            if(url=="https://api.github.com/repos/"+UpdateTrust.Repository+"/releases/latest")
                response=new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{draft=false,prerelease=false,assets=new[]{new{name="builder-update.json",browser_download_url=release+"builder-update.json"}}}))};
            else if(url==release+"builder-update.json")response=new(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(manifest))};
            else if(url==release+"TechLoungeOS-Builder-win-x64.zip")
            {response=new(HttpStatusCode.Found);response.Headers.Location=new Uri("https://release-assets.githubusercontent.com/tlos-fixture/package.zip");}
            else if(url=="https://release-assets.githubusercontent.com/tlos-fixture/package.zip")
            {
                response=new(HttpStatusCode.OK){Content=truncated?new ByteArrayContent(new byte[64]):new StreamContent(File.OpenRead(package))};
                response.Content.Headers.ContentLength=new FileInfo(package).Length;
            }
            else throw new IOException("Unexpected downloader fixture request.");
            return Task.FromResult(response);
        }
    }
}
