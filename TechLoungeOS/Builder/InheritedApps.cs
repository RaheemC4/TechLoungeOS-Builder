using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public record AppReference(string Id, string Evidence);
public record SourceAppFile(string Path, string Text);
public record SourceAppScan(AppReference[] Apps, string[] ReviewFiles);

public static class InheritedApps
{
    public static SourceAppScan Parse(IEnumerable<SourceAppFile> files)
    {
        var apps = new List<AppReference>(); var review = new List<string>();
        foreach (var file in files)
        {
            review.Add(file.Path);
            if (file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var doc = JsonDocument.Parse(file.Text);
                    if (doc.RootElement.TryGetProperty("Sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
                        foreach (var source in sources.EnumerateArray())
                        {
                            if (!source.TryGetProperty("SourceDetails", out var details) ||
                                !details.TryGetProperty("Name", out var name) || name.GetString() != "winget" ||
                                !source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array) continue;
                            foreach (var package in packages.EnumerateArray())
                                if (package.TryGetProperty("PackageIdentifier", out var id) && id.ValueKind == JsonValueKind.String && AppCatalog.ValidId(id.GetString()!))
                                    apps.Add(new(id.GetString()!, file.Path + " (WinGet import list; execution not confirmed)"));
                        }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
            }
            // Only literal package IDs on direct install lines. Never evaluate source scripts.
            foreach (var line in file.Text.Split('\n'))
            {
                if (!Regex.IsMatch(line, @"^\s*(?:&\s*)?(?:winget(?:\.exe)?|\$winget\.Source)\s+install\s", RegexOptions.IgnoreCase)) continue;
                var match = Regex.Match(line, "(?:^|\\s)(?:--id|-i)(?:[ =]+)['\"]?([A-Za-z0-9_.-]+)['\"]?(?=\\s|$)", RegexOptions.IgnoreCase);
                if (match.Success && AppCatalog.ValidId(match.Groups[1].Value))
                    apps.Add(new(match.Groups[1].Value, file.Path + " (install script reference; execution not confirmed)"));
            }
        }
        return new(apps.DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToArray(), review.Distinct().ToArray());
    }

    public static async Task<SourceAppScan> Scan(string iso, int index, Action<string> log)
    {
        if (index < 1 || !File.Exists(iso)) throw new InvalidOperationException("Select an existing ISO edition before scanning.");
        var work = Path.Combine(Path.GetTempPath(), "TechLoungeOS-app-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        log("Scanning source deployment metadata using a read-only mount: " + work);
        // Deployment scripts may contain credentials. Never send their contents to build/UI logs.
        var runner = new ProcessRunner(_ => { });
        // The scan owns a unique read-only WIM mount. Never execute or import source scripts.
        var json = await runner.PowerShell($$"""
            $iso = {{Policy.Ps(Path.GetFullPath(iso))}}
            $mount = {{Policy.Ps(work)}}
            $disk = Get-DiskImage -ImagePath $iso
            $owned = !$disk.Attached
            $mounted = $false
            try {
              if ($owned) { $disk = Mount-DiskImage -ImagePath $iso -PassThru }
              $v = $disk | Get-Volume
              if (!$v.DriveLetter) { throw 'ISO has no drive letter.' }
              $root = "$($v.DriveLetter):\"
              $image = @('sources\install.wim','sources\install.esd') | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
              if (!$image) { throw 'No supported installation image.' }
              $mounted = $true
              Mount-WindowsImage -ImagePath $image -Index {{index}} -Path $mount -ReadOnly -CheckIntegrity | Out-Null
              $locations = @((Join-Path $root 'sources\$OEM$'), (Join-Path $mount 'Windows\Setup\Scripts'), (Join-Path $mount 'ProgramData\TechLoungeOS'), (Join-Path $mount 'Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'))
              $result = @()
              $queue = New-Object 'System.Collections.Generic.Queue[string]'
              foreach ($folder in $locations) { if (Test-Path -LiteralPath $folder -PathType Container) { $queue.Enqueue($folder) } }
              $visited = 0
              $totalText = 0
              while ($queue.Count -gt 0) {
                $folder = $queue.Dequeue()
                $ancestor = [IO.DirectoryInfo]::new($folder)
                while ($null -ne $ancestor) {
                  if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked deployment paths require manual review.' }
                  $ancestor = $ancestor.Parent
                }
                foreach ($item in Get-ChildItem -LiteralPath $folder -Force -ErrorAction Stop) {
                  if (++$visited -gt 5000) { throw 'Source app scan exceeds 5000 entries. Review custom deployment files manually.' }
                  if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                  if ($item.PSIsContainer) { $queue.Enqueue($item.FullName); continue }
                  if ($item.Extension -notin @('.cmd','.bat','.ps1','.xml','.json','.exe','.msi','.msix','.lnk')) { continue }
                  $text = ''
                  if ($item.Extension -in @('.cmd','.bat','.ps1','.xml','.json') -and $item.Length -le 1048576) { $totalText += $item.Length; if ($totalText -gt 4194304) { throw 'Deployment text exceeds the 4 MB scan limit. Review source files manually.' }; $text = [IO.File]::ReadAllText($item.FullName) }
                  $relative = if ($item.FullName.StartsWith($mount, [StringComparison]::OrdinalIgnoreCase)) { 'image:' + $item.FullName.Substring($mount.Length) } else { 'media:' + $item.FullName.Substring($root.Length) }
                  $result += [pscustomobject]@{ Path = $relative; Text = $text }
                  if ($result.Count -gt 200) { throw 'Source contains more than 200 deployment files. Review it manually.' }
                }
              }
              ConvertTo-Json -InputObject $result -Compress
            } finally {
              try { if ($mounted) { Dismount-WindowsImage -Path $mount -Discard | Out-Null } }
              finally { if ($owned) { Dismount-DiskImage -ImagePath $iso | Out-Null } }
            }
            """);
        Directory.Delete(work); // Only remove the now-empty mount directory after confirmed discard.
        return Parse(JsonSerializer.Deserialize<SourceAppFile[]>(json) ?? []);
    }
}
