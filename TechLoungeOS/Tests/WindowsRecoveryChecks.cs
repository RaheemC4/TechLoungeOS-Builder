using TechLoungeOS;

static class WindowsRecoveryChecks
{
    const string Registered="<WindowsRE><InstallState state='1'/><WinreBCD id='{39c29b31-4632-4abb-a7c8-d612555761f2}'/><WinreLocation path='\\Recovery\\WindowsRE'/></WindowsRE>";
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"recovery-checks");Directory.CreateDirectory(folder);
        var image=Path.Combine(folder,@"Windows\System32\Recovery\Winre.wim");
        var reagent=Path.Combine(folder,@"Windows\System32\reagentc.exe");
        var runner=new FixtureRunner(folder);
        try{await WindowsRecovery.ValidateSource(runner,folder);throw new Exception("Expected missing recovery rejection");}
        catch(IOException){check(runner.Commands.Count==0,"missing recovery tools stop before a servicing command");}
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);File.WriteAllText(reagent,"inert prerequisite fixture");File.WriteAllText(image,"inert WIM fixture");
        await WindowsRecovery.ValidateSource(runner,folder);
        check(runner.Commands.Single().SequenceEqual(new[]{"/Get-WimInfo","/WimFile:"+image}),"source recovery image is independently read by DISM before customization");
        File.Delete(image);
        try{await WindowsRecovery.ValidateSource(runner,folder);throw new Exception("Expected missing Winre rejection");}
        catch(IOException){check(true,"source with recovery stripped fails explicitly instead of receiving a false recovery pass");}

        var nativeSource=Path.Combine(folder,"RecoveryFixture.cs");var native=Path.Combine(folder,"RecoveryFixture.exe");
        File.WriteAllText(nativeSource,"""
            using System;using System.IO;
            class RecoveryFixture {
              static int Main(string[] args){
                string root=Environment.GetEnvironmentVariable("windir");
                File.AppendAllText(Path.Combine(root,"calls.txt"),args[0]+"\n");
                if(Environment.GetEnvironmentVariable("TLOS_RECOVERY_FAIL")=="1")return 5;
                if(args[0]=="/enable")File.WriteAllText(Path.Combine(root,@"System32\Recovery\ReAgent.xml"),"<WindowsRE><InstallState state='1'/><WinreBCD id='{39c29b31-4632-4abb-a7c8-d612555761f2}'/><WinreLocation path='\\Recovery\\WindowsRE'/></WindowsRE>");
                return 0;
              }
            }
            """);
        var process=new ProcessRunner(_=>{});
        await process.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),["/nologo","/target:exe","/out:"+native,nativeSource]);
        foreach(var scenario in new[]{"registered","missing","failure","dtd"})
        {
            var fake=Path.Combine(folder,scenario);var system=Path.Combine(fake,"System32");Directory.CreateDirectory(Path.Combine(system,"Recovery"));File.Copy(native,Path.Combine(system,"reagentc.exe"));
            var xml=Path.Combine(system,@"Recovery\ReAgent.xml");
            if(scenario=="registered"||scenario=="failure")File.WriteAllText(xml,Registered);
            if(scenario=="dtd")File.WriteAllText(xml,"<!DOCTYPE WindowsRE [<!ENTITY x SYSTEM 'file:///C:/not-read'>]><WindowsRE>&x;</WindowsRE>");
            var data=Path.Combine(fake,"data");var state=Path.Combine(data,@"TechLoungeOS\recovery");Directory.CreateDirectory(state);
            if(scenario=="failure")File.WriteAllText(Path.Combine(state,"complete"),"stale");
            var setup="$env:windir="+Policy.Ps(fake)+";$env:ProgramData="+Policy.Ps(data)+";$env:TLOS_RECOVERY_FAIL="+Policy.Ps(scenario=="failure"?"1":"0")+";\n"+WindowsRecovery.Script;
            var failed=false;try{await process.PowerShell(setup);}catch(Exception){failed=true;}
            check(failed==(scenario is "failure" or "dtd"),"native recovery script result: "+scenario);
            check(File.Exists(Path.Combine(state,"complete"))==!failed,"recovery completion never survives failed verification: "+scenario);
            if(scenario=="registered")check(File.ReadAllText(xml)==Registered&&File.ReadAllLines(Path.Combine(fake,"calls.txt")).SequenceEqual(new[]{"/info"}),"registered source recovery location is retained without an enable rewrite");
            if(scenario=="missing")check(File.ReadAllLines(Path.Combine(fake,"calls.txt")).SequenceEqual(new[]{"/enable","/info"}),"unregistered recovery enables once and verifies registration");
        }
    }
}
