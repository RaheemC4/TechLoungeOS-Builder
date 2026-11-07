using System;
using System.Runtime.InteropServices;
internal static class ConsoleFixture
{
    [DllImport("kernel32.dll")]private static extern IntPtr GetConsoleWindow();
    private static int Main(string[] args){Console.OutputEncoding=new System.Text.UTF8Encoding(false);Console.WriteLine("console="+GetConsoleWindow().ToInt64());Console.WriteLine(args[0]);if(args[0]=="--wait")System.Threading.Thread.Sleep(30000);for(int i=0;i<2000;i++){Console.WriteLine("stdout-drained");Console.Error.WriteLine("stderr-drained");}return 17;}
}
