using System.Security.Cryptography;
using System.Text.Json;

namespace TechLoungeOS;

// Only active WinPE/Setup artwork changes. Component-store images and WinRE stay owned by Windows.
internal static class WinPeArtwork
{
    // winre.jpg is also an active WinPE startup asset in this source. This runs
    // only against private boot.wim mounts, never the installed WinRE image.
    private static readonly string[] Pictures = [@"Windows\System32\winpe.jpg", @"Windows\System32\winre.jpg", @"Windows\System32\setup.bmp", @"sources\background.bmp",
        @"Windows\System32\config\systemprofile\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper"];
    internal static bool IsPicture(string path) => Pictures.Contains(path, StringComparer.OrdinalIgnoreCase);

    internal static Dictionary<string,string> Stage(string mount,string evidence)
    {
        ValidateReference(mount,@"Windows\System32\config\SOFTWARE",@"Microsoft\Windows NT\CurrentVersion\WinPE","CustomBackground");
        ValidateReference(mount,@"Windows\System32\config\DEFAULT",@"Control Panel\Desktop","Wallpaper");
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var relative in Pictures)
        {
            var path=Path.Combine(mount,relative);
            Policy.RequireUnlinkedPath(Path.GetDirectoryName(path)!);
            if(Directory.Exists(path))throw new IOException("A Windows Setup background is a folder. The source boot image was retained.");
            // The stock WinPE path is supplied even when absent. Other assets are replaced only if inherited.
            if(!File.Exists(path)&&relative!=Pictures[0])continue;
            var bytes=Read(relative.EndsWith(".bmp",StringComparison.OrdinalIgnoreCase)?"startup.bmp":"startup.jpg");
            Replace(path,bytes,Path.Combine(evidence,result.Count.ToString()));
            result.Add(relative,Convert.ToHexString(SHA256.HashData(bytes)));
        }
        return result;
    }

    internal static void StageMedia(string media,string evidence)
    {
        var path=Path.Combine(media,@"sources\background.bmp");
        Policy.RequireUnlinkedPath(path);
        if(Directory.Exists(path))throw new IOException("The Windows Setup background is a folder.");
        if(File.Exists(path))Replace(path,Read("startup.bmp"),evidence);
    }

    private static void ValidateReference(string mount,string hiveRelative,string branch,string name)
    {
        var hive=Path.Combine(mount,hiveRelative);if(!File.Exists(hive))return;
        var value=SourcePreservation.ReadStringValue(hive,branch,name);if(string.IsNullOrWhiteSpace(value))return;
        var relative=value.Replace('/', '\\');
        foreach(var prefix in new[]{@"%SystemRoot%\",@"%windir%\",@"X:\Windows\"})
            if(relative.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){relative=@"Windows\"+relative[prefix.Length..];break;}
        if(!IsPicture(relative))throw new IOException("This WinPE uses an unrecognised startup background ("+name+"). Its original image was retained rather than leaving mixed setup branding.");
    }

    internal static byte[] Read(string name)
    {
        using var source=typeof(WinPeArtwork).Assembly.GetManifestResourceStream("TechLoungeOS.PeArtwork."+name)??throw new IOException("The Windows Setup background was not packaged.");
        using var bytes=new MemoryStream();source.CopyTo(bytes);return bytes.ToArray();
    }

    private static void Replace(string path,byte[] bytes,string evidence)
    {
        Policy.RequireUnlinkedPath(path);Policy.RequireUnlinkedPath(evidence);
        if(Directory.Exists(evidence)||File.Exists(evidence))throw new IOException("Existing startup-artwork recovery files need review.");
        Directory.CreateDirectory(evidence);
        var temporary=Path.Combine(evidence,"new-picture.tmp");var backup=Path.Combine(evidence,"original-picture");
        File.WriteAllBytes(temporary,bytes);
        var existed=File.Exists(path);var attributes=existed?File.GetAttributes(path):FileAttributes.Normal;
        if(existed)SourcePreservation.RequireImageFile(path,1,32L<<20);
        string Hash(string file)
        {
            try{return BootableDrive.Hash(file);}
            catch(UnauthorizedAccessException){return ImageReadAccess.Run(()=>{using var input=ImageReadAccess.OpenRead(file);return Convert.ToHexString(SHA256.HashData(input));});}
        }
        var original=existed?Hash(path):null;
        void Commit()
        {
            try
            {
                if(existed){File.SetAttributes(path,attributes&~FileAttributes.ReadOnly);File.Replace(temporary,path,backup);}
                else File.Move(temporary,path);
            }
            finally
            {
                // Restore the original file object too, including its untouched WinSxS hard links.
                if(existed)KnownWallpaperWriteAccess.RestoreAttributes(File.Exists(backup)?backup:path,attributes);
            }
            KnownWallpaperWriteAccess.RestoreAttributes(path,attributes);
        }
        try{Commit();}
        catch(Exception error) when(existed&&!File.Exists(backup)&&File.Exists(temporary)&&
            (error is UnauthorizedAccessException||error is IOException {InnerException:System.ComponentModel.Win32Exception {NativeErrorCode:5}}))
        {
            KnownWallpaperWriteAccess.Run(()=>
            {
                var security=KnownWallpaperWriteAccess.ReadSecurity(path);
                KnownWallpaperWriteAccess.ReplaceEntry(path,path,temporary,evidence,()=>
                {
                    Commit();
                    KnownWallpaperWriteAccess.RestoreSecurity(backup,security);
                });
            });
        }
        if(Hash(path)!=Convert.ToHexString(SHA256.HashData(bytes))||
            existed&&(Hash(backup)!=original||(File.GetAttributes(backup)&KnownWallpaperWriteAccess.PreservedAttributes)!=(attributes&KnownWallpaperWriteAccess.PreservedAttributes)))
            throw new IOException("Windows Setup artwork readback failed. The private boot image must not be saved.");
        File.WriteAllText(Path.Combine(evidence,"result.json"),JsonSerializer.Serialize(new{path,original,updated=Hash(path),originalFileRetained=existed}));
    }
}
