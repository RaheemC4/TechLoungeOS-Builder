using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

// Cumulative and explicitly reviewed enablement transactions share strict preservation gates.
public static class WindowsImageUpdates
{
    // This is an in-memory handoff for one unchanged private mount, never a
    // cached report read from disk. It can replace only one adjacent pre-scan.
    public sealed class VerifiedHealthScan
    {
        private readonly string mount,output;
        private int used;
        internal VerifiedHealthScan(string mount,string output)
        {
            RequireHealthy(output);this.mount=Path.GetFullPath(mount);this.output=output;
        }
        internal string Take(string image)
        {
            if(!mount.Equals(Path.GetFullPath(image),StringComparison.OrdinalIgnoreCase)||Interlocked.Exchange(ref used,1)!=0)
                throw new IOException("The update health scan does not belong to this unchanged image transaction.");
            return output;
        }
    }
    public sealed record Payload(string File, long Bytes, string Sha256);
    public sealed record Enablement(string RuleId,string BaselineVersion);
    public sealed record Plan(int Schema, string SourceVersion, string TargetVersion, Payload[] Packages,Enablement? Feature=null);

    public static Plan ReadPlan(string folder)
    {
        if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Choose a local Windows update package folder.");
        Policy.RequireUnlinkedPath(folder);
        var path = Path.Combine(folder, "update-plan.json");
        SourcePreservation.RequireImageFile(path, 2, 65536);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked update plans are not supported.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var plan = JsonSerializer.Deserialize<Plan>(stream, new JsonSerializerOptions
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 })
            ?? throw new IOException("The Windows update plan is empty.");
        ValidatePlan(plan);
        return plan;
    }

    public static void ValidatePlan(Plan plan)
    {
        var source = ParseVersion(plan.SourceVersion);
        var target = ParseVersion(plan.TargetVersion);
        if(target<=source)throw new IOException("Windows update plans must move forward, without downgrading the source.");
        if (plan.Packages == null || plan.Packages.Length is < 1 or > 8)
            throw new IOException("Include at most eight verified Windows update packages.");
        if(plan.Schema==1 && plan.Feature is null)
        {
            if(source.Build!=target.Build)throw new IOException("Feature upgrades require a reviewed enablement route.");
        }
        else if(plan.Schema==2 && plan.Feature is { } feature)
        {
            var baseline=ParseVersion(feature.BaselineVersion);
            var rule=WindowsUpgradeRules.RequireTransition(feature.RuleId,source,baseline,target);var package=plan.Packages[^1];
            if((baseline>source && plan.Packages.Length<2) ||
                (baseline==source && plan.Packages.Length!=1) || package is null || package.File!=rule.File || package.Bytes!=rule.Bytes || package.Sha256!=rule.Sha256)
                throw new IOException("The enablement plan does not match its reviewed source, prerequisites, target or pinned package.");
        }
        else throw new IOException("Unsupported Windows update plan schema or upgrade instructions.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var item in plan.Packages)
        {
            if (item == null || item.File == null || !Regex.IsMatch(item.File,
                @"\Awindows11\.0-kb[0-9]{6,8}-x64_[a-fA-F0-9]{40}\.msu\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                !names.Add(item.File) || item.Bytes is < 1 or > 8589934592 || item.Sha256 == null ||
                !Regex.IsMatch(item.Sha256, @"\A[a-fA-F0-9]{64}\z", RegexOptions.CultureInvariant))
                throw new IOException("Invalid Windows update filename, size, checksum or duplicate package.");
            bytes += item.Bytes;
        }
        if (bytes > 21474836480) throw new IOException("Windows update packages exceed the 20 GiB build budget.");
    }

    private static Version ParseVersion(string text)
    {
        if (!Version.TryParse(text, out var value) || value.Major != 10 || value.Minor != 0 || value.Build < 22000 || value.Revision < 0)
            throw new IOException("The Windows update plan needs a complete Windows 11 version, including its revision.");
        return value;
    }

    internal static Version ReadVersion(string mount)
    {
        var hive = Path.Combine(mount, @"Windows\System32\config\SOFTWARE");
        const string key = @"Microsoft\Windows NT\CurrentVersion";
        var build = SourcePreservation.ReadStringValue(hive, key, "CurrentBuildNumber");
        var revision = SourcePreservation.ReadDwordValue(hive, key, "UBR");
        if (!int.TryParse(build, out var number) || number < 22000 || revision == null || revision > int.MaxValue)
            throw new IOException("The source image did not provide a supported Windows version for offline updating.");
        return new Version(10, 0, number, (int)revision.Value);
    }

    internal static string ReadEdition(string mount)=>SourcePreservation.ReadStringValue(Path.Combine(mount,@"Windows\System32\config\SOFTWARE"),@"Microsoft\Windows NT\CurrentVersion","EditionID")
        ??throw new IOException("The source Windows edition could not be checked for feature-upgrade eligibility.");

    public static async Task<Version> Apply(ICommandRunner run, string mount, string workspace,
        string folder, Plan plan, Version importedVersion, Action<string> log, CancellationToken ct,
        Func<string, IReadOnlyDictionary<string, string>>? serviceReader = null, Func<string, Version>? versionReader = null,Func<string,string>? editionReader=null,Func<string,string?>? releaseReader=null,Action<VerifiedHealthScan>? verifiedHealth=null)
    {
        ValidatePlan(plan);
        var source = ParseVersion(plan.SourceVersion);
        var target = ParseVersion(plan.TargetVersion);
        if (source != importedVersion) throw new IOException("The update plan does not match the selected source Windows version. No package was applied.");
        var migration=WindowsUpdateMigrations.Find(plan);
        var sourceEdition=plan.Feature is null?null:(editionReader??ReadEdition)(mount);
        if(plan.Feature is { } route)WindowsUpgradeRules.RequireEdition(route.RuleId,sourceEdition!);
        var root = Path.Combine(workspace, "windows-update");
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Use a new workspace for Windows image updating.");
        Policy.RequireUnlinkedPath(root);
        Directory.CreateDirectory(root);
        string PackagePath(Payload item)=>Path.Combine(root,plan.Feature is null?"":item.File==plan.Packages[^1].File?"enablement":"cumulative",item.File);
        var held = new List<FileStream>();
        var applied = false;
        var operationStarted = false;
        Version? actual = null;
        void Report(bool passed, string? error) => File.WriteAllText(Path.Combine(root, "result.json"),
            JsonSerializer.Serialize(new { source = source.ToString(), target = target.ToString(), actual = actual?.ToString(),
                packageOperationStarted = operationStarted, packageCommandSucceeded = applied, preservationPassed = passed, error }, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            ct.ThrowIfCancellationRequested();
            if(plan.Feature is not null && File.Exists(Path.Combine(mount,@"Windows\WinSxS\pending.xml")))
                throw new IOException("Finish the source image’s existing pending servicing before a feature upgrade. No package was applied.");
            actual = (versionReader ?? ReadVersion)(mount);
            if (actual != source) throw new IOException("The offline Windows version differs from the imported metadata. No package was applied.");
            var beforeApps = ImageServicing.Packages(await run.Dism("/Image:" + mount, "/Get-ProvisionedAppxPackages"));
            var beforeFeatures = ImageServicing.Features(await run.Dism("/Image:" + mount, "/Get-Features", "/Format:List"));
            var beforeConfig = (serviceReader ?? SourcePreservation.CaptureUpdateConfiguration)(mount);
            var beforePayloads=WindowsUpdatePreservation.Capture(mount);
            File.WriteAllText(Path.Combine(root, "before.json"), JsonSerializer.Serialize(new { apps = beforeApps, features = beforeFeatures, configuration = beforeConfig,removedPayloads=beforePayloads }));
            WindowsUpdateMigrations.RequireSource(migration,mount,beforeConfig,beforePayloads);
            var correctedArtwork=await KnownWallpaperRepair.TryApply(run,mount,root,log,ct);
            RequireHealthy(await run.Dism("/Image:" + mount, "/Cleanup-Image", "/ScanHealth"));
            foreach (var item in plan.Packages)
            {
                ct.ThrowIfCancellationRequested();
                var input = Path.Combine(folder, item.File);
                Policy.RequireUnlinkedPath(input);
                if ((File.GetAttributes(input) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked Windows update packages are not supported.");
                var output=PackagePath(item);Policy.RequireUnlinkedPath(output);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using (var read = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var write = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (read.Length != item.Bytes) throw new IOException("Windows update size differs from the plan: " + item.File);
                    await read.CopyToAsync(write, ct);
                    await write.FlushAsync(ct);
                }
                var lease = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read);
                held.Add(lease); // Deny mutation until DISM and all readback checks finish.
                if (!Convert.ToHexString(await SHA256.HashDataAsync(lease, ct)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Windows update checksum differs from the plan: " + item.File);
                try { await RuntimePack.VerifyMicrosoft(run, output); }
                catch(IOException error) { throw new IOException("Windows update Microsoft signature verification failed: "+item.File,error); }
            }
            File.WriteAllText(Path.Combine(root, "verified-plan.json"), JsonSerializer.Serialize(plan));
            ct.ThrowIfCancellationRequested();
            log("Applying verified Windows updates to the private image. The original ISO is unchanged.");
            // DISM resolves checkpoint dependencies only from this verified private folder.
            // Do not use IgnoreCheck, ResetBase, RestoreHealth or kill servicing on cancellation.
            if(plan.Feature is { } upgrade && ParseVersion(upgrade.BaselineVersion)>source)
            {
                log("Installing the required cumulative update before enabling the new Windows release.");
                operationStarted=true;
                await run.Dism("/Image:"+mount,"/Add-Package","/PackagePath:"+PackagePath(plan.Packages[^2]),"/PreventPending","/NoRestart");
                actual=(versionReader??ReadVersion)(mount);
                if(actual!=ParseVersion(upgrade.BaselineVersion))throw new IOException("The prerequisite Windows version was not reached. Enablement will not be applied.");
                ct.ThrowIfCancellationRequested();
            }
            operationStarted = true;
            // Enablement follows its verified prerequisite using normal DISM applicability checks.
            // PreventPending would skip it after our own freshly installed offline prerequisite.
            var finalArguments=new List<string>{"/Image:"+mount,"/Add-Package","/PackagePath:"+PackagePath(plan.Packages[^1]),"/NoRestart"};
            if(plan.Feature is null)finalArguments.Insert(3,"/PreventPending");
            await run.Dism(finalArguments.ToArray());
            applied = true;
            ct.ThrowIfCancellationRequested();
            actual = (versionReader ?? ReadVersion)(mount);
            if (actual != target) throw new IOException("The update did not reach its declared Windows version. The image will not be committed.");
            if(plan.Feature is { } feature)
            {
                var display=(releaseReader??(image=>SourcePreservation.ReadStringValue(Path.Combine(image,@"Windows\System32\config\SOFTWARE"),@"Microsoft\Windows NT\CurrentVersion","DisplayVersion")))(mount);
                if(display!=WindowsUpgradeRules.Get(feature.RuleId).Name || !string.Equals(sourceEdition,(editionReader??ReadEdition)(mount),StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The upgraded release name or Windows edition did not match the planned in-place enablement. No image will be exported.");
            }
            var afterFeatures = ImageServicing.Features(await run.Dism("/Image:" + mount, "/Get-Features", "/Format:List"));
            if(await WindowsUpdateMigrations.DisableNewCamera(run,mount,migration,beforeFeatures,afterFeatures,ct))
            {
                log("Disabling the newly introduced camera framework while retaining the source image's existing features and recovery files.");
                afterFeatures=ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
            }
            var afterApps = ImageServicing.Packages(await run.Dism("/Image:" + mount, "/Get-ProvisionedAppxPackages"));
            var afterConfig = (serviceReader ?? SourcePreservation.CaptureUpdateConfiguration)(mount);
            var afterPayloads=WindowsUpdatePreservation.Capture(mount);
            File.WriteAllText(Path.Combine(root, "after.json"), JsonSerializer.Serialize(new { version = actual.ToString(), apps = afterApps, features = afterFeatures, configuration = afterConfig,removedPayloads=afterPayloads }));
            File.WriteAllText(Path.Combine(root,"preservation-differences.json"),JsonSerializer.Serialize(
                WindowsUpdatePreservation.Differences(beforeApps,afterApps,beforeFeatures,afterFeatures,beforeConfig,afterConfig,beforePayloads,afterPayloads),
                new JsonSerializerOptions{WriteIndented=true}));
            var healthAfter=await run.Dism("/Image:" + mount, "/Cleanup-Image", "/ScanHealth");
            RequireHealthy(healthAfter);
            SourcePreservation.VerifyComponents(beforeApps, afterApps, beforeFeatures,WindowsUpdateMigrations.ExistingFeatures(migration,beforeFeatures,afterFeatures), []);
            WindowsUpdateMigrations.VerifyConfiguration(migration,beforeConfig,afterConfig);
            WindowsUpdatePreservation.Verify(mount,beforePayloads,migration);
            if(correctedArtwork)KnownWallpaperRepair.VerifyRetainedArtwork(mount,root);
            ct.ThrowIfCancellationRequested();
            Report(true, null);
            log("Windows version and source-preservation checks passed, with only reviewed internal migrations permitted. Recovery and installation acceptance are still required.");
            verifiedHealth?.Invoke(new VerifiedHealthScan(mount,healthAfter));
            return actual;
        }
        catch (Exception error)
        {
            try { Report(false, error.Message); }
            catch (Exception reportError) when (reportError is IOException or UnauthorizedAccessException)
            { log("Could not save Windows update failure evidence: " + reportError.Message); }
            throw;
        }
        finally { foreach (var lease in held) lease.Dispose(); }
    }

    private static void RequireHealthy(string output)
    {
        if (!output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Windows updating requires a healthy component store. This image still needs repair; the build was stopped to protect its removed components.");
    }
}
