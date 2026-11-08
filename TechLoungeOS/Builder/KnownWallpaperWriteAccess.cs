using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Runtime.ExceptionServices;
using Microsoft.Win32.SafeHandles;

namespace TechLoungeOS;

// Existing administrator rights are enabled only on a disposable impersonation token.
internal static class KnownWallpaperWriteAccess
{
    internal const FileAttributes PreservedAttributes=FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive|FileAttributes.NotContentIndexed;

    internal static void RestoreAttributes(string path,FileAttributes attributes)
    {
        // Restore after security writes, which can set Archive again. Backup intent uses only
        // the caller's scoped restore right; no permanent source ACL grant is required.
        using var handle=CreateFileW(SourcePreservation.NativePath(path),0x100,7,0,3,0x02000000,0);
        if(handle.IsInvalid)throw new IOException("Cannot restore checked image attributes: "+path,new Win32Exception(Marshal.GetLastWin32Error()));
        var value=attributes&PreservedAttributes;
        var info=new BasicInfo{Attributes=(uint)(value==0?FileAttributes.Normal:value)};
        if(!SetFileInformationByHandle(handle,0,ref info,(uint)Marshal.SizeOf<BasicInfo>()))
            throw new IOException("Cannot restore checked image attributes: "+path,new Win32Exception(Marshal.GetLastWin32Error()));
    }
    internal static void Run(Action action)
    {
        using var current=WindowsIdentity.GetCurrent(TokenAccessLevels.Query|TokenAccessLevels.Duplicate);
        const TokenAccessLevels access=TokenAccessLevels.Query|TokenAccessLevels.Duplicate|TokenAccessLevels.AdjustPrivileges|TokenAccessLevels.Impersonate;
        if(!DuplicateTokenEx(current.AccessToken,(uint)access,0,2,2,out var token)){var error=Failure();token?.Dispose();throw error;}
        using(token)
        {
            foreach(var name in new[]{"SeBackupPrivilege","SeRestorePrivilege"})
            {
                if(!LookupPrivilegeValue(null,name,out var luid))throw Failure();
                var state=new Privileges{Count=1,Luid=luid,Attributes=2};
                if(!AdjustTokenPrivileges(token,false,ref state,0,0,0)||Marshal.GetLastWin32Error()!=0)throw Failure();
            }
            WindowsIdentity.RunImpersonated(token,action);
        }
    }
    internal static FileStream CreateNew(string path)
    {
        var handle=CreateFileW(path,0x40000000,0,0,1,0x02000000,0); // backup semantics; create only, never truncate
        if(handle.IsInvalid){var error=Marshal.GetLastWin32Error();handle.Dispose();throw new IOException("Cannot stage the approved internal picture.",new Win32Exception(error));}
        try{return new FileStream(handle,FileAccess.Write);}catch{handle.Dispose();throw;}
    }
    internal static FileStream OpenRead(string path)
    {
        var handle=CreateFileW(SourcePreservation.NativePath(path),0x80000000,1,0,3,0x02000000,0);
        if(handle.IsInvalid){var error=Marshal.GetLastWin32Error();handle.Dispose();throw new IOException("Cannot read the checked image asset.",new Win32Exception(error));}
        try{return new FileStream(handle,FileAccess.Read);}catch{handle.Dispose();throw;}
    }
    internal static void ReplaceEntry(string target,string active,string temporary,string evidence,Action replace)
    {
        var parent=Path.GetDirectoryName(target)!;
        var paths=new[]{target,active,parent,temporary}.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var originals=paths.ToDictionary(path=>path,ReadSecurity,StringComparer.OrdinalIgnoreCase);
        var attributes=new[]{target,active}.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path=>path,File.GetAttributes,StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(Path.Combine(evidence,"permissions.json"),System.Text.Json.JsonSerializer.Serialize(originals.ToDictionary(x=>x.Key,x=>Convert.ToBase64String(x.Value))));
        // The temporary staging file is ours; source-file and parent security must round-trip exactly.
        Exception? primary=null;var cleanup=new List<Exception>();
        try
        {
            var user=WindowsIdentity.GetCurrent().User??throw new IOException("Missing current user identity.");
            foreach(var path in new[]{parent,target,temporary})
            {
                FileSystemSecurity acl=Directory.Exists(path)?new DirectorySecurity():new FileSecurity();
                acl.SetSecurityDescriptorBinaryForm(originals[path]);
                acl.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.FullControl,InheritanceFlags.None,PropagationFlags.None,AccessControlType.Allow));
                SetSecurity(path,acl.GetSecurityDescriptorBinaryForm(),4); // non-inheritable, temporary rule only
            }
            replace();
        }
        catch(Exception error){primary=error;}
        finally
        {
            // Replacement separates the store entry from the active Ghost hard link; restore both file objects.
            foreach(var path in paths)
            {
                if(path==temporary && !File.Exists(path))continue;
                try{SetSecurity(path,originals[path],15);RequireSameSecurity(path,originals[path]);}
                catch(Exception error){cleanup.Add(new IOException("Could not restore exact private-image permissions: "+path,error));}
            }
            foreach(var entry in attributes)
            {
                try
                {
                    RestoreAttributes(entry.Key,entry.Value);
                    if((File.GetAttributes(entry.Key)&PreservedAttributes)!=(entry.Value&PreservedAttributes))
                        throw new IOException("Private image attribute readback differs: "+entry.Key);
                }
                catch(Exception error){cleanup.Add(error);}
            }
        }
        if(cleanup.Count!=0){if(primary!=null)cleanup.Insert(0,primary);throw new AggregateException("Permission restoration failed; this private image must not be committed.",cleanup);}
        if(primary!=null)ExceptionDispatchInfo.Capture(primary).Throw();
    }
    internal static string SecurityKey(byte[] data)
    {
        var sd=new RawSecurityDescriptor(data,0);var flags=sd.ControlFlags;
        // Windows canonicalises a NULL/absent audit list and clears AI on a protected DACL.
        // Keep every owner, ACE, audit/label, protection flag and all other control bits.
        if(sd.SystemAcl is null)flags&=~ControlFlags.SystemAclPresent;
        if((flags&ControlFlags.DiscretionaryAclProtected)!=0)flags&=~ControlFlags.DiscretionaryAclAutoInherited;
        sd.SetFlags(flags);
        return ((int)flags)+":"+sd.ResourceManagerControl+":"+sd.GetSddlForm(AccessControlSections.All);
    }
    internal static void RequireSameSecurity(string path,byte[] expected)
    {
        if(SecurityKey(ReadSecurity(path))!=SecurityKey(expected))throw new IOException("Private file security readback mismatch: "+path);
    }
    internal static void RestoreSecurity(string path,byte[] expected)
    {
        SetSecurity(path,expected,15);RequireSameSecurity(path,expected);
    }
    private static SafeFileHandle OpenSecurity(string path)
    {
        var handle=CreateFileW(path,0x010E0000,7,0,3,0x02000000,0); // READ_CONTROL, WRITE_DAC/OWNER, SYSTEM_SECURITY; backup intent
        if(handle.IsInvalid){var error=Marshal.GetLastWin32Error();handle.Dispose();throw new IOException("Cannot open private security metadata: "+path,new Win32Exception(error));}
        return handle;
    }
    internal static byte[] ReadSecurity(string path)
    {
        using var handle=OpenSecurity(path);
        _=GetKernelObjectSecurity(handle,15,null,0,out var required);
        if(required is <20 or >1048576)throw new IOException("Invalid file security descriptor size.");
        var data=new byte[required];if(!GetKernelObjectSecurity(handle,15,data,required,out _))throw Failure();return data;
    }
    private static void SetSecurity(string path,byte[] data,uint fields)
    {
        using var handle=OpenSecurity(path);var descriptor=new RawSecurityDescriptor(data,0);
        if((fields&4)!=0)fields|=(descriptor.ControlFlags&ControlFlags.DiscretionaryAclProtected)!=0?0x80000000u:0x20000000u;
        if((fields&8)!=0)fields|=(descriptor.ControlFlags&ControlFlags.SystemAclProtected)!=0?0x40000000u:0x10000000u;
        if(!SetKernelObjectSecurity(handle,fields,data))throw Failure();
    }
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetKernelObjectSecurity(SafeFileHandle handle,uint fields,byte[]? data,uint length,out uint required);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetKernelObjectSecurity(SafeFileHandle handle,uint fields,byte[] data);
    private static IOException Failure()=>new("The existing administrator backup/restore rights are unavailable. No system policy was changed.",new Win32Exception(Marshal.GetLastWin32Error()));
    [StructLayout(LayoutKind.Sequential)]private struct Luid{public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)]private struct Privileges{public uint Count;public Luid Luid;public uint Attributes;}
    [StructLayout(LayoutKind.Sequential)]private struct BasicInfo{public long Creation,Access,Write,Change;public uint Attributes;}
    [DllImport("kernel32.dll",SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int kind,ref BasicInfo info,uint size);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token,uint access,nint security,int level,int type,out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool LookupPrivilegeValue(string? system,string name,out Luid luid);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token,[MarshalAs(UnmanagedType.Bool)]bool disableAll,ref Privileges state,uint size,nint previous,nint returned);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,nint security,uint creation,uint flags,nint template);
}
