using System.Text.RegularExpressions;

namespace TechLoungeOS;

public record CatalogApp(string Name, string Id, string Version, string? Description = null)
{
    public string Details => Description == null ? $"{Id} · {Version} · WinGet community repository" :
        Description + (Id.StartsWith("TechLounge.", StringComparison.Ordinal) ? "" : " · " + Id);
}

public static class AppCatalog
{
    // Only an explicitly labelled architecture variant is blocked here; generic ARM developer tools remain searchable.
    public static bool ExplicitArmVariant(CatalogApp app)=>
        (app.Id.EndsWith(".arm64",StringComparison.OrdinalIgnoreCase)||app.Id.EndsWith(".arm",StringComparison.OrdinalIgnoreCase))&&
        Regex.IsMatch(app.Name,@"\bARM(?:64)?\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    // IDs also become per-user marker filenames. Reject path characters and script syntax.
    public static bool ValidId(string id) => id.Length <= 200 &&
        Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_-]*(?:\.[A-Za-z0-9][A-Za-z0-9_-]*)+\z");

    internal static string SearchQuery(string query)
    {
        if (query.Trim().Length is < 2 or > 100 || query.Any(char.IsControl) || query.TrimStart().StartsWith('-'))
            throw new InvalidOperationException("Enter 2 to 100 characters of an app name.");
        return query.Trim();
    }
    public static async Task<CatalogApp[]> Search(string query,CancellationToken ct=default)
    {
        query=SearchQuery(query);ct.ThrowIfCancellationRequested();
        try
        {
            var text = await new ProcessRunner(_ => { }).Run("winget.exe",
                ["search", "--query", query.Trim(), "--source", "winget", "--count", "30",
                 "--disable-interactivity", "--accept-source-agreements"],ct);
            return Parse(text);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("App search needs Microsoft App Installer (WinGet) on this computer. Your existing selections are kept.", ex);
        }
    }

    public static CatalogApp[] Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var separator = Array.FindIndex(lines, l => Regex.IsMatch(l.Trim(), @"\A-{10,}\z"));
        if (separator < 1) throw new InvalidDataException("WinGet returned an unfamiliar result format. Update App Installer and try again.");
        // Resolve column starts from the header, independent of translated column names.
        var columns = Regex.Matches(lines[separator - 1], @"\S+")
            .Select(m => m.Index).ToArray();
        if (columns.Length < 3) throw new InvalidDataException("WinGet app columns could not be read safely.");
        var results = new List<CatalogApp>();
        foreach (var line in lines.Skip(separator + 1))
        {
            if (line.Length <= columns[2]) continue;
            var name = line[..columns[1]].Trim();
            var id = line[columns[1]..columns[2]].Trim();
            var version = line[columns[2]..(columns.Length > 3 ? Math.Min(columns[3], line.Length) : line.Length)].Trim();
            if (name.Length > 0 && !name.Contains('…') && ValidId(id) && version.Length > 0 && !version.Contains('…'))
                results.Add(new(name, id, version));
        }
        return RecommendedApps.Selectable(results).Take(30).ToArray();
    }
}
