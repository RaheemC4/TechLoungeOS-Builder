using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public static class WindowsReleaseCatalog
{
    public static readonly Uri Address = new("https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information");
    public sealed record Release(string Name, Version Version, DateOnly Published, string Type, string KnowledgeBase);
    public sealed record VersionReport(Release LatestCompatible,Release? Current,Release? NewestFeature,DateTimeOffset CheckedAt,WindowsUpgradeRules.Offer? Upgrade=null);
    public static string DisplayName(Version version)=>version.Major==10 && version.Minor==0 ? version.Build switch
    {
        22000=>"Windows 11 21H2",22621=>"Windows 11 22H2",22631=>"Windows 11 23H2",26100=>"Windows 11 24H2",
        26200=>"Windows 11 25H2",28000=>"Windows 11 26H1",26300=>"Windows 11 26H2",_=>"Windows 11 · release unconfirmed"
    }:"Windows release unconfirmed";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static Release[] Parse(string html)
    {
        if (html.Length is < 10 or > 2097152) throw new IOException("Windows release information exceeds its supported size.");
        var releases = new List<Release>();
        foreach (Match section in Regex.Matches(html,
            @"<strong>\s*Version\s+(?<name>[0-9]{2}H[12])\s+\(OS build\s+(?<build>[0-9]{5,6})\)\s*</strong>\s*(?:</summary>\s*)?<table\b[^>]*>(?<table>.*?)</table>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout))
        {
            int build = int.Parse(section.Groups["build"].Value, CultureInfo.InvariantCulture);
            foreach (Match row in Regex.Matches(section.Groups["table"].Value, @"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase, Timeout))
            {
                var cells = Regex.Matches(row.Groups["row"].Value, @"<td\b[^>]*>(?<cell>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase, Timeout)
                    .Select(cell=>Text(cell.Groups["cell"].Value)).ToArray();
                if (cells.Length != 5 || cells[0] is not ("General Availability Channel" or "LTSC • General Availability Channel")) continue;
                var initial=cells[4].Length==0 && (cells[1].Length==0 || Regex.IsMatch(cells[1],@"\A[0-9]{4}-[0-9]{2} A\z",RegexOptions.CultureInvariant,Timeout));
                if ((!initial && !Regex.IsMatch(cells[1], @"\A[0-9]{4}-[0-9]{2} (B|C|D|OOB)\z", RegexOptions.CultureInvariant, Timeout)) ||
                    !DateOnly.TryParseExact(cells[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                    !Version.TryParse(cells[3], out var shortVersion) || shortVersion.Build != -1 || shortVersion.Major != build ||
                    (!initial && !Regex.IsMatch(cells[4], @"\AKB[0-9]{6,8}\z", RegexOptions.CultureInvariant, Timeout)))
                    throw new IOException("Microsoft's Windows release layout changed. No update was selected.");
                releases.Add(new(section.Groups["name"].Value.ToUpperInvariant(), new Version(10,0,build,shortVersion.Minor), date, initial?"GA":cells[1], cells[4]));
            }
        }
        if (releases.Count == 0 || releases.GroupBy(r=>(r.Version,r.Type)).Any(g=>g.Count()>1))
            throw new IOException("Microsoft's Windows release information is missing or ambiguous. No update was selected.");
        return releases.ToArray();
    }

    internal static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html,@"<[^>]*>","",RegexOptions.CultureInvariant,Timeout)),@"\s+"," ",RegexOptions.CultureInvariant,Timeout).Trim();
    public static VersionReport Summarise(IEnumerable<Release> releases,Version source,DateTimeOffset checkedAt)
    {
        var rows=releases.Where(r=>r.Published<=DateOnly.FromDateTime(checkedAt.UtcDateTime)).ToArray();
        var latest=LatestCompatible(rows,source,DateOnly.FromDateTime(checkedAt.UtcDateTime));
        var current=rows.Where(r=>r.Version==source).OrderBy(r=>r.Published).FirstOrDefault();
        var newest=rows.Where(r=>r.Type=="GA").OrderByDescending(r=>r.Name,StringComparer.Ordinal).FirstOrDefault();
        return new(latest,current,newest,checkedAt,WindowsUpgradeRules.Find(rows,source,DateOnly.FromDateTime(checkedAt.UtcDateTime)));
    }

    public static Release LatestCompatible(IEnumerable<Release> releases, Version source, DateOnly today)
    {
        if(source.Major!=10 || source.Minor!=0 || source.Build<22000 || source.Revision<0)
            throw new IOException("A full Windows 11 source version is required before checking for image updates.");
        // Never pick the highest build number: some releases target different devices.
        // Feature enablement/upgrade packages require their own preservation validation.
        return releases.Where(r=>r.Version.Build==source.Build && r.Published<=today &&
            (r.Type.EndsWith(" B",StringComparison.Ordinal)||r.Type.EndsWith(" OOB",StringComparison.Ordinal)))
            .OrderByDescending(r=>r.Version).FirstOrDefault()
            ?? throw new IOException("No stable cumulative update was found for this Windows release. The source was not changed.");
    }
}
