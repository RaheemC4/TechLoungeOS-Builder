using TechLoungeOS;

static class WallpaperChecks
{
    public static async Task Run(string root, BuildOptions options, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "wallpaper-fixture"); Directory.CreateDirectory(folder);
        var wallpaper = Path.Combine(folder, "background.bmp");
        var bytes = Convert.FromHexString("424D3A0000000000000036000000280000000100000001000000010018000000000004000000130B0000130B00000000000000000000FF000000");
        File.WriteAllBytes(wallpaper, bytes); Wallpaper.Validate(wallpaper);
        check(new BuildOptions("",1,"","","",[],[],false,false,null).WallpaperPreset=="waterfall.ice","ice-blue waterfall is the default preset");
        foreach(var invalid in new[]{"../ice","waterfall.red","unknown.ice"}) {
            try{WallpaperPresets.Validate(invalid);throw new Exception("Expected preset rejection");}
            catch(InvalidOperationException){check(true,"unknown or unsafe preset selection rejected: "+invalid);}
        }
        check(true, "local wallpaper passes size and image-header validation");
        var bad = Path.Combine(folder, "not-an-image.png"); File.WriteAllText(bad, new string('x', 60));
        try { Wallpaper.Validate(bad); throw new Exception("Expected malformed wallpaper rejection"); }
        catch (InvalidOperationException) { check(true, "renamed non-image wallpaper rejected before staging"); }
        var mount = Path.Combine(folder, "image");
        Provisioning.Stage(mount, options with { WallpaperFile = wallpaper, Apps = [], GameMode = false, DisableSuggestions = false });
        check(File.ReadAllBytes(Path.Combine(mount, "ProgramData", "TechLoungeOS", "wallpaper.bmp")).SequenceEqual(bytes), "wallpaper payload is staged intact");
        var library=Path.Combine(mount,@"ProgramData\TechLoungeOS\Wallpapers");
        check(Directory.GetFiles(library,"*.png").Length==15 && File.Exists(Path.Combine(library,"Personalise.exe")) && Directory.GetFiles(library).Sum(f=>new FileInfo(f).Length)<12L*1024*1024,"all five approved designs and three colours remain available after install with a compact native companion");
        check(!File.ReadAllText(Path.Combine(mount,@"ProgramData\TechLoungeOS\first-login.ps1")).Contains(WallpaperPresets.FirstLogin("waterfall.ice")),"a chosen custom image takes precedence over the default preset");
        var presetMount=Path.Combine(folder,"preset-image");
        Provisioning.Stage(presetMount,options with{WallpaperFile=null,WallpaperPreset="waterfall.ice",Apps=[],GameMode=false,DisableSuggestions=false});
        var presetScript=File.ReadAllText(Path.Combine(presetMount,@"ProgramData\TechLoungeOS\first-login.ps1"));
        check(presetScript.Contains("--apply") && presetScript.Contains("waterfall.ice") && presetScript.Contains("Wallpapers.lnk"),"new users receive the selected preset and a Start menu wallpaper picker shortcut");

        var fakeInstaller = Path.Combine(folder, "fake-winget.cmd"); File.WriteAllText(fakeInstaller, "@exit /b 0\r\n");
        var calls = Path.Combine(folder, "wallpaper-api-calls.txt");
        var local = Path.Combine(folder, "local-user");
        // The native desktop API is replaced before the generated script runs. No host wallpaper or HKCU setting changes.
        var fakeApi = "public static class TechLoungeOSDesktop { public static bool SystemParametersInfo(int a,int b,string path,int flags) { System.IO.File.AppendAllText(System.Environment.GetEnvironmentVariable(\"TL_WALL_CALL\"),path+\"\\n\"); return true; } }";
        string Header(string user, bool managed) => "$env:LOCALAPPDATA=" + Policy.Ps(user) + ";$env:ProgramData=" + Policy.Ps(Path.Combine(mount, "ProgramData")) + ";$env:TL_WALL_CALL=" + Policy.Ps(calls) + ";"
            + "Add-Type " + Policy.Ps(fakeApi) + ";function Get-ItemProperty { param($Path,$ErrorAction) " + (managed ? "@{NoChangingWallPaper=1}" : "@{}") + " };"
            + "function Get-Command { param($Name,$ErrorAction) [pscustomobject]@{Source=" + Policy.Ps(fakeInstaller) + "} };";
        var run = new ProcessRunner(_ => { });
        var script = Provisioning.Script(["VideoLAN.VLC"], false, false, "wallpaper.bmp");
        await run.PowerShell(Header(local, false) + script);
        var target = Path.Combine(local, "TechLoungeOS", "desktop-background.bmp");
        check(File.Exists(target) && File.ReadAllLines(calls).Single() == target && File.Exists(Path.Combine(local, "TechLoungeOS", "desktop-background.done")), "generated first-login script decodes wallpaper and calls the substituted desktop API");
        await run.PowerShell(Header(local, false) + script);
        check(File.ReadAllLines(calls).Length == 1, "completed wallpaper provisioning does not override the user on later sign-ins");
        var managedUser = Path.Combine(folder, "managed-user");
        try { await run.PowerShell(Header(managedUser, true) + script); }
        catch (IOException) { /* Managed wallpaper is deliberately blocked; unrelated fake app must still complete. */ }
        var state = Path.Combine(managedUser, "TechLoungeOS");
        check(!File.Exists(Path.Combine(state, "desktop-background.done")) && File.Exists(Path.Combine(state, "VideoLAN.VLC.done")) && File.ReadAllLines(calls).Length == 1,
            "wallpaper policy is preserved and does not block independent app provisioning");
    }
}
