namespace TechLoungeOS;

// Windows Setup owns this first-desktop background, before Explorer starts.
// The per-monitor helper refines it afterwards without delaying the desktop.
public static class InitialWallpaper
{
    public static string? Stage(string mount, BuildOptions options)
    {
        var source = options.WallpaperFile ?? (options.WallpaperPreset == null ? null :
            Personalisation.Catalog.PathFor(WallpaperPresets.Library, options.WallpaperPreset));
        if (source == null) return null;
        Wallpaper.Validate(source);
        var name = "first-desktop" + Path.GetExtension(source).ToLowerInvariant();
        var folder = Path.Combine(mount, @"Windows\Web\Wallpaper\TechLoungeOS");
        Policy.RequireUnlinkedPath(folder);
        if (Directory.Exists(folder)) throw new IOException("An existing initial wallpaper needs review; use the original source image.");
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, name);
        File.Copy(source, destination, false);
        if (RuntimePack.Hash(source) != RuntimePack.Hash(destination)) throw new IOException("Initial desktop wallpaper copy did not verify.");
        return @"%WINDIR%\Web\Wallpaper\TechLoungeOS\" + name;
    }
}
