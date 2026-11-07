using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public static class WindowsUpdateDownload
{
    public sealed record FileMetadata(string File, Uri Address, string Sha256);
    private const long MaximumFile = 8589934592, MaximumTotal = 21474836480;
    private static readonly TimeSpan MatchTimeout=TimeSpan.FromSeconds(2);

    public static Guid FindUpdate(string html, WindowsReleaseCatalog.Release release)
    {
        if(html.Length>2097152)throw new IOException("The Microsoft Update Catalog response exceeds its budget.");
        var ids=new List<Guid>();
        var expected=@"\A[0-9]{4}-[0-9]{2} Cumulative Update for Windows 11,? version "+Regex.Escape(release.Name)+
            @" for x64-based Systems \("+Regex.Escape(release.KnowledgeBase)+@"\) \("+release.Version.Build+@"\."+release.Version.Revision+@"\)\z";
        foreach(Match link in Regex.Matches(html,@"<a\s[^>]*\bid\s*=\s*['""](?<id>[a-fA-F0-9-]{36})_link['""][^>]*>(?<title>.*?)</a>",RegexOptions.Singleline|RegexOptions.IgnoreCase,MatchTimeout))
            if(Regex.IsMatch(WindowsReleaseCatalog.Text(link.Groups["title"].Value),expected,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,MatchTimeout) && Guid.TryParse(link.Groups["id"].Value,out var id))ids.Add(id);
        if(ids.Count!=1)throw new IOException("Microsoft Update Catalog did not return one exact x64 cumulative update. No package was downloaded.");
        return ids[0];
    }

    public static FileMetadata[] ParseFiles(string html, Guid update, string knowledgeBase)
    {
        if(html.Length>2097152 || !Regex.IsMatch(knowledgeBase,@"\AKB[0-9]{6,8}\z",RegexOptions.CultureInvariant,MatchTimeout))
            throw new IOException("Invalid Windows update download metadata.");
        var identity=Regex.Matches(html,@"downloadInformation\[0\]\.updateID\s*=\s*'(?<id>[^']*)';",RegexOptions.CultureInvariant,MatchTimeout);
        if(identity.Count!=1 || !Guid.TryParse(identity[0].Groups["id"].Value,out var actual) || actual!=update)
            throw new IOException("Microsoft Update Catalog download identity did not match the requested update.");
        var entries=new SortedDictionary<int,Dictionary<string,string>>();
        foreach(Match item in Regex.Matches(html,@"downloadInformation\[0\]\.files\[(?<index>[0-9]{1,2})\]\.(?<field>url|fileName|sha256)\s*=\s*'(?<value>[^'\r\n]*)';",RegexOptions.CultureInvariant,MatchTimeout))
        {
            var index=int.Parse(item.Groups["index"].Value,System.Globalization.CultureInfo.InvariantCulture);
            if(!entries.TryGetValue(index,out var fields))entries.Add(index,fields=new(StringComparer.Ordinal));
            if(!fields.TryAdd(item.Groups["field"].Value,item.Groups["value"].Value))throw new IOException("Duplicate Windows update download metadata.");
        }
        if(entries.Count is <1 or >8 || entries.Keys.Where((key,index)=>key!=index).Any())throw new IOException("Incomplete Windows update checkpoint metadata.");
        var files=new List<FileMetadata>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var fields in entries.Values)
        {
            if(fields.Count!=3 || !fields.TryGetValue("fileName",out var name) ||
                !Regex.IsMatch(name,@"\Awindows11\.0-kb[0-9]{6,8}-x64_[a-fA-F0-9]{40}\.msu\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,MatchTimeout) || !names.Add(name) ||
                !Uri.TryCreate(fields["url"],UriKind.Absolute,out var uri) || !AllowedPayload(uri) ||
                Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath))!=name)
                throw new IOException("Unsafe or incomplete Windows update payload metadata.");
            byte[] sha;
            try{sha=Convert.FromBase64String(fields["sha256"]);}catch(FormatException error){throw new IOException("Invalid Windows update SHA256.",error);}
            if(sha.Length!=32)throw new IOException("Invalid Windows update SHA256 length.");
            files.Add(new(name,uri,Convert.ToHexString(sha)));
        }
        string target="windows11.0-"+knowledgeBase.ToLowerInvariant()+"-x64_";
        if(files.Count(f=>f.File.StartsWith(target,StringComparison.OrdinalIgnoreCase))!=1)
            throw new IOException("The requested cumulative update is missing or ambiguous in its checkpoint downloads.");
        // DISM is pointed to the target MSU; required checkpoints share only this private folder.
        return files.OrderBy(f=>f.File.StartsWith(target,StringComparison.OrdinalIgnoreCase)?1:0).ThenBy(f=>f.File,StringComparer.Ordinal).ToArray();
    }

    internal static bool AllowedPayload(Uri uri) => uri.Scheme==Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.UserInfo.Length==0 && uri.Fragment.Length==0 &&
        (uri.Host.Equals("catalog.sf.dl.delivery.mp.microsoft.com",StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("download.windowsupdate.com",StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("dl.delivery.mp.microsoft.com",StringComparison.OrdinalIgnoreCase));

    private static HttpClient Client() => new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(30)};

    public static async Task<WindowsReleaseCatalog.Release> Check(Version source,CancellationToken ct)=>(await CheckDetails(source,ct)).LatestCompatible;
    public static async Task<WindowsReleaseCatalog.VersionReport> CheckDetails(Version source,CancellationToken ct)
    {
        using var client=Client();
        using var request=new HttpRequestMessage(HttpMethod.Get,WindowsReleaseCatalog.Address);
        var html=await Metadata(client,request,ct);
        return WindowsReleaseCatalog.Summarise(WindowsReleaseCatalog.Parse(html),source,DateTimeOffset.UtcNow);
    }

    public static async Task<WindowsImageUpdates.Plan> Prepare(string folder,Version source,WindowsReleaseCatalog.Release release,
        Action<string> log,CancellationToken ct,HttpClient? client=null)
    {
        if(source.Major!=10 || source.Minor!=0 || source.Build<22000 || source.Revision<0 ||
            release.Version.Major!=10 || release.Version.Minor!=0 || release.Version.Revision<0 ||
            release.Version.Build!=source.Build || release.Version<=source ||
            !Regex.IsMatch(release.KnowledgeBase,@"\AKB[0-9]{6,8}\z",RegexOptions.CultureInvariant,MatchTimeout))
            throw new IOException("The requested update must move forward within the source Windows release.");
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Windows update downloads require a new private folder.");
        Policy.RequireUnlinkedPath(folder);
        Directory.CreateDirectory(folder);
        using var owned=client==null?Client():null;
        client??=owned!;
        using var search=new HttpRequestMessage(HttpMethod.Get,"https://www.catalog.update.microsoft.com/Search.aspx?q="+Uri.EscapeDataString(release.KnowledgeBase));
        var update=FindUpdate(await Metadata(client,search,ct),release);
        using var dialog=new HttpRequestMessage(HttpMethod.Post,"https://www.catalog.update.microsoft.com/DownloadDialog.aspx")
        {Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"updateIDs",JsonSerializer.Serialize(new[]{new{size=0,updateID=update.ToString(),uidInfo=update.ToString()}})}})};
        var files=ParseFiles(await Metadata(client,dialog,ct),update,release.KnowledgeBase);
        var payloads=new List<WindowsImageUpdates.Payload>();long total=0;
        foreach(var file in files)
        {
            ct.ThrowIfCancellationRequested();
            log("Downloading Microsoft Windows update "+file.File);
            using var request=new HttpRequestMessage(HttpMethod.Get,file.Address);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri is not Uri final || !AllowedPayload(final))throw new IOException("Windows update download left the permitted Microsoft hosts.");
            var length=response.Content.Headers.ContentLength;
            if(length is null or <1 or >MaximumFile || total+length>MaximumTotal)throw new IOException("Windows update download has an unsupported size.");
            var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
            if(drive.AvailableFreeSpace<length+(10L<<30))throw new IOException("Not enough space to download the Windows update safely.");
            string path=Path.Combine(folder,file.File);bool complete=false,created=false;
            try
            {
                await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                created=true;
                await using(var input=await response.Content.ReadAsStreamAsync(ct))
                using(var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer=new byte[1048576];long count=0;int read;
                    while((read=await input.ReadAsync(buffer,ct))!=0)
                    {
                        count+=read;if(count>length)throw new IOException("Windows update download exceeded its advertised size.");
                        await output.WriteAsync(buffer.AsMemory(0,read),ct);sha.AppendData(buffer,0,read);
                    }
                    if(count!=length || Convert.ToHexString(sha.GetHashAndReset())!=file.Sha256)throw new IOException("Windows update download checksum or size did not match Microsoft metadata.");
                    await output.FlushAsync(ct);
                }
                }
                complete=true;
            }
            finally{if(created && !complete && File.Exists(path))File.Delete(path);}
            total+=length.Value;payloads.Add(new(file.File,length.Value,file.Sha256));
        }
        var plan=new WindowsImageUpdates.Plan(1,source.ToString(),release.Version.ToString(),payloads.ToArray());
        WindowsImageUpdates.ValidatePlan(plan);
        await using(var output=new FileStream(Path.Combine(folder,"update-plan.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await JsonSerializer.SerializeAsync(output,plan,cancellationToken:ct);
        return plan;
    }

    private static async Task<string> Metadata(HttpClient client,HttpRequestMessage request,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        response.EnsureSuccessStatusCode();
        if(response.RequestMessage?.RequestUri!=request.RequestUri)throw new IOException("Windows update metadata redirected unexpectedly.");
        if(response.Content.Headers.ContentLength>2097152)throw new IOException("Windows update metadata exceeds its budget.");
        await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await input.ReadAsync(buffer,timeout.Token))!=0)
        {if(output.Length+read>2097152)throw new IOException("Windows update metadata exceeds its budget.");await output.WriteAsync(buffer.AsMemory(0,read),timeout.Token);}
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
