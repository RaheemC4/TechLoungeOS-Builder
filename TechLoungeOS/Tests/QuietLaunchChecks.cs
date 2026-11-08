using TechLoungeOS;

static class QuietLaunchChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"quiet O'Brien 测试");Directory.CreateDirectory(folder);
        var binary=Path.Combine(AppContext.BaseDirectory,"FirstLogin.exe");
        using(var stream=File.OpenRead(binary))using(var read=new BinaryReader(stream)){
            stream.Position=0x3c;var pe=read.ReadInt32();stream.Position=pe+24+68;
            check(read.ReadUInt16()==2,"first-login entry is a Windows GUI executable rather than a console program");
        }
        var run=new ProcessRunner(_=>{});
        await run.Run(Path.Combine(AppContext.BaseDirectory,"FirstLoginChecks.exe"),[folder,Path.Combine(AppContext.BaseDirectory,"FirstLoginConsoleFixture.exe")]);
        check(File.ReadAllText(Path.Combine(folder,"restart-completion-checks.txt")).Contains("past the former eight-second timeout"),
            "completed setup retains Restart now and Later without auto-dismissal; Later closes without restarting Windows");
        check(File.Exists(Path.Combine(folder,"checks.txt"))&&File.ReadAllText(Path.Combine(folder,"bootstrap.log")).Contains("console=0"),
            "quiet launcher starts a real console child without a console, preserves Unicode/quotes/exit code, drains both pipes and scopes private WebView2 without launching Tweaks");
        check(File.ReadAllText(Path.Combine(folder,"desktop-gate-checks.txt")).Contains("stable, unstable and missing shell checks passed"),
            "installer bootstrap waits for a stable desktop and bounds absent or interrupted shell readiness without changing the host desktop");
        check(File.Exists(Path.Combine(folder,"setup-progress.png"))&&File.ReadAllText(Path.Combine(folder,"progress-checks.txt")).Contains("Packaged Sora faces"),
            "real Framework helper loads the packaged progress font, renders off-screen and reads atomic status updates without starting an installer");
        check(File.ReadAllText(Path.Combine(folder,"installer-power-checks.txt")).Contains("Idle sleep only, previous state restored, rejection and exceptional exit passed"),
            "installer power request permits display sleep, restores prior thread state once and releases on failure without changing the host power plan");
        check(File.ReadAllText(Path.Combine(folder,"progress-interaction-checks.txt")).Contains("successful retry and duplicate-click protection passed"),
            "actual Framework message loop retains running work when hidden or closed, shows failure, retries once and closes after success with inert work only");
        var mount=Path.Combine(folder,"mount");
        Provisioning.Stage(mount,new BuildOptions("",1,"","","",[],[],false,false,null));
        var startup=Path.Combine(mount,@"Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        check(File.ReadAllText(Path.Combine(mount,@"ProgramData\TechLoungeOS\Sora-LICENCE.txt")).Contains("SIL OPEN FONT LICENSE"),
            "the staged setup font retains its redistribution licence beside the installed helper");
        check(File.ReadAllBytes(Path.Combine(startup,"TechLoungeOS.exe")).SequenceEqual(File.ReadAllBytes(binary))&&!File.Exists(Path.Combine(startup,"TechLoungeOS.cmd"))&&File.Exists(Path.Combine(mount,@"ProgramData\TechLoungeOS\FirstLogin.exe")),
            "default-user startup and Tweaks shortcut share the packaged quiet executable and no visible CMD launcher is staged");
        var script=File.ReadAllText(Path.Combine(mount,@"ProgramData\TechLoungeOS\first-login.ps1"));
        check(script.Contains("$link.Arguments = '--open-tweaks'")&&!script.Contains("$link.TargetPath = Join-Path $env:SystemRoot"),"Tweaks desktop shortcut uses the quiet launcher instead of a PowerShell console");
    }
}
