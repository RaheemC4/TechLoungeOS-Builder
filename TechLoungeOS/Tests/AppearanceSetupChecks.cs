using System.Xml.Linq;
using TechLoungeOS;

static class AppearanceSetupChecks
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"appearance");Directory.CreateDirectory(folder);
        var options=new BuildOptions("unused.iso",1,folder,"unused-output.iso","unused-tool",[],[],false,false,null) { WallpaperPreset=null };
        check(InitialWallpaper.Stage(Path.Combine(folder,"keep"),options)==null,"keeping the source background stages no replacement first-desktop image");
        var source=Path.Combine(folder,"custom.png");
        File.WriteAllBytes(source,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var mount=Path.Combine(folder,"image");
        var installed=InitialWallpaper.Stage(mount,options with {WallpaperFile=source});
        check(installed==@"%WINDIR%\Web\Wallpaper\TechLoungeOS\first-desktop.png"&&File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(Path.Combine(mount,@"Windows\Web\Wallpaper\TechLoungeOS\first-desktop.png"))),
            "the selected first-desktop image is staged byte-exact under Windows without replacing a source image");
        var ns=SetupAnswerFile.Namespace;
        foreach(var automatic in new[]{true,false}) {
            var xml=XDocument.Parse(SetupAnswerFile.Template(automatic,initialWallpaper:installed));
            var themes=xml.Descendants(ns+"Themes").ToArray();
            check(themes.Length==2&&themes.All(t=>t.Element(ns+"ThemeName")?.Value=="TechLoungeOS"&&t.Element(ns+"DesktopBackground")?.Value==installed),
                (automatic?"automatic":"manual")+" sign-in carries the chosen background in specialize and OOBE before the first desktop");
        }
        try{SetupAnswerFile.Template(false,initialWallpaper:@"\\untrusted\share\background.png");throw new Exception("Expected remote theme rejection");}
        catch(IOException){check(true,"setup theme rejects an arbitrary remote or unstaged wallpaper path");}
        LockScreen.Stage(mount);
        var image=Path.Combine(mount,@"ProgramData\TechLoungeOS\Appearance\lock-screen.png");
        Wallpaper.Validate(image);
        check(new FileInfo(image).Length<2*1024*1024,"the approved clean lock-screen PNG is bundled within a two-MiB budget");
        var runner=new ProcessRunner(_=>{});
        var prefix=LockScreen.Functions+"\n$script:written=$null\nfunction Get-ItemProperty {}\n"+
            "function Write-TechLoungeLockScreenImage($Image){$script:written=$Image}\n"+
            "";
        var success=await runner.PowerShell(prefix+"Set-TechLoungeLockScreen "+Policy.Ps(image)+";if($script:written -ne "+Policy.Ps(image)+"){throw 'Wrong image'};'verified'");
        check(success.Contains("verified"),"lock-screen orchestration requires the exact selected asset and matching image readback using inert native API substitutes");
        try{await runner.PowerShell(prefix+"function Write-TechLoungeLockScreenImage {throw 'Windows did not read back the chosen lock screen'};Set-TechLoungeLockScreen "+Policy.Ps(image));throw new Exception("Expected mismatched lock-screen rejection");}
        catch(IOException ex)when(ex.Message.Contains("did not read back")){check(true,"successful lock-screen API completion alone cannot mark the wrong background complete");}
        try{await runner.PowerShell(prefix+"function Get-ItemProperty {[pscustomobject]@{NoChangingLockScreen=1}};Set-TechLoungeLockScreen "+Policy.Ps(image));throw new Exception("Expected managed lock-screen rejection");}
        catch(IOException ex)when(ex.Message.Contains("managed by policy")){check(true,"managed lock-screen policy is retained rather than overwritten");}
        await runner.PowerShell("$t=$null;$e=$null;[void][Management.Automation.Language.Parser]::ParseInput("+Policy.Ps(LockScreen.FirstLogin)+",[ref]$t,[ref]$e);if($e.Count){throw ($e|Out-String)}");
        check(true,"lock-screen setup parses in Windows PowerShell 5.1; no host lock screen was changed");
    }
}
