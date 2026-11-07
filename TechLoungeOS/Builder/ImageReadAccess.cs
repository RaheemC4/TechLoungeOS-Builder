using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace TechLoungeOS;

// Read protected image contents without changing their owner or ACLs. Only a duplicated
// impersonation token is adjusted; RunImpersonated restores the caller on success or failure.
internal static class ImageReadAccess
{
    internal static T Run<T>(Func<T> read)
    {
        using var current=WindowsIdentity.GetCurrent(TokenAccessLevels.Query|TokenAccessLevels.Duplicate);
        const TokenAccessLevels access=TokenAccessLevels.Query|TokenAccessLevels.Duplicate|TokenAccessLevels.AdjustPrivileges|TokenAccessLevels.Impersonate;
        if(!DuplicateTokenEx(current.AccessToken,(uint)access,0,2,2,out var token))
        {var error=Failure();token?.Dispose();throw error;}
        using(token)
        {
            if(!LookupPrivilegeValue(null,"SeBackupPrivilege",out var luid))throw Failure();
            var privileges=new Privileges{Count=1,Luid=luid,Attributes=2};
            if(!AdjustTokenPrivileges(token,false,ref privileges,0,0,0)||Marshal.GetLastWin32Error()!=0)throw Failure();
            return WindowsIdentity.RunImpersonated(token,read);
        }
    }
    private static IOException Failure()=>new("WinPE preservation needs the existing administrator backup-read privilege. No file permissions were changed. Keep the original setup background if this privilege is unavailable.",new Win32Exception(Marshal.GetLastWin32Error()));
    internal static FileStream OpenRead(string path)
    {
        var handle=CreateFile(SourcePreservation.NativePath(path),0x80000000,7,0,3,0x02000000,0); // GENERIC_READ, BACKUP_SEMANTICS
        if(handle.IsInvalid){var error=Marshal.GetLastWin32Error();handle.Dispose();throw new IOException("Cannot back up image file: "+path,new Win32Exception(error));}
        try{return new FileStream(handle,FileAccess.Read);}
        catch{handle.Dispose();throw;}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Luid{public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)]private struct Privileges{public uint Count;public Luid Luid;public uint Attributes;}
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token,uint access,nint security,int level,int type,out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system,string name,out Luid luid);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token,[MarshalAs(UnmanagedType.Bool)]bool disableAll,ref Privileges state,uint size,nint previous,nint returned);
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,nint security,uint creation,uint flags,nint template);
}
