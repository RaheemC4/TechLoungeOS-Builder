using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TechLoungeOS;

// Microsoft-owned catalogues and original signed archives only. Never acquire account
// tickets, decrypt packages, change TLS validation or execute an installer on the host.
public static class StorePackageDownload
{
    private const string Fe3 = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx";
    private const string Service = "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService";
    private static readonly XNamespace S = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace A = "http://www.w3.org/2005/08/addressing";
    private static readonly XNamespace W = Service;
    public record Archive(string Moniker, string FileName, long Size, string Sha256, string Digest, string UpdateId, string Revision);

    public static async Task Acquire(string product, string identity, string folder, Version target, CancellationToken ct)
    {
        if (!StoreXbox.Products.Contains((product, identity))) throw new IOException("Unapproved Windows component.");
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(8) };
        if (identity == "Microsoft.StorePurchaseApp")
        {
            await Catalogue(client, product, identity, folder, target, ct);
            return;
        }
        var platformVersion = ((ulong)target.Major << 48) | ((ulong)target.Minor << 32) | ((ulong)target.Build << 16) | (uint)Math.Max(0,target.Revision);
        var uri = new Uri($"https://sfdataservice.microsoft.com/package-retrieval/Packages?productId={product}&architecture=x64&platformName=Windows.Desktop&platformVersion={platformVersion}");
        var data=await Read(client, new HttpRequestMessage(HttpMethod.Get, uri), 4 << 20, ct);
        if(data.Length==0)
        {
            await Catalogue(client,product,identity,folder,target,ct);
            return;
        }
        using var json = JsonDocument.Parse(data);
        var root = json.RootElement;
        if (root.GetProperty("productId").GetString() != product) throw new IOException("Microsoft package catalogue product mismatch.");
        var entries = root.GetProperty("dependencies").EnumerateArray().Prepend(root.GetProperty("package")).ToArray();
        if (entries.Length is < 1 or > 48) throw new IOException("Unsupported Microsoft dependency set.");
        long total = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var item = entries[index];
            var name = item.GetProperty("fullName").GetString()!;
            var actualIdentity = name.Split('_')[0];
            if (index == 0 ? actualIdentity != identity : !StoreXbox.Dependency(actualIdentity)) throw new IOException("Unexpected Microsoft package identity.");
            var size = item.GetProperty("sizeInBytes").GetInt64(); total = checked(total + size);
            if (total > 2L << 30) throw new IOException("Microsoft package set exceeds its size budget.");
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension is not (".appx" or ".appxbundle" or ".msix" or ".msixbundle")) extension = index == 0 ? ".msixbundle" : ".appx";
            // This service currently repeats the main bundle digest on every dependency.
            // Require HTTPS for dependencies; Prepare verifies their Microsoft signatures
            // and records their actual SHA-256 before they can be included in an image.
            var expectedHash = index == 0 ? Convert.ToHexString(Convert.FromBase64String(item.GetProperty("digest").GetString()!)) : null;
            await Download(client, new Uri(item.GetProperty("url").GetString()!), Path.Combine(folder, $"official-{index}{extension}"), size, expectedHash, ct);
        }
    }

    public static bool AllowedDownload(Uri uri) => uri.IsAbsoluteUri && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        ((uri.Scheme == "https" && uri.Host == "sfdataservice.microsoft.com") ||
         (uri.Scheme is "https" or "http" && uri.Host.EndsWith(".delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase)));

    public static bool ValidArchive(Uri uri, long size, string? hash) => AllowedDownload(uri) && size is > 0 and <= 2L * 1024 * 1024 * 1024 &&
        (hash == null ? uri.Scheme == "https" && uri.Host == "sfdataservice.microsoft.com" : hash.Length == 64 && hash.All(Uri.IsHexDigit));

    private static async Task Download(HttpClient client, Uri uri, string destination, long size, string? hash, CancellationToken ct)
    {
        // Some Microsoft FE3 archives are published on HTTP-only CDN hosts. Their exact
        // SHA-256 is obtained over HTTPS and Authenticode is verified before staging.
        if (!ValidArchive(uri,size,hash))
            throw new IOException("Unsupported Microsoft archive URL, size or SHA-256.");
        var partial = destination + ".partial";
        using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long length = 0; int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                length += read;
                if (length > size) throw new IOException("Microsoft archive exceeds its declared size.");
                await output.WriteAsync(buffer.AsMemory(0,read), ct);
            }
            if (length != size) throw new IOException("Incomplete Microsoft archive.");
        }
        if (hash != null && !RuntimePack.Hash(partial).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Microsoft archive SHA-256 mismatch.");
        File.Move(partial,destination,false);
    }

    public static XDocument Xml(string text) => Xml(text,0);
    private static XDocument Xml(string text,int depth)
    {
        if(depth > 4) throw new IOException("Microsoft catalogue XML fragments are too deeply nested.");
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 << 20 });
        var document = XDocument.Load(reader);
        foreach (var fragment in document.Descendants().Where(e=>e.Name.LocalName=="Xml" && !e.HasElements).ToArray())
            if (fragment.Value.TrimStart().StartsWith('<')) fragment.ReplaceNodes(Xml("<Fragment>"+fragment.Value+"</Fragment>",depth+1).Root!.Nodes());
        return document;
    }
    private static string Value(XElement element,string name) => element.Elements().Single(e=>e.Name.LocalName==name).Value;

    public static Archive SelectBundle(XDocument document,string identity)
    {
        var files = document.Descendants().Where(e=>e.Name.LocalName=="File" && ((string?)e.Attribute("InstallerSpecificIdentifier"))?.StartsWith(identity+"_",StringComparison.Ordinal)==true)
            .Where(e=>((string?)e.Attribute("InstallerSpecificIdentifier"))!.Contains("_neutral_~_8wekyb3d8bbwe",StringComparison.Ordinal) && Path.GetExtension((string?)e.Attribute("FileName")) is ".msixbundle" or ".appxbundle")
            .OrderByDescending(e=>Version.Parse(((string)e.Attribute("InstallerSpecificIdentifier")!).Split('_')[1])).ToArray();
        if (files.Length==0) throw new IOException("No applicable original "+identity+" bundle in Microsoft's catalogue.");
        return Describe(document,files[0]);
    }

    public static Archive[] CatalogueDependencies(XDocument document) => document.Descendants()
        .Where(e=>e.Name.LocalName=="File" && StoreXbox.Dependency(((string?)e.Attribute("InstallerSpecificIdentifier") ?? "").Split('_')[0])
            && (((string?)e.Attribute("InstallerSpecificIdentifier"))?.Contains("_x64__",StringComparison.Ordinal)==true || ((string?)e.Attribute("InstallerSpecificIdentifier"))?.Contains("_x86__",StringComparison.Ordinal)==true)
            && Path.GetExtension((string?)e.Attribute("FileName")) is ".appx" or ".msix")
        .GroupBy(e=>(string)e.Attribute("InstallerSpecificIdentifier")!).Select(g=>Describe(document,g.First())).ToArray();

    private static Archive Describe(XDocument document,XElement file)
    {
        var key=Value(file.Parent!.Parent!.Parent!,"ID");
        var info=document.Descendants().Single(e=>e.Name.LocalName=="UpdateInfo" && e.Elements().Any(c=>c.Name.LocalName=="ID" && c.Value==key));
        var update=info.Descendants().Single(e=>e.Name.LocalName=="UpdateIdentity" && e.Parent!.Name.LocalName=="Xml");
        var digest=file.Elements().Single(e=>e.Name.LocalName=="AdditionalDigest" && (string?)e.Attribute("Algorithm")=="SHA256").Value;
        var updateId=(string)update.Attribute("UpdateID")!; var revision=(string)update.Attribute("RevisionNumber")!;
        if (!Guid.TryParse(updateId,out _) || !uint.TryParse(revision,out _)) throw new IOException("Invalid Microsoft update identity.");
        return new((string)file.Attribute("InstallerSpecificIdentifier")!, (string)file.Attribute("FileName")!,
            long.Parse((string)file.Attribute("Size")!,CultureInfo.InvariantCulture),Convert.ToHexString(Convert.FromBase64String(digest)),(string)file.Attribute("Digest")!,updateId,revision);
    }

    private static XElement E(string name,object? content=null) => new(W+name,content);
    private static async Task<XDocument> Post(HttpClient client,string action,XElement body,bool secured,CancellationToken ct)
    {
        XNamespace security="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd",utility="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd",tickets="http://schemas.microsoft.com/msus/2014/10/WindowsUpdateAuthorization";
        var endpoint=Fe3+(secured?"/secured":"");
        var envelope=new XElement(S+"Envelope",new XAttribute(XNamespace.Xmlns+"s",S),new XAttribute(XNamespace.Xmlns+"a",A),
            new XElement(S+"Header",new XElement(A+"Action",new XAttribute(S+"mustUnderstand","1"),Service+"/"+action),new XElement(A+"MessageID","urn:uuid:"+Guid.NewGuid()),new XElement(A+"To",new XAttribute(S+"mustUnderstand","1"),endpoint),
                new XElement(security+"Security",new XAttribute(S+"mustUnderstand","1"),new XElement(utility+"Timestamp",new XElement(utility+"Created",DateTime.UtcNow.ToString("o")),new XElement(utility+"Expires",DateTime.UtcNow.AddMinutes(5).ToString("o"))),
                    new XElement(tickets+"WindowsUpdateTicketsToken",new XAttribute(utility+"id","ClientMSA"),new XElement(tickets+"TicketType",new XAttribute("Name","MSA"),new XAttribute("Version","1.0"),new XAttribute("Policy","MBI_SSL"),new XElement(tickets+"User"))))),new XElement(S+"Body",body));
        using var request=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=new StringContent(envelope.ToString(SaveOptions.DisableFormatting),Encoding.UTF8,"application/soap+xml")};
        return Xml(Encoding.UTF8.GetString(await Read(client,request,8<<20,ct)));
    }

    private static async Task Catalogue(HttpClient client,string product,string identity,string folder,Version target,CancellationToken ct)
    {
        using var metadata=JsonDocument.Parse(await Read(client,new HttpRequestMessage(HttpMethod.Get,
            $"https://displaycatalog.mp.microsoft.com/v7.0/products/{product}?market=US&languages=en-US"),4<<20,ct));
        var item=metadata.RootElement.GetProperty("Product");
        if(item.GetProperty("ProductId").GetString()!=product)throw new IOException("Microsoft component catalogue product mismatch.");
        var categories=item.GetProperty("DisplaySkuAvailabilities").EnumerateArray()
            .Select(s=>s.GetProperty("Sku").GetProperty("Properties").GetProperty("FulfillmentData"))
            .Where(s=>s.GetProperty("PackageFamilyName").GetString()==identity+"_8wekyb3d8bbwe")
            .Select(s=>s.GetProperty("WuCategoryId").GetString()!).Distinct().ToArray();
        if(categories.Length!=1 || !Guid.TryParse(categories[0],out _))throw new IOException("Microsoft component catalogue family/category mismatch.");
        var category=categories[0];
        var cookie=await Post(client,"GetCookie",E("GetCookie",new[]{E("oldCookie"),E("lastChange","2015-10-21T17:01:07Z"),E("currentTime",DateTime.UtcNow.ToString("o")),E("protocolVersion","1.40")}),false,ct);
        var encrypted=cookie.Descendants().Single(e=>e.Name.LocalName=="EncryptedData").Value;
        var expiration=cookie.Descendants().Single(e=>e.Name.LocalName=="Expiration").Value;
        // Catalogue platform prerequisite IDs, not host update state. See STORE-XBOX.md.
        var platformIds="1 2 3 11 19 544 549 2359974 2359977 5169044 8788830 23110993 23110994 54341900 54343656 59830006 59830007 59830008 60484010 62450018 62450019 62450020 66027979 66053150 97657898 98822896 98959022 98959023 98959024 98959025 98959026 104433538 104900364 105489019 117765322 129905029 130040031 132387090 132393049 132399034 138537048 140377312 143747671 158941041 158941042 158941043 158941044 159123858 159130928 164836897 164847386 164848327 164852241 164852244 164852246 164852252 164852253";
        var attributes=$"App=WU;OSArchitecture=AMD64;OSVersion={target};DeviceFamily=Windows.Desktop;FlightRing=Retail;IsFlightingEnabled=0;";
        var parameters=E("parameters",new[]{
            E("ExpressQuery","false"),E("InstalledNonLeafUpdateIDs",platformIds.Split(' ').Select(id=>E("int",id))),E("OtherCachedUpdateIDs"),
            E("SkipSoftwareSync","false"),E("NeedTwoGroupOutOfScopeUpdates","true"),E("FilterAppCategoryIds",E("CategoryIdentifier",E("Id",category))),
            E("TreatAppCategoryIdsAsInstalled","true"),E("AlsoPerformRegularSync","false"),E("ComputerSpec"),
            E("ExtendedUpdateInfoParameters",new[]{E("XmlUpdateFragmentTypes",E("XmlUpdateFragmentType","Extended")),E("Locales",new[]{E("string","en-US"),E("string","en")})}),
            E("ClientPreferredLanguages",E("string","en-US")),
            E("ProductsParameters",new[]{E("SyncCurrentVersionOnly","false"),E("DeviceAttributes",attributes),E("CallerAttributes","Interactive=1;IsSeeker=0;"),E("Products")})});
        var sync=await Post(client,"SyncUpdates",E("SyncUpdates",new[]{E("cookie",new[]{E("Expiration",expiration),E("EncryptedData",encrypted)}),parameters}),false,ct);
        var archives=CatalogueDependencies(sync).Prepend(SelectBundle(sync,identity)).ToArray();
        if(archives.Length>24 || archives.Sum(a=>a.Size)>2L<<30)throw new IOException("Store Purchase package set exceeds its budget.");
        for(var index=0;index<archives.Length;index++)
        {
            var archive=archives[index];
            var locations=await Post(client,"GetExtendedUpdateInfo2",E("GetExtendedUpdateInfo2",new[]{E("updateIDs",E("UpdateIdentity",new[]{E("UpdateID",archive.UpdateId),E("RevisionNumber",archive.Revision)})),E("infoTypes",E("XmlUpdateFragmentType","FileUrl")),E("deviceAttributes",attributes)}),true,ct);
            var location=locations.Descendants().FirstOrDefault(e=>e.Name.LocalName=="FileLocation" && e.Elements().Any(c=>c.Name.LocalName=="FileDigest" && c.Value==archive.Digest)) ?? throw new IOException("No digest-matched Microsoft archive URL.");
            await Download(client,new Uri(Value(location,"Url")),Path.Combine(folder,$"official-purchase-{index}"+Path.GetExtension(archive.FileName)),archive.Size,archive.Sha256,ct);
        }
    }

    private static async Task<byte[]> Read(HttpClient client,HttpRequestMessage request,int maximum,CancellationToken ct)
    {
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();
        var buffer=new byte[65536];int count;
        while((count=await input.ReadAsync(buffer,ct))>0){if(output.Length+count>maximum)throw new IOException("Microsoft catalogue response exceeds its limit.");output.Write(buffer,0,count);}
        return output.ToArray();
    }
}
