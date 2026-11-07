using System.Net;
using System.Net.Http;
using TechLoungeOS;

internal static class IsoToolChecks
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        check(IsoTool.Allowed(new(IsoTool.DownloadUrl)),"Automatic build tool uses the exact Microsoft symbol-server resource");
        foreach(var bad in new[]{"http://msdl.microsoft.com/download/symbols/oscdimg.exe/3D44737265000/oscdimg.exe",IsoTool.DownloadUrl+"?other=1",IsoTool.DownloadUrl+"#fragment",IsoTool.DownloadUrl.Replace("msdl.microsoft.com","msdl.microsoft.com.evil.test"),IsoTool.DownloadUrl.Replace("oscdimg.exe/3D","different.exe/3D")})
            check(!IsoTool.Allowed(new(bad)),"Unapproved Microsoft-tool location is rejected");
        var file=Path.Combine(root,"tool-check.bin");File.WriteAllText(file,"inert, never executed");
        check(!IsoTool.Approved(file),"A present executable is never trusted merely because it exists");
        foreach(var redirect in new[]{false,true})
        {
            using var http=new HttpClient(new Reply(redirect));var target=Path.Combine(root,"tool-"+redirect+".partial");
            try{await IsoTool.Fetch(http,target,CancellationToken.None);check(false,"Invalid build tool must fail");}
            catch(IOException){check(true,redirect?"An off-domain tool redirect is blocked before downloading":"Unrecognised downloaded bytes fail the pinned tool checksum");}
        }
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        using var client=new HttpClient(new Reply(false));
        try{await IsoTool.Fetch(client,Path.Combine(root,"cancelled-tool.partial"),cancelled.Token);check(false,"Cancelled tool download must stop");}
        catch(OperationCanceledException){check(true,"Build-tool acquisition honours cancellation");}
    }
    private sealed class Reply(bool redirect):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var response=new HttpResponseMessage(redirect?HttpStatusCode.Redirect:HttpStatusCode.OK){Content=new ByteArrayContent("inert fixture; never executed"u8.ToArray())};
            if(redirect)response.Headers.Location=new("https://example.invalid/tool.exe");return Task.FromResult(response);
        }
    }
}
