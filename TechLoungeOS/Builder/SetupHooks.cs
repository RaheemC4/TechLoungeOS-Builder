using System.Text;

namespace TechLoungeOS;

public static class SetupHooks
{
    public static string ImageHook(string mount) => Path.Combine(mount, @"Windows\Setup\Scripts\SetupComplete.cmd");
    public static string OemScripts(string media) => Path.Combine(media, @"sources\$OEM$\$$\Setup\Scripts");
    public static byte[] Read(string path)
    {
        Policy.RequireUnlinkedPath(path);
        if (File.Exists(path)) SourcePreservation.RequireImageFile(path, 0, 1 << 20);
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (bytes.Contains((byte)0) || bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            throw new IOException("The inherited SetupComplete has an unsupported size or encoding. It was not rewritten.");
        return bytes;
    }

    // Windows copies an OEM script over the image script. Preserve that existing precedence.
    public static bool Prepare(string media, string mount)
    {
        var oem = Path.Combine(OemScripts(media), "SetupComplete.cmd");
        if (!File.Exists(oem)) return false;
        var body = Read(oem); var image = ImageHook(mount); var previous = Read(image);
        var backup = Path.Combine(mount, @"ProgramData\TechLoungeOS\source-hooks");
        Directory.CreateDirectory(backup);
        File.WriteAllBytes(Path.Combine(backup, "Image-SetupComplete.cmd"), previous);
        File.WriteAllBytes(Path.Combine(backup, "OEM-SetupComplete.cmd"), body);
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllBytes(image, body);
        return true;
    }

    public static void Prefix(string mount, string prefix)
    {
        var path = ImageHook(mount); var original = Read(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [..Encoding.ASCII.GetBytes(prefix), ..original]);
    }

    public static void FinalizeOem(string media, string mount)
    {
        var folder = OemScripts(media); Policy.RequireUnlinkedPath(folder);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "SetupComplete.cmd"), Read(ImageHook(mount)));
    }
}
