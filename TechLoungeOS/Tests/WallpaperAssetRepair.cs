using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

// Explicitly approved native test only. Never called by the shipped Builder.
internal static class WallpaperAssetRepair
{
    internal const string Component=@"Windows\WinSxS\amd64_microsoft-windows-t..nbackgrounds-client_31bf3856ad364e35_10.0.26100.1_none_da0a6bf461fe5775\img100.jpg";
    internal const string Active=@"Windows\Web\Screen\img100.jpg";
    internal const string GhostHash="9363F9E568986C3B9BB02C210FA98C0B08A0C8EC301A7C798FA2B5E6AC723584";
    internal const string MicrosoftHash="61643221D2F14BE9E180E507D98ABEA29EEBFB7472797342EC3E25073B7E4CAD";
    private static string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
    private static string Acl(string path)=>new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Owner|AccessControlSections.Group|AccessControlSections.Access);

    internal static void Repair(string mount,string workspace)
    {
        if(mount!=@"S:\feature-repair1609\mount")throw new IOException("Exact owned private native mount required.");
        AssetWriteAccess.Run(()=>Replace(Path.Combine(mount,Component),Path.Combine(mount,Active),@"S:\wallpaper1605\img100.jpg",workspace,GhostHash,MicrosoftHash,true));
    }
    private static void Replace(string target,string active,string donor,string workspace,string oldHash,string donorHash,bool native=false)
    {
        foreach(var file in new[]{target,active,donor}){Policy.RequireUnlinkedPath(Path.GetDirectoryName(file)!);SourcePreservation.RequireImageFile(file,1,16*1024*1024);}
        if(target==active || Hash(target)!=oldHash || Hash(active)!=oldHash || Hash(donor)!=donorHash)
            throw new IOException("Expected exact Ghost artwork and canonical Microsoft donor required.");
        Directory.CreateDirectory(workspace);
        var before=new {targetHash=Hash(target),activeHash=Hash(active),targetAttributes=File.GetAttributes(target),activeAttributes=File.GetAttributes(active),targetAcl=Acl(target),activeAcl=Acl(active)};
        File.WriteAllText(Path.Combine(workspace,"before.json"),JsonSerializer.Serialize(before));
        var temporary=Path.Combine(Path.GetDirectoryName(target)!,"img100.tlos-1609.tmp");
        if(Directory.Exists(temporary) || (File.Exists(temporary)&&Hash(temporary)!=donorHash))throw new IOException("Unexpected repair staging file.");
        File.Copy(target,Path.Combine(workspace,"original-store.jpg"),false);
        // Replacing a directory entry separates its hard link. Writing the existing file would also change Ghost's visible image.
        if(!File.Exists(temporary))
        {
            using var input=new FileStream(donor,FileMode.Open,FileAccess.Read,FileShare.Read);
            using var output=AssetWriteAccess.CreateNew(temporary);input.CopyTo(output);output.Flush(true);
        }
        if(Hash(temporary)!=donorHash)throw new IOException("Staged donor checksum mismatch.");
        void ReplaceEntry()
        {
            try{File.SetAttributes(target,before.targetAttributes&~FileAttributes.ReadOnly);File.Replace(temporary,target,null);}
            finally{File.SetAttributes(active,before.activeAttributes);File.SetAttributes(target,before.targetAttributes);}
        }
        if(native)AssetWriteAccess.ReplaceEntry(target,active,temporary,workspace,ReplaceEntry);else ReplaceEntry();
        if(Hash(target)!=donorHash || Hash(active)!=oldHash || Acl(target)!=before.targetAcl || Acl(active)!=before.activeAcl)
            throw new IOException("Targeted repair did not preserve the visible artwork and permissions. Do not commit this image.");
        File.WriteAllText(Path.Combine(workspace,"result.json"),JsonSerializer.Serialize(new{passed=true,componentHash=Hash(target),visibleHash=Hash(active),permissionsPreserved=true,component=target}));
    }
    internal static void Fixture(string root)
    {
        Directory.CreateDirectory(root);
        var store=Path.Combine(root,"store.jpg");var active=Path.Combine(root,"active.jpg");var donor=Path.Combine(root,"donor.jpg");
        File.WriteAllText(store,"Ghost visible artwork");File.WriteAllText(donor,"Microsoft component backup");
        if(!CreateHardLinkW(active,store,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        // Independent literal byte checks accompany checksum and ACL checks in the real operation.
        Replace(store,active,donor,Path.Combine(root,"evidence"),Hash(store),Hash(donor));
        if(File.ReadAllText(active)!="Ghost visible artwork" || File.ReadAllText(store)!="Microsoft component backup")throw new IOException("Hard-link isolation regression.");
        Console.WriteLine("PASS: canonical component replacement retains the original artwork hard link, attributes, ACLs and backup bytes.");
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateHardLinkW(string link,string existing,nint security);
}
