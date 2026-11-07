using System.Text.Json;
using TechLoungeOS;

// A test-only, fixed-identity revision of the audited installer, never an installed VM capture.
internal static class WallpaperRevision
{
    internal static async Task Run(bool resumeCopiedMedia = false)
    {
        const string workspace = @"S:\wallpaper-revision1250", output = @"S:\Ghost-full-1250.iso";
        const string bootHash = "F2074710A6B5C848461691775F910FC3039E99072D0633FF85417444FAA38BCB";
        const string oldLauncher = "58051DC3D8489C250225B763A9ABEC09F411992F687354086AB24B784A2DB93F";
        const string oldWallpaper = "25DC0710AD8B203B767C8ED5DCC5B4BA007A335334579364C8F3B4E0451A5A5C";
        const string newLauncher = "50AD4786D872BF83BA8765DA28A3C5FF951EA99B853D13EE10A80260D02276AF";
        const string newWallpaper = "22384B8CF3A333E9F3143E12A87DB544655390A3EF9D5E4283F935ECDE986A0F";
        void Log(string text) { Console.WriteLine(text); }
        var run = new ProcessRunner(Log);
        var source = (await run.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU'){throw 'Owned VM required'}
            $boot=@(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1176' -and $_.IsBoot})
            $scratch=Get-Partition -DriveLetter S|Get-Disk
            if($boot.Count -ne 1 -or ([string]$scratch.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $scratch.IsBoot -or $scratch.IsSystem){throw 'Owned revision disk required'}
            $sources=@(Get-Volume|Where-Object {$_.DriveType -eq 'CD-ROM' -and $_.DriveLetter -and (Test-Path ($_.DriveLetter+':\sources\install.wim'))})
            if($sources.Count -ne 1){throw 'One read-only audited installer CD required'}
            $sources[0].DriveLetter+':\'
            """)).Trim();
        if (source.Length != 3 || source[1..] != @":\") throw new IOException("Unexpected installer volume.");
        foreach (var path in new[] { workspace, output, source }) Policy.RequireUnlinkedPath(path);
        if (File.Exists(output) || File.Exists(Path.Combine(workspace, "state.json")) || Directory.Exists(Path.Combine(workspace, "mount"))) throw new IOException("Inspect the previous revision before retrying.");
        if (Directory.Exists(workspace) != resumeCopiedMedia) throw new IOException("Expected a new revision or the explicitly retained copy-only checkpoint.");
        if (new DriveInfo("S").AvailableFreeSpace < (resumeCopiedMedia ? 30L : 45L) << 30) throw new IOException("Insufficient revision workspace reserve.");
        var isoTool = Path.Combine(AppContext.BaseDirectory, "oscdimg.exe");
        if (!File.Exists(isoTool) || BootableDrive.Hash(isoTool) != "D2709F8099DDD202BC41BCB399ECD5638503C6466F416D1A321769AF83A70C3A") throw new IOException("Verified ISO export tool missing; no servicing started.");
        if (BootableDrive.Hash(Path.Combine(source, @"sources\boot.wim")) != bootHash) throw new IOException("Audited WinPE image changed.");
        foreach (var item in new[] { ("FirstLogin.exe", newLauncher), ("Personalise.exe", newWallpaper) })
            if (BootableDrive.Hash(Path.Combine(AppContext.BaseDirectory, item.Item1)) != item.Item2) throw new IOException("Candidate helper changed.");
        Directory.CreateDirectory(workspace);
        using var lease = new FileStream(Path.Combine(workspace, "build.lock"), resumeCopiedMedia ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        run = new ProcessRunner(Log, Path.Combine(workspace, "scratch"));
        var media = Path.Combine(workspace, "media");
        if (!resumeCopiedMedia)
            await run.PowerShell($"& robocopy.exe {Policy.Ps(source)} {Policy.Ps(media)} /E /COPY:DAT /DCOPY:DAT /R:0 /W:0 /XJ /NFL /NDL /NP; if($LASTEXITCODE -ge 8){{throw 'Installer copy failed'}}; exit 0");
        Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(root, p), BootableDrive.Hash, StringComparer.OrdinalIgnoreCase);
        var original = Snapshot(source);
        var copied = Snapshot(media);
        if (original.Count != copied.Count || original.Any(p => !copied.TryGetValue(p.Key, out var h) || h != p.Value)) throw new IOException("Installer copy differs from the read-only source.");
        var windows = Path.Combine(media, @"sources\install.wim");
        File.SetAttributes(windows, File.GetAttributes(windows) & ~FileAttributes.ReadOnly);
        var mount = Path.Combine(workspace, "mount"); Directory.CreateDirectory(mount);
        var mounted = false;
        try
        {
            mounted = true;
            await run.Dism("/Mount-Image", "/ImageFile:" + windows, "/Index:1", "/MountDir:" + mount, "/CheckIntegrity");
            var apps = ImageServicing.Packages(await run.Dism("/Image:" + mount, "/Get-ProvisionedAppxPackages"));
            var features = ImageServicing.Features(await run.Dism("/Image:" + mount, "/Get-Features", "/Format:List"));
            var configuration = SourcePreservation.CaptureConfiguration(mount);
            if (apps.Length != 11 || features.Length != 136 || configuration.Count != 705) throw new IOException("Audited preservation baseline differs.");
            File.WriteAllText(Path.Combine(workspace, "appx-after.json"), JsonSerializer.Serialize(apps));
            File.WriteAllText(Path.Combine(workspace, "features-after.json"), JsonSerializer.Serialize(features));
            File.WriteAllText(Path.Combine(workspace, "configuration-staged.json"), JsonSerializer.Serialize(configuration));
            var common = Path.Combine(mount, @"ProgramData\TechLoungeOS");
            var manifest = Path.Combine(common, "payload-sha256.json");
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest)) ?? throw new IOException("Integration manifest missing.");
            foreach (var entry in hashes)
                if (BootableDrive.Hash(Path.Combine(common, entry.Key)) != entry.Value) throw new IOException("Previous integration checksum failed: " + entry.Key);
            foreach (var item in new[] {
                (@"ProgramData\TechLoungeOS\FirstLogin.exe", "FirstLogin.exe", oldLauncher, newLauncher),
                (@"Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\TechLoungeOS.exe", "FirstLogin.exe", oldLauncher, newLauncher),
                (@"ProgramData\TechLoungeOS\Wallpapers\Personalise.exe", "Personalise.exe", oldWallpaper, newWallpaper) })
            {
                var file = Path.Combine(mount, item.Item1);
                if (BootableDrive.Hash(file) != item.Item3) throw new IOException("Unexpected previous helper: " + item.Item1);
                var attributes = File.GetAttributes(file);
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                File.WriteAllBytes(file, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, item.Item2)));
                File.SetAttributes(file, attributes);
                if (BootableDrive.Hash(file) != item.Item4) throw new IOException("Replacement checksum failed.");
            }
            hashes["FirstLogin.exe"] = newLauncher; hashes[@"Wallpapers\Personalise.exe"] = newWallpaper;
            var manifestAttributes = File.GetAttributes(manifest);
            File.SetAttributes(manifest, manifestAttributes & ~FileAttributes.ReadOnly);
            File.WriteAllText(manifest, JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
            File.SetAttributes(manifest, manifestAttributes);
            foreach (var entry in hashes)
                if (BootableDrive.Hash(Path.Combine(common, entry.Key)) != entry.Value) throw new IOException("Revised integration checksum failed: " + entry.Key);
            SourcePreservation.VerifyServices(configuration, SourcePreservation.CaptureConfiguration(mount));
            PostAppReadback.Check(mount, RecommendedApps.PresetIds, true);
            await run.Dism("/Unmount-Image", "/MountDir:" + mount, "/Commit", "/CheckIntegrity"); mounted = false;
        }
        catch (Exception failure)
        {
            if (mounted)
                try { await run.Dism("/Unmount-Image", "/MountDir:" + mount, "/Discard"); }
                catch (Exception cleanup) { throw new AggregateException("Revision failed and the private mount needs recovery.", failure, cleanup); }
            throw;
        }
        var after = Snapshot(media);
        if (after.Count != original.Count || original.Any(p => p.Key != @"sources\install.wim" && (!after.TryGetValue(p.Key, out var h) || h != p.Value))) throw new IOException("Installer files outside install.wim changed.");
        BootableDrive.Check16GbTarget(media, Log);
        await IsoExport.Create(run, media, output, isoTool, Log, CancellationToken.None);
        File.WriteAllText(Path.Combine(workspace, "state.json"), JsonSerializer.Serialize(new { phase = "Complete", options = new { Output = output, Apps = RecommendedApps.PresetIds, IncludeOffice = true } }));
        var readback = await ExportReadback.Run(output, workspace, newLauncher, Log);
        File.WriteAllText(Path.Combine(workspace, "export-readback1250.json"), readback);
        File.WriteAllText(Path.Combine(workspace, "revision-result.json"), JsonSerializer.Serialize(new { complete = true, output, sourceFiles = original.Count, unchangedOutsideInstallWim = true, bootWimSha256 = bootHash, launcherSha256 = newLauncher, wallpaperSha256 = newWallpaper, installedVmCaptured = false }));
        Log("1250 revision and independent readback passed; fresh installation remains required.");
    }
}
