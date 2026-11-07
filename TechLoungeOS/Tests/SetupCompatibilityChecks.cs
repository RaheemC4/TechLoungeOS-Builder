using TechLoungeOS;

static class SetupCompatibilityChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var mount = Path.Combine(root, "compatibility-hive");
        var hive = Path.Combine(mount, @"Windows\System32\config\SYSTEM");
        SettingsChecks.WriteHive(hive, @"ControlSet001\Services\OriginalGhostService", 42);
        var services = SourcePreservation.CaptureServices(mount);
        SetupCompatibility.ApplyHive(hive, new Version(10, 0));
        var values = SetupCompatibility.ReadValues(hive);
        check(values.Count == 3 && values["BypassTPMCheck"] == 1 && values["BypassSecureBootCheck"] == 1 && values["BypassRAMCheck"] == 1,
            "an actual offline hive receives all three Rufus-style Setup eligibility defaults");
        SourcePreservation.VerifyServices(services, SourcePreservation.CaptureServices(mount));
        check(services.Count == 1 && SourcePreservation.CaptureServices(mount)[@"ControlSet001\OriginalGhostService"] == services[@"ControlSet001\OriginalGhostService"],
            "adding Setup bypasses preserves the existing service configuration");
        var hash = BootableDrive.Hash(hive); SetupCompatibility.ApplyHive(hive, new Version(10, 0));
        check(BootableDrive.Hash(hive) == hash, "already-correct bypass values do not rewrite the source hive");

        async Task CheckPreparation(string name, bool failSecond, bool cancel, string? metadata = null)
        {
            var work = Path.Combine(root, "compatibility-" + name); var media = Path.Combine(work, "media");
            Directory.CreateDirectory(Path.Combine(media, "sources"));
            var boot = Path.Combine(media, @"sources\boot.wim"); File.WriteAllText(boot, "inert boot WIM fixture");
            var before = BootableDrive.Hash(boot);
            using var stop = new CancellationTokenSource();
            var run = new Runner(metadata, failSecond, cancel ? stop : null);
            try
            {
                await SetupCompatibility.Prepare(run, media, work, _ => { }, stop.Token);
                if (failSecond || cancel || metadata != null) throw new Exception("Expected compatibility transaction rejection");
                check(run.Calls.Count(a => a.Contains("/Mount-Image")) == 2 && run.Calls.Count(a => a.Contains("/Commit")) == 2 &&
                    File.Exists(Path.Combine(work, "setup-compatibility-original-boot.wim")) &&
                    File.ReadAllText(Path.Combine(work, "setup-compatibility.json")).Contains("originalFilesPreserved"),
                    "every boot image is prepared and verified even without the custom WinPE toolbar");
            }
            catch (IOException) when (failSecond || metadata != null)
            {
                check(BootableDrive.Hash(boot) == before && !File.Exists(Path.Combine(work, "setup-compatibility-original-boot.wim")) &&
                    (failSecond ? run.Calls.Any(a => a.Contains("/Discard")) : run.Calls.Count == 0),
                    failSecond ? "a failed second PE image retains the original boot image and discards its private mount" : "unsupported boot metadata is rejected before starting any mount transaction");
            }
            catch (OperationCanceledException) when (cancel)
            {
                check(BootableDrive.Hash(boot) == before && !run.Calls.Any(a => a.Contains("/Commit")) && run.Calls.Any(a => a.Contains("/Discard")),
                    "cancelling compatibility preparation discards the private mount without replacing boot media");
            }
        }
        await CheckPreparation("success", false, false);
        await CheckPreparation("failure", true, false);
        await CheckPreparation("cancel", false, true);
        await CheckPreparation("architecture", false, false, """[{"ImageIndex":1,"Architecture":"12","Version":"10.0.26100.1"}]""");
    }
    private sealed class Runner(string? metadata, bool failSecond, CancellationTokenSource? cancel) : ICommandRunner
    {
        public List<string[]> Calls { get; } = [];
        public Task<string> PowerShell(string script, CancellationToken ct = default) => Task.FromResult(metadata ?? """[{"ImageIndex":1,"Architecture":"9","Version":"10.0.26100.1"},{"ImageIndex":2,"Architecture":"9","Version":"10.0.26100.1"}]""");
        public Task<string> Dism(params string[] args)
        {
            Calls.Add(args);
            if (args.Contains("/Mount-Image"))
            {
                if (failSecond && args.Contains("/Index:2")) throw new IOException("Injected second PE failure");
                var mount = args.Single(a => a.StartsWith("/MountDir:", StringComparison.Ordinal))[10..];
                SettingsChecks.WriteHive(Path.Combine(mount, @"Windows\System32\config\SYSTEM"), @"ControlSet001\Services\Existing", 77);
                File.WriteAllText(Path.Combine(mount, "original-tool.txt"), "unchanged original tool fixture");
                cancel?.Cancel();
            }
            return Task.FromResult("");
        }
        public Task<string> Run(string exe, IEnumerable<string> args, CancellationToken ct = default) => throw new InvalidOperationException("No source tool may execute in compatibility preparation");
    }
}
