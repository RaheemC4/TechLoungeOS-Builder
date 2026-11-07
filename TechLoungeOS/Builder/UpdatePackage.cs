using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS.Updates;
public record UpdateFile(string Path,long Bytes,string Sha256);
public record UpdateManifest(string Product,string Version,string Channel,string Architecture,int MinimumWindowsBuild,
    DateTimeOffset PublishedUtc,string PackageUrl,long PackageBytes,string PackageSha256,UpdateFile[] Files);
public record UpdateEnvelope(string Payload,string Signature);

public static class UpdatePackage
{
    public const long MaximumPackageBytes=512L<<20;
    public const long MaximumExtractedBytes=2L<<30;
    public const string Marker=".techloungeos-install.json";
    public static UpdateManifest VerifyEnvelope(byte[] bytes,string publicKey,Version current,int osBuild,DateTimeOffset now)
    {
        if(bytes.Length>1<<20)throw new InvalidDataException("Update manifest exceeds its limit.");
        using var json=JsonDocument.Parse(bytes);UniqueProperties(json.RootElement);
        var envelope=JsonSerializer.Deserialize<UpdateEnvelope>(bytes)??throw new InvalidDataException("Missing update envelope.");
        var payload=Convert.FromBase64String(envelope.Payload);var signature=Convert.FromBase64String(envelope.Signature);
        if(payload.Length>512*1024 || signature.Length!=64)throw new InvalidDataException("Unexpected update signature format.");
        using var key=ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey),out var read);
        if(read!=Convert.FromBase64String(publicKey).Length || key.KeySize!=256 || !key.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new InvalidDataException("The update is not signed by TechLoungeOS Builder's trusted release key.");
        using var signed=JsonDocument.Parse(payload);UniqueProperties(signed.RootElement);
        var m=JsonSerializer.Deserialize<UpdateManifest>(payload)??throw new InvalidDataException("Missing signed update metadata.");
        Validate(m,current,osBuild,now);return m;
    }
    public static void Validate(UpdateManifest m,Version current,int osBuild,DateTimeOffset now)
    {
        if(m.Product!=UpdateTrust.Product || m.Channel!=UpdateTrust.Channel || m.Architecture!="win-x64")throw new InvalidDataException("Update product, channel or architecture does not match this app.");
        if(!Regex.IsMatch(m.Version??"",@"\A\d+\.\d+\.\d+\.\d+\z") || !Version.TryParse(m.Version,out var version) || version<=current)throw new InvalidDataException("The update is not newer than this app.");
        if(m.MinimumWindowsBuild<22000 || m.MinimumWindowsBuild>osBuild || m.PublishedUtc>now.AddDays(1))throw new InvalidDataException("This update's Windows requirements or publication date are invalid.");
        OfficialAsset(m.PackageUrl);
        if(m.PackageBytes<1 || m.PackageBytes>MaximumPackageBytes || !IsHash(m.PackageSha256))throw new InvalidDataException("Invalid update package size/hash.");
        if(m.Files is null || m.Files.Length<3 || m.Files.Length>4096)throw new InvalidDataException("Invalid update file count.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var f in m.Files)
        {
            Relative(f.Path);
            if(!names.Add(f.Path) || f.Bytes<0 || !IsHash(f.Sha256))throw new InvalidDataException("Duplicate or invalid update file.");
            total=checked(total+f.Bytes);if(total>MaximumExtractedBytes)throw new InvalidDataException("Update expands beyond its limit.");
        }
        foreach(var required in new[]{UpdateTrust.MainExe,"updater/TechLoungeOS.Updater.exe",Marker})
            if(!names.Contains(required))throw new InvalidDataException("Update is missing "+required);
    }
    public static Uri OfficialAsset(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="github.com" || uri.Port!=443 || uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="" ||
           !uri.AbsolutePath.StartsWith("/"+UpdateTrust.Repository+"/releases/download/",StringComparison.Ordinal) || uri.AbsolutePath.Contains("..",StringComparison.Ordinal))
            throw new InvalidDataException("Update does not come from the dedicated Builder release repository.");
        return uri;
    }
    public static void Relative(string name)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Length>220 || name.Contains('\\') || name.StartsWith('/') || name.Any(c=>c<32 || ":*?\"<>|".Contains(c)))throw new InvalidDataException("Unsafe update path.");
        foreach(var part in name.Split('/'))
            if(part.Length==0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || Regex.IsMatch(part.Split('.')[0],@"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z",RegexOptions.IgnoreCase))
                throw new InvalidDataException("Unsafe update filename.");
    }
    public static void Unlinked(string path)
    {
        for(var p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
            if((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked update paths are not accepted.");
    }
    public static string Hash(string file){using var s=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(s));}
    public static bool IsHash(string? hash)=>hash!=null && Regex.IsMatch(hash,@"\A[0-9A-Fa-f]{64}\z");
    private static void UniqueProperties(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in e.EnumerateObject()){if(!names.Add(p.Name))throw new InvalidDataException("Duplicate JSON property.");UniqueProperties(p.Value);}
        }
        else if(e.ValueKind==JsonValueKind.Array)foreach(var child in e.EnumerateArray())UniqueProperties(child);
    }
    public static void Extract(string package,string folder,UpdateManifest m)
    {
        Unlinked(package);Unlinked(folder);
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Update extraction requires a new folder.");
        if(new FileInfo(package).Length!=m.PackageBytes || !Hash(package).Equals(m.PackageSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Downloaded update package failed verification.");
        using var zip=ZipFile.OpenRead(package);var files=m.Files.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(zip.Entries.Count>8192)throw new InvalidDataException("Update archive has too many entries.");
        foreach(var e in zip.Entries)
        {
            if(!e.FullName.StartsWith(UpdateTrust.ArchiveRoot,StringComparison.Ordinal))throw new InvalidDataException("Unexpected archive root.");
            var relative=e.FullName[UpdateTrust.ArchiveRoot.Length..];
            if(e.FullName.EndsWith('/')){if(relative.Length>0)Relative(relative.TrimEnd('/'));continue;}
            Relative(relative);
            if(((e.ExternalAttributes>>16)&0xF000)==0xA000 || (e.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked archive entries are forbidden.");
            if(!seen.Add(relative)||!files.TryGetValue(relative,out var f)||e.Length!=f.Bytes)throw new InvalidDataException("Unexpected, duplicate or wrong-sized archive file.");
        }
        if(seen.Count!=files.Count)throw new InvalidDataException("Update archive is incomplete.");
        Directory.CreateDirectory(folder);
        foreach(var e in zip.Entries.Where(e=>!e.FullName.EndsWith('/')))
        {
            var relative=e.FullName[UpdateTrust.ArchiveRoot.Length..];var f=files[relative];var dest=Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using(var input=e.Open())using(var output=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var buffer=new byte[81920];long total=0;int count;
                while((count=input.Read(buffer))>0){total+=count;if(total>f.Bytes)throw new InvalidDataException("Expanded file exceeds declared size.");output.Write(buffer,0,count);}
                if(total!=f.Bytes)throw new InvalidDataException("Truncated update file.");output.Flush(true);
            }
            if(!Hash(dest).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update file failed SHA-256: "+relative);
        }
        var version=FileVersionInfo.GetVersionInfo(Path.Combine(folder,UpdateTrust.MainExe)).FileVersion;
        if(!Version.TryParse(version,out var actual)||actual!=Version.Parse(m.Version))throw new InvalidDataException("Application file version does not match the signed update.");
    }
}
