using TechLoungeOS;

static class HelloChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"hello-fixture");Directory.CreateDirectory(folder);
        var common=Path.Combine(folder,"common");var request=Path.Combine(common,@"TechLoungeOS\account-setup\hello-requested");Directory.CreateDirectory(Path.GetDirectoryName(request)!);
        var calls=Path.Combine(folder,"calls.txt");var fake=Path.Combine(folder,"winget.cmd");File.WriteAllText(fake,"@exit /b 1\r\n");
        string Header(string name)=>"$env:USERNAME='FixtureUser';$env:ProgramData="+Policy.Ps(common)+";$env:LOCALAPPDATA="+Policy.Ps(Path.Combine(folder,name))+";"
            +"function Start-Process {param($FilePath) if($FilePath -ne 'ms-settings:signinoptions'){throw 'Unexpected process'};Add-Content "+Policy.Ps(calls)+" $FilePath};"
            +"function Get-Command {param($Name,$ErrorAction) [pscustomobject]@{Source="+Policy.Ps(fake)+"}};";
        var run=new ProcessRunner(_=>{});var script=Provisioning.Script([],false,false);
        File.WriteAllText(request,"AnotherUser");await run.PowerShell(Header("other")+script);
        check(!File.Exists(calls),"Hello invitation does not open for another user's account marker");
        File.WriteAllText(request,"FixtureUser");
        try{await run.PowerShell(Header("failed")+Provisioning.Script(["Vendor.Failure"],false,false));throw new Exception("Expected provisioning failure");}
        catch(IOException){check(!File.Exists(calls),"Hello invitation waits while a selected app has failed instead of opening before a setup restart");}
        await run.PowerShell(Header("success")+script);await run.PowerShell(Header("success")+script);
        check(File.ReadAllLines(calls).SequenceEqual(new[]{"ms-settings:signinoptions"}),"successful provisioning offers Windows Hello once without changing a password, PIN or automatic sign-in");
    }
}
