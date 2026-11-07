using System.Runtime.InteropServices;

namespace TechLoungeOS;

public partial class MainWindow
{
    private uint? previousExecutionState;
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern uint SetThreadExecutionState(uint flags);

    private void SetActivityPower(bool active)
    {
        Dispatcher.VerifyAccess();
        const uint continuous=0x80000000,systemRequired=1;
        if(active && previousExecutionState==null)
        {
            var previous=SetThreadExecutionState(continuous|systemRequired);
            if(previous==0){Write("Windows could not prevent idle sleep. Keep the PC awake until this operation finishes.");return;}
            previousExecutionState=previous;
            // Preserve any earlier request on this UI thread without adding a display request.
            if(SetThreadExecutionState(previous|continuous|systemRequired)==0)
                Write("Windows could not retain the previous activity request. Keep the PC awake until this operation finishes.");
        }
        else if(!active && previousExecutionState is uint previous)
        {
            if(SetThreadExecutionState(previous)==0){Write("Windows could not release the activity request; closing the app releases it.");return;}
            previousExecutionState=null;
        }
    }
}
