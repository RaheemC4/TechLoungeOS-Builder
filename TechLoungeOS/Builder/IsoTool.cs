using System.Net.Http;
using System.Security.Cryptography;
using TechLoungeOS.Updates;

namespace TechLoungeOS;

// Exact Microsoft symbol-server build. No general unsigned-executable exception.
public static class IsoTool
{
    public const string DownloadUrl="https://msdl.microsoft.com/download/symbols/oscdimg.exe/3D44737265000/oscdimg.exe";
    public const string Hash="F5129F313ED7EB46F2677CF522E64264A225F226307ED0DDB52BB14C46E7CFDD";
    public const string SignedHash="D2709F8099DDD202BC41BCB399ECD5638503C6466F416D1A321769AF83A70C3A";
    private const string BlobPath="/b-4712e0edc5a240eabf23330d7df68e77/92AA5333BAA72732D9C9B9CAA3198C2697AABBCD7E36381221B56A088A3A851700.blob";
    private static readonly SemaphoreSlim Gate=new(1,1);
    public static string CachePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TechLoungeOS.Builder.Updates","build-tools",Hash,"oscdimg.exe");
    public static bool Allowed(Uri uri)=>uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo==""&&uri.Fragment==""&&
        ((uri.IdnHost=="msdl.microsoft.com"&&uri.AbsolutePath==new Uri(DownloadUrl).AbsolutePath&&uri.Query=="") ||
         (uri.IdnHost=="vsblobprodscussu5shard61.blob.core.windows.net"&&uri.AbsolutePath==BlobPath));
    public static bool Approved(string file)
    {
        Policy.RequireUnlinkedPath(file);
        if(!File.Exists(file)||new FileInfo(file).Length>1048576)return false;
        using var stream=File.OpenRead(file);
        var hash=Convert.ToHexString(SHA256.HashData(stream));return hash is Hash or SignedHash;
    }
    public static async Task<string> Ensure(string? previous,CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            // Only a protected cache is executable: do not run a medium-token writable file.
            var root=UpdateStorage.Root();var folder=Path.Combine(root,"build-tools",Hash);
            UpdatePackage.Unlinked(folder);Directory.CreateDirectory(folder);
            if(Approved(CachePath))return CachePath;
            var temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+".partial");
            try
            {
                if(previous is not null && File.Exists(previous) && Approved(previous))File.Copy(previous,temporary,false);
                else
                {
                    using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(2)};
                    await Fetch(client,temporary,ct);
                }
                if(!Approved(temporary))throw new IOException("The Microsoft build component failed verification. It was not used.");
                ct.ThrowIfCancellationRequested();UpdatePackage.Unlinked(CachePath);File.Move(temporary,CachePath,true);
                return CachePath;
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception error)when(error is IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {throw new IOException("Could not prepare a required Microsoft build component. Check your internet connection and try again. No USB drive has been erased.",error);}
        finally{Gate.Release();}
    }
    internal static async Task Fetch(HttpClient client,string destination,CancellationToken ct)
    {
        var address=new Uri(DownloadUrl);
        for(var hop=0;hop<4;hop++)
        {
            if(!Allowed(address))throw new IOException("Microsoft build-tool download left its approved location.");
            using var request=new HttpRequestMessage(HttpMethod.Get,address);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {address=response.Headers.Location is { } next?new Uri(address,next):throw new IOException("Empty build-tool redirect.");continue;}
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is >1048576)throw new IOException("Build-tool download is too large.");
            using var input=await response.Content.ReadAsStreamAsync(ct);
            await using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            {
                var buffer=new byte[81920];int count;long total=0;
                while((count=await input.ReadAsync(buffer,ct))>0){total+=count;if(total>1048576)throw new IOException("Build-tool download is too large.");await output.WriteAsync(buffer.AsMemory(0,count),ct);}
                output.Flush(true);
            }
            if(!Approved(destination))throw new IOException("Microsoft build-tool checksum differs.");
            return;
        }
        throw new IOException("Too many build-tool download redirects.");
    }
}
