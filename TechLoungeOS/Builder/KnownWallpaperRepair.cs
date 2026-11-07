using System.Security.Cryptography;
using System.Text.Json;

namespace TechLoungeOS;

// User-approved on 3 November: only this exact altered RTM asset may be corrected.
// The visible custom artwork is retained. No Windows payload is downloaded or bundled.
internal static class KnownWallpaperRepair
{
    // Sparse/WIM backing flags describe the old storage entry and cannot be set with SetFileAttributes.
    private const FileAttributes PreservedAttributes=KnownWallpaperWriteAccess.PreservedAttributes;
    internal const string Component=@"Windows\WinSxS\amd64_microsoft-windows-t..nbackgrounds-client_31bf3856ad364e35_10.0.26100.1_none_da0a6bf461fe5775\img100.jpg";
    internal const string Active=@"Windows\Web\Screen\img100.jpg";
    internal const string AlteredHash="9363F9E568986C3B9BB02C210FA98C0B08A0C8EC301A7C798FA2B5E6AC723584";
    internal const string MicrosoftHash="61643221D2F14BE9E180E507D98ABEA29EEBFB7472797342EC3E25073B7E4CAD";

    // Windows servicing runs after the correction. Recheck the visible source artwork
    // before accepting that later operation, rather than relying on the earlier repair check.
    internal static void VerifyRetainedArtwork(string mount,string workspace)
    {
        var evidence=Path.Combine(workspace,"asset-repair");
        using var before=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"before.json")));
        if(before.RootElement.GetProperty("activeHash").GetString()!=AlteredHash)
            throw new IOException("The recorded custom artwork does not match the approved correction.");
        var active=Path.Combine(mount,Active);
        RequireArtwork(active,AlteredHash,(FileAttributes)before.RootElement.GetProperty("activeAttributes").GetInt32());
        var permissions=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(evidence,"permissions.json")))
            ??throw new IOException("Missing original artwork permissions.");
        if(!permissions.TryGetValue(active,out var security))throw new IOException("Original visible-artwork permissions were not recorded.");
        KnownWallpaperWriteAccess.Run(()=>KnownWallpaperWriteAccess.RequireSameSecurity(active,Convert.FromBase64String(security)));
    }

    internal static void RequireArtwork(string active,string expected,FileAttributes attributes)
    {
        SourcePreservation.RequireImageFile(active,1,16*1024*1024);
        using var file=ImageReadAccess.OpenRead(active);
        if(Convert.ToHexString(SHA256.HashData(file))!=expected ||
            (File.GetAttributes(active)&PreservedAttributes)!=(attributes&PreservedAttributes))
            throw new IOException("Windows servicing changed the source artwork or its attributes. No image will be exported.");
    }

    internal static async Task<bool> TryApply(ICommandRunner run,string mount,string workspace,Action<string> log,CancellationToken ct)
    {
        var target=Path.Combine(mount,Component);var active=Path.Combine(mount,Active);
        if(!File.Exists(target)||!File.Exists(active))return false;
        Policy.RequireUnlinkedPath(mount);
        var windows=Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');
        var offline=Path.GetFullPath(mount).TrimEnd('\\');
        if(offline.StartsWith(@"\\",StringComparison.Ordinal) || offline.Equals(windows,StringComparison.OrdinalIgnoreCase) ||
            offline.StartsWith(windows+"\\",StringComparison.OrdinalIgnoreCase) || windows.StartsWith(offline+"\\",StringComparison.OrdinalIgnoreCase))
            throw new IOException("The checked artwork correction requires a private offline image, separate from the running Windows installation.");
        bool known=false;
        foreach(var path in new[]{target,active}){Policy.RequireUnlinkedPath(Path.GetDirectoryName(path)!);SourcePreservation.RequireImageFile(path,1,16*1024*1024);}
        using(var file=ImageReadAccess.OpenRead(target))known=Convert.ToHexString(SHA256.HashData(file))==AlteredHash;
        if(known){using var file=ImageReadAccess.OpenRead(active);known=Convert.ToHexString(SHA256.HashData(file))==AlteredHash;}
        if(!known)return false;
        ct.ThrowIfCancellationRequested();
        await run.PowerShell($$"""
            $m=@(Get-WindowsImage -Mounted|Where-Object {$_.Path.TrimEnd('\') -ieq {{Policy.Ps(offline)}}})
            if($m.Count -ne 1 -or [string]$m[0].MountStatus -ne 'Ok'){throw 'A registered usable private WIM mount is required for the checked image-file correction'}
            """,ct);
        var donor=Path.Combine(windows,Component[8..]);
        if(!File.Exists(donor))throw new IOException("This image needs a matching Microsoft wallpaper backup which is unavailable on this PC. The update was stopped without repairing other components.");
        var evidence=Path.Combine(workspace,"asset-repair");Policy.RequireUnlinkedPath(evidence);
        ct.ThrowIfCancellationRequested();
        KnownWallpaperWriteAccess.Run(()=>ReplaceVerifiedFiles(target,active,donor,evidence,AlteredHash,MicrosoftHash));
        log("Corrected the known internal wallpaper copy; the source artwork and permissions are unchanged. A complete health scan is required next.");
        return true;
    }

    internal static void ReplaceVerifiedFiles(string target,string active,string donor,string evidence,string expectedOriginal,string expectedDonor)
    {
        foreach(var path in new[]{target,active,donor}){Policy.RequireUnlinkedPath(Path.GetDirectoryName(path)!);SourcePreservation.RequireImageFile(path,1,16*1024*1024);}
        if(target.Equals(active,StringComparison.OrdinalIgnoreCase)||target.Equals(donor,StringComparison.OrdinalIgnoreCase)||active.Equals(donor,StringComparison.OrdinalIgnoreCase))
            throw new IOException("The internal copy, visible artwork and verified replacement must be separate paths.");
        if(Directory.Exists(evidence)||File.Exists(evidence))throw new IOException("Fresh artwork-correction evidence required.");
        Policy.RequireUnlinkedPath(evidence);
        string Hash(string path){using var file=KnownWallpaperWriteAccess.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
        using var replacement=KnownWallpaperWriteAccess.OpenRead(donor); // Deny writes until the correction has finished.
        if(Hash(target)!=expectedOriginal||Hash(active)!=expectedOriginal||Convert.ToHexString(SHA256.HashData(replacement))!=expectedDonor)
            throw new IOException("The internal wallpaper or matching Microsoft backup differs from its checked checksum. No repair was performed.");
        replacement.Position=0;
        Directory.CreateDirectory(evidence);
        var targetAttributes=File.GetAttributes(target);var activeAttributes=File.GetAttributes(active);
        File.WriteAllText(Path.Combine(evidence,"before.json"),JsonSerializer.Serialize(new{targetHash=expectedOriginal,activeHash=expectedOriginal,targetAttributes,activeAttributes}));
        using(var input=KnownWallpaperWriteAccess.OpenRead(target))
        using(var backup=new FileStream(Path.Combine(evidence,"original-store.jpg"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(backup);backup.Flush(true);}
        var temporary=Path.Combine(Path.GetDirectoryName(target)!,"img100.tlos-"+Guid.NewGuid().ToString("N")+".tmp");
        using(var output=KnownWallpaperWriteAccess.CreateNew(temporary)){replacement.CopyTo(output);output.Flush(true);}
        if(Hash(temporary)!=expectedDonor)throw new IOException("Staged wallpaper checksum differs. This private image must not be committed.");
        KnownWallpaperWriteAccess.ReplaceEntry(target,active,temporary,evidence,()=>
        {
            KnownWallpaperWriteAccess.RestoreAttributes(target,targetAttributes&~FileAttributes.ReadOnly);
            File.Replace(temporary,target,null);
        });
        if(Hash(target)!=expectedDonor||Hash(active)!=expectedOriginal||
            (File.GetAttributes(target)&PreservedAttributes)!=(targetAttributes&PreservedAttributes)||
            (File.GetAttributes(active)&PreservedAttributes)!=(activeAttributes&PreservedAttributes))
            throw new IOException("The checked correction did not retain artwork and attributes. This private image must not be committed.");
        File.WriteAllText(Path.Combine(evidence,"result.json"),JsonSerializer.Serialize(new{passed=true,componentHash=expectedDonor,visibleHash=expectedOriginal,permissionsPreserved=true,onlyKnownInternalAsset=true}));
    }
}
