using System.Text.Json;
using TechLoungeOS;

static class PeServicing
{
    public static async Task VerifyCompleted(string config, string drive)
    {
        drive = Path.GetFullPath(drive).TrimEnd('\\') + "\\";
        if (!File.Exists(@"X:\Windows\System32\wpeutil.exe") || Path.GetPathRoot(drive) != drive ||
            !File.Exists(Path.Combine(drive, "TL-VALIDATION-ID.txt"))) throw new IOException("A marked validation volume in Windows PE is required.");
        var work = Path.Combine(drive, "work");
        if (!File.ReadAllText(Path.Combine(work, "complete.txt")).StartsWith("Applied image serviced and healthy;", StringComparison.Ordinal))
            throw new IOException("Only a completed applied-image job can be reverified.");
        if (!File.ReadAllText(Path.Combine(work, "servicing.log")).Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The original full health scan is not recorded.");
        var options = JsonSerializer.Deserialize<BuildOptions>(File.ReadAllText(config))!;
        var mount = Path.Combine(drive, "image");
        var proof = Path.Combine(work, "reverified-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(proof);
        using var log = new StreamWriter(Path.Combine(proof, "verification.log")) { AutoFlush = true };
        var logGate = new object();
        void Write(string text) { lock (logGate) { log.WriteLine(text); if (text.StartsWith('>')) Console.WriteLine(text); } }
        var runner = new ProcessRunner(Write, Path.Combine(proof, "scratch"), Path.Combine(drive, "tools", "dism.exe"));
        foreach (var name in new[] { "appx-before.json", "features-before.json", "services-before.json" })
            File.Copy(Path.Combine(work, name), Path.Combine(proof, name), false);
        var packages = ImageServicing.Packages(await runner.Dism("/Image:" + mount, "/Get-ProvisionedAppxPackages"));
        var features = ImageServicing.Features(await runner.Dism("/Image:" + mount, "/Get-Features", "/Format:List"));
        var health = await runner.Dism("/Image:" + mount, "/Cleanup-Image", "/CheckHealth");
        if (!health.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase)) throw new IOException("Current image health is unverified.");
        if (!File.Exists(@"X:\Windows\System32\offreg.dll")) File.Copy(Path.Combine(drive, "offreg.dll"), @"X:\Windows\System32\offreg.dll", false);
        var services = SourcePreservation.CaptureConfiguration(mount);
        File.WriteAllText(Path.Combine(proof, "appx-after.json"), JsonSerializer.Serialize(packages));
        File.WriteAllText(Path.Combine(proof, "features-after.json"), JsonSerializer.Serialize(features));
        File.WriteAllText(Path.Combine(proof, "services-after.json"), JsonSerializer.Serialize(services));
        RealExport.VerifyPreservationEvidence(options, proof);
        // Publish fresh audit records only after all native checks pass. The original scan log remains intact.
        foreach (var name in new[] { "appx-after.json", "features-after.json", "services-after.json" })
            File.Copy(Path.Combine(proof, name), Path.Combine(work, name), true);
        File.WriteAllText(Path.Combine(work, "verification.json"), JsonSerializer.Serialize(new { mode = "Completed job reverified", originalFullScanRetained = true, proof }));
        Console.WriteLine("Completed image reverified: components, services and current health match.");
    }

    // Invoked only inside a disposable Windows PE VM against its new virtual disk.
    public static async Task Run(string config, string drive, bool applied = false)
    {
        drive = Path.GetFullPath(drive).TrimEnd('\\') + "\\";
        if (!Directory.Exists(@"X:\Windows\System32") || !File.Exists(@"X:\Windows\System32\wpeutil.exe"))
            throw new InvalidOperationException("This validation worker requires Windows PE.");
        if (Path.GetPathRoot(drive) != drive || !File.Exists(Path.Combine(drive, "TL-VALIDATION-ID.txt")))
            throw new InvalidOperationException("A marked validation virtual volume is required.");
        // Validation-only dependency staging into PE's RAM drive, never the installed host OS.
        var offlineRegistry = @"X:\Windows\System32\offreg.dll";
        if (!File.Exists(offlineRegistry)) File.Copy(Path.Combine(drive, "offreg.dll"), offlineRegistry, false);
        var source = Path.GetDirectoryName(Path.GetFullPath(config))!;
        var options = JsonSerializer.Deserialize<BuildOptions>(File.ReadAllText(config))!;
        options = options with { Workspace = Path.Combine(drive, "work"), TweaksFolder = options.TweaksFolder == null ? null : Path.Combine(source, "tweaks") };
        var mount = Path.Combine(drive, applied ? "image" : "mount");
        if (Directory.Exists(options.Workspace) || (!applied && Directory.Exists(mount)))
            throw new IOException("Use a fresh validation workspace and mount folder. Inspect prior servicing and recovery before retrying.");
        if (applied && (!File.Exists(Path.Combine(drive, "TL-APPLIED-IMAGE.txt")) || !File.Exists(Path.Combine(mount, @"Windows\System32\ntoskrnl.exe"))))
            throw new IOException("A separately prepared applied-image validation volume is required.");
        Directory.CreateDirectory(options.Workspace);
        using var log = new StreamWriter(Path.Combine(options.Workspace, "servicing.log")) { AutoFlush = true };
        var logGate = new object();
        void Write(string text) { lock (logGate) { Console.WriteLine(text); log.WriteLine(text); } }
        var tools = Path.Combine(drive, "tools", "dism.exe");
        if (!File.Exists(tools)) throw new FileNotFoundException("Stage the complete trusted ADK DISM directory for this PE validation pass.", tools);
        var runner = new ProcessRunner(Write, Path.Combine(options.Workspace, "scratch"), tools);
        if (!applied) Directory.CreateDirectory(mount);
        var mounted = false;
        try
        {
            if (!applied)
            {
                mounted = true;
                await runner.Dism("/Mount-Image", "/ImageFile:" + Path.Combine(drive, "selected.wim"), "/Index:1", "/MountDir:" + mount, "/CheckIntegrity");
            }
            await ImageServicing.Run(runner, mount, options, Write, CancellationToken.None);
            // The capture/finalizer validation route still requires a healthy source.
            if (!File.ReadAllText(Path.Combine(options.Workspace, "health-after.txt")).Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
                throw new IOException("PE capture validation requires a healthy component store; use the native app workflow for preserved source-health warnings.");
            if (mounted) await runner.Dism("/Unmount-Image", "/MountDir:" + mount, "/Commit", "/CheckIntegrity");
            mounted = false;
            File.WriteAllText(Path.Combine(options.Workspace, "complete.txt"), applied ? "Applied image serviced and healthy; capture and installation unverified." : "Image serviced and committed; installation unverified.");
        }
        catch (Exception ex) { Write(ex.ToString()); throw; }
        finally { if (mounted) await runner.Dism("/Unmount-Image", "/MountDir:" + mount, "/Discard"); }
    }
}
