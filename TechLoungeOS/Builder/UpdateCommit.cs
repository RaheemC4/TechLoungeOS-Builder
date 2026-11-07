using System.Diagnostics;
using System.Text.Json;

namespace TechLoungeOS.Updates;
public record UpdateMove(string Relative,bool Existed,string OldSha256,string NewSha256,bool? WasReadOnly=null);
public record UpdateJournal(string Install,string Backup,UpdateMove[] Moves,bool Complete);
public static class UpdateCommit
{
    public static void ValidateInstall(string folder)
    {
        folder=Path.GetFullPath(folder);UpdatePackage.Unlinked(folder);
        foreach(var root in new[]{Path.GetPathRoot(folder),Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)})
            if(string.Equals(folder.TrimEnd('\\'),root?.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Updates require a dedicated application folder.");
        var marker=Path.Combine(folder,UpdatePackage.Marker);UpdatePackage.Unlinked(marker);
        if(!File.Exists(marker)||new FileInfo(marker).Length>4096)throw new IOException("Extract the whole official Builder package into its own folder before enabling updates.");
        using var json=JsonDocument.Parse(File.ReadAllBytes(marker));
        if(json.RootElement.GetProperty("Product").GetString()!=UpdateTrust.Product)throw new IOException("This is not a recognised Builder installation.");
        if(!File.Exists(Path.Combine(folder,UpdateTrust.MainExe)))throw new IOException("Installed Builder executable is missing.");
    }
    public static string Apply(string install,string extracted,UpdateManifest manifest,Action<int>? afterMove=null)
    {
        install=Path.GetFullPath(install);ValidateInstall(install);UpdatePackage.Unlinked(extracted);
        var journal=Path.Combine(install,".techloungeos-update-journal.json");
        if(File.Exists(journal))throw new IOException("An interrupted update needs recovery before another update can start.");
        foreach(var f in manifest.Files)
        {
            UpdatePackage.Relative(f.Path);var source=Path.Combine(extracted,f.Path);var target=Path.Combine(install,f.Path);
            UpdatePackage.Unlinked(source);UpdatePackage.Unlinked(target);
            if(!File.Exists(source)||new FileInfo(source).Length!=f.Bytes||!UpdatePackage.Hash(source).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Staged update file changed.");
            if(Directory.Exists(target))throw new IOException("An application file conflicts with a folder: "+f.Path);
        }
        var current=FileVersionInfo.GetVersionInfo(Path.Combine(install,UpdateTrust.MainExe)).FileVersion;
        if(!Version.TryParse(current,out var installed)||installed>=Version.Parse(manifest.Version))throw new IOException("This update is no longer newer than the installed app.");
        var backup=Path.Combine(install,".techloungeos-rollback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
        var moves=new List<UpdateMove>();
        void Save(bool complete)
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new UpdateJournal(install,backup,moves.ToArray(),complete));
            using(var file=new FileStream(journal+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
            if(File.Exists(journal))File.Replace(journal+".tmp",journal,null);
            else File.Move(journal+".tmp",journal);
        }
        Save(false);
        try
        {
            // Commit the main executable last. Unknown user files are never moved or deleted.
            foreach(var f in manifest.Files.OrderBy(f=>f.Path==UpdateTrust.MainExe?1:0))
            {
                var target=Path.Combine(install,f.Path);var old=Path.Combine(backup,f.Path);
                var existed=File.Exists(target);var previous=existed?UpdatePackage.Hash(target):"";
                if(existed&&previous.Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))continue;
                var readOnly=existed&&(File.GetAttributes(target)&FileAttributes.ReadOnly)!=0;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                var temp=target+".tlos-new-"+Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(Path.Combine(extracted,f.Path),temp,false);
                    if(!UpdatePackage.Hash(temp).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Copied update file failed verification.");
                    moves.Add(new(f.Path,existed,previous,f.Sha256,readOnly));Save(false);
                    if(readOnly)SetReadOnly(target,false);
                    if(existed)File.Replace(temp,target,old);
                    else File.Move(temp,target);
                    if(readOnly)SetReadOnly(target,true);
                    afterMove?.Invoke(moves.Count);
                }
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            Save(true);File.Move(journal,Path.Combine(backup,"completed.json"));return backup;
        }
        catch
        {
            // Keep both the durable journal and backups if rollback cannot finish.
            Recover(install);throw;
        }
    }
    public static void Recover(string install)
    {
        install=Path.GetFullPath(install);UpdatePackage.Unlinked(install);
        if(install.TrimEnd('\\')==Path.GetPathRoot(install)?.TrimEnd('\\') || install.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid recovery location.");
        var journal=Path.Combine(install,".techloungeos-update-journal.json");UpdatePackage.Unlinked(journal);
        if(!File.Exists(journal)||new FileInfo(journal).Length>1<<20)throw new IOException("No bounded recovery journal exists.");
        var j=JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(journal))??throw new IOException("Invalid recovery journal.");
        if(Path.GetFullPath(j.Install)!=install || Path.GetDirectoryName(Path.GetFullPath(j.Backup))!=install.TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(j.Backup).StartsWith(".techloungeos-rollback-",StringComparison.Ordinal) || j.Moves.Length>4096)throw new IOException("Recovery journal does not belong to this app folder.");
        UpdatePackage.Unlinked(j.Backup);
        foreach(var move in j.Moves)
        {
            UpdatePackage.Relative(move.Relative);
            UpdatePackage.Unlinked(Path.Combine(install,move.Relative));UpdatePackage.Unlinked(Path.Combine(j.Backup,move.Relative));
            if((move.Existed&&!UpdatePackage.IsHash(move.OldSha256))||!UpdatePackage.IsHash(move.NewSha256))throw new IOException("Invalid recovery file hash.");
        }
        foreach(var move in j.Moves.Reverse())
        {
            var target=Path.Combine(install,move.Relative);var old=Path.Combine(j.Backup,move.Relative);
            if(move.Existed)
            {
                if(File.Exists(old))
                {
                    if(UpdatePackage.Hash(old)!=move.OldSha256)throw new IOException("Previous-version backup is damaged; recovery files were retained.");
                    if(File.Exists(target)&&UpdatePackage.Hash(target)!=move.NewSha256&&UpdatePackage.Hash(target)!=move.OldSha256)throw new IOException("A recovery target changed; backups were retained.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var readOnly=move.WasReadOnly??((File.GetAttributes(old)&FileAttributes.ReadOnly)!=0);
                    SetReadOnly(old,false);if(File.Exists(target))SetReadOnly(target,false);
                    if(File.Exists(target))File.Replace(old,target,null);else File.Move(old,target);
                    SetReadOnly(target,readOnly);
                }
                else if(!File.Exists(target)||UpdatePackage.Hash(target)!=move.OldSha256)throw new IOException("A previous-version file is missing; recovery evidence was retained.");
                else if(move.WasReadOnly is bool readOnly)SetReadOnly(target,readOnly);
            }
            else if(File.Exists(target))
            {
                if(!UpdatePackage.Hash(target).Equals(move.NewSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("A recovery target changed; it was not deleted.");
                File.Delete(target);
            }
        }
        File.Move(journal,Path.Combine(j.Backup,"rolled-back.json"));
    }
    private static void SetReadOnly(string path,bool value)
    {
        var attributes=File.GetAttributes(path);
        File.SetAttributes(path,value?attributes|FileAttributes.ReadOnly:attributes&~FileAttributes.ReadOnly);
    }
}
