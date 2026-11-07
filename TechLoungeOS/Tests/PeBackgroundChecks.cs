using System.Text;
using TechLoungeOS;

internal static class PeBackgroundChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        foreach(var newline in new[]{"\r\n","\n"})
        {
            var original=Encoding.Latin1.GetBytes(";custom source comment"+newline+"[LaunchApps]"+newline+"%SYSTEMDRIVE%\\Windows\\System32\\LaunchBar_x64.exe"+newline+";caf\u00e9"+newline);
            var result=WinPeBackground.AddLaunch(original);var marker=Encoding.ASCII.GetBytes(WinPeBackground.LaunchLine+newline);
            var offset=Encoding.Latin1.GetString(result).IndexOf(WinPeBackground.LaunchLine,StringComparison.Ordinal);
            var restored=result[..offset].Concat(result[(offset+marker.Length)..]).ToArray();
            var retained=Encoding.Latin1.GetBytes(";custom source comment"+newline+"[LaunchApps]"+newline+";caf\u00e9"+newline);
            check(restored.SequenceEqual(retained),"WinPE startup replaces only the exact legacy toolbar entry and retains unrelated bytes and line endings");
        }
        foreach(var shell in new[]{"[LaunchApp]\r\nAppPath=custom.exe\r\n","[LaunchApps]\r\n[LaunchApps]\r\n","[LaunchApps]\r\nTechLoungeOS.exe\r\n","[LaunchApps]"})
            try{WinPeBackground.AddLaunch(Encoding.ASCII.GetBytes(shell));throw new Exception("Expected shell rejection");}
            catch(IOException){check(true,"ambiguous or already customised WinPE startup is rejected before writing");}
        var prepared=Encoding.ASCII.GetBytes("[LaunchApps]\r\nprepare-image-tools.exe, /preserve\r\n%SYSTEMDRIVE%\\Windows\\System32\\LaunchBar_x64.exe\r\n");
        check(Encoding.ASCII.GetString(WinPeBackground.AddLaunch(prepared))=="[LaunchApps]\r\nprepare-image-tools.exe, /preserve\r\n"+WinPeBackground.LaunchLine+"\r\n","new WinPE shell starts in the old toolbar's foreground slot after the original tool initialization, with no detached process or old toolbar launch");
        try{WinPeBackground.AddLaunch(Encoding.ASCII.GetBytes("[LaunchApps]\r\nunknown-shell.exe\r\n"));throw new Exception("Expected unknown shell rejection");}
        catch(IOException){check(true,"an unknown foreground shell is rejected rather than blocking initialization or silently replacing another setup environment");}
        var mount=Path.Combine(root,"pe-fixture");var system=Path.Combine(mount,@"Windows\System32");Directory.CreateDirectory(system);
        var config=Encoding.ASCII.GetBytes("[LaunchApps]\r\n%SYSTEMDRIVE%\\Windows\\System32\\LaunchBar_x64.exe\r\n");File.WriteAllBytes(Path.Combine(system,"winpeshl.ini"),config);
        foreach(var name in new[]{"cmd.exe","kernel32.dll","user32.dll","gdi32.dll","msvcrt.dll","advapi32.dll","shell32.dll","ole32.dll","msimg32.dll"})File.WriteAllText(Path.Combine(system,name),"fixture prerequisite");
        File.WriteAllText(Path.Combine(system,"LaunchBar_x64.exe"),"unchanged inherited tool");
        var exe=new byte[1024];exe[0]=(byte)'M';exe[1]=(byte)'Z';WinPeBackground.StageMounted(mount,exe);
        check(File.ReadAllText(Path.Combine(system,"LaunchBar_x64.exe"))=="unchanged inherited tool"&&File.ReadAllBytes(Path.Combine(system,@"TechLoungeOS\winpeshl.original.ini")).SequenceEqual(config),"background staging preserves existing tools and a byte-exact startup backup");
        var before=File.ReadAllBytes(Path.Combine(system,"winpeshl.ini"));
        try{WinPeBackground.StageMounted(mount,exe);throw new Exception("Expected existing-folder rejection");}
        catch(IOException){check(File.ReadAllBytes(Path.Combine(system,"winpeshl.ini")).SequenceEqual(before),"repeated background integration cannot overwrite an existing tool folder or shell");}
        SourcePreservation.RequireImageFile(Path.Combine(system,"winpeshl.ini"),0,long.MaxValue);
        check(File.ReadAllBytes(Path.Combine(system,"winpeshl.ini")).SequenceEqual(before),"WinPE uses the shared image-file validation without changing content; reparse policy is covered by settings checks");
        var invoked=false;
        using var originalIdentity=System.Security.Principal.WindowsIdentity.GetCurrent();
        try
        {
            var text=ImageReadAccess.Run(()=>{invoked=true;using var stream=ImageReadAccess.OpenRead(Path.Combine(system,"LaunchBar_x64.exe"));using var reader=new StreamReader(stream);return reader.ReadToEnd();});
            try{ImageReadAccess.Run<int>(()=>throw new InvalidOperationException("fixture read failure"));throw new Exception("Expected scoped read failure");}
            catch(InvalidOperationException ex) when(ex.Message=="fixture read failure"){}
            using var afterIdentity=System.Security.Principal.WindowsIdentity.GetCurrent();
            check(text=="unchanged inherited tool"&&afterIdentity.ImpersonationLevel==originalIdentity.ImpersonationLevel,"backup read retains content and restores caller identity after a throwing callback");
        }
        catch(IOException ex) when(ex.InnerException is System.ComponentModel.Win32Exception native&&native.NativeErrorCode is 5 or 1300)
        {
            using var afterIdentity=System.Security.Principal.WindowsIdentity.GetCurrent();
            check(!invoked&&afterIdentity.ImpersonationLevel==originalIdentity.ImpersonationLevel&&ex.Message.Contains("No file permissions were changed"),"restricted caller refuses backup read before invoking it and retains its identity");
        }
    }
}
