using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

// Versioned data travels with the signed app update. Internet release names never authorise a new servicing route.
public static class WindowsUpgradeRules
{
    public sealed record Rule(string Id,string Name,int[] SourceBuilds,int TargetBuild,int MinimumRevision,string[] Editions,
        string KnowledgeBase,string File,Uri Address,long Bytes,string Sha256,Uri Support);
    public sealed record Offer(string RuleId,WindowsReleaseCatalog.Release Baseline,WindowsReleaseCatalog.Release Target);
    private sealed record Catalog(int Schema,Rule[] Rules);
    private static readonly Rule[] Rules=Load();
    private static Rule[] Load()
    {
        using var input=typeof(WindowsUpgradeRules).Assembly.GetManifestResourceStream("TechLoungeOS.WindowsUpgradeRules.json")
            ??throw new IOException("Windows upgrade compatibility data is missing.");
        var catalog=JsonSerializer.Deserialize<Catalog>(input,new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=8})
            ??throw new IOException("Windows upgrade compatibility data is empty.");
        if(catalog.Schema!=1 || catalog.Rules is not {Length:>0 and <=32})throw new IOException("Unsupported Windows upgrade compatibility data.");
        foreach(var rule in catalog.Rules)
        {
            if(rule is null || !Regex.IsMatch(rule.Id??"",@"\A[a-z0-9-]{1,64}\z") || !Regex.IsMatch(rule.Name??"",@"\A[0-9]{2}H[12]\z") ||
                rule.SourceBuilds is not {Length:>0 and <=8} || rule.SourceBuilds.Any(b=>b<22000 || b>=rule.TargetBuild) || rule.MinimumRevision<1 ||
                rule.Editions is not {Length:>0 and <=32} || rule.Editions.Any(e=>!Regex.IsMatch(e,@"\A[A-Za-z0-9]{1,64}\z")) ||
                !Regex.IsMatch(rule.KnowledgeBase??"",@"\AKB[0-9]{6,8}\z") ||
                !Regex.IsMatch(rule.File??"",@"\Awindows11\.0-"+Regex.Escape(rule.KnowledgeBase!)+@"-x64_[a-f0-9]{40}\.msu\z",RegexOptions.IgnoreCase) ||
                rule.Address is null || !WindowsUpdateDownload.AllowedPayload(rule.Address) || Uri.UnescapeDataString(Path.GetFileName(rule.Address.AbsolutePath))!=rule.File ||
                rule.Bytes is <1 or >4194304 || !Regex.IsMatch(rule.Sha256??"",@"\A[A-F0-9]{64}\z") ||
                rule.Support is null || rule.Support.Scheme!="https" || rule.Support.Host!="support.microsoft.com" || rule.Support.UserInfo.Length!=0 || !rule.Support.IsDefaultPort)
                throw new IOException("Unsafe Windows upgrade compatibility rule.");
        }
        if(catalog.Rules.DistinctBy(r=>r.Id).Count()!=catalog.Rules.Length)throw new IOException("Duplicate Windows upgrade compatibility rule.");
        return catalog.Rules;
    }
    public static Rule Get(string id)=>Rules.SingleOrDefault(r=>r.Id==id)??throw new IOException("This Windows upgrade route needs a newer Builder compatibility update.");
    public static Offer? Find(IEnumerable<WindowsReleaseCatalog.Release> releases,Version source,DateOnly today)
    {
        if(source.Major!=10 || source.Minor!=0 || source.Revision<0)return null;
        var published=releases.Where(r=>r.Published<=today).ToArray();
        var rows=published.Where(r=> (r.Type.EndsWith(" B",StringComparison.Ordinal)||r.Type.EndsWith(" OOB",StringComparison.Ordinal))).ToArray();
        var offers=new List<Offer>();
        foreach(var rule in Rules.Where(r=>r.SourceBuilds.Contains(source.Build)))
        {
            if(!published.Any(r=>r.Type=="GA" && r.Name==rule.Name && r.Version.Build==rule.TargetBuild))continue;
            var target=rows.Where(r=>r.Version.Build==rule.TargetBuild && r.Name==rule.Name).OrderByDescending(r=>r.Version).FirstOrDefault();
            if(target is null || source.Revision>target.Version.Revision || target.Version.Revision<rule.MinimumRevision)continue;
            var baseline=rows.Where(r=>r.Version==new Version(10,0,source.Build,target.Version.Revision)).OrderBy(r=>r.Published).FirstOrDefault();
            if(baseline is not null)offers.Add(new(rule.Id,baseline,target));
        }
        return offers.OrderByDescending(o=>o.Target.Name,StringComparer.Ordinal).FirstOrDefault();
    }
    public static Rule RequireTransition(string id,Version source,Version baseline,Version target)
    {
        var rule=Get(id);
        if(source.Major!=10 || source.Minor!=0 || source.Revision<0 || !rule.SourceBuilds.Contains(source.Build) ||
            baseline.Major!=10 || baseline.Minor!=0 || baseline.Build!=source.Build || baseline<source || baseline.Revision<rule.MinimumRevision ||
            target.Major!=10 || target.Minor!=0 || target.Build!=rule.TargetBuild || target.Revision!=baseline.Revision || target<=source)
            throw new IOException("The Windows enablement route does not match its supported source, prerequisites and target.");
        return rule;
    }
    public static WindowsReleaseCatalog.VersionReport ForEdition(WindowsReleaseCatalog.VersionReport report,string? edition)
    {
        // Older caller metadata can be tentative; the mounted hive is always checked again before servicing.
        return report.Upgrade is { } offer && edition is not null && !Get(offer.RuleId).Editions.Contains(edition,StringComparer.OrdinalIgnoreCase)
            ?report with{Upgrade=null}:report;
    }
    public static void RequireEdition(string ruleId,string edition)
    {
        if(!Get(ruleId).Editions.Contains(edition,StringComparer.OrdinalIgnoreCase))
            throw new IOException("The selected Windows edition is not eligible for this enablement upgrade. LTSC editions are not converted. Update within its existing release instead.");
    }
}
