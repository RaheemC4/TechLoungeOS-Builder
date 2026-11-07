using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

// Read files only. These checks must never register tasks or execute the payloads.
internal static class PostAppReadback
{
    internal static void Check(string mount, string[] selected, bool office)
    {
        var root = Path.Combine(mount, @"ProgramData\TechLoungeOS");
        var assembly = typeof(BootOptimizer).Assembly;
        if(Directory.Exists(Path.Combine(root,"Standalone")))throw new IOException("Excluded activation payload was staged in the image.");
        foreach (var item in new[] {
            (@"BootOptimizer\Optimize-Boot.ps1", "TechLoungeOS.OptimizeBootSource.ps1"),
            (@"BootOptimizer\worker.ps1", "TechLoungeOS.BootOptimizerWorker.ps1"),
            (@"BootOptimizer\app-readiness.ps1", "TechLoungeOS.PostAppReadiness.ps1"),
            (@"PowerDefaults\worker.ps1", "TechLoungeOS.PowerDefaults.ps1"),
            (@"PowerDefaults\app-readiness.ps1", "TechLoungeOS.PostAppReadiness.ps1") })
        {
            using var source = assembly.GetManifestResourceStream(item.Item2)
                ?? throw new IOException("Verifier resource missing: " + item.Item2);
            if (BootableDrive.Hash(Path.Combine(root, item.Item1)) != Convert.ToHexString(SHA256.HashData(source)))
                throw new IOException("Exported post-install payload differs: " + item.Item1);
        }
        var expected = RecommendedApps.NormalizeIds(selected).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(var queue in new[]{("BootOptimizer",BootOptimizer.Queue),("PowerDefaults",FixedSetupTask.Queue("TechLoungeOS-PowerDefaults"))})
            if(File.ReadAllText(Path.Combine(root,queue.Item1,"queue.ps1"))!=queue.Item2)
                throw new IOException("Exported protected task registration differs: "+queue.Item1);
        foreach (var folder in new[] { "PowerDefaults", "BootOptimizer" })
        {
            var path = Path.Combine(root, folder);
            var apps = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(path, "apps.json")))
                ?? throw new IOException("Missing selected-app manifest.");
            if (!apps.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
                throw new IOException("Exported post-app selection differs: " + folder);
            foreach (var state in new[] { "complete", "attempted.json", "result.json", "queue-ready", "state" })
                if (Path.Exists(Path.Combine(path, state))) throw new IOException("Execution state was baked into the image: " + folder + "\\" + state);
        }
        var script = File.ReadAllText(Path.Combine(root, "first-login.ps1"));
        if(script.Contains("TechLoungeOS-Standalone",StringComparison.Ordinal) || script.Contains("Invoke-Step 'standalone'",StringComparison.Ordinal))throw new IOException("Excluded activation action appears in first-login setup.");
        if(office && !script.Contains("Invoke-Step 'office'",StringComparison.Ordinal))throw new IOException("Office deployment step is missing.");
        int Step(string id)
        {
            var text = "Invoke-Step '" + id + "'";
            var index = script.IndexOf(text, StringComparison.Ordinal);
            if (index < 0 || script.LastIndexOf(text, StringComparison.Ordinal) != index)
                throw new IOException("Expected one first-login step: " + id);
            return index;
        }
        var boot = Step("boot-optimizer"); var power = Step("power-defaults");
        if (power <= boot || expected.Any(id => Step(id) >= boot))
            throw new IOException("Exported post-app order must be apps, Boot Optimizer, then power defaults.");
    }

    internal static void CheckFixtures(string folder)
    {
        var mount = Path.Combine(folder, "post-app-readback-fixture-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mount);
        var apps = new[] { "Fixture.Reader" };
        BootOptimizer.Stage(mount, apps); PowerDefaults.Stage(mount, apps);
        var login = Path.Combine(mount, @"ProgramData\TechLoungeOS\first-login.ps1");
        var script = Provisioning.Script(apps, false, false, bootOptimizer:true, office:true, desktopDefaults:true);
        File.WriteAllText(login, script);
        Check(mount, apps, true);
        void Reject(string message)
        {
            try { Check(mount, apps, true); }
            catch (IOException error) when (error.Message.Contains(message, StringComparison.Ordinal)) { return; }
            throw new IOException("Post-app readback accepted " + message);
        }
        var worker = Path.Combine(mount, @"ProgramData\TechLoungeOS\BootOptimizer\worker.ps1");
        var original = File.ReadAllBytes(worker);
        File.AppendAllText(worker, "# changed"); Reject("payload differs"); File.WriteAllBytes(worker, original);
        var queue = Path.Combine(mount, @"ProgramData\TechLoungeOS\BootOptimizer\queue.ps1");
        var registration = File.ReadAllBytes(queue);
        File.AppendAllText(queue, "# changed"); Reject("task registration differs"); File.WriteAllBytes(queue, registration);
        var attempted = Path.Combine(mount, @"ProgramData\TechLoungeOS\BootOptimizer\attempted.json");
        File.WriteAllText(attempted, "{}"); Reject("Execution state"); File.Delete(attempted);
        File.WriteAllText(login, script.Replace("Invoke-Step 'boot-optimizer'", "Invoke-Step '__swap'")
            .Replace("Invoke-Step 'power-defaults'", "Invoke-Step 'boot-optimizer'")
            .Replace("Invoke-Step '__swap'", "Invoke-Step 'power-defaults'"));
        Reject("post-app order"); File.WriteAllText(login, script);
        Console.WriteLine("Post-app readback verifies exact files and order; tampering, baked state and reversed execution are rejected. No payload executed.");
    }
}
