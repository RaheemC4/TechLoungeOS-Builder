using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace TechLoungeOS;

internal static class AppArtwork
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, Task<BitmapImage?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Downloads = new(4);
    private const string Revision = "5ce78130bf1d802ce3460c376a5fc76a7379e362";
    private record IconEntry(string File, string Sha256);
    private static readonly Dictionary<string, IconEntry> Icons = LoadCatalog();

    private static Dictionary<string, IconEntry> LoadCatalog()
    {
        using var stream = typeof(AppArtwork).Assembly.GetManifestResourceStream("TechLoungeOS.Builder.Artwork.catalog.json")!;
        return new(JsonSerializer.Deserialize<Dictionary<string, IconEntry>>(stream)!, StringComparer.OrdinalIgnoreCase);
    }

    public static Task<BitmapImage?> Get(CatalogApp app)
    {
        if (RecommendedApps.IsDiscord(app.Id)) app = app with { Id = "Discord.Discord" };
        if (RecommendedApps.IsChatGPT(app.Id)) app = app with { Id = "OpenAI.ChatGPT" };
        var key = app.Id + "/" + app.Version;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        if (Cache.Count >= 300) Cache.Clear();
        var embedded=app.Id.ToUpperInvariant() switch{"DISCORD.DISCORD"=>"Discord","OPENAI.CHATGPT"=>"ChatGPT","STREMIO.STREMIO"=>"Stremio","PROTON.PROTONVPN"=>"ProtonVPN","BRAVE.BRAVE"=>"Brave","GOOGLE.CHROME"=>"Chrome","VALVE.STEAM"=>"Steam","GURU3D.AFTERBURNER"=>"Afterburner","REALIX.HWINFO"=>"HWiNFO",_=>null};
        if(embedded!=null)
        {
            using var stream=typeof(AppArtwork).Assembly.GetManifestResourceStream("TechLoungeOS.AppIcons."+embedded+".png")??throw new IOException("Packaged app icon is missing: "+embedded);
            using var bytes=new MemoryStream();stream.CopyTo(bytes);
            return Cache[key]=Task.FromResult<BitmapImage?>(DecodeBitmap(bytes.ToArray(),0));
        }
        return Cache[key] = Fetch(app);
    }

    private static async Task<BitmapImage?> Fetch(CatalogApp app)
    {
        BitmapImage? image = null;
        await Downloads.WaitAsync();
        try
        {
            if (!AppCatalog.ValidId(app.Id)) return null;
            // Exact package identity only. Similar names, forks and nightly builds must not borrow a logo.
            if (Icons.TryGetValue(app.Id, out var icon))
            {
                try { return await CatalogIcon(icon); }
                catch (Exception ex) when (OptionalFailure(ex)) { /* Try WinGet metadata if the optional mirror is unavailable. */ }
            }
            if (!Regex.IsMatch(app.Version, @"\A[A-Za-z0-9.+_-]{1,100}\z")) return null;
            var path = app.Id.Replace('.', '/');
            var manifest = await Read(new Uri($"https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/{char.ToLowerInvariant(app.Id[0])}/{path}/{Uri.EscapeDataString(app.Version)}/{app.Id}.locale.en-US.yaml"));
            var match = Regex.Match(System.Text.Encoding.UTF8.GetString(manifest), @"(?m)^\s*-?\s*IconUrl:\s*['""']?(https://[^\s'""']+)\s*['""']?\s*$");
            if (match.Success && Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri) && TrustedIcon(uri))
            {
                image = Decode(await Read(uri));
            }
        }
        catch (Exception ex) when (OptionalFailure(ex))
        { /* Artwork is optional. Search and selection must remain usable without it. */ }
        finally { Downloads.Release(); }
        return image;
    }

    private static bool OptionalFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException or IOException or
        UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.COMException;

    private static async Task<BitmapImage> CatalogIcon(IconEntry icon)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechLoungeOS", "AppIcons");
        var file = Path.Combine(folder, icon.Sha256 + ".png");
        byte[]? bytes = null;
        try
        {
            Policy.RequireUnlinkedPath(folder);
            if (File.Exists(file) && new FileInfo(file).Length <= 2097152)
            {
                var cached = await File.ReadAllBytesAsync(file);
                if (Matches(cached, icon.Sha256)) bytes = cached;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A read-only cache must not prevent artwork. */ }
        var fromCache = bytes != null;
        if (bytes == null)
        {
            bytes = await Read(new Uri($"https://raw.githubusercontent.com/memstechtips/package-icons/{Revision}/icons/{icon.File}"), 2097152);
            if (!Matches(bytes, icon.Sha256)) throw new IOException("App icon hash mismatch.");
        }
        var image = Decode(bytes);
        if (!fromCache)
        {
            // Replace a directory entry atomically, never overwrite through an existing file link.
            var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Policy.RequireUnlinkedPath(folder); Directory.CreateDirectory(folder);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await output.WriteAsync(bytes);
                Policy.RequireUnlinkedPath(folder); File.Move(temporary, file, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache writes are optional. */ }
            finally
            {
                try { Policy.RequireUnlinkedPath(folder); File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return image;
    }

    private static bool Matches(byte[] bytes, string hash) => Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase);

    private static BitmapImage Decode(byte[] bytes)=>DecodeBitmap(bytes,64);
    private static BitmapImage DecodeBitmap(byte[] bytes,int width)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096) throw new IOException("App icon dimensions exceed supported bounds.");
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if(width>0)image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }

    private static bool TrustedIcon(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        (uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("cdn.winget.microsoft.com", StringComparison.OrdinalIgnoreCase));

    private static async Task<byte[]> Read(Uri uri, int limit = 262144)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new IOException("Artwork metadata is too large.");
        using var stream = await response.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int length;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while ((length = await stream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (output.Length + length > limit) throw new IOException("Artwork metadata is too large.");
            output.Write(buffer, 0, length);
        }
        return output.ToArray();
    }
}
