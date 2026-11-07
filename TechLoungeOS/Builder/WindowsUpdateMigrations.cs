using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

// Exact, reviewed servicing transitions. Unknown versions or custom values still fail closed.
internal static class WindowsUpdateMigrations
{
    internal const string CameraFeature="Microsoft-Windows-Camera-CCX-Deployment";
    internal sealed record Transition(string Key,string? Before,string? After,string? Replaces=null);
    internal sealed record Requirement(string Key,string Hash);
    internal sealed record MetadataTransition(string Key,string Before,string After);
    internal sealed record TaskTransition(string BeforeRaw,string BeforeFiles,string AfterFiles,string AfterRegistration,string AfterSemantic);
    internal sealed record FrameworkTransition(string Added,string Source,string SourceManifest,string AddedManifest,bool RetiresSource=false);
    internal sealed record Route(string RuleId,string SourceVersion,string TargetVersion,string[] PackageSha256,Transition[] Transitions,
        Requirement[]? RequiredServices=null,MetadataTransition[]? Metadata=null,TaskTransition? Tasks=null,FrameworkTransition[]? Frameworks=null);
    private sealed record Catalog(int Schema,Route[] Routes);
    private static readonly Route[] Routes=Load();

    private static Route[] Load()
    {
        using var input=typeof(WindowsUpdateMigrations).Assembly.GetManifestResourceStream("TechLoungeOS.WindowsUpdateMigrations.json")
            ??throw new IOException("Windows migration compatibility data is missing.");
        var catalog=JsonSerializer.Deserialize<Catalog>(input,new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=8})
            ??throw new IOException("Windows migration compatibility data is empty.");
        bool Hash(string? value)=>value is not null && Regex.IsMatch(value,@"\A[A-F0-9]{64}\z",RegexOptions.CultureInvariant);
        if(catalog.Schema!=2 || catalog.Routes is not {Length:>0 and <=32})throw new IOException("Unsupported Windows migration compatibility data.");
        foreach(var route in catalog.Routes)
        {
            if(route is null || route.RuleId is null || !Version.TryParse(route.SourceVersion,out var source) ||
                !Version.TryParse(route.TargetVersion,out var target) || target<=source ||
                route.PackageSha256 is not {Length:>0 and <=8} || route.PackageSha256.Any(h=>!Hash(h)) ||
                route.Transitions is not {Length:>0 and <=128} ||
                route.Transitions.Any(t=>t is null || !Regex.IsMatch(t.Key??"",@"\AControlSet[0-9]{3}\\[A-Za-z0-9_.-]{1,128}\z",RegexOptions.CultureInvariant) ||
                    (t.Before is null && t.After is null) || t.Before==t.After || (t.Before is not null&&!Hash(t.Before)) || (t.After is not null&&!Hash(t.After))) ||
                route.Transitions.DistinctBy(t=>t.Key,StringComparer.OrdinalIgnoreCase).Count()!=route.Transitions.Length)
                throw new IOException("Unsafe Windows migration compatibility data.");
            WindowsUpgradeRules.RequireTransition(route.RuleId,source,new Version(source.Major,source.Minor,source.Build,target.Revision),target);
            bool Service(string? key)=>Regex.IsMatch(key??"",@"\AControlSet[0-9]{3}\\[A-Za-z0-9_.-]{1,128}\z",RegexOptions.CultureInvariant);
            bool App(string? name)=>Regex.IsMatch(name??"",@"\AMicrosoft\.[A-Za-z0-9.]{1,100}_8wekyb3d8bbwe\z",RegexOptions.CultureInvariant);
            if(route.RequiredServices is { } required && (required.Length is <1 or >16 || required.Any(r=>r is null||!Service(r.Key)||!Hash(r.Hash)) || required.DistinctBy(r=>r.Key,StringComparer.OrdinalIgnoreCase).Count()!=required.Length) ||
                route.Metadata is { } metadata && (metadata.Length is <1 or >3 || metadata.Any(m=>m is null||!WindowsUpdateMetadata.Fields.Any(f=>f.RawKey==m.Key)||!(Hash(m.Before)||m.Before=="missing-value")||!Hash(m.After)||m.Before==m.After) || metadata.DistinctBy(m=>m.Key).Count()!=metadata.Length) ||
                route.Tasks is { } tasks && new[]{tasks.BeforeRaw,tasks.BeforeFiles,tasks.AfterFiles,tasks.AfterRegistration,tasks.AfterSemantic}.Any(h=>!Hash(h)) ||
                route.Frameworks is { } frameworks && (frameworks.Length is <1 or >8 || route.RequiredServices is null || frameworks.Any(f=>f is null||!App(f.Added)||!App(f.Source)||f.Added==f.Source||!Hash(f.SourceManifest)||!Hash(f.AddedManifest)) || frameworks.DistinctBy(f=>f.Added).Count()!=frameworks.Length || frameworks.Where(f=>f.RetiresSource).DistinctBy(f=>f.Source).Count()!=frameworks.Count(f=>f.RetiresSource)))
                throw new IOException("Unsafe Windows metadata migration requirements.");
            foreach(var transition in route.Transitions.Where(t=>t.Replaces is not null))
                if(transition.Before is not null || transition.After is null ||
                    route.Transitions.Count(old=>old.Key==transition.Replaces && old.Before is not null && old.After is null &&
                        old.Key.Split('\\')[0]==transition.Key.Split('\\')[0])!=1)
                    throw new IOException("A replacement service requires one matching retired source service.");
        }
        if(catalog.Routes.DistinctBy(r=>(r.RuleId,r.SourceVersion,r.TargetVersion)).Count()!=catalog.Routes.Length)
            throw new IOException("Duplicate Windows migration compatibility data.");
        return catalog.Routes;
    }

    internal static Route? Find(WindowsImageUpdates.Plan plan)=>Routes.SingleOrDefault(r=>
        plan.Feature?.RuleId==r.RuleId && plan.SourceVersion==r.SourceVersion && plan.TargetVersion==r.TargetVersion &&
        plan.Packages.Select(p=>p.Sha256).SequenceEqual(r.PackageSha256,StringComparer.OrdinalIgnoreCase));

    internal static void RequireSource(Route? route,string mount,IReadOnlyDictionary<string,string> before,WindowsUpdatePreservation.Snapshot payloads)
    {
        if(route is null)return;
        RequireServices(route,before);
        if(route.Tasks is { } task && (before.GetValueOrDefault(TaskCacheIdentity.RawKey)!=task.BeforeRaw || before.GetValueOrDefault(WindowsUpdateMetadata.TaskFilesKey)!=task.BeforeFiles))
            throw new IOException("This Windows upgrade's reviewed task migration does not match your source task definitions or permissions. Existing task customisations were retained; no update was applied.");
        foreach(var framework in route.Frameworks??[])
        {
            if(!payloads.SystemApps.Contains(framework.Source,StringComparer.OrdinalIgnoreCase) || !ManifestMatches(mount,framework.Source,framework.SourceManifest))
                throw new IOException("This source removed or changed a required Windows framework. The upgrade was stopped to preserve its existing components.");
        }
    }

    private static void RequireServices(Route route,IReadOnlyDictionary<string,string> before)
    {
        if((route.RequiredServices??[]).Any(r=>before.GetValueOrDefault(r.Key)!=r.Hash))
            throw new IOException("This source's service settings differ from the reviewed Windows upgrade. Custom disabled or removed services will not be replaced.");
    }

    internal static bool IsReviewedFramework(Route? route,string mount,WindowsUpdatePreservation.Snapshot before,string app,bool retired=false)
    {
        var matches=(route?.Frameworks??[]).Where(f=>retired?f.RetiresSource&&f.Source.Equals(app,StringComparison.OrdinalIgnoreCase):f.Added.Equals(app,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1)return false;var row=matches[0];
        return before.SystemApps.Contains(row.Source,StringComparer.OrdinalIgnoreCase) &&
            Directory.Exists(Path.Combine(mount,@"Windows\SystemApps",row.Source))!=row.RetiresSource && ManifestMatches(mount,row.Added,row.AddedManifest);
    }

    private static bool ManifestMatches(string mount,string app,string expected)
    {
        var path=Path.Combine(mount,@"Windows\SystemApps",app,"AppxManifest.xml");
        SourcePreservation.RequireImageFile(path,1,2L<<20);Policy.RequireUnlinkedPath(Path.GetDirectoryName(path)!);
        return BootableDrive.Hash(path)==expected;
    }

    internal static async Task<bool> DisableNewCamera(ICommandRunner run,string mount,Route? route,
        ImageServicing.Feature[] before,ImageServicing.Feature[] after,CancellationToken ct)
    {
        if(route is null || before.Any(f=>f.FeatureName==CameraFeature))return false;
        var added=after.Where(f=>f.FeatureName==CameraFeature).ToArray();
        if(added.Length!=1)throw new IOException("The reviewed Windows migration did not provide its expected camera feature.");
        if(CameraDisabled(added[0].State))return false;
        ct.ThrowIfCancellationRequested();
        await run.Dism("/Image:"+mount,"/Disable-Feature","/FeatureName:"+CameraFeature,"/Remove","/NoRestart");
        return true; // The caller must independently inventory and verify the resulting state.
    }

    internal static void VerifyConfiguration(Route? route,IReadOnlyDictionary<string,string> before,IReadOnlyDictionary<string,string> after)
    {
        var expected=new Dictionary<string,string>(before,StringComparer.OrdinalIgnoreCase);
        if(route is not null)
        {
            RequireServices(route,before);
            var actual=new Dictionary<string,string>(after,StringComparer.OrdinalIgnoreCase);
            foreach(var transition in route.Transitions)
            {
                // Full branch hashes bind all other values, including Start, triggers and custom permissions.
                if(expected.GetValueOrDefault(transition.Key)!=transition.Before || actual.GetValueOrDefault(transition.Key)!=transition.After)continue;
                if(transition.Replaces is { } predecessor)
                {
                    var retired=route.Transitions.Single(t=>t.Key==predecessor);
                    // Do not resurrect a service the custom image had already stripped, or
                    // replace its custom disabled/settings branch with Windows defaults.
                    if(before.GetValueOrDefault(predecessor)!=retired.Before || actual.GetValueOrDefault(predecessor)!=retired.After)continue;
                }
                if(transition.After is null)expected.Remove(transition.Key);else expected[transition.Key]=transition.After;
            }
            foreach(var row in route.Metadata??[])
            {
                var value=WindowsUpdateMetadata.ValueKey(row.Key);var remainder=WindowsUpdateMetadata.RemainderKey(row.Key);
                if(expected.ContainsKey(row.Key) && actual.ContainsKey(row.Key) && before.GetValueOrDefault(value)==row.Before && actual.GetValueOrDefault(value)==row.After &&
                    before.TryGetValue(remainder,out var kept) && Regex.IsMatch(kept,@"\A[A-F0-9]{64}\z",RegexOptions.CultureInvariant) && actual.GetValueOrDefault(remainder)==kept)
                {expected[row.Key]=actual[row.Key];expected[value]=row.After;}
            }
            if(route.Tasks is { } tasks && before.GetValueOrDefault(TaskCacheIdentity.RawKey)==tasks.BeforeRaw &&
                before.GetValueOrDefault(WindowsUpdateMetadata.TaskFilesKey)==tasks.BeforeFiles && actual.GetValueOrDefault(WindowsUpdateMetadata.TaskFilesKey)==tasks.AfterFiles &&
                actual.GetValueOrDefault(TaskCacheIdentity.RegistrationKey)==tasks.AfterRegistration &&
                actual.GetValueOrDefault(TaskCacheIdentity.SemanticKey)==tasks.AfterSemantic &&
                new[]{TaskCacheIdentity.RawKey,TaskCacheIdentity.SemanticKey,TaskCacheIdentity.RegistrationKey}.All(k=>before.TryGetValue(k,out var h)&&Regex.IsMatch(h,@"\A[A-F0-9]{64}\z",RegexOptions.CultureInvariant)&&
                    actual.TryGetValue(k,out var next)&&Regex.IsMatch(next,@"\A[A-F0-9]{64}\z",RegexOptions.CultureInvariant)))
                foreach(var key in new[]{TaskCacheIdentity.RawKey,TaskCacheIdentity.SemanticKey,TaskCacheIdentity.RegistrationKey,WindowsUpdateMetadata.TaskFilesKey})
                    if(actual.TryGetValue(key,out var value))expected[key]=value;
        }
        SourcePreservation.VerifyServices(expected,after);
    }

    internal static ImageServicing.Feature[] ExistingFeatures(Route? route,ImageServicing.Feature[] before,ImageServicing.Feature[] after)
    {
        if(route is null || before.Any(f=>f.FeatureName==CameraFeature))return after;
        var added=after.Where(f=>f.FeatureName==CameraFeature).ToArray();
        // Windows client retains feature installation files for recovery even with /Remove.
        // Only this exact new feature may remain disabled; the configuration gate still
        // rejects a restored Ccx01000 driver registration or any other unreviewed service.
        if(added.Length!=1 || !CameraDisabled(added[0].State))
            throw new IOException("Preservation check: the newly introduced camera feature must be fully disabled.");
        return after.Where(f=>f.FeatureName!=CameraFeature).ToArray();
    }

    private static bool CameraDisabled(string state)=>state.Equals("Disabled",StringComparison.OrdinalIgnoreCase) ||
        state.Equals("Disabled with Payload Removed",StringComparison.OrdinalIgnoreCase);
}
