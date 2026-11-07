using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TechLoungeOS.FirstLogin
{
    internal static class DesktopReady
    {
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string name,string title);
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        internal static bool Visible()
        {
            var desktop=GetShellWindow();var taskbar=FindWindow("Shell_TrayWnd",null);
            return desktop!=IntPtr.Zero && taskbar!=IntPtr.Zero && IsWindowVisible(desktop) && IsWindowVisible(taskbar);
        }

        internal static bool Wait(Func<bool> ready,Action<int> sleep,int milliseconds)
        {
            if(milliseconds<1000 || milliseconds>60000)throw new ArgumentOutOfRangeException("milliseconds");
            // Require a stable shell, rather than starting installers as Explorer first appears.
            int consecutive=0;
            for(int elapsed=0;elapsed<milliseconds;elapsed+=250)
            {
                consecutive=ready()?consecutive+1:0;
                if(consecutive==4)return true;
                sleep(250);
            }
            return false;
        }

        internal static bool Wait(){return Wait(Visible,Thread.Sleep,60000);}
    }
}
