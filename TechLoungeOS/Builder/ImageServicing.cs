using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public static class ImageServicing
{
    public record Package(string DisplayName, string PackageName);
    public record Feature(string FeatureName, string State);
    public static Package[] Packages(string text) => Records(text, "DisplayName", "PackageName")
        .Select(r => new Package(r.Item1, r.Item2)).ToArray();
    public static Feature[] Features(string text) => Records(text, "Feature Name", "State")
        .Select(r => new Feature(r.Item1, r.Item2)).ToArray();

    private static (string, string)[] Records(string text, string first, string last)
    {
        var starts = Regex.Matches(text, @"(?m)^[ \t]*" + Regex.Escape(first) + @"[ \t]*:[ \t]*(?<value>[^\r\n]+)");
        var result = new List<(string, string)>();
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
            var match = Regex.Match(text[start.Index..end], @"(?m)^[ \t]*" + Regex.Escape(last) + @"[ \t]*:[ \t]*(?<value>[^\r\n]+)");
            if (!match.Success) throw new InvalidDataException("Incomplete DISM inventory record: " + first);
            result.Add((start.Groups["value"].Value.Trim(), match.Groups["value"].Value.Trim()));
        }
        if (!text.Contains("The operation completed successfully", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DISM did not confirm a complete inventory.");
        if (starts.Count == 0 && Regex.IsMatch(text, @"(?m)^\s*" + Regex.Escape(last) + @"\s*:"))
            throw new InvalidDataException("Unrecognised DISM inventory layout.");
        return result.ToArray();
    }

    public static async Task Run(ICommandRunner run, string mount, BuildOptions options, Action<string> log, CancellationToken ct, Func<string, IReadOnlyDictionary<string, string>>? serviceReader = null,WindowsImageUpdates.VerifiedHealthScan? verifiedInitialHealth=null)
    {
        ct.ThrowIfCancellationRequested();
        if (options.Remove.Any(c => !Policy.CanRemove(c)) || !Policy.ValidApps(options.Apps))
            throw new InvalidOperationException("Unsupported component or app selection.");
        Package[] packages;
        Feature[] features;
        try
        {
            packages = Packages(await run.Dism("/Image:" + mount, "/Get-ProvisionedAppxPackages"));
            ct.ThrowIfCancellationRequested();
            features = Features(await run.Dism("/Image:" + mount, "/Get-Features", "/Format:List"));
        }
        catch (IOException ex)
        {
            throw new IOException("Offline Windows inventory failed before component removal. If launched through an IDE or automation tool, close the builder and launch it directly from Windows, then retry with a new workspace. Otherwise review the DISM log for source or servicing-host problems. No removals were attempted by this servicing pass.", ex);
        }
        File.WriteAllText(Path.Combine(options.Workspace, "appx-before.json"), JsonSerializer.Serialize(packages));
        File.WriteAllText(Path.Combine(options.Workspace, "features-before.json"), JsonSerializer.Serialize(features));
        ct.ThrowIfCancellationRequested();
        // Only the same build's completed update pass hands off this full scan.
        // Component removals and the final post-provisioning scan still run below.
        var healthBefore = verifiedInitialHealth?.Take(mount) ?? await run.Dism("/Image:" + mount, "/Cleanup-Image", "/ScanHealth");
        if(verifiedInitialHealth is not null)log("Reusing the full health scan just completed by this image's verified update transaction.");
        File.WriteAllText(Path.Combine(options.Workspace, "health-before.txt"), healthBefore);
        ct.ThrowIfCancellationRequested();
        var inheritedRepairable = RepairableHealth(healthBefore);
        if(verifiedInitialHealth is not null && inheritedRepairable)
            throw new IOException("The verified update health handoff was not healthy. The image will not be committed.");
        var pending = options.Remove.Distinct().Where(c => c.Kind == "Appx"
            ? packages.Any(p => p.DisplayName == c.Name)
            : features.Any(f => f.FeatureName == c.Name && !f.State.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase))).ToHashSet();
        if (inheritedRepairable && pending.Count != 0)
            throw new IOException("The source already has a repairable component store. Deselect component removals to preserve it and add apps/settings only. Automatic repair could restore intentionally stripped components.");
        if (inheritedRepairable)
            log("Source health warning: the component store is already repairable. Preserving it without repair; only file-based setup and first-login payloads are allowed. This does not certify source health or update compatibility.");
        var servicesBefore = serviceReader?.Invoke(mount) ?? SourcePreservation.CaptureConfiguration(mount);
        File.WriteAllText(Path.Combine(options.Workspace, "services-before.json"), JsonSerializer.Serialize(servicesBefore));
        foreach (var component in options.Remove.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            if (!pending.Contains(component))
            {
                log("Already absent or disabled: " + component.Name);
                continue;
            }
            if (component.Kind == "Appx")
            {
                var names = packages.Where(p => p.DisplayName == component.Name).Select(p => p.PackageName).ToArray();
                foreach (var name in names)
                {
                    if (!Regex.IsMatch(name, @"^[A-Za-z0-9_.~\-]+$")) throw new InvalidDataException("Invalid package identity.");
                    await run.Dism("/Image:" + mount, "/Remove-ProvisionedAppxPackage", "/PackageName:" + name);
                }
            }
            else await run.Dism("/Image:" + mount, "/Disable-Feature", "/FeatureName:" + component.Name, "/NoRestart");
        }
        ct.ThrowIfCancellationRequested();
        var afterPackages = Packages(await run.Dism("/Image:" + mount, "/Get-ProvisionedAppxPackages"));
        ct.ThrowIfCancellationRequested();
        var afterFeatures = Features(await run.Dism("/Image:" + mount, "/Get-Features", "/Format:List"));
        foreach (var component in options.Remove)
        {
            if (component.Kind == "Appx" && afterPackages.Any(p => p.DisplayName == component.Name))
                throw new IOException("Selected app is still provisioned: " + component.Name);
            if (component.Kind == "Feature" && afterFeatures.Any(f => f.FeatureName == component.Name && !f.State.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Selected feature is not fully disabled: " + component.Name);
        }
        SourcePreservation.VerifyComponents(packages, afterPackages, features, afterFeatures, options.Remove);
        File.WriteAllText(Path.Combine(options.Workspace, "appx-after.json"), JsonSerializer.Serialize(afterPackages));
        File.WriteAllText(Path.Combine(options.Workspace, "features-after.json"), JsonSerializer.Serialize(afterFeatures));
        ct.ThrowIfCancellationRequested();
        Provisioning.Stage(mount, options);
        ct.ThrowIfCancellationRequested();
        var health = await run.Dism("/Image:" + mount, "/Cleanup-Image", "/ScanHealth");
        File.WriteAllText(Path.Combine(options.Workspace, "health-after.txt"), health);
        ct.ThrowIfCancellationRequested();
        if (RepairableHealth(health) != inheritedRepairable)
            throw new IOException("Component-store health changed from the source baseline. The image will not be committed.");
        var servicesAfter = serviceReader?.Invoke(mount) ?? SourcePreservation.CaptureConfiguration(mount);
        File.WriteAllText(Path.Combine(options.Workspace, "services-after.json"), JsonSerializer.Serialize(servicesAfter));
        SourcePreservation.VerifyServices(servicesBefore, servicesAfter);
        ct.ThrowIfCancellationRequested();
    }

    private static bool RepairableHealth(string output)
    {
        if (output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase)) return false;
        if (output.Contains("The component store is repairable.", StringComparison.OrdinalIgnoreCase)) return true;
        throw new IOException("DISM did not establish a supported component-store health state. The image will not be committed.");
    }
}
