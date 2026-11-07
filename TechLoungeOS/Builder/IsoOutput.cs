namespace TechLoungeOS;

internal static class IsoOutput
{
    internal record Destination(string Path,string? PreviousHash);
    private static void Unlinked(string path)
    {
        Policy.RequireUnlinkedPath(path);
        try
        {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked ISO files are not accepted.");
        }
        catch(FileNotFoundException){ }
        catch(DirectoryNotFoundException){ }
    }
    internal static string StagingPath(string destination)
    {
        if(string.IsNullOrWhiteSpace(destination))throw new IOException("Choose where to save your customised ISO.");
        var full=Path.GetFullPath(destination);
        if(!Path.GetExtension(full).Equals(".iso",StringComparison.OrdinalIgnoreCase))throw new IOException("Choose an ISO filename ending in .iso.");
        Unlinked(full);
        return Path.Combine(Path.GetDirectoryName(full)!,".tlos-"+Guid.NewGuid().ToString("N")+".iso");
    }
    internal static Destination Capture(string destination)
    {
        var path=Path.GetFullPath(destination);Unlinked(path);
        if(Directory.Exists(path))throw new IOException("Choose an ISO filename, not a folder.");
        return new(path,File.Exists(path)?BootableDrive.Hash(path):null);
    }
    internal static void Commit(string verifiedIso,Destination destination)
    {
        verifiedIso=Path.GetFullPath(verifiedIso);Unlinked(verifiedIso);Unlinked(destination.Path);
        if(verifiedIso.Equals(destination.Path,StringComparison.OrdinalIgnoreCase) || !Path.GetDirectoryName(verifiedIso)!.Equals(Path.GetDirectoryName(destination.Path),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Verified ISO replacement must use a separate temporary file in the destination folder.");
        // ReplaceFile requires the staged source handle to be closed on Windows.
        // Verify again after replacement and retain the old backup if readback fails.
        var expected=BootableDrive.Hash(verifiedIso);
        string? backup=null;
        if(destination.PreviousHash is null)
            File.Move(verifiedIso,destination.Path,false); // A newly appeared file must never be overwritten.
        else
        {
            using var existing=new FileStream(destination.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            var actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(existing));
            if(!actual.Equals(destination.PreviousHash,StringComparison.OrdinalIgnoreCase))throw new IOException("The destination ISO changed during the build. It was not replaced; choose another filename.");
            backup=Path.Combine(Path.GetDirectoryName(destination.Path)!,".tlos-backup-"+Guid.NewGuid().ToString("N")+".iso");
            File.Replace(verifiedIso,destination.Path,backup);
        }
        if(!BootableDrive.Hash(destination.Path).Equals(expected,StringComparison.OrdinalIgnoreCase))
            throw new IOException("The saved ISO did not pass its final checksum check."+(backup is null?"":" The previous ISO is retained at "+backup));
        if(backup is not null)
        {
            if(!BootableDrive.Hash(backup).Equals(destination.PreviousHash,StringComparison.OrdinalIgnoreCase))throw new IOException("The saved ISO is verified, but the previous-file backup changed. It was retained at "+backup);
            File.Delete(backup);
        }
    }
    internal static void RemoveTemporary(string iso,string expectedHash)
    {
        Unlinked(iso);
        if(!Path.GetFileName(iso).StartsWith(".tlos-",StringComparison.Ordinal) || !BootableDrive.Hash(iso).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Temporary ISO cleanup was skipped because its identity changed.");
        File.Delete(iso);
    }
}

internal record UsbBuildResult(string Iso,bool Temporary);
