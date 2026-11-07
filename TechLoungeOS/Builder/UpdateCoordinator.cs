using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TechLoungeOS.Updates;
// Kernel mutexes coordinate startup/update without depending on an update-cache folder.
public sealed class UpdateCoordinator:IDisposable
{
    private readonly Mutex mutex;
    private bool disposed;
    private UpdateCoordinator(Mutex mutex)=>this.mutex=mutex;
    public static void EnsureAppClosed(string install,int? exceptProcessId=null) =>
        EnsureAppClosed(install,Process.GetProcessesByName(Path.GetFileNameWithoutExtension(UpdateTrust.MainExe)),exceptProcessId);
    // Own this snapshot: Windows may still enumerate a process which has just exited.
    internal static void EnsureAppClosed(string install,IEnumerable<Process> processes,int? exceptProcessId=null)
    {
        var exe=Path.Combine(Path.GetFullPath(install),UpdateTrust.MainExe);
        foreach(var process in processes)
        {
            using(process)
            {
                if(process.Id==exceptProcessId || process.HasExited)continue;
                try
                {
                    if(string.Equals(process.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase) && !process.HasExited)
                        throw new IOException($"Another copy of this installation is running (PID {process.Id}). Close the other Builder window and retry; its work was left untouched.");
                }
                catch(Exception ex) when((ex is InvalidOperationException or Win32Exception) && process.HasExited){ }
            }
        }
    }
    public static UpdateCoordinator Enter(string install)
    {
        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(install).TrimEnd('\\').ToUpperInvariant())));
        // A mutex can only be released by its owning thread. Other users can wait, not bypass its ownership.
        if(!ConvertDescriptor("D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00100001;;;AU)",1,out var descriptor,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=descriptor};
            var handle=CreateMutexEx(ref attributes,"Global\\TechLoungeOS.Update."+id,0,0x00100001);
            if(handle.IsInvalid){var error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}
            var m=new Mutex();var original=m.SafeWaitHandle;m.SafeWaitHandle=handle;original.Dispose();
            bool acquired;
            try{acquired=m.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired){m.Dispose();throw new IOException("An app update is currently being applied. Reopen Builder when it finishes.");}
            return new(m);
        }
        finally{LocalFree(descriptor);}
    }
    public void Dispose(){if(disposed)return;disposed=true;mutex.ReleaseMutex();mutex.Dispose();}
    [StructLayout(LayoutKind.Sequential)]private struct SecurityAttributes{public int Length;public nint Descriptor;public int Inherit;}
    [DllImport("advapi32.dll",EntryPoint="ConvertStringSecurityDescriptorToSecurityDescriptorW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool ConvertDescriptor(string text,uint revision,out nint descriptor,out uint size);
    [DllImport("kernel32.dll",EntryPoint="CreateMutexExW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeWaitHandle CreateMutexEx(ref SecurityAttributes attributes,string name,uint flags,uint access);
    [DllImport("kernel32.dll")]private static extern nint LocalFree(nint memory);
}
