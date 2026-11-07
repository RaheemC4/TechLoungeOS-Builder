using TechLoungeOS;

static class InputChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"input-defaults");Directory.CreateDirectory(folder);
        var header="""
            Add-Type @'
            public static class TechLoungeOSInput {
              public static int[] Values=new[]{6,10,1};public static int Lines=3;public static bool Reject=false;
              public static int[] Mouse(){return (int[])Values.Clone();}
              public static int Wheel(){return Lines;}
              public static void Apply(int[] mouse,int wheel){if(!Reject){Values=(int[])mouse.Clone();Lines=wheel;}}
            }
            '@
            function Invoke-Step {param($name,$action) & $action}
            """+"\n$state="+Policy.Ps(folder)+"\n";
        var run=new ProcessRunner(_=>{});
        await run.PowerShell(header+InputDefaults.Script+"\n$b=Import-Clixml (Join-Path $state input-before.xml);if([TechLoungeOSInput]::Mouse()[0] -ne 6 -or [TechLoungeOSInput]::Mouse()[1] -ne 10 -or [TechLoungeOSInput]::Mouse()[2] -ne 0 -or [TechLoungeOSInput]::Wheel() -ne 6 -or $b.Mouse[2] -ne 1 -or $b.Wheel -ne 3){throw 'Input values or original backup differ'}");
        check(true,"input setup preserves pointer thresholds, disables acceleration and selects six lines with the original settings backed up in an isolated native substitute");
        try{await run.PowerShell(header+"[TechLoungeOSInput]::Reject=$true\n"+InputDefaults.Script);throw new Exception("Expected input readback failure");}
        catch(IOException){check(true,"input setup rejects a native setting operation whose actual readback differs");}
        var provision=Provisioning.Script(["7zip.7zip"],false,false,desktopDefaults:true);
        check(provision.IndexOf("Invoke-Step 'desktop-defaults'",StringComparison.Ordinal)<provision.IndexOf("Install-WinGetApp '7zip.7zip'",StringComparison.Ordinal),"desktop appearance and input preferences apply before long app work in the desktop-first flow");
    }
}
