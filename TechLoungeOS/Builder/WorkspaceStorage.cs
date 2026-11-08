namespace TechLoungeOS;

internal static class WorkspaceStorage
{
    internal record Volume(string Root,string Format,long Free,bool Fixed);
    internal static string Select(IEnumerable<Volume> volumes,long required,IEnumerable<string> excludedVolumes)=>volumes
        .Where(v=>v.Fixed && v.Format.Equals("NTFS",StringComparison.OrdinalIgnoreCase) && v.Free>=required &&
            !excludedVolumes.Any(excluded=>v.Root.TrimEnd('\\',':').Equals(excluded.TrimEnd('\\',':'),StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(v=>v.Free).Select(v=>v.Root).FirstOrDefault()
        ?? throw new IOException("There isn’t enough free space on a suitable local drive. Free some space and try again.");

    internal static string NewPath(string operation,long required,IEnumerable<string>? excludedVolumes=null)
    {
        var volumes=new List<Volume>();
        foreach(var drive in DriveInfo.GetDrives())
        {
            try
            {
                if(drive.DriveType==DriveType.Fixed && drive.IsReady)
                    volumes.Add(new(drive.RootDirectory.FullName,drive.DriveFormat,drive.AvailableFreeSpace,true));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ /* Unavailable drives are not candidates. */ }
        }
        while(volumes.Count>0)
        {
            var root=Select(volumes,required,excludedVolumes??[]);
            var path=Path.Combine(root,"TechLoungeOS-Work",operation+"-"+Guid.NewGuid().ToString("N"));
            try
            {
                Policy.RequireUnlinkedPath(path);
                var parent=new DirectoryInfo(Path.GetDirectoryName(path)!);
                while(!parent.Exists)parent=parent.Parent??throw new IOException("No local storage parent");
                ProbeDirectory(parent.FullName);
                return path;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){volumes.RemoveAll(v=>v.Root==root);}
        }
        throw new IOException("No writable local drive has enough free space. Free some space and try again.");
    }

    internal static void ProbeDirectory(string directory)
    {
        Policy.RequireUnlinkedPath(directory);
        var path=Path.Combine(directory,".tlos-probe-"+Guid.NewGuid().ToString("N"));
        var expected=System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096);
        using var probe=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,4096,FileOptions.DeleteOnClose|FileOptions.WriteThrough);
        probe.Write(expected);probe.Flush(true);probe.Position=0;
        var actual=new byte[expected.Length];probe.ReadExactly(actual);
        if(!actual.AsSpan().SequenceEqual(expected))throw new IOException("The working drive did not read back the files correctly. Check the drive before trying again.");
    }
}
