using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

static class RealExport
{
    internal static void VerifyEvidence(BuildOptions options, string evidence)
    {
        if (!File.Exists(Path.Combine(evidence, "complete.txt")) || File.ReadAllText(Path.Combine(evidence, "TL-RESULT.txt")).Trim() != "0")
            throw new IOException("A successful Windows PE servicing result and a finished WIM are required.");
        VerifyPreservationEvidence(options, evidence);
    }

    internal static void VerifyPreservationEvidence(BuildOptions options, string evidence)
    {
        T Read<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(evidence, name)))
            ?? throw new IOException("Missing validation data: " + name);
        var packagesBefore = Read<ImageServicing.Package[]>("appx-before.json");
        var featuresBefore = Read<ImageServicing.Feature[]>("features-before.json");
        var packages = Read<ImageServicing.Package[]>("appx-after.json");
        var features = Read<ImageServicing.Feature[]>("features-after.json");
        var servicesBefore = Read<Dictionary<string, string>>("services-before.json");
        var servicesAfter = Read<Dictionary<string, string>>("services-after.json");
        if (servicesBefore.Count == 0) throw new IOException("Service preservation evidence is empty.");
        SourcePreservation.VerifyServices(servicesBefore, servicesAfter);
        SourcePreservation.VerifyComponents(packagesBefore, packages, featuresBefore, features, options.Remove);
        foreach (var package in packagesBefore.Where(p => !options.Remove.Any(c => c.Kind == "Appx" && c.Name == p.DisplayName)))
            if (!packages.Any(p => p.PackageName == package.PackageName)) throw new IOException("An unselected source package disappeared.");
        foreach (var component in options.Remove)
        {
            if (component.Kind == "Appx" && packages.Any(p => p.DisplayName == component.Name)) throw new IOException("Selected app is still present.");
            if (component.Kind == "Feature" && features.Any(f => f.FeatureName == component.Name && !f.State.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Selected feature is still enabled or pending.");
        }
    }

    public static async Task FinalizeVm(string config, string servicedWim, string evidence)
    {
        var options = JsonSerializer.Deserialize<BuildOptions>(File.ReadAllText(config))!;
        VerifyEvidence(options, evidence);
        // Reject a changed input before replacing any media files or publishing an output.
        await using var source = File.OpenRead(options.Iso);
        var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(source));
        using var original = JsonDocument.Parse(File.ReadAllText(config + ".result.json"));
        if (sourceHash != original.RootElement.GetProperty("sourceBefore").GetString()) throw new IOException("Source ISO changed.");
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.Workspace, "state.json")));
        if (state.RootElement.GetProperty("wim").GetString() != Path.Combine(options.Workspace, "selected.wim"))
            throw new IOException("Workspace identity does not match the build journal.");
        using var exclusive = new FileStream(Path.Combine(options.Workspace, "build.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var media = Path.Combine(options.Workspace, "media");
        var destination = Path.Combine(media, @"sources\install.wim");
        var backup = Path.Combine(options.Workspace, "original-install.wim");
        if (File.Exists(options.Output) || File.Exists(backup)) throw new IOException("A previous finalization or output already exists. Review it before retrying.");
        if (!File.Exists(servicedWim)) throw new FileNotFoundException("Serviced WIM missing.");
        File.Move(destination, backup);
        File.Copy(servicedWim, destination, false);
        var runner = new ProcessRunner(Console.WriteLine);
        await IsoExport.Create(runner, media, options.Output, options.Oscdimg, Console.WriteLine, CancellationToken.None);
        var editions = await new BuildEngine(Console.WriteLine).Import(options.Output);
        var expected = state.RootElement.GetProperty("edition").Deserialize<Edition>()!;
        if (editions.Length != 1 || editions[0].ImageIndex != 1 || editions[0].ImageName != expected.ImageName
            || editions[0].Architecture != expected.Architecture || editions[0].Version != expected.Version)
            throw new IOException("Output edition metadata does not match the original selected image.");
        await using var output = File.OpenRead(options.Output);
        File.WriteAllText(config + ".vm-export.json", JsonSerializer.Serialize(new
        {
            status = "VM-assisted ISO exported and reimported", options.Output, options,
            evidence = Path.GetFullPath(evidence), sourceServicesPreserved = true,
            bytes = output.Length, sourceHash, outputSha256 = Convert.ToHexString(await SHA256.HashDataAsync(output)),
            editions, installationValidated = false, hostNativeServicingValidated = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static async Task Run(string config)
    {
        var options = JsonSerializer.Deserialize<BuildOptions>(File.ReadAllText(config))
            ?? throw new IOException("No build options supplied.");
        async Task<string> Hash(string path)
        {
            await using var file = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(file));
        }
        var sourceBefore = await Hash(options.Iso);
        try
        {
            var engine = new BuildEngine(Console.WriteLine);
            await engine.Build(options, CancellationToken.None);
            var editions = await engine.Import(options.Output);
            if (editions.Length != 1 || editions[0].ImageIndex != 1)
                throw new IOException("Exported ISO did not contain exactly one Windows image at index 1.");
            var sourceAfter = await Hash(options.Iso);
            if (sourceBefore != sourceAfter) throw new IOException("Source ISO changed during the build.");
            File.WriteAllText(config + ".result.json", JsonSerializer.Serialize(new
            {
                status = "Exported and reimported", output = options.Output,
                bytes = new FileInfo(options.Output).Length,
                sourceBefore, sourceAfter, outputSha256 = await Hash(options.Output), editions,
                installationValidated = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(config + ".result.json", JsonSerializer.Serialize(new { status = "Failed", error = ex.ToString(), sourceBefore }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }
}
