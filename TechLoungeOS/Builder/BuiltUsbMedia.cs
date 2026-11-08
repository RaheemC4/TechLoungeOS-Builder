using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public sealed partial class BootableDrive
{
    public static PreparedUsb LoadCheckpoint(string path)
    {
        path=Path.GetFullPath(path);Policy.RequireUnlinkedPath(path);
        SourcePreservation.RequireImageFile(path,1,16<<20);
        var saved=JsonSerializer.Deserialize<PreparedUsb>(File.ReadAllText(path))??throw new IOException("The prepared setup record could not be read.");
        var workspace=Path.GetDirectoryName(path)!;
        if(Path.GetFileName(path)!="prepared.json" || !workspace.Equals(Path.GetFullPath(saved.Workspace),StringComparison.OrdinalIgnoreCase) ||
            saved.Files is null || saved.Files.Length==0 || (saved.Iso is null)!=(saved.IsoSha256 is null))
            throw new IOException("The prepared setup record does not belong to this folder.");
        Policy.RequireUnlinkedPath(Path.Combine(workspace,"media"));
        if(!Directory.Exists(Path.Combine(workspace,"media")))throw new IOException("The prepared setup files are no longer available.");
        var drive=new DriveInfo(Path.GetPathRoot(workspace)!);
        if(drive.DriveType!=DriveType.Fixed || drive.DriveFormat!="NTFS")throw new IOException("Prepared setup files must be on a local NTFS drive.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long bytes=0;
        foreach(var file in saved.Files)
        {
            if(file is null || string.IsNullOrWhiteSpace(file.RelativePath) || Path.IsPathRooted(file.RelativePath) || file.RelativePath.Contains(':') ||
                file.RelativePath.Split('\\','/').Any(p=>p is "" or "." or "..") || !names.Add(file.RelativePath.Replace('/','\\')) ||
                file.Length<0 || !Regex.IsMatch(file.Sha256??"",@"\A[0-9A-F]{64}\z"))
                throw new IOException("The prepared setup file list is invalid. No USB was changed.");
            bytes=checked(bytes+file.Length);
        }
        if(bytes!=saved.Bytes)throw new IOException("The prepared setup size does not match its file list.");
        return saved with{Workspace=workspace}; // Write rechecks every file before erasing; the temporary ISO need not exist.
    }

    // Only an owned, completed build can reuse its private media tree.
    internal async Task<PreparedUsb> PrepareBuiltMedia(string workspace,UsbDisk disk,CancellationToken ct,string? exportedIso=null,string? exportedHash=null)
    {
        ct.ThrowIfCancellationRequested();
        if(Rejection(disk) is { } reason)throw new IOException(reason);
        workspace=Path.GetFullPath(workspace);
        Policy.RequireUnlinkedPath(workspace);
        if(disk.Volumes.Contains(Path.GetPathRoot(workspace)![..1],StringComparer.OrdinalIgnoreCase))
            throw new IOException("The working files cannot be on the USB target.");
        if((exportedIso is null)!=(exportedHash is null))throw new IOException("The saved ISO has no matching verification record.");
        if(exportedIso is not null)
        {
            Policy.RequireUnlinkedPath(exportedIso);SourcePreservation.RequireImageFile(exportedIso,1,long.MaxValue);
            if(disk.Volumes.Contains(Path.GetPathRoot(Path.GetFullPath(exportedIso))![..1],StringComparer.OrdinalIgnoreCase))throw new IOException("The saved ISO cannot be on the USB target.");
            if(UsbFileTransfer.Hash(exportedIso,ct,_=>{})!=exportedHash)throw new IOException("The saved ISO changed since this build completed. Choose the changed ISO again instead of reusing its previous build files.");
        }
        var state=Path.Combine(workspace,"state.json");
        SourcePreservation.RequireImageFile(state,1,1<<20);
        using(var document=JsonDocument.Parse(File.ReadAllText(state)))
        {
            var root=document.RootElement;var options=root.GetProperty("options");
            var phase=options.GetProperty("ExportIso").GetBoolean()?"Complete":"USB media ready";
            if(root.GetProperty("phase").GetString()!=phase ||
                !workspace.Equals(Path.GetFullPath(options.GetProperty("Workspace").GetString()!),StringComparison.OrdinalIgnoreCase))
                throw new IOException("Only completed build files can be reused. Your USB has not been changed.");
        }
        SourcePreservation.RequireImageFile(Path.Combine(workspace,"build.lock"),0,1<<20);
        var operationPath=Path.Combine(workspace,"usb-preparation.lock");
        if(File.Exists(operationPath))SourcePreservation.RequireImageFile(operationPath,0,1<<20);
        using var buildLease=new FileStream(Path.Combine(workspace,"build.lock"),FileMode.Open,FileAccess.Read,FileShare.Read);
        using var operation=new FileStream(operationPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var checkpoint=Path.Combine(workspace,"prepared.json");
        if(File.Exists(checkpoint))
        {
            var saved=LoadCheckpoint(checkpoint);
            if(saved.Workspace!=workspace || saved.Iso is not null || saved.IsoSha256 is not null)
                throw new IOException("USB preparation checkpoint does not belong to this build.");
            return saved; // Write rechecks every file and the target identity before erasure.
        }
        if(!File.Exists(SplitReceipt(workspace))&&Directory.EnumerateFiles(Path.Combine(workspace,@"media\sources"),"install*.swm").Any())
            throw new IOException("Unrecognised split files were found in the working folder. Files were retained.");
        RecoverSplitSource(workspace);
        var image=Path.Combine(workspace,@"media\sources\install.wim");
        var required=!NeedsFullDataVolume(disk)&&File.Exists(image)&&new FileInfo(image).Length>Fat32Limit?checked(new FileInfo(image).Length+(1L<<30)):1L<<30;
        var storage=new DriveInfo(Path.GetPathRoot(workspace)!);
        if(storage.DriveType==DriveType.Network)throw new IOException("Use local working files, separate from the USB target.");
        Policy.ValidateStorage(storage.DriveFormat,storage.AvailableFreeSpace,required,true);
        log("Reusing your customised setup files directly. No temporary ISO or second media copy is needed.");
        var prepared=await PrepareFiles(workspace,disk,ct);
        var backup=Path.Combine(workspace,"usb-source.wim");
        try{if(File.Exists(backup))File.Delete(backup);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {log("Prepared files are ready; the temporary split-image source was retained: "+error.Message);}
        return prepared;
    }

    private record SplitSource(long Bytes,string Sha256);
    private static string SplitReceipt(string workspace)=>Path.Combine(workspace,"usb-split-source.json");
    private static void RecordSplitSource(string workspace,string image)
    {
        var receipt=SplitReceipt(workspace);
        var expected=new SplitSource(new FileInfo(image).Length,Hash(image));
        if(File.Exists(receipt))
        {
            SourcePreservation.RequireImageFile(receipt,1,1<<20);
            if(JsonSerializer.Deserialize<SplitSource>(File.ReadAllText(receipt))!=expected)
                throw new IOException("The original prepared image changed. Its recovery files were retained.");
        }
        else
        {
            using var output=new FileStream(receipt,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            JsonSerializer.Serialize(output,expected);output.Flush(true);
        }
    }
    private static void RecoverSplitSource(string workspace)
    {
        var receipt=SplitReceipt(workspace);if(!File.Exists(receipt))return;
        SourcePreservation.RequireImageFile(receipt,1,1<<20);
        var expected=JsonSerializer.Deserialize<SplitSource>(File.ReadAllText(receipt))??throw new IOException("Split-image recovery receipt is invalid.");
        var image=Path.Combine(workspace,@"media\sources\install.wim");var backup=Path.Combine(workspace,"usb-source.wim");
        if(File.Exists(image)==File.Exists(backup))throw new IOException("Split-image recovery has an ambiguous source. Files were retained.");
        var source=File.Exists(backup)?backup:image;
        SourcePreservation.RequireImageFile(source,1,long.MaxValue);
        if(new FileInfo(source).Length!=expected.Bytes || Hash(source)!=expected.Sha256)
            throw new IOException("The split-image recovery source changed. Files were retained.");
        var parts=Directory.GetFiles(Path.Combine(workspace,@"media\sources"),"install*.swm");
        foreach(var part in parts)
        {
            if(!Regex.IsMatch(Path.GetFileName(part),@"\Ainstall(?:[2-9]|[1-9][0-9]+)?\.swm\z",RegexOptions.IgnoreCase))
                throw new IOException("Unrecognised split-image recovery file. Files were retained.");
            SourcePreservation.RequireImageFile(part,0,long.MaxValue);
        }
        if(source==backup)File.Move(backup,image,false);
        foreach(var part in parts){File.SetAttributes(part,FileAttributes.Normal);File.Delete(part);}
    }
}
