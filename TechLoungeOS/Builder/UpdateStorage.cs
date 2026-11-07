using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace TechLoungeOS.Updates;
public static class UpdateStorage
{
    private static readonly SecurityIdentifier Admin=new(WellKnownSidType.BuiltinAdministratorsSid,null);
    private static readonly SecurityIdentifier SystemSid=new(WellKnownSidType.LocalSystemSid,null);
    private static readonly SecurityIdentifier OwnerRights=new("S-1-3-4");
    // .NET includes SYNCHRONIZE on allow rules; neither flag permits changing a DACL.
    private const FileSystemRights OwnerAccess=FileSystemRights.ReadPermissions|FileSystemRights.Synchronize;
    private static DirectorySecurity Acl()
    {
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{Admin,SystemSid})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        // Inherited owner rights stop a split-token owner changing a staged file's DACL.
        acl.AddAccessRule(new FileSystemAccessRule(OwnerRights,FileSystemRights.ReadPermissions,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));return acl;
    }
    public static string Root()
    {
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TechLoungeOS.Builder.Updates");
        UpdatePackage.Unlinked(root);
        if(!Directory.Exists(root))new DirectoryInfo(root).Create(Acl());
        var acl=new DirectoryInfo(root).GetAccessControl();
        var user=WindowsIdentity.GetCurrent().User??throw new IOException("The current Windows account has no security identifier.");
        if(acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !(owner.Equals(Admin)||owner.Equals(user)) || !acl.AreAccessRulesProtected)throw new IOException("Update storage has an unexpected owner or inherited permissions.");
        var rules=acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if(rules.Any(a=>a.AccessControlType==AccessControlType.Allow && !a.IdentityReference.Equals(Admin) && !a.IdentityReference.Equals(SystemSid) && !a.IdentityReference.Equals(OwnerRights)))throw new IOException("Update storage has unsafe access permissions.");
        if(!rules.Any(a=>a.IdentityReference.Equals(OwnerRights) && a.FileSystemRights==OwnerAccess) || rules.Any(a=>a.IdentityReference.Equals(OwnerRights)&&a.FileSystemRights!=OwnerAccess))throw new IOException("Update storage is missing its owner-rights protection.");
        return root;
    }
    public static string Stage()
    {
        var folder=Path.Combine(Root(),Guid.NewGuid().ToString("N"));new DirectoryInfo(folder).Create(Acl());return folder;
    }
    internal static void DiscardStage(string folder,string root)
    {
        folder=Path.GetFullPath(folder);root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if(!string.Equals(Path.GetDirectoryName(folder),root,StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(folder),"N",out _))throw new IOException("Only this operation's update staging folder can be discarded.");
        // Validate the complete tree before deletion. Never follow a substituted link
        // into an installation, another stage or retained rollback evidence.
        void Validate(string path)
        {
            UpdatePackage.Unlinked(path);
            if(Directory.Exists(path))foreach(var entry in Directory.EnumerateFileSystemEntries(path))Validate(entry);
        }
        Validate(folder);
        if(Directory.Exists(folder))Directory.Delete(folder,true);
    }
    public static FileStream Lease(string install,bool exclusive)
    {
        return ResourceLease("install:"+Path.GetFullPath(install).TrimEnd('\\').ToUpperInvariant(),exclusive);
    }
    public static FileStream ResourceLease(string resource,bool exclusive)
    {
        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
        var file=Path.Combine(Root(),id+".instances.lock");UpdatePackage.Unlinked(file);
        return new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,exclusive?FileShare.None:FileShare.ReadWrite);
    }
}
