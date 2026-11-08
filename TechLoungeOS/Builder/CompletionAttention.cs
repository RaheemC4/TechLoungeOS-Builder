using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TechLoungeOS;

internal static class CompletionAttention
{
    internal static void Notify(Window window)
    {
        if(window.IsActive||window.Owner?.IsActive==true)return;
        var target=window.Owner??window;var handle=new WindowInteropHelper(target).Handle;
        if(handle==0)return;
        const uint flashTray=2,flashUntilForeground=12;
        var info=new FlashInfo{Size=(uint)Marshal.SizeOf<FlashInfo>(),Window=handle,Flags=flashTray|flashUntilForeground,Count=uint.MaxValue};
        // The taskbar belongs to the owner; returning to its modal USB window
        // must also acknowledge the alert. Windows controls the flash cadence.
        window.Activated+=Acknowledge;window.Closed+=Acknowledge;
        _=FlashWindowEx(ref info);
        void Acknowledge(object? sender,EventArgs e)
        {
            window.Activated-=Acknowledge;window.Closed-=Acknowledge;
            var stop=new FlashInfo{Size=(uint)Marshal.SizeOf<FlashInfo>(),Window=handle};
            _=FlashWindowEx(ref stop);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo {public uint Size;public nint Window;public uint Flags,Count,Timeout;}
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
