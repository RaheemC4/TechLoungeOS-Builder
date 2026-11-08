using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

// This exception is restricted to the inspected Ghost deployment bundle. Unknown
// deployment scripts are never rewritten or executed on the build host.
internal static class SourcePresentation
{
    internal const string DeployHash="EE7E91A8241BB43171E9C8CB62665C4611034F72BD5A55819944D5DF667E3371";
    private static readonly (string Path,string Hash)[] Inputs=[
        (@"Windows\System32\oobe\windeploy.cmd",DeployHash),
        (@"Windows\System32\update.dll","238742AD6FFCCE3ECE6514BFC6E5F308AA0041D700A8CF81B8939D4F2EACF85F"),
        (@"ProgramData\ssh\updateau.dll","21A63567225538044C55DAE520AF9456DA8D590BCBE8633444038350ED15DD9B")];
    internal static void Stage(string mount,BuildOptions options,Action<string> log)
    {
        var deploy=Path.Combine(mount,Inputs[0].Path);
        if(!File.Exists(deploy))return;
        var original=SetupHooks.Read(deploy);
        if(!Encoding.ASCII.GetString(original).Contains("zpaq x update.dll",StringComparison.OrdinalIgnoreCase))return;
        foreach(var input in Inputs){
            var path=Path.Combine(mount,input.Path);Policy.RequireUnlinkedPath(path);
            SourcePreservation.RequireImageFile(path,16,32L<<20);
            using var file=File.OpenRead(path);
            if(Convert.ToHexString(SHA256.HashData(file))!=input.Hash)
                throw new IOException("This source has an unrecognised Ghost first-login bundle. Its presentation/restart scripts need review before a TechLoungeOS build can continue.");
        }
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS\SourcePresentation");
        Policy.RequireUnlinkedPath(folder);
        if(Directory.Exists(folder))throw new IOException("Source presentation preparation already exists; use the original source image.");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"source-windeploy.cmd"),original);
        const string call="oobe\\windeploy.exe";
        var text=new UTF8Encoding(false,true).GetString(original);
        var matches=Regex.Matches(text,@"(?m)^oobe\\windeploy\.exe\r?$");
        if(matches.Count!=1)throw new IOException("The inspected source deployment handoff is ambiguous.");
        var prepare="\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%ProgramData%\\TechLoungeOS\\SourcePresentation\\prepare.ps1\"\r\nif errorlevel 1 exit /b 1\r\n";
        File.WriteAllText(Path.Combine(folder,"deploy-source.cmd"),text.Replace(call,prepare+call,StringComparison.Ordinal),new UTF8Encoding(false,true));
        using(var input=typeof(SourcePresentation).Assembly.GetManifestResourceStream("TechLoungeOS.SourcePresentation.ps1")??throw new IOException("Source presentation helper was not packaged."))
        using(var output=File.Create(Path.Combine(folder,"prepare.ps1")))input.CopyTo(output);
        var wallpaper=options.WallpaperFile!=null?"wallpaper"+Path.GetExtension(options.WallpaperFile).ToLowerInvariant():
            options.WallpaperPreset!=null?@"Wallpapers\"+Personalisation.Catalog.FileName(options.WallpaperPreset):null;
        if(wallpaper!=null){
            var chosen=Path.Combine(mount,@"ProgramData\TechLoungeOS",wallpaper);
            Wallpaper.Validate(chosen);
            var source=Path.Combine(mount,@"Windows\Resources\Themes\GHOSTV5_AI.01.theme");
            SourcePreservation.RequireImageFile(source,32,65536);
            var theme=File.ReadAllText(source);
            foreach(var (key,value) in new[]{("DisplayName","TechLoungeOS"),("Wallpaper",@"%ProgramData%\TechLoungeOS\"+wallpaper)}){
                var pattern=@"(?im)^"+key+@"=[^\r\n]*";
                if(Regex.Matches(theme,pattern).Count!=1)throw new IOException("The source desktop theme has an ambiguous "+key+" setting.");
                theme=Regex.Replace(theme,pattern,_=>key+"="+value);
            }
            File.WriteAllText(Path.Combine(folder,"TechLoungeOS.theme"),theme,Encoding.Unicode);
            File.WriteAllText(Path.Combine(folder,"wallpaper.txt"),wallpaper,new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(folder,"pending"),"Awaiting the retained source setup work. No app installer may start before ready.");
        // The archive replaces its original windeploy.cmd. Run the inspected
        // deployment body from a stable separate file so it cannot overwrite itself.
        File.WriteAllText(deploy,"@echo off\r\ncall \"%ProgramData%\\TechLoungeOS\\SourcePresentation\\deploy-source.cmd\" %*\r\nexit /b %errorlevel%\r\n",Encoding.ASCII);
        log("Retained the checked source setup work; its intro, forced restart and selected-theme override are replaced by the TechLoungeOS handoff.");
    }
    internal const string FirstLogin="\n"+"""
        $sourceHandoff=Join-Path $env:ProgramData 'TechLoungeOS\SourcePresentation'
        if(Test-Path -LiteralPath (Join-Path $sourceHandoff 'pending')){
          Invoke-Step 'source-presentation' {
            $waiting=[Diagnostics.Stopwatch]::StartNew()
            while(!(Test-Path -LiteralPath (Join-Path $sourceHandoff 'ready'))){
              Write-SetupProgress 'source-presentation' 'Finishing the retained Windows configuration before installing apps. Keep this PC on.'
              if(Test-Path -LiteralPath (Join-Path $sourceHandoff 'failed')){throw 'Source setup needs attention. Review ProgramData\TechLoungeOS\SourcePresentation\prepare.log before retrying.'}
              if($waiting.Elapsed.TotalMinutes -ge 5){throw 'Source setup has not finished. No app installer was started; review the source setup logs and retry.'}
              Start-Sleep -Milliseconds 500
            }
          }
          if(!(Test-Path -LiteralPath "$state\source-presentation.done")){exit 1}
        }
        """+"\n";
}
