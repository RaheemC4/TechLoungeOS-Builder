using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TechLoungeOS;

internal static class ArtworkChecks
{
    public static void Run()
    {
        var type = typeof(MainWindow).Assembly.GetType("TechLoungeOS.AppArtwork")!;
        var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public)!;
        var fetch = type.GetMethod("Fetch", BindingFlags.Static | BindingFlags.NonPublic)!;
        BitmapImage? Fetch(string id) => ((Task<BitmapImage?>)get.Invoke(null, [new CatalogApp(id, id, "fixture/no-manifest")])!).GetAwaiter().GetResult();
        foreach(var id in RecommendedApps.PresetIds) {
            var embedded=Fetch(id);
            if(embedded==null||!embedded.IsFrozen||embedded.PixelWidth<32)throw new Exception("Packaged publisher icon missing: "+id);
            Console.WriteLine("PASS offline publisher icon: "+id);
        }
        using var resource = type.Assembly.GetManifestResourceStream("TechLoungeOS.Builder.Artwork.catalog.json")!;
        using var catalog = JsonDocument.Parse(resource);
        foreach (var id in new[] { "VideoLAN.VLC", "Valve.Steam", "Mozilla.Firefox" })
        {
            // Exercise the hash-pinned cache path explicitly, even where the
            // normal picker prefers its packaged offline publisher icon.
            var image = ((Task<BitmapImage?>)fetch.Invoke(null,[new CatalogApp(id,id,"fixture/no-manifest")])!).GetAwaiter().GetResult();
            if (image == null || !image.IsFrozen || image.PixelWidth != 64) throw new Exception("Real package artwork did not load: " + id);
            var hash = catalog.RootElement.GetProperty(id).GetProperty("Sha256").GetString()!;
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechLoungeOS", "AppIcons", hash + ".png");
            if (!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cache))).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Cache bytes do not match pinned artwork: " + id);
            Console.WriteLine("PASS real decoded icon and verified disk cache: " + id);
            if (id == "VideoLAN.VLC")
            {
                var original = File.ReadAllBytes(cache);
                try
                {
                    File.WriteAllBytes(cache, new byte[original.Length]);
                    var repaired = ((Task<BitmapImage?>)fetch.Invoke(null, [new CatalogApp(id, id, "fixture/no-manifest")])!).GetAwaiter().GetResult();
                    if (repaired == null || !File.ReadAllBytes(cache).SequenceEqual(original)) throw new Exception("Corrupt cache was not repaired from verified source bytes.");
                    Console.WriteLine("PASS same-length corrupt cached icon is rejected and replaced with verified artwork.");
                }
                finally { File.WriteAllBytes(cache, original); }
            }
        }
        if (Fetch("../escape") != null || Fetch("Unknown.NotInCatalog") != null) throw new Exception("Missing/invalid identity must use neutral fallback.");
        Console.WriteLine("PASS invalid and unmapped identities return fallback without using a similarly named app.");
        var decode = type.GetMethod("Decode", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Reject(byte[] bytes)
        {
            try { decode.Invoke(null, [bytes]); }
            catch (TargetInvocationException) { return; }
            throw new Exception("Invalid artwork accepted.");
        }
        Reject([0, 1, 2, 3]);
        var bitmap = BitmapSource.Create(4097, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4097 * 4], 4097 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); Reject(output.ToArray());
        var matches = type.GetMethod("Matches", BindingFlags.Static | BindingFlags.NonPublic)!;
        if ((bool)matches.Invoke(null, [new byte[] { 1, 2 }, new string('0', 64)])!) throw new Exception("Corrupt cached artwork accepted.");
        Console.WriteLine("PASS malformed image, oversized dimensions and mismatched hash rejected.");
    }
}
