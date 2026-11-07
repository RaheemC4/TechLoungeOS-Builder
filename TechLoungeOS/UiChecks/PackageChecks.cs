using System.Security.Cryptography;
using TechLoungeOS;

// Run against the published executable, without importing or servicing an image.
internal static class PackageChecks
{
    internal static void Run()
    {
        Preflight.ValidatePackage();
        var assembly = typeof(WinPeBackground).Assembly;
        foreach(var payload in new[]{("TechLoungeOS.OptimizeBootSource.ps1",BootOptimizer.SourceHash)})
        {
            using var content=assembly.GetManifestResourceStream(payload.Item1)??throw new IOException("Published post-install payload is missing: "+payload.Item1);
            if(!Convert.ToHexString(SHA256.HashData(content)).Equals(payload.Item2,StringComparison.OrdinalIgnoreCase))throw new IOException("Published post-install payload checksum differs: "+payload.Item1);
        }
        foreach(var name in new[]{"TechLoungeOS.PostAppReadiness.ps1","TechLoungeOS.BootOptimizerWorker.ps1"})
        {
            using var content=assembly.GetManifestResourceStream(name)??throw new IOException("Published post-install worker is missing: "+name);
            if(content.Length<500)throw new IOException("Published post-install worker is incomplete: "+name);
        }
        Console.WriteLine("Published post-install files match their exact checksums; payloads were not executed.");
        if(assembly.GetManifestResourceNames().Any(name=>name.StartsWith("TechLoungeOS.Standalone",StringComparison.Ordinal)))throw new IOException("The release must not embed the excluded activation payload.");
        Span<byte> header = stackalloc byte[4];
        foreach (var name in new[] { "manrope-regular.ttf", "manrope-semibold.ttf" })
        {
            using var font = assembly.GetManifestResourceStream("TechLoungeOS.PeFonts." + name)
                ?? throw new IOException("Published WinPE font is missing: " + name);
            font.ReadExactly(header);
            if (!header.SequenceEqual(new byte[] { 0, 1, 0, 0 }) || font.Length < 100_000)
                throw new IOException("Published WinPE font is invalid: " + name);
            font.Position = 0;
            Console.WriteLine(name + " SHA256=" + Convert.ToHexString(SHA256.HashData(font)));
        }
        using var licence = assembly.GetManifestResourceStream("TechLoungeOS.PeFonts.OFL.txt")
            ?? throw new IOException("Published WinPE font licence is missing.");
        using var reader = new StreamReader(licence);
        if (!reader.ReadToEnd().Contains("SIL OPEN FONT LICENSE", StringComparison.Ordinal))
            throw new IOException("Published WinPE font licence is invalid.");
        using var native = assembly.GetManifestResourceStream("TechLoungeOS.PeBackground.exe")
            ?? throw new IOException("Published WinPE executable is missing.");
        if (native.Length < 1024 || native.ReadByte() != 'M' || native.ReadByte() != 'Z')
            throw new IOException("Published WinPE executable is invalid.");
        foreach (var name in new[] { "AccountSetup.exe", "FirstLogin.exe", "DiscordLauncher.exe", "Personalise.exe" })
        {
            using var helper = File.OpenRead(Path.Combine(AppContext.BaseDirectory, name));
            if (helper.Length < 1024 || helper.ReadByte() != 'M' || helper.ReadByte() != 'Z')
                throw new IOException("Published setup helper is invalid: " + name);
        }
        Console.WriteLine("Published WinPE payload, font licence and setup helpers passed. No servicing performed.");
    }
}
