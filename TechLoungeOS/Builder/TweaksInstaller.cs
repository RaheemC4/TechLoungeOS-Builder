using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

// Keep the upstream installer and updater intact. Hashes establish transport/package
// integrity, not antivirus approval or an independent publisher signature.
public static class TweaksInstaller
{
    public record Release(string Version, string Url, string Sha256, string Revision);
    public record Package(string Version, string Sha256);
    public static Release ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("schema", out var schema)
                || !schema.TryGetInt32(out var number) || number != 2)
                throw MetadataError();
            var version = RequiredString(r, "version");
            var tag = RequiredString(r, "release_tag");
            var revision = RequiredString(r, "app_revision");
            var url = RequiredString(r, "download_url");
            if (!r.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
                throw MetadataError();
            var hash = RequiredString(files, "TechLoungeTweaks/TechLoungeTweaks.zip");
            if (RequiredString(r, "distribution") != "installer" || !ValidVersion(version)
                || !Hash(hash) || !Hash(revision) || tag != $"app-{version}-{hash[..12].ToLowerInvariant()}"
                || url != $"https://github.com/RaheemC4/tech-tips/releases/download/{tag}/TechLoungeTweaks.zip")
                throw MetadataError();
            return new(version, url, hash, revision);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw MetadataError(ex); }
    }
    private static IOException MetadataError(Exception? inner = null) => new(
        "The current TechLoungeTweaks release metadata is unsupported. No unverified installer was used. Check for a Builder update and try again.", inner);
    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String && field.GetString() is string text
            ? text : throw MetadataError();
    public static bool ValidVersion(string value) => Regex.IsMatch(value, @"\A\d{4}\.\d{2}\.\d{2}\.\d{6}\z");
    private static bool Hash(string value) => Regex.IsMatch(value, @"\A[0-9a-fA-F]{64}\z");
    public static Package Extract(string zip, string folder, Release release)
    {
        if (!RuntimePack.Hash(zip).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("TechLoungeTweaks download SHA-256 mismatch.");
        using var archive = ZipFile.OpenRead(zip);
        var expected = new[] { "TechLoungeTweaks-Setup.exe", "installer.json", "INSTALL.txt" };
        if (archive.Entries.Count != expected.Length || archive.Entries.Any(e => !expected.Contains(e.FullName, StringComparer.Ordinal))
            || archive.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != expected.Length)
            throw new IOException("Unexpected files in the TechLoungeTweaks installer archive.");
        var manifest = archive.GetEntry("installer.json")!;
        if (manifest.Length > 16384) throw new IOException("TechLoungeTweaks installer manifest is too large.");
        using var manifestStream = manifest.Open();
        using var doc = JsonDocument.Parse(manifestStream);
        var r = doc.RootElement;
        var hash = r.GetProperty("sha256").GetString()!;
        if (r.GetProperty("schema").GetInt32() != 2 || r.GetProperty("version").GetString() != release.Version
            || r.GetProperty("app_revision").GetString() != release.Revision || !Hash(hash))
            throw new IOException("TechLoungeTweaks inner manifest does not match its release.");
        var exe = archive.GetEntry("TechLoungeTweaks-Setup.exe")!;
        if (exe.Length is < 1024 or > 512L * 1024 * 1024) throw new IOException("TechLoungeTweaks installer exceeds its size budget.");
        Policy.RequireUnlinkedPath(folder);
        Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, "TechLoungeTweaks-Setup.exe");
        using (var input = exe.Open())
        using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(target);
        if (!RuntimePack.Hash(output).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("TechLoungeTweaks installer SHA-256 mismatch.");
        var result = new Package(release.Version, hash);
        File.WriteAllText(Path.Combine(folder, "installer.json"), JsonSerializer.Serialize(result));
        return result;
    }
    public static async Task Prepare(string folder, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Policy.RequireUnlinkedPath(folder);
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Choose a new Tweaks installer preparation folder. Existing downloads are retained.");
        Directory.CreateDirectory(folder);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TechLoungeOS-Builder/0.1");
        var release = ParseRelease(await client.GetStringAsync("https://raw.githubusercontent.com/RaheemC4/tech-tips/main/TechLoungeTweaks/release.json", ct));
        var zip = Path.Combine(folder, "download.zip");
        try
        {
            using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 512L * 1024 * 1024) throw new IOException("TechLoungeTweaks download exceeds its size budget.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536]; long length = 0; int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    length += n;
                    if (length > 512L * 1024 * 1024) throw new IOException("TechLoungeTweaks download exceeds its size budget.");
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            Extract(zip, folder, release);
        }
        finally { if (File.Exists(zip)) File.Delete(zip); }
    }
    public static string FirstLogin
    {
        get
        {
            using var stream = typeof(TweaksInstaller).Assembly.GetManifestResourceStream("TechLoungeOS.TweaksInstaller.ps1") ?? throw new IOException("The Tweaks installer worker was not packaged.");
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
    }
}
