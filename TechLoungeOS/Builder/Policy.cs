namespace TechLoungeOS;

public record Edition(int ImageIndex, string ImageName, string Architecture, string Version,string? EditionId=null);
public record Component(string Kind, string Name);
public record BuildOptions(string Iso, int Index, string Workspace, string Output, string Oscdimg,
    Component[] Remove, string[] Apps, bool GameMode, bool DisableSuggestions, string? TweaksFolder)
{
    public string? WallpaperFile { get; init; }
    public string? WallpaperPreset { get; init; } = Personalisation.Catalog.Default;
    public bool AnimatedWinPe { get; init; }
    public string? SetupRegion { get; init; }
    public string? SetupKeyboard { get; init; }
    public bool UseCurrentTweaksInstaller { get; init; }
    public bool IncludeNvidiaSetup { get; init; } = true;
    public bool IncludeAmdIntelGraphics { get; init; } = true;
    public bool Target16GbUsb { get; init; } = true;
    public bool IncludeRuntimePack { get; init; } = true;
    public bool IncludeEdge { get; init; } = true;
    public bool IncludeNet8 { get; init; }
    public bool IncludeStoreXbox { get; init; } = true;
    public string? StoreXboxPackages { get; init; }
    public bool IncludeOffice { get; init; } = true;
    public string? OfficeSource { get; init; }
    public string? WindowsUpdateFolder { get; init; }
    public bool UpdateWindows { get; init; }
}

public static class Policy
{
    // Positive lists prevent arbitrary servicing of security, recovery or shared frameworks.
    public static readonly string[] RemovableApps = ["Microsoft.BingNews", "Microsoft.BingWeather",
        "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.People", "Microsoft.WindowsFeedbackHub", "Microsoft.YourPhone", "Clipchamp.Clipchamp"];
    public static readonly string[] RemovableFeatures = ["Printing-XPSServices-Features", "WorkFolders-Client"];
    public static bool ValidApps(string[] apps) => apps.Length <= 100 && apps.All(id=>AppCatalog.ValidId(id)&&!RecommendedApps.UnsupportedDiscord(id)) &&
        apps.Select(RecommendedApps.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() == apps.Length;
    public static bool CanRemove(Component c) => c.Kind switch
    {
        "Appx" => RemovableApps.Contains(c.Name, StringComparer.Ordinal),
        "Feature" => RemovableFeatures.Contains(c.Name, StringComparer.Ordinal),
        _ => false
    };
    public static string Ps(string value) => "'" + value.Replace("'", "''") + "'";
    public static void Validate(BuildOptions o,bool requireIsoTool=true)
    {
        SetupRegional.ValidateSelections(o);
        Wallpaper.Validate(o.WallpaperFile);
        WallpaperPresets.Validate(o.WallpaperPreset);
        if(o.UpdateWindows && o.WindowsUpdateFolder != null)
            throw new InvalidOperationException("Choose automatic Microsoft update discovery or a prepared Windows update plan, not both.");
        if(o.WindowsUpdateFolder != null)
        {
            _ = WindowsImageUpdates.ReadPlan(o.WindowsUpdateFolder);
            var updates = Path.GetFullPath(o.WindowsUpdateFolder).TrimEnd(Path.DirectorySeparatorChar);
            var work = Path.GetFullPath(o.Workspace).TrimEnd(Path.DirectorySeparatorChar);
            if(work.Equals(updates,StringComparison.OrdinalIgnoreCase) || work.StartsWith(updates+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The workspace cannot be inside the Windows update package folder.");
        }
        if (o.OfficeSource != null)
        {
            if (!Path.IsPathFullyQualified(o.OfficeSource) || o.OfficeSource.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(Path.Combine(o.OfficeSource, "setup.exe")) || !Directory.Exists(Path.Combine(o.OfficeSource, "Office")))
                throw new InvalidOperationException("The local Office cache must contain setup.exe and its Office folder.");
            RequireUnlinkedPath(o.OfficeSource);
            var office = Path.GetFullPath(o.OfficeSource).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetFullPath(o.Workspace).StartsWith(office + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The workspace cannot be inside the Office cache.");
        }
        if (o.IncludeNet8 && !o.IncludeRuntimePack) throw new InvalidOperationException("Enable the runtime pack before adding .NET 8 compatibility.");
        if (o.StoreXboxPackages != null)
        {
            if (!Path.IsPathFullyQualified(o.StoreXboxPackages) || o.StoreXboxPackages.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(o.StoreXboxPackages)) throw new InvalidOperationException("Choose an existing local offline Store/Xbox package folder.");
            Policy.RequireUnlinkedPath(o.StoreXboxPackages);
            var supplied = Path.GetFullPath(o.StoreXboxPackages).TrimEnd(Path.DirectorySeparatorChar);
            var work = Path.GetFullPath(o.Workspace).TrimEnd(Path.DirectorySeparatorChar);
            if (work.Equals(supplied, StringComparison.OrdinalIgnoreCase) || work.StartsWith(supplied + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The workspace cannot be inside the Store/Xbox package folder.");
        }
        foreach (var path in new[] { o.Iso, o.Workspace, o.Output, o.Oscdimg })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
                throw new InvalidOperationException("Choose full local drive paths for the ISO, workspace, output and ADK tool. Network paths and alternate data streams are not supported.");
        if (o.Workspace.Contains('#') || o.Workspace.Contains(','))
            throw new InvalidOperationException("The workspace cannot contain # or comma because the ISO boot-entry format uses these separators.");
        if (!File.Exists(o.Iso) || !o.Iso.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select an existing Windows ISO.");
        if (o.Index < 1 || o.Remove.Any(c => !CanRemove(c)) || !ValidApps(o.Apps))
            throw new InvalidOperationException("The build contains an unsupported edition, component or app.");
        if (requireIsoTool && (!File.Exists(o.Oscdimg) || !Path.GetFileName(o.Oscdimg).Equals("oscdimg.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A required Microsoft build component is unavailable. Check your connection and try again; Builder prepares it automatically.");
        if (!o.Output.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || File.Exists(o.Output))
            throw new InvalidOperationException("Choose a new output ISO filename. Existing files are never overwritten.");
        var workspace = Path.GetFullPath(o.Workspace).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.Exists(workspace) || File.Exists(workspace))
            throw new InvalidOperationException("Choose a new, empty workspace path.");
        foreach (var p in new[] { o.Iso, o.Output, o.Oscdimg, o.TweaksFolder, o.StoreXboxPackages, o.OfficeSource, o.WindowsUpdateFolder }.Where(p => p != null))
            if (Path.GetFullPath(p!).StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Inputs and output must be outside the workspace.");
        if (o.UseCurrentTweaksInstaller && (o.TweaksFolder == null || !Provisioning.HasPrivateRuntime(o.TweaksFolder)))
            throw new InvalidOperationException("Current Tweaks integration requires a support folder with the complete trusted x64 WebView2Runtime folder.");
        if (!o.UseCurrentTweaksInstaller && o.TweaksFolder != null && !File.Exists(Path.Combine(o.TweaksFolder, "TechLoungeTweaks.exe")))
            throw new InvalidOperationException("Choose an extracted release containing TechLoungeTweaks.exe.");
        if (o.TweaksFolder != null && workspace.StartsWith(Path.GetFullPath(o.TweaksFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The workspace cannot be inside the TechLoungeTweaks release.");
        RequireUnlinkedPath(workspace);
        RequireUnlinkedPath(Path.GetDirectoryName(Path.GetFullPath(o.Output))!);
    }

    public static void RequireUnlinkedPath(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item != null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked destination paths are not accepted: " + item.FullName);
    }

    public static void ValidateStorage(string format, long free, long required, bool workspace)
    {
        if (workspace && !format.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DISM workspace must be on a local NTFS drive.");
        if (!workspace && (format.Equals("FAT32", StringComparison.OrdinalIgnoreCase) || format.Equals("FAT", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an NTFS or exFAT output drive. FAT/FAT32 cannot hold a large Windows ISO.");
        if (free < required)
            throw new InvalidOperationException($"Insufficient disk space. Estimated required: {Math.Ceiling(required / 1073741824d)} GB; available: {Math.Floor(free / 1073741824d)} GB. Choose a larger drive.");
    }
}
