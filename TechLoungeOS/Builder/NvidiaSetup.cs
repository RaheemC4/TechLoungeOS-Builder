using System.Net.Http;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TechLoungeOS;

public static class NvidiaSetup
{
    public const string RelativeFolder = @"Windows\Setup\TechLoungeOS-Nvidia";
    public static bool Enabled(BuildOptions options) => options.IncludeNvidiaSetup && options.TweaksFolder != null;
    public static string Script => ReadResource("TechLoungeOS.NvidiaSetup.ps1");
    private static string ReadResource(string name)
    {
        using var stream = typeof(NvidiaSetup).Assembly.GetManifestResourceStream(name) ?? throw new IOException("NVIDIA setup worker was not packaged.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static void Validate(string tweaks)
    {
        foreach (var name in new[] { "nvidiaProfileInspector.exe", "TechLoungeProfile.nip" })
            SourcePreservation.RequireImageFile(Path.Combine(tweaks,"resources",name), 16, 16 << 20);
        _ = Profile(Path.Combine(tweaks,"resources","TechLoungeProfile.nip"));
    }
    public static XDocument Profile(string path)
    {
        // The supplied profile has a UTF-8 BOM but declares UTF-16. Decode its
        // actual bytes first; normalize the declaration in our private copy.
        using var text = new StreamReader(path,new UTF8Encoding(false,true),detectEncodingFromByteOrderMarks:true);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var doc = XDocument.Load(reader);
        if(doc.Declaration != null) doc.Declaration.Encoding="utf-8";
        var profiles = doc.Root?.Elements("Profile").ToArray() ?? [];
        if (doc.Root?.Name != "ArrayOfProfile" || profiles.Length != 1 || profiles[0].Element("Settings")?.Elements("ProfileSetting").Any() != true)
            throw new IOException("The supplied NVIDIA profile must contain one global settings profile.");
        var profile = profiles[0];
        if (profile.Element("Executeables")?.HasElements == true || profile.Element("ExecutableFindFiles")?.HasElements == true) throw new IOException("Automatic NVIDIA setup requires a global profile, not application associations.");
        const string global = "_GLOBAL_DRIVER_PROFILE (Base Profile)";
        var name = profile.Element("ProfileName");
        if (name != null && !string.IsNullOrWhiteSpace(name.Value) && name.Value != global) throw new IOException("Automatic NVIDIA setup only imports the global TechLounge profile.");
        // Some supplied profiles omit the name. Normalize a private copy only.
        if (name == null) profile.AddFirst(new XElement("ProfileName", global)); else name.Value = global;
        return doc;
    }
    public static async Task Prepare(string prepared, CancellationToken ct)
    {
        Directory.CreateDirectory(prepared);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
        var bytes = await client.GetByteArrayAsync("https://www.7-zip.org/a/7zr.exe", ct);
        if (bytes.Length is < 100_000 or > 4_000_000 || bytes[0] != 'M' || bytes[1] != 'Z') throw new IOException("Official 7-Zip extractor download is invalid.");
        await File.WriteAllBytesAsync(Path.Combine(prepared,"7zr.exe"),bytes,ct);
        await File.WriteAllTextAsync(Path.Combine(prepared,"7-Zip-license.txt"),await client.GetStringAsync("https://www.7-zip.org/license.txt",ct),ct);
        var version=System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(prepared,"7zr.exe"));
        var source=$"https://github.com/ip7z/7zip/releases/download/{version.FileMajorPart}.{version.FileMinorPart:D2}/7z{version.FileMajorPart}{version.FileMinorPart:D2}-src.7z";
        await File.WriteAllTextAsync(Path.Combine(prepared,"extractor-source.txt"),"https://www.7-zip.org/a/7zr.exe\nSHA256 " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + "\n7-Zip reduced extractor " + version.FileVersion + "; GNU LGPL; see 7-Zip-license.txt\nMatching upstream source: " + source + "\nSource/licence redistribution review required before publishing a release.\n",ct);
    }
    public static void Stage(string mount, string tweaks, string prepared)
    {
        Validate(tweaks);
        var target = Path.Combine(mount,RelativeFolder); Policy.RequireUnlinkedPath(target);
        if (Directory.Exists(target)) throw new IOException("The source already contains NVIDIA setup payloads.");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(Path.Combine(tweaks,"resources"),"nvidiaProfileInspector.*")) File.Copy(file,Path.Combine(target,Path.GetFileName(file)),false);
        Profile(Path.Combine(tweaks,"resources","TechLoungeProfile.nip")).Save(Path.Combine(target,"TechLoungeProfile.nip"));
        foreach(var name in new[]{"7zr.exe","extractor-source.txt","7-Zip-license.txt"}) File.Copy(Path.Combine(prepared,name),Path.Combine(target,name),false);
        File.WriteAllText(Path.Combine(target,"install.ps1"),Script,Encoding.UTF8);
        File.WriteAllText(Path.Combine(target,"queue.ps1"),Queue,Encoding.UTF8);
        File.WriteAllText(Path.Combine(target,"driver-lock.ps1"),GraphicsQueue.LockScript,Encoding.UTF8);
        Provisioning.WriteHashes(target);
        SetupHooks.Prefix(mount,"@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"%SystemRoot%\\Setup\\TechLoungeOS-Nvidia\\queue.ps1\"\r\n");
    }
    public static string Queue => GraphicsQueue.Script(nvidia:true);
    public static string FirstLogin=>GraphicsQueue.FirstLogin(true);
}
