using System.Reflection;
using System.Runtime.InteropServices;
using TechLoungeOS;

internal static class ActivityPowerChecks
{
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern uint SetThreadExecutionState(uint flags);

    internal static void Run(MainWindow window)
    {
        const uint continuous=0x80000000,systemRequired=1,displayRequired=2;
        var baseline=SetThreadExecutionState(continuous);
        if(baseline==0||SetThreadExecutionState(baseline)==0)throw new Exception("Native activity-state probe failed.");
        var busy=typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic)!;
        try
        {
            busy.Invoke(window,[true]);
            var active=SetThreadExecutionState(continuous);SetThreadExecutionState(active);
            if((active&systemRequired)==0 || (active&displayRequired)!=(baseline&displayRequired))
                throw new Exception("Media work must prevent idle system sleep without changing display-sleep requirements.");
            busy.Invoke(window,[false]);
            var restored=SetThreadExecutionState(continuous);SetThreadExecutionState(restored);
            if(restored!=baseline)throw new Exception("Idle app must restore the original thread power request.");
        }
        finally{busy.Invoke(window,[false]);SetThreadExecutionState(baseline);}
        Console.WriteLine("Actual Windows thread activity flags prevent idle sleep only during media work and restore the original flags afterwards; no power plan changed.");
    }
}
