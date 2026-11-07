using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace TechLoungeOS.Updates;
public record ReadyUpdate(UpdateManifest Manifest,string Folder);
public sealed class AppUpdateFeedUnavailableException() : IOException("No public Builder app release has been published yet.");
public sealed class AppUpdates
{
    private readonly HttpClient client;
    public AppUpdates(HttpClient? client=null)
    {
        this.client=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(3)};
        if(!this.client.DefaultRequestHeaders.UserAgent.Any())this.client.DefaultRequestHeaders.UserAgent.ParseAdd("TechLoungeOS-Builder/0.1");
    }
    public async Task<ReadyUpdate?> Check(Version current,CancellationToken ct)
    {
        var feed=new Uri("https://api.github.com/repos/"+UpdateTrust.Repository+"/releases/latest");
        using var response=await client.GetAsync(feed,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode==HttpStatusCode.NotFound)throw new AppUpdateFeedUnavailableException();
        response.EnsureSuccessStatusCode();
        var metadata=await Bytes(response,512*1024,ct);
        using var json=JsonDocument.Parse(metadata);
        if(json.RootElement.GetProperty("draft").GetBoolean() || json.RootElement.GetProperty("prerelease").GetBoolean())throw new AppUpdateFeedUnavailableException();
        var assets=json.RootElement.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()=="builder-update.json").ToArray();
        if(assets.Length!=1)throw new IOException("The official release is missing its signed update manifest.");
        var url=UpdatePackage.OfficialAsset(assets[0].GetProperty("browser_download_url").GetString()!);
        using var manifestResponse=await Get(url,ct);
        var envelope=await Bytes(manifestResponse,1<<20,ct);
        // A signed current/older release is harmless; verify authenticity before comparing versions.
        var signed=UpdatePackage.VerifyEnvelope(envelope,UpdateTrust.PublicKeySpki,new Version(0,0,0,0),Environment.OSVersion.Version.Build,DateTimeOffset.UtcNow);
        if(Version.Parse(signed.Version)<=current)return null;
        var folder=ProtectedStage();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(folder,"builder-update.json"),envelope,ct);
            using var package=await Get(new(signed.PackageUrl),ct);
            if(package.Content.Headers.ContentLength is long length && length!=signed.PackageBytes)throw new IOException("Update download size does not match its signature.");
            using(var input=await package.Content.ReadAsStreamAsync(ct))using(var output=new FileStream(Path.Combine(folder,"package.zip"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var buffer=new byte[81920];long total=0;int count;
                while((count=await input.ReadAsync(buffer,ct))>0){total+=count;if(total>signed.PackageBytes)throw new IOException("Update download exceeds its signed size.");await output.WriteAsync(buffer.AsMemory(0,count),ct);}
                output.Flush(true);if(total!=signed.PackageBytes)throw new IOException("Update download was incomplete.");
            }
            UpdatePackage.Extract(Path.Combine(folder,"package.zip"),Path.Combine(folder,"verified"),signed);
            return new(signed,folder);
        }
        catch(Exception error){DiscardFailedStage(folder,error);throw;}
    }
    public static ReadyUpdate StageLocal(string envelopeFile,string packageFile,Version current)
    {
        UpdatePackage.Unlinked(envelopeFile);UpdatePackage.Unlinked(packageFile);
        if(new FileInfo(envelopeFile).Length>1<<20)throw new IOException("Update manifest is too large.");
        var bytes=File.ReadAllBytes(envelopeFile);
        var manifest=UpdatePackage.VerifyEnvelope(bytes,UpdateTrust.PublicKeySpki,current,Environment.OSVersion.Version.Build,DateTimeOffset.UtcNow);
        var folder=ProtectedStage();
        try
        {
            File.WriteAllBytes(Path.Combine(folder,"builder-update.json"),bytes);
            File.Copy(packageFile,Path.Combine(folder,"package.zip"));UpdatePackage.Extract(Path.Combine(folder,"package.zip"),Path.Combine(folder,"verified"),manifest);
            return new(manifest,folder);
        }
        catch(Exception error){DiscardFailedStage(folder,error);throw;}
    }
    private static void DiscardFailedStage(string folder,Exception error)
    {
        try{UpdateStorage.DiscardStage(folder,UpdateStorage.Root());}
        catch(Exception cleanup){throw new AggregateException("The update failed and its incomplete download could not be removed. Retained folder: "+folder,error,cleanup);}
    }
    public static string ProtectedStage()
    {
        return UpdateStorage.Stage();
    }
    public static void Launch(ReadyUpdate update,bool restart)
    {
        var install=Path.GetFullPath(AppContext.BaseDirectory);UpdateCommit.ValidateInstall(install);
        UpdatePackage.Unlinked(update.Folder);
        var verified=UpdatePackage.VerifyEnvelope(File.ReadAllBytes(Path.Combine(update.Folder,"builder-update.json")),UpdateTrust.PublicKeySpki,CurrentVersion,Environment.OSVersion.Version.Build,DateTimeOffset.UtcNow);
        if(verified.Version!=update.Manifest.Version)throw new IOException("Staged update changed.");
        var helper=Path.Combine(update.Folder,"verified","updater","TechLoungeOS.Updater.exe");UpdatePackage.Unlinked(helper);
        var helperFile=verified.Files.Single(f=>f.Path=="updater/TechLoungeOS.Updater.exe");
        if(!File.Exists(helper)||new FileInfo(helper).Length!=helperFile.Bytes||!UpdatePackage.Hash(helper).Equals(helperFile.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("The signed updater component is missing or changed.");
        var copy=Path.Combine(update.Folder,"apply.exe");File.Copy(helper,copy,false);
        var self=Process.GetCurrentProcess();var info=new ProcessStartInfo(copy){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"--apply",update.Folder,install,self.Id.ToString(),self.StartTime.ToUniversalTime().Ticks.ToString(),restart?"restart":"exit"})info.ArgumentList.Add(arg);
        _=Process.Start(info)??throw new IOException("Could not start the updater.");
    }
    public static Version CurrentVersion=>typeof(AppUpdates).Assembly.GetName().Version??new Version(0,1,0,0);
    private async Task<HttpResponseMessage> Get(Uri uri,CancellationToken ct)
    {
        for(var i=0;i<6;i++)
        {
            if(uri.Scheme!="https" || uri.Port!=443 || uri.UserInfo!="" || uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))throw new IOException("Unexpected update download host.");
            var r=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)r.StatusCode is 301 or 302 or 303 or 307 or 308)
            {var next=r.Headers.Location??throw new IOException("Missing redirect location.");uri=new Uri(uri,next);r.Dispose();continue;}
            r.EnsureSuccessStatusCode();return r;
        }
        throw new IOException("Too many update download redirects.");
    }
    private static async Task<byte[]> Bytes(HttpResponseMessage r,int limit,CancellationToken ct)
    {
        if(r.Content.Headers.ContentLength>limit)throw new IOException("Update response is too large.");
        using var input=await r.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[81920];int count;
        while((count=await input.ReadAsync(buffer,ct))>0){if(output.Length+count>limit)throw new IOException("Update response is too large.");await output.WriteAsync(buffer.AsMemory(0,count),ct);}return output.ToArray();
    }
}
