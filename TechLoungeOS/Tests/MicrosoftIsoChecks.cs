using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TechLoungeOS;

internal static class MicrosoftIsoChecks
{
    private const string Address="https://software.download.prss.microsoft.com/image/Win11_x64.iso?token=fixture";
    internal static async Task Run(string root,Action<bool,string> check)
    {
        void Reject(Action action,string message){try{action();check(false,message);}catch(IOException){check(true,message);}}
        check(MicrosoftIso.ProductId("<select id='other'><option value='7'></select><select id='product-edition'><option value=''>Choose</option><option value='3210'>Windows 11</option></select>")=="3210","Microsoft product is discovered from the current Windows download page");
        Reject(()=>MicrosoftIso.ProductId("<select id='other'><option value='3210'></select>"),"A changed Microsoft catalogue does not guess a product ID");
        foreach(var url in new[]{"http://download.microsoft.com/a.iso","https://download.microsoft.com.evil.test/a.iso","https://user@download.microsoft.com/a.iso","https://download.microsoft.com:444/a.iso","https://download.microsoft.com/a.exe","https://download.microsoft.com/a.iso#fragment"})
            Reject(()=>MicrosoftIso.IsoAddress(url),"Untrusted ISO address rejected: "+url);
        var languages=MicrosoftIso.Languages("""{"Skus":[{"Id":3,"Language":"German"},{"Id":1,"Language":"English"},{"Id":2,"Language":"English International"}]}""");
        check(languages.Select(x=>x.Id).SequenceEqual(new[]{"2","1","3"})&&languages[0].DisplayName=="English (United Kingdom)"&&languages[1].DisplayName=="English (United States)","Download language defaults to UK English, then US English");
        var currentNames=MicrosoftIso.Languages("""{"Skus":[{"Id":27160,"Language":"English"},{"Id":27128,"Language":"English (United Kingdom)"}]}""");
        check(currentNames[0].Id=="27128"&&currentNames[1].Id=="27160","The live Microsoft UK language spelling retains UK-first ordering");
        Reject(()=>MicrosoftIso.Languages("""{"Skus":[{"Id":1,"Language":"English"},{"Id":1,"Language":"Other"}]}"""),"Duplicate Microsoft language identities are rejected");
        Reject(()=>MicrosoftIso.Languages("""{"Errors":[{"Value":"refused"}]}"""),"Microsoft refusal remains a visible failure");
        var link=MicrosoftIso.Link("""{"ProductDownloadOptions":[{"DownloadType":2,"Uri":"https://download.microsoft.com/arm64.iso"},{"DownloadType":1,"Uri":"https://download.microsoft.com/x64.iso","LocalizedProductDisplayName":"Windows 11"}]}""");
        check(link.FileName=="x64.iso"&&link.Sha256 is null,"The x64 link is selected without claiming a missing published hash was verified");
        Reject(()=>MicrosoftIso.Link("""{"ProductDownloadOptions":[{"DownloadType":2,"Uri":"https://download.microsoft.com/arm64.iso"}]}"""),"ARM64-only replies do not offer an unsupported image");
        Reject(()=>MicrosoftIso.DirectLink("https://download.microsoft.com/a%2Fb.iso"),"Encoded filename separators are rejected");
        var bytes=new byte[2*1024*1024];Encoding.ASCII.GetBytes("CD001").CopyTo(bytes,32769);
        var sha=Convert.ToHexString(SHA256.HashData(bytes));
        var folder=Path.Combine(root,"official-iso");Directory.CreateDirectory(folder);
        var calls=new List<string>();
        using var http=new HttpClient(new Handler(request=>
        {
            calls.Add(request.RequestUri!.Host);
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }));
        var service=new MicrosoftIso(http);var selected=new MicrosoftIsoLink(new(Address),"Win11_x64.iso","Windows 11",sha);
        var destination=Path.Combine(folder,"download.iso");var progress=new List<long>();
        var result=await service.Download(selected,destination,new ProgressSink(p=>progress.Add(p.Bytes)),CancellationToken.None);
        check(result.PublishedHashMatched&&result.Sha256==sha&&File.ReadAllBytes(destination).SequenceEqual(bytes)&&progress[^1]==bytes.Length,"ISO download writes complete bytes, checks the published hash and reports progress");
        var before=calls.Count;
        try{await service.Download(selected,destination,null,CancellationToken.None);check(false,"Existing ISO is retained");}
        catch(IOException){check(calls.Count==before&&File.ReadAllBytes(destination).SequenceEqual(bytes),"An existing ISO is retained without starting a download");}
        var mismatch=Path.Combine(folder,"mismatch.iso");
        try{await service.Download(selected with{Sha256=new string('0',64)},mismatch,null,CancellationToken.None);check(false,"Hash mismatch is rejected");}
        catch(IOException){check(!File.Exists(mismatch)&&!Directory.GetFiles(folder,"*.partial").Any(),"Hash mismatch removes only its incomplete download");}
        using(var cancel=new CancellationTokenSource())
        {
            var cancelled=Path.Combine(folder,"cancel.iso");
            try{await service.Download(selected,cancelled,new ProgressSink(_=>cancel.Cancel()),cancel.Token);check(false,"Download cancellation stops the stream");}
            catch(OperationCanceledException){check(!File.Exists(cancelled)&&!Directory.GetFiles(folder,"*.partial").Any(),"Cancelled ISO download removes the partial file and retains completed media");}
        }
        var race=Path.Combine(folder,"race.iso");
        try{await service.Download(selected,race,new ProgressSink(_=>{if(!File.Exists(race))File.WriteAllText(race,"Keep this independently created ISO");}),CancellationToken.None);check(false,"Destination race is rejected");}
        catch(IOException){check(File.ReadAllText(race)=="Keep this independently created ISO"&&!Directory.GetFiles(folder,"*.partial").Any(),"A destination created during download is never overwritten");}
        var requests=0;
        using var redirect=new HttpClient(new Handler(_=>
        {
            requests++;var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new("https://evil.test/a.iso");return response;
        }));
        try{await new MicrosoftIso(redirect).Download(selected,Path.Combine(folder,"redirect.iso"),null,CancellationToken.None);check(false,"Redirect is rejected");}
        catch(IOException){check(requests==1&&!File.Exists(Path.Combine(folder,"redirect.iso")),"Download redirects cannot leave Microsoft's permitted hosts");}
        using var shortReply=new HttpClient(new Handler(_=>
        {
            var content=new ByteArrayContent(new byte[32]);content.Headers.ContentLength=bytes.Length;
            return new(HttpStatusCode.OK){Content=content};
        }));
        try{await new MicrosoftIso(shortReply).Download(selected,Path.Combine(folder,"short.iso"),null,CancellationToken.None);check(false,"Truncated download is rejected");}
        catch(IOException){check(!Directory.GetFiles(folder,"*.partial").Any()&&!File.Exists(Path.Combine(folder,"short.iso")),"A truncated download is cleaned up and cannot be imported");}
        var sequence=new List<string>();
        using var catalogueClient=new HttpClient(new Handler(request=>
        {
            var url=request.RequestUri!;sequence.Add(url.Host+url.AbsolutePath);
            var body=url.Host=="vlscppe.microsoft.com"?"tags":url.AbsolutePath.EndsWith("mdt.js",StringComparison.Ordinal)?"var x='?w=ABC123';rticks=\\\"+987654":
                url.Host=="ov-df.microsoft.com"?"session":url.AbsolutePath.Contains("getskuinformationbyproductedition",StringComparison.Ordinal)?"""{"Skus":[{"Id":2,"Language":"English International"},{"Id":1,"Language":"English"}]}""":
                "<select id='product-edition'><option value='3210'>Windows 11</option></select>";
            return new(HttpStatusCode.OK){Content=new StringContent(body)};
        }));
        var catalogue=await new MicrosoftIso(catalogueClient).Catalogue(CancellationToken.None);
        check(catalogue.Product=="3210"&&Guid.TryParse(catalogue.Session,out _)&&catalogue.Languages[0].Id=="2"&&sequence.Count==5,"The anonymous Microsoft catalogue session follows the complete page/session/language flow");
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(response(request));}
    }
    private sealed class ProgressSink(Action<IsoDownloadProgress> callback):IProgress<IsoDownloadProgress>
    {
        public void Report(IsoDownloadProgress value)=>callback(value);
    }
}
