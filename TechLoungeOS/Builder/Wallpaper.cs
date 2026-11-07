namespace TechLoungeOS;

public static class Wallpaper
{
    public static void Validate(string? path)
    {
        if (path == null) return;
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0 || !File.Exists(path))
            throw new InvalidOperationException("Choose an existing wallpaper image on a local drive.");
        Policy.RequireUnlinkedPath(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var file = new FileInfo(path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length is < 24 or > 33554432)
            throw new InvalidOperationException("Use a local wallpaper image between 24 bytes and 32 MB. Copy cloud-only or linked files locally first.");
        using var stream = File.OpenRead(path);
        Span<byte> bytes = stackalloc byte[8]; stream.ReadExactly(bytes);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var valid = extension switch
        {
            ".png" => bytes.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            ".bmp" => bytes[0] == 66 && bytes[1] == 77,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Wallpaper must be a PNG, JPEG or BMP with a matching file header.");
    }

    public static string FirstLogin(string? stagedName) => stagedName == null ? "" : $$"""

        Invoke-Step 'desktop-background' {
          foreach ($root in @('HKCU:','HKLM:')) {
            $policy = Get-ItemProperty "$root\Software\Microsoft\Windows\CurrentVersion\Policies\System" -ErrorAction SilentlyContinue
            $desktopPolicy = Get-ItemProperty "$root\Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop" -ErrorAction SilentlyContinue
            if ($policy.Wallpaper -or $desktopPolicy.NoChangingWallPaper -eq 1) { throw 'Desktop background is managed by policy. Existing policy was preserved.' }
          }
          Add-Type -AssemblyName System.Drawing
          $source = Join-Path $env:ProgramData {{Policy.Ps(Path.Combine("TechLoungeOS", stagedName))}}
          $image = [Drawing.Image]::FromFile($source)
          try {
            if ([long]$image.Width * $image.Height -gt 60000000) { throw 'Wallpaper exceeds 60 million pixels.' }
            $target = Join-Path $state 'desktop-background.bmp'
            $image.Save($target, [Drawing.Imaging.ImageFormat]::Bmp)
          } finally { $image.Dispose() }
          if (-not ('TechLoungeOSDesktop' -as [type])) {
            Add-Type 'using System; using System.Runtime.InteropServices; public static class TechLoungeOSDesktop { [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool SystemParametersInfo(int action, int parameter, string path, int flags); }'
          }
          if (-not [TechLoungeOSDesktop]::SystemParametersInfo(20, 0, $target, 3)) { throw 'Windows could not apply the desktop background.' }
        }
        """;
}
