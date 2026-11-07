using System.Text;
using TechLoungeOS;

internal static class PeStartupChecks
{
    internal static void Run(string root, Action<bool,string> check)
    {
        var shell=WinPeStartup.StandardShell(Encoding.ASCII.GetBytes("wpeinit\r\n"),"winpeshl.exe",1,1,null);
        check(Encoding.ASCII.GetString(shell)=="[LaunchApps]\r\n%SYSTEMROOT%\\System32\\wpeinit.exe\r\n%SYSTEMROOT%\\System32\\TechLoungeOS\\PeBackground.exe, --pe\r\n",
            "stock WinPE initialization runs once before the foreground account wizard");
        var keyboard=Encoding.ASCII.GetString(WinPeStartup.StandardShell(Encoding.ASCII.GetBytes("WPEINIT\n"),"WINPESHL.EXE",1,1,"0809:00000809"));
        check(keyboard=="[LaunchApps]\r\n%SYSTEMROOT%\\System32\\wpeinit.exe\r\n%SYSTEMROOT%\\System32\\wpeutil.exe, SetKeyboardLayout 0809:00000809\r\n%SYSTEMROOT%\\System32\\TechLoungeOS\\PeBackground.exe, --pe\r\n",
            "stock shell keeps initialization before the chosen keyboard and foreground wizard");
        foreach(var custom in new[]{"wpeinit\r\ncustom-tools.exe", "call wpeinit", "", "wpeinit\0", "wpeinit & start setup.exe"})
            Refuses(()=>WinPeStartup.StandardShell(Encoding.ASCII.GetBytes(custom),"winpeshl.exe",1,1,null),check,"custom startup is not silently skipped");
        Refuses(()=>WinPeStartup.StandardShell(Encoding.ASCII.GetBytes("wpeinit"),"custom.exe",1,1,null),check,"unknown boot shell is retained");
        Refuses(()=>WinPeStartup.StandardShell(Encoding.ASCII.GetBytes("wpeinit"),"winpeshl.exe",0,1,null),check,"nonstandard setup state is rejected");
        Refuses(()=>WinPeStartup.StandardShell(Encoding.ASCII.GetBytes("wpeinit"),"winpeshl.exe",1,null,null),check,"missing setup state is rejected");
        Refuses(()=>WinPeStartup.StandardShell(Encoding.ASCII.GetBytes("wpeinit"),"winpeshl.exe",1,1,"0809:00000809\r\nother.exe"),check,"keyboard cannot inject a shell entry");

        var mount=Path.Combine(root,"stock-pe");var system=Path.Combine(mount,@"Windows\System32");Directory.CreateDirectory(system);
        var startnet=Encoding.ASCII.GetBytes("wpeinit\r\n");File.WriteAllBytes(Path.Combine(system,"startnet.cmd"),startnet);
        SettingsChecks.WriteValues(Path.Combine(system,@"config\SYSTEM"),[
            ("Setup","CmdLine",1,Encoding.Unicode.GetBytes("winpeshl.exe\0")),
            ("Setup","SetupType",4,BitConverter.GetBytes(1)),
            ("Setup","SystemSetupInProgress",4,BitConverter.GetBytes(1))]);
        Directory.CreateDirectory(Path.Combine(mount,"sources"));File.WriteAllText(Path.Combine(mount,@"sources\setup.exe"),"fixture");
        foreach(var name in new[]{"cmd.exe","wpeinit.exe","kernel32.dll","user32.dll","gdi32.dll","msvcrt.dll","advapi32.dll","shell32.dll","ole32.dll","msimg32.dll",
            "gdiplus.dll","shlwapi.dll","mpr.dll","uxtheme.dll","winmm.dll","urlmon.dll","wininet.dll","propsys.dll","dwmapi.dll","winspool.drv","oleaut32.dll","iphlpapi.dll","userenv.dll","comctl32.dll","version.dll","comdlg32.dll"})
            File.WriteAllText(Path.Combine(system,name),"fixture prerequisite");
        var before=WinPeBackground.Snapshot(mount,CancellationToken.None);
        var exe=new byte[1024];exe[0]=(byte)'M';exe[1]=(byte)'Z';
        File.WriteAllText(Path.Combine(system,"startnet.cmd"),"wpeinit\r\ncustom-tools.exe");
        Refuses(()=>WinPeBackground.StageMounted(mount,exe),check,"custom stock-image initialization aborts staging");
        check(!File.Exists(Path.Combine(system,"winpeshl.ini"))&&!Directory.Exists(Path.Combine(system,"TechLoungeOS")),
            "rejected startup leaves no replacement shell or partial tool payload");
        File.WriteAllBytes(Path.Combine(system,"startnet.cmd"),startnet);
        Refuses(()=>WinPeBackground.StageMounted(mount,exe),check,"a loose legacy common-controls DLL does not replace the required version 6 assembly");
        const string assembly="amd64_microsoft.windows.common-controls_6595b64144ccf1df_6.0.26100.8037_none_3e092faae333b53b";
        var sx=Path.Combine(mount,@"Windows\WinSxS");Directory.CreateDirectory(Path.Combine(sx,assembly));Directory.CreateDirectory(Path.Combine(sx,"Manifests"));
        File.WriteAllText(Path.Combine(sx,assembly,"comctl32.dll"),"fixture assembly");
        File.WriteAllText(Path.Combine(sx,"Manifests",assembly+".manifest"),"fixture manifest");
        File.Delete(Path.Combine(system,"comctl32.dll"));
        before=WinPeBackground.Snapshot(mount,CancellationToken.None);
        var expected=WinPeBackground.StageMounted(mount,exe);
        var after=WinPeBackground.Snapshot(mount,CancellationToken.None);
        check(before.All(entry=>after[entry.Key]==entry.Value)&&expected.All(entry=>after[entry.Key]==entry.Value),
            "stock integration preserves every original file and verifies all added payload hashes");
        check(!File.Exists(Path.Combine(system,"comctl32.dll")),"stock integration uses the existing side-by-side assembly without copying Windows DLLs");
        check(File.ReadAllBytes(Path.Combine(system,"winpeshl.ini")).SequenceEqual(shell)&&
            !File.Exists(Path.Combine(system,@"TechLoungeOS\winpeshl.original.ini"))&&
            File.ReadAllText(Path.Combine(system,@"TechLoungeOS\startup-original.json")).Contains("\"winpeshlIniExisted\":false"),
            "stock integration records original shell absence without a fabricated backup");
        check(after[@"Windows\System32\TechLoungeOS\Tools\Explorer++\Explorer++.exe"]=="22AC20341C5525658EBE5E9C4B9AC9F7C152B18080FF347861FAC3F04D56A2D6"&&
            after[@"Windows\System32\TechLoungeOS\Tools\Explorer++\source.zip"]=="3531F0BA75B5BFB64BE4674A960B57C62C2ADF62A3B04578E485E2442FDBC655",
            "stock file manager includes the pinned upstream binary and matching source archive");
    }
    private static void Refuses(Action action,Action<bool,string> check,string name)
    {try{action();throw new Exception("Expected rejection: "+name);}catch(IOException){check(true,name);}}
}
