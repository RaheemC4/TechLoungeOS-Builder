using TechLoungeOS;

static class DesktopChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var registry=@"HKCU:\Software\TechLoungeOS-DesktopFixture-"+Guid.NewGuid().ToString("N");
        var run=new ProcessRunner(_=>{});
        try {
            foreach(var machine in new[]{true,false}) {
                var state=Path.Combine(root,"desktop-"+machine);Directory.CreateDirectory(state);
                var prefix=registry+"\\"+machine+"\\";
                var script=DesktopDefaults.Script(machine)
                    .Replace(machine?"$desktopState=Join-Path $env:ProgramData 'TechLoungeOS\\desktop-machine';$desktopHive='HKLM:\\'":"$desktopState=Join-Path $state 'desktop-user';$desktopHive='HKCU:\\'",
                        "$desktopState="+Policy.Ps(state)+";$desktopHive="+Policy.Ps(prefix));
                if(script.Contains("$desktopHive='HKLM:\\'") || script.Contains("$desktopHive='HKCU:\\'"))throw new Exception("Desktop fixture isolation failed");
                var settings=DesktopDefaults.Settings.Where(s=>s.Scope==(machine?"Machine":"User")).ToArray();
                var first=settings[0];
                await run.PowerShell("New-Item -Path "+Policy.Ps(prefix+first.Path)+" -Force|Out-Null;New-ItemProperty -LiteralPath "+Policy.Ps(prefix+first.Path)+" -Name "+Policy.Ps(first.Name)+" -Value 77 -PropertyType DWord -Force|Out-Null");
                await run.PowerShell(script);
                foreach(var setting in settings) {
                    var value=await run.PowerShell("Get-ItemPropertyValue -LiteralPath "+Policy.Ps(prefix+setting.Path)+" -Name "+Policy.Ps(setting.Name));
                    check(value.Trim()==setting.Value.ToString(),"desktop profile reads back "+setting.Tweak+" in an isolated HKCU fixture");
                }
                var backup=await run.PowerShell("(Import-Clixml -LiteralPath "+Policy.Ps(Path.Combine(state,first.Tweak+".before.xml"))+").Previous.Value");
                check(backup.Trim()=="77" && File.Exists(Path.Combine(state,"complete")),"desktop profile retains its original registry value before recording completion");
                await run.PowerShell("Set-ItemProperty -LiteralPath "+Policy.Ps(prefix+first.Path)+" -Name "+Policy.Ps(first.Name)+" -Value 45");
                await run.PowerShell(script);
                check((await run.PowerShell("Get-ItemPropertyValue -LiteralPath "+Policy.Ps(prefix+first.Path)+" -Name "+Policy.Ps(first.Name))).Trim()=="45","completed desktop settings respect a later user's change");
            }
            check(DesktopDefaults.Settings.Single(s=>s.Name=="EnableLUA").Value==1 && !DesktopDefaults.Settings.Any(s=>s.Path.Contains("Defender") || s.Path.Contains("WindowsUpdate")),"Never notify retains UAC and the desktop profile does not alter Defender or Windows Update");
        }finally{await run.PowerShell("if(Test-Path -LiteralPath "+Policy.Ps(registry)+"){Remove-Item -LiteralPath "+Policy.Ps(registry)+" -Recurse -Force}");}
    }
}
