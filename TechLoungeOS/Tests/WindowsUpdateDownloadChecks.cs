using System.Net;
using System.Security.Cryptography;
using System.Text;
using TechLoungeOS;

static class WindowsUpdateDownloadChecks
{
    private static readonly Guid Id=new("62c21efa-416e-430c-84ee-bba3c8d583c2");
    private const string Checkpoint="windows11.0-kb5043080-x64_953449672073f8fb99badb4cc6d5d7849b9c83e8.msu";
    private const string Target="windows11.0-kb5134133-x64_0e59d2f988a5422dfb489e97faed3b3835fc7568.msu";
    private static readonly byte[] Bytes=Encoding.UTF8.GetBytes("inert update downloader fixture; not an MSU");
    private static readonly WindowsReleaseCatalog.Release Release=new("25H2",new Version(10,0,26200,9725),new DateOnly(2026,10,26),"2026-10 OOB","KB5134133");

    public static async Task Run(string root,Action<bool,string> check)
    {
        var html="<strong>Version 26H1 (OS build 28000)</strong><table>"+Row("2026-10 B","2026-10-13","28000.3231","KB5127646")+"</table>"+
            "<details><summary><strong>Version 25H2 (OS build 26200)</strong></summary><table>"+
            Row("2026-10 D","2026-10-28","26200.9818","KB5127936")+
            Row("2026-10 OOB","2026-10-26","26200.9725","KB5134133")+
            Row("2026-10 B","2026-10-13","26200.9711","KB5127648")+"</table></details>"+
            "<strong>Version 24H2 (OS build 26100)</strong><table>"+
            Row("2024-10 A","2024-10-01","26100.1742","").Replace("General Availability Channel","LTSC • General Availability Channel",StringComparison.Ordinal)+
            Row("2026-10 OOB","2026-10-26","26100.9725","KB5134133").Replace("General Availability Channel","LTSC • General Availability Channel",StringComparison.Ordinal)+"</table>";
        var releases=WindowsReleaseCatalog.Parse(html);
        var datedHtml=html+"<strong>Version 26H2 (OS build 26300)</strong><table>"+Row("","2026-09-29","26300.9457","")+"</table>"+
            "<strong>Version 25H2 (OS build 26200)</strong><table>"+Row("2026-09 OOB","2026-09-14","26200.9457","KB5129195")+"</table>";
        var report=WindowsReleaseCatalog.Summarise(WindowsReleaseCatalog.Parse(datedHtml),new Version(10,0,26200,9457),new DateTimeOffset(2026,11,1,18,0,0,TimeSpan.Zero));
        check(report.Current?.Published==new DateOnly(2026,9,14) && report.LatestCompatible==Release && report.NewestFeature?.Name=="26H2" && report.NewestFeature.Published==new DateOnly(2026,9,29),
            "build publication dates and newest feature release remain distinct from stable compatible servicing updates; higher-build 26H1 is not mislabelled newest");
        check(WindowsReleaseCatalog.DisplayName(new Version(10,0,26300,9457))=="Windows 11 26H2" && WindowsReleaseCatalog.DisplayName(new Version(10,0,27000,1)).Contains("unconfirmed"),
            "known Windows release names display immediately while unknown build families are never guessed");
        check(WindowsReleaseCatalog.LatestCompatible(releases,new Version(10,0,26200,9457),new DateOnly(2026,11,1))==Release,
            "Windows image update discovery selects stable/OOB within the source release and excludes a newer preview or unrelated higher build");
        check(WindowsReleaseCatalog.LatestCompatible(releases,new Version(10,0,26200,9457),new DateOnly(2026,10,20)).Version==new Version(10,0,26200,9711),
            "not-yet-published update rows cannot be selected early");
        check(WindowsReleaseCatalog.LatestCompatible(releases,new Version(10,0,26100,1742),new DateOnly(2026,11,1)).Version==new Version(10,0,26100,9725),
            "LTSC cumulative updates remain discoverable alongside an initial availability row without a KB");
        foreach(var bad in new[]{"<html>Unknown Microsoft layout</html>",html+html,html.Replace("26200.9725","26100.9725",StringComparison.Ordinal)})
            try{WindowsReleaseCatalog.Parse(bad);throw new Exception("Unexpected release metadata accepted");}
            catch(IOException){check(true,"missing, ambiguous or inconsistent release metadata fails without selecting an update");}
        var search=Search(Release);
        check(WindowsUpdateDownload.FindUpdate(search,Release)==Id,"catalogue lookup selects the exact x64 release, KB and target revision");
        foreach(var bad in new[]{search+search,search.Replace("x64-based","arm64-based",StringComparison.Ordinal),search.Replace("KB5134133","KB5134134",StringComparison.Ordinal)})
            try{WindowsUpdateDownload.FindUpdate(bad,Release);throw new Exception("Ambiguous or incorrect update accepted");}
            catch(IOException){check(true,"ambiguous, wrong-architecture or wrong-KB catalogue results are rejected");}
        var files=WindowsUpdateDownload.ParseFiles(Metadata(),Id,"KB5134133");
        check(files.Length==2&&files[0].File==Checkpoint&&files[1].File==Target&&files.All(f=>f.Sha256==Convert.ToHexString(SHA256.HashData(Bytes))),
            "catalogue metadata pins SHA256 and stages checkpoint packages before the requested target without evaluating its JavaScript");
        foreach(var bad in new[]{Metadata().Replace(Id.ToString(),Guid.Empty.ToString(),StringComparison.Ordinal),Metadata().Replace("https://download.windowsupdate.com/","https://download.windowsupdate.com.evil.invalid/",StringComparison.Ordinal),Metadata().Replace(".sha256 = '",".sha256 = 'bad",StringComparison.Ordinal),Metadata()+"downloadInformation[0].files[0].url = 'https://download.windowsupdate.com/duplicate';"})
            try{WindowsUpdateDownload.ParseFiles(bad,Id,"KB5134133");throw new Exception("Unsafe payload metadata accepted");}
            catch(IOException){check(true,"download identity, host, checksum and duplicate-field changes are rejected");}
        using(var client=new HttpClient(new Handler("")))
        {
            var folder=Path.Combine(root,"mock Windows downloads");
            var plan=await WindowsUpdateDownload.Prepare(folder,new Version(10,0,26200,9457),Release,_=>{},CancellationToken.None,client);
            check(plan.Packages.Select(p=>p.File).SequenceEqual(new[]{Checkpoint,Target})&&WindowsImageUpdates.ReadPlan(folder).TargetVersion=="10.0.26200.9725"&&
                plan.Packages.All(p=>File.ReadAllBytes(Path.Combine(folder,p.File)).SequenceEqual(Bytes)),
                "real HTTP client against an inert handler downloads exact pinned bytes and writes a source-bound offline update plan");
        }
        foreach(var mode in new[]{"corrupt","oversize","redirect","metadata-redirect"})
        {
            using var client=new HttpClient(new Handler(mode));var folder=Path.Combine(root,"download failure "+mode);
            try{await WindowsUpdateDownload.Prepare(folder,new Version(10,0,26200,9457),Release,_=>{},CancellationToken.None,client);throw new Exception("Expected rejected transfer: "+mode);}
            catch(IOException){check(!File.Exists(Path.Combine(folder,"update-plan.json"))&&!File.Exists(Path.Combine(folder,Checkpoint)),"unsafe or corrupted download cannot create a usable plan: "+mode);}
        }
        var raced=Path.Combine(root,"download conflicting file");
        using(var client=new HttpClient(new Handler("",()=>File.WriteAllText(Path.Combine(raced,Checkpoint),"existing file must survive"))))
        {
            try{await WindowsUpdateDownload.Prepare(raced,new Version(10,0,26200,9457),Release,_=>{},CancellationToken.None,client);throw new Exception("A conflicting output was overwritten");}
            catch(IOException){check(File.ReadAllText(Path.Combine(raced,Checkpoint))=="existing file must survive"&&!File.Exists(Path.Combine(raced,"update-plan.json")),"failed download creation never deletes or overwrites a pre-existing conflicting file");}
        }
    }
    private static string Row(string type,string date,string version,string kb)=>"<tr><td>General Availability Channel</td><td>"+type+"</td><td>"+date+"</td><td>"+version+"</td><td>"+(kb.Length==0?"":"<a href='https://support.microsoft.com/help/"+kb[2..]+"'>"+kb+"</a>")+"</td></tr>";
    private static string Search(WindowsReleaseCatalog.Release release)=>"<a id='"+Id+"_link' href='javascript:void(0);'>2026-10 Cumulative Update for Windows 11, version "+release.Name+" for x64-based Systems ("+release.KnowledgeBase+") ("+release.Version.Build+"."+release.Version.Revision+")</a>";
    private static string Metadata()=>"downloadInformation[0].updateID ='"+Id+"';\n"+string.Join("\n",new[]{Target,Checkpoint}.Select((name,i)=>"downloadInformation[0].files["+i+"].url = 'https://download.windowsupdate.com/test/"+name+"';\n"+
        "downloadInformation[0].files["+i+"].fileName = '"+name+"';\n"+"downloadInformation[0].files["+i+"].sha256 = '"+Convert.ToBase64String(SHA256.HashData(Bytes))+"';"));
    private sealed class Handler(string mode,Action? beforePayload=null):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var uri=request.RequestUri!;
            HttpContent content;
            if(uri.Host=="www.catalog.update.microsoft.com")
            {
                if(uri.AbsolutePath=="/Search.aspx"&&request.Method==HttpMethod.Get)content=new StringContent(Search(Release));
                else if(uri.AbsolutePath=="/DownloadDialog.aspx"&&request.Method==HttpMethod.Post&&
                    (await request.Content!.ReadAsStringAsync(ct)).Contains(Id.ToString(),StringComparison.Ordinal))content=new StringContent(Metadata());
                else throw new Exception("Unexpected metadata request");
            }
            else if(uri.Host=="download.windowsupdate.com")
            {
                beforePayload?.Invoke();
                content=new ByteArrayContent(mode=="corrupt"?new byte[Bytes.Length]:Bytes);
                if(mode=="oversize")content.Headers.ContentLength=8589934593;
            }
            else throw new Exception("No real network request is allowed in this fixture");
            var returned=request;
            if(mode=="redirect"&&uri.Host=="download.windowsupdate.com"||mode=="metadata-redirect")returned=new HttpRequestMessage(HttpMethod.Get,"https://untrusted.invalid/");
            return new HttpResponseMessage(HttpStatusCode.OK){Content=content,RequestMessage=returned};
        }
    }
}
