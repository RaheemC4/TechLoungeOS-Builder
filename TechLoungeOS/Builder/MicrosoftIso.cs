using System.Net;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public sealed record IsoLanguage(string Id,string Name)
{
    public string DisplayName=>Name switch{"English International"=>"English (United Kingdom)","English"=>"English (United States)",_=>Name};
}
public sealed record IsoCatalogue(string Product,string Session,IsoLanguage[] Languages);
public sealed record MicrosoftIsoLink(Uri Address,string FileName,string Description,string? Sha256);
public sealed record IsoDownloadProgress(long Bytes,long Total);
public sealed record IsoDownloadResult(string Path,string Sha256,bool PublishedHashMatched);

public sealed class MicrosoftIso(HttpClient client)
{
    public const string DownloadPage="https://www.microsoft.com/en-us/software-download/windows11";
    private const long MaximumIso=16L<<30;
    private static readonly TimeSpan PatternTimeout=TimeSpan.FromSeconds(2);
    public static HttpClient CreateClient()=>new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromHours(4)};
    private static Match Match(string text,string pattern)=>Regex.Match(text,pattern,RegexOptions.CultureInvariant|RegexOptions.Singleline|RegexOptions.IgnoreCase,PatternTimeout);
    private static bool Numeric(string value)=>Regex.IsMatch(value,@"\A[0-9]{1,16}\z",RegexOptions.CultureInvariant,PatternTimeout);

    public static string ProductId(string html)
    {
        if(html.Length>4*1024*1024)throw new IOException("Microsoft's download page is too large.");
        var section=Match(html,"""<select\b[^>]*\bid\s*=\s*["']product-edition["'][^>]*>(?<items>.*?)</select>""");
        var option=Match(section.Groups["items"].Value,"""<option\b[^>]*\bvalue\s*=\s*["'](?<id>[0-9]{1,16})["'][^>]*>""");
        if(!option.Success)throw new IOException("Microsoft changed its download page. Use the official website or import an existing ISO.");
        return option.Groups["id"].Value;
    }
    public static Uri IsoAddress(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Fragment!=""||
           uri.IdnHost is not ("software.download.prss.microsoft.com" or "software-download.microsoft.com" or "download.microsoft.com")||
           !uri.AbsolutePath.EndsWith(".iso",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use a direct ISO link from Microsoft's download page. Other download sites are not accepted.");
        return uri;
    }
    private static Uri Api(string action,string product,string sku,string session)=>new(
        $"https://www.microsoft.com/software-download-connector/api/{action}?profile=606624d44113&productEditionId={product}&SKU={sku}&friendlyFileName=undefined&Locale=en-US&sessionID={session}");
    private static void CheckReply(JsonElement value)
    {
        if(value.TryGetProperty("Errors",out var errors)&&errors.ValueKind==JsonValueKind.Array&&errors.GetArrayLength()>0)
            throw new IOException("Microsoft could not provide a download. Try again later, use its official website, or import an ISO you already have.");
    }
    public static IsoLanguage[] Languages(string json)
    {
        using var data=JsonDocument.Parse(json);CheckReply(data.RootElement);
        var values=data.RootElement.GetProperty("Skus").EnumerateArray().Select(item=>new IsoLanguage(item.GetProperty("Id").ToString(),item.GetProperty("Language").GetString()??"")).ToArray();
        if(values.Length is <1 or >150||values.Any(v=>!Numeric(v.Id)||v.Name.Length is <1 or >120||v.Name.Any(char.IsControl))||values.Select(v=>v.Id).Distinct().Count()!=values.Length)
            throw new IOException("Microsoft returned an unexpected language list.");
        return values.OrderBy(v=>v.DisplayName.Equals("English (United Kingdom)",StringComparison.OrdinalIgnoreCase)?0:v.DisplayName.Equals("English (United States)",StringComparison.OrdinalIgnoreCase)?1:2).ThenBy(v=>v.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<IsoCatalogue> Catalogue(CancellationToken ct)
    {
        var product=ProductId(await Text(new(DownloadPage),ct));
        var session=Guid.NewGuid().ToString();
        // Establish the ordinary anonymous download session used by Microsoft's own page.
        await Text(new($"https://vlscppe.microsoft.com/tags?org_id=y6jn8c31&session_id={session}"),ct);
        const string instance="560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175";
        var descriptor=await Text(new($"https://ov-df.microsoft.com/mdt.js?instanceId={instance}&PageId=si&session_id={session}"),ct);
        var challenge=Match(descriptor,@"[?&]w=(?<w>[A-F0-9]{1,2048})").Groups["w"].Value;
        var ticks=Match(descriptor,"rticks=.*?(?<ticks>[0-9]{1,24})").Groups["ticks"].Value;
        if(challenge.Length==0||ticks.Length==0)throw new IOException("Microsoft's download session is unavailable. Use the official website or try again later.");
        await Text(new($"https://ov-df.microsoft.com/?session_id={session}&CustomerId={instance}&PageId=si&w={challenge}&mdt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&rticks={ticks}"),ct);
        return new(product,session,Languages(await Text(Api("getskuinformationbyproductedition",product,"undefined",session),ct)));
    }
    public async Task<MicrosoftIsoLink> Resolve(IsoCatalogue catalogue,IsoLanguage language,CancellationToken ct)
    {
        if(!Numeric(catalogue.Product)||!Guid.TryParse(catalogue.Session,out _)||!catalogue.Languages.Contains(language))throw new IOException("Choose a language from the current Microsoft catalogue.");
        return Link(await Text(Api("GetProductDownloadLinksBySku","undefined",language.Id,catalogue.Session),ct));
    }
    public static MicrosoftIsoLink Link(string json)
    {
        using var data=JsonDocument.Parse(json);CheckReply(data.RootElement);
        var options=data.RootElement.GetProperty("ProductDownloadOptions").EnumerateArray().Where(v=>v.GetProperty("DownloadType").GetInt32()==1).ToArray();
        if(options.Length!=1)throw new IOException("Microsoft did not return one Windows 11 x64 ISO. ARM64 media is not supported by this Builder.");
        var item=options[0];var address=IsoAddress(item.GetProperty("Uri").GetString()??"");
        var filename=Uri.UnescapeDataString(Path.GetFileName(address.AbsolutePath));
        if(filename.Length is <5 or >180||filename.IndexOfAny(Path.GetInvalidFileNameChars())>=0||filename!=Path.GetFileName(filename))throw new IOException("Microsoft returned an invalid ISO filename.");
        var hash=item.TryGetProperty("Sha256",out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
        if(hash is not null&&!Regex.IsMatch(hash,@"\A[A-Fa-f0-9]{64}\z",RegexOptions.CultureInvariant,PatternTimeout))throw new IOException("Microsoft returned an invalid SHA-256 value.");
        return new(address,filename,item.TryGetProperty("LocalizedProductDisplayName",out var label)?label.GetString()??filename:filename,hash?.ToUpperInvariant());
    }
    public static MicrosoftIsoLink DirectLink(string value)
    {
        var address=IsoAddress(value.Trim());
        var filename=Uri.UnescapeDataString(Path.GetFileName(address.AbsolutePath));
        if(filename.Length is <5 or >180||filename.IndexOfAny(Path.GetInvalidFileNameChars())>=0||filename!=Path.GetFileName(filename))throw new IOException("The Microsoft link has an invalid ISO filename.");
        return new(address,filename,"Windows 11 ISO from Microsoft's website",null);
    }
    private async Task<HttpResponseMessage> Get(Uri uri,bool iso,CancellationToken ct)
    {
        for(var hop=0;hop<5;hop++)
        {
            if(iso)IsoAddress(uri.AbsoluteUri);
            else if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Fragment!=""||uri.IdnHost is not ("www.microsoft.com" or "vlscppe.microsoft.com" or "ov-df.microsoft.com"))throw new IOException("Microsoft's download request left its permitted hosts.");
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) TechLoungeOS-Builder/0.1");
            request.Headers.Referrer=new(DownloadPage);
            var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next=response.Headers.Location;response.Dispose();
                if(next is null)throw new IOException("Microsoft returned an empty redirect.");
                uri=new(uri,next);continue;
            }
            if(!response.IsSuccessStatusCode){var code=(int)response.StatusCode;response.Dispose();throw new IOException($"Microsoft's download service returned {code}. Try later or use the official website.");}
            return response;
        }
        throw new IOException("Microsoft returned too many download redirects.");
    }
    private async Task<string> Text(Uri address,CancellationToken ct)
    {
        using var response=await Get(address,false,ct);const int limit=4*1024*1024;
        if(response.Content.Headers.ContentLength>limit)throw new IOException("Microsoft's catalogue response is too large.");
        using var input=await response.Content.ReadAsStreamAsync(ct);using var memory=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,ct))>0){if(memory.Length+count>limit)throw new IOException("Microsoft's catalogue response is too large.");memory.Write(buffer,0,count);}
        return new UTF8Encoding(false,true).GetString(memory.ToArray());
    }
    public async Task<IsoDownloadResult> Download(MicrosoftIsoLink link,string destination,IProgress<IsoDownloadProgress>? progress,CancellationToken ct)
    {
        IsoAddress(link.Address.AbsoluteUri);destination=Path.GetFullPath(destination);Policy.RequireUnlinkedPath(destination);
        if(!destination.EndsWith(".iso",StringComparison.OrdinalIgnoreCase)||File.Exists(destination)||!Directory.Exists(Path.GetDirectoryName(destination)))throw new IOException("Choose a new ISO filename in an existing folder. Existing ISOs are never replaced by a download.");
        using var response=await Get(link.Address,true,ct);
        var length=response.Content.Headers.ContentLength;
        if(response.StatusCode!=HttpStatusCode.OK||length is null or <1048576 or >MaximumIso)throw new IOException("Microsoft did not provide a valid ISO download size.");
        var drive=new DriveInfo(Path.GetPathRoot(destination)!);
        Policy.ValidateStorage(drive.DriveFormat,drive.AvailableFreeSpace,length.Value+(1L<<30),false);
        var temporary=destination+".tlos-"+Guid.NewGuid().ToString("N")+".partial";
        var created=false;
        try
        {
            Policy.RequireUnlinkedPath(temporary);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true))
            {
                created=true;using var input=await response.Content.ReadAsStreamAsync(ct);var buffer=new byte[1024*1024];long total=0;int count;var reported=Stopwatch.GetTimestamp();
                while((count=await input.ReadAsync(buffer,ct))>0)
                {
                    total+=count;if(total>length)throw new IOException("The download exceeded Microsoft's advertised size.");
                    await output.WriteAsync(buffer.AsMemory(0,count),ct);hash.AppendData(buffer,0,count);
                    if(total==length||Stopwatch.GetElapsedTime(reported)>=TimeSpan.FromMilliseconds(100)){progress?.Report(new(total,length.Value));reported=Stopwatch.GetTimestamp();}
                }
                if(total!=length)throw new IOException("The ISO download was incomplete. Try again.");
                output.Flush(true);
            }
            var actual=Convert.ToHexString(hash.GetHashAndReset());
            if(link.Sha256 is not null&&!actual.Equals(link.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("The ISO did not match Microsoft's published SHA-256. It has not been imported.");
            Policy.RequireUnlinkedPath(temporary);
            using(var input=File.OpenRead(temporary))
            {
                input.Position=32768;var descriptor=new byte[7];input.ReadExactly(descriptor);
                var signature=Encoding.ASCII.GetString(descriptor,1,5);
                if(signature is not ("CD001" or "BEA01" or "NSR02" or "NSR03"))throw new IOException("The downloaded file is not an ISO installation image.");
            }
            ct.ThrowIfCancellationRequested();Policy.RequireUnlinkedPath(destination);File.Move(temporary,destination,false);created=false;
            return new(destination,actual,link.Sha256 is not null);
        }
        finally{if(created)File.Delete(temporary);}
    }
}
