namespace TechLoungeOS;

internal static class WindowsUpdatePreservation
{
    internal sealed record Snapshot(string[] AbsentPayloads,string[] SystemApps);
    internal sealed record Difference(string Kind,string Name,string? Before,string? After);

    // Keep every mismatch before the strict gates throw, so one failure cannot hide others.
    // This is diagnostic evidence only; it neither authorises migrations nor edits the image.
    internal static Difference[] Differences(ImageServicing.Package[] beforeApps,ImageServicing.Package[] afterApps,
        ImageServicing.Feature[] beforeFeatures,ImageServicing.Feature[] afterFeatures,
        IReadOnlyDictionary<string,string> beforeConfig,IReadOnlyDictionary<string,string> afterConfig,
        Snapshot beforePayloads,Snapshot afterPayloads)
    {
        var result=new List<Difference>();
        foreach(var app in beforeApps.Where(a=>!afterApps.Any(b=>a.PackageName==b.PackageName)))
            result.Add(new("app",app.DisplayName,app.PackageName,null));
        foreach(var app in afterApps.Where(a=>!beforeApps.Any(b=>a.PackageName==b.PackageName)))
            result.Add(new("app",app.DisplayName,null,app.PackageName));
        foreach(var name in beforeFeatures.Select(f=>f.FeatureName).Union(afterFeatures.Select(f=>f.FeatureName),StringComparer.Ordinal))
        {
            var before=beforeFeatures.FirstOrDefault(f=>f.FeatureName==name)?.State;
            var after=afterFeatures.FirstOrDefault(f=>f.FeatureName==name)?.State;
            if(!string.Equals(before,after,StringComparison.OrdinalIgnoreCase))result.Add(new("feature",name,before,after));
        }
        var source=new Dictionary<string,string>(beforeConfig,StringComparer.OrdinalIgnoreCase);
        var target=new Dictionary<string,string>(afterConfig,StringComparer.OrdinalIgnoreCase);
        foreach(var name in source.Keys.Union(target.Keys,StringComparer.OrdinalIgnoreCase))
        {
            var before=source.GetValueOrDefault(name);var after=target.GetValueOrDefault(name);
            if(before!=after)result.Add(new("configuration",name,before,after));
        }
        foreach(var path in beforePayloads.AbsentPayloads.Except(afterPayloads.AbsentPayloads,StringComparer.OrdinalIgnoreCase))
            result.Add(new("security-payload",path,"absent","present"));
        foreach(var name in afterPayloads.SystemApps.Except(beforePayloads.SystemApps,StringComparer.OrdinalIgnoreCase))
            result.Add(new("system-app",name,"absent","present"));
        foreach(var name in beforePayloads.SystemApps.Except(afterPayloads.SystemApps,StringComparer.OrdinalIgnoreCase))
            result.Add(new("system-app",name,"present","absent"));
        return result.OrderBy(d=>d.Kind,StringComparer.Ordinal).ThenBy(d=>d.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    // These cover Defender/security payloads which are not always provisioned Appx packages or optional features.
    private static readonly string[] Payloads=[@"Program Files\Windows Defender",@"ProgramData\Microsoft\Windows Defender",
        @"Windows\System32\SecurityHealthService.exe",@"Windows\System32\SecurityHealthSystray.exe",
        @"Windows\System32\WindowsPowerShell\v1.0\Modules\Defender"];
    internal static Snapshot Capture(string mount)=>new(Payloads.Where(p=>!Exists(Path.Combine(mount,p))).ToArray(),SystemApps(mount));
    internal static void Verify(string mount,Snapshot before,WindowsUpdateMigrations.Route? migration=null)
    {
        foreach(var absent in before.AbsentPayloads)
            if(Exists(Path.Combine(mount,absent)))throw new IOException("Preservation check: Windows update restored a removed security payload: "+absent+". No image will be exported.");
        var after=SystemApps(mount);
        foreach(var app in after)
            if(!before.SystemApps.Contains(app,StringComparer.OrdinalIgnoreCase) && !WindowsUpdateMigrations.IsReviewedFramework(migration,mount,before,app))
                throw new IOException("Preservation check: Windows update added a system app: "+app+". No image will be exported.");
        foreach(var app in before.SystemApps.Except(after,StringComparer.OrdinalIgnoreCase))
            if(!WindowsUpdateMigrations.IsReviewedFramework(migration,mount,before,app,retired:true))
                throw new IOException("Preservation check: Windows update removed a source system app: "+app+". No image will be exported.");
    }
    private static bool Exists(string path)=>File.Exists(path)||Directory.Exists(path);
    private static string[] SystemApps(string mount)
    {
        var folder=Path.Combine(mount,@"Windows\SystemApps");if(!Directory.Exists(folder))return [];
        Policy.RequireUnlinkedPath(folder);
        return Directory.EnumerateDirectories(folder).Select(p=>Path.GetFileName(p)!).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
