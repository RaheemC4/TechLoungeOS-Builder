using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TechLoungeOS;

internal static class BuildResources
{
    // Pinned wimlib documents ~640 MiB per default 64 MiB LZMS chunk/thread.
    // Budget 768 MiB/thread, reserve 512 MiB, and leave half the free RAM unused.
    internal static int CompressionThreads(int processors,ulong availableBytes)
    {
        if(availableBytes<(1UL<<30))throw new IOException("Free at least 1 GB of memory before compressing Windows. Your prepared image has been retained.");
        var memoryThreads=(availableBytes-(512UL<<20))/2/(768UL<<20);
        return Math.Clamp((int)Math.Min(memoryThreads,(ulong)Math.Clamp(processors,1,8)),1,8);
    }
    internal static (int Threads,ulong AvailableBytes) CompressionPlan()
    {
        var available=AvailableMemory();
        return (CompressionThreads(Environment.ProcessorCount,available),available);
    }
    internal static int DownloadConcurrency(ulong availableBytes)=>availableBytes<(2UL<<30)?1:2;
    internal static ulong AvailableMemory()
    {
        var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(!GlobalMemoryStatusEx(ref memory))throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows could not report available memory for build preparation.");
        return memory.AvailablePhysical;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length,Load;
        public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
