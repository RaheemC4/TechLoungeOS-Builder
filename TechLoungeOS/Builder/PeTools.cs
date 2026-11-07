using System.Security.Cryptography;

namespace TechLoungeOS;

internal static class PeTools
{
    // Stable upstream x64 binary and matching source; never replace an image's tools.
    internal static Dictionary<string, byte[]> Read(string mount)
    {
        foreach (var name in new[] { "shell32.dll", "gdiplus.dll", "shlwapi.dll", "mpr.dll", "uxtheme.dll", "winmm.dll", "urlmon.dll", "wininet.dll", "propsys.dll", "dwmapi.dll", "kernel32.dll", "user32.dll", "gdi32.dll", "winspool.drv", "advapi32.dll", "ole32.dll", "oleaut32.dll", "iphlpapi.dll", "userenv.dll", "version.dll", "comdlg32.dll" })
            SourcePreservation.RequireImageFile(Path.Combine(mount, @"Windows\System32", name), 1, 256L << 20);
        RequireCommonControls(mount);
        return new(StringComparer.Ordinal)
        {
            [@"Tools\Explorer++\Explorer++.exe"] = ReadVerified("Explorer++.exe", "22AC20341C5525658EBE5E9C4B9AC9F7C152B18080FF347861FAC3F04D56A2D6"),
            [@"Tools\Explorer++\source.zip"] = ReadVerified("source.zip", "3531F0BA75B5BFB64BE4674A960B57C62C2ADF62A3B04578E485E2442FDBC655"),
            [@"Tools\Explorer++\LICENSE.txt"] = ReadVerified("LICENSE.txt", "0B383D5A63DA644F628D99C33976EA6487ED89AAA59F0B3257992DEAC1171E6B"),
            [@"Tools\Explorer++\NOTICE.txt"] = ReadVerified("NOTICE.txt", null)
        };
    }

    private static void RequireCommonControls(string mount)
    {
        // Explorer++ requests Common Controls 6 in its embedded activation manifest.
        // Stock WinPE supplies it through WinSxS, without a loose System32 DLL.
        var root=Path.Combine(mount,@"Windows\WinSxS");Policy.RequireUnlinkedPath(root);
        if(!Directory.Exists(root))throw new IOException("Windows PE lacks its Common Controls 6 assembly.");
        var assemblies=Directory.EnumerateDirectories(root,"amd64_microsoft.windows.common-controls_6595b64144ccf1df_6.0.*_none_*").Take(33).ToArray();
        if(assemblies.Length is 0 or >32)throw new IOException("Windows PE has missing or ambiguous Common Controls 6 assemblies.");
        foreach(var directory in assemblies)
        {
            Policy.RequireUnlinkedPath(directory);
            SourcePreservation.RequireImageFile(Path.Combine(directory,"comctl32.dll"),1,256L<<20);
            var manifest=Path.Combine(root,"Manifests",Path.GetFileName(directory)+".manifest");Policy.RequireUnlinkedPath(manifest);
            SourcePreservation.RequireImageFile(manifest,1,4L<<20);
        }
    }

    private static byte[] ReadVerified(string name, string? expected)
    {
        using var source = typeof(PeTools).Assembly.GetManifestResourceStream("TechLoungeOS.PeTools." + name)
            ?? throw new IOException("A Windows PE file-manager payload was not packaged: " + name);
        if (source.Length is < 1 or > (8 << 20)) throw new IOException("Invalid Windows PE file-manager payload size.");
        using var bytes = new MemoryStream(); source.CopyTo(bytes);
        var result = bytes.ToArray();
        if (expected != null && Convert.ToHexString(SHA256.HashData(result)) != expected)
            throw new IOException("The Windows PE file-manager payload failed verification: " + name);
        return result;
    }
}
