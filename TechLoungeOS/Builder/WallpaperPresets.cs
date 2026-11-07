using TechLoungeOS.Personalisation;

namespace TechLoungeOS;

public static class WallpaperPresets
{
    public record Choice(string Id,string Label);
    public static Choice[] Designs => Catalog.Designs.Select((id,index)=>new Choice(id,Catalog.Titles[index])).ToArray();
    public static Choice[] Colours => [new("ice","Ice"),new("violet","Violet"),new("amber","Amber")];
    public static string Library => Path.Combine(AppContext.BaseDirectory,"Wallpapers");
    public static void Validate(string? id)
    {
        if(id!=null && !Catalog.Valid(id))throw new InvalidOperationException("Choose a listed wallpaper design and colour.");
    }
    public static void Stage(string provisioning)
    {
        var destination=Path.Combine(provisioning,"Wallpapers");Policy.RequireUnlinkedPath(destination);
        if(Directory.Exists(destination))throw new IOException("Existing wallpaper collection would be overwritten.");
        ValidateLibrary();
        var executable=Path.Combine(AppContext.BaseDirectory,"Personalise.exe");SourcePreservation.RequireImageFile(executable,512,1<<20);
        Directory.CreateDirectory(destination);
        foreach(var design in Catalog.Designs)foreach(var colour in Catalog.Colours)
            File.Copy(Catalog.PathFor(Library,design+"."+colour),Path.Combine(destination,Catalog.FileName(design+"."+colour)),false);
        File.Copy(executable,Path.Combine(destination,"Personalise.exe"),false);
    }
    public static void ValidateLibrary()
    {
        Policy.RequireUnlinkedPath(Library);
        foreach(var design in Catalog.Designs)foreach(var colour in Catalog.Colours)
            Wallpaper.Validate(Catalog.PathFor(Library,design+"."+colour));
    }
    public const string LibraryShortcut="""

        Invoke-Step 'wallpaper-library' {
          $folder=Join-Path ([Environment]::GetFolderPath('Programs')) 'TechLoungeOS'
          New-Item -Path $folder -ItemType Directory -Force|Out-Null
          $link=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $folder 'Wallpapers.lnk'))
          $link.TargetPath=Join-Path $env:ProgramData 'TechLoungeOS\Wallpapers\Personalise.exe'
          $link.WorkingDirectory=Split-Path $link.TargetPath
          $link.Description='Choose a TechLoungeOS wallpaper design and colour'
          $link.Save()
        }
        """;
    public static string FirstLogin(string? id)=>id==null?"":$$"""

        Invoke-Step 'desktop-background' {
          $helper=Join-Path $env:ProgramData 'TechLoungeOS\Wallpapers\Personalise.exe'
          $process=Start-Process -FilePath $helper -ArgumentList @('--apply',{{Policy.Ps(id)}}) -PassThru -Wait -WindowStyle Hidden
          if($process.ExitCode -ne 0){throw 'Preset wallpaper could not be applied. Review LocalAppData\TechLoungeOS\wallpaper-error.txt; other provisioning continues.'}
        }
        """;
}
