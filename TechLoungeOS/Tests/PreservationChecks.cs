using TechLoungeOS;

static class PreservationChecks
{
    public static async Task Run(string root, BuildOptions options, Action<bool, string> check)
    {
        SettingsChecks.Run(root, check);
        check(SourcePreservation.Classify("Ghost_Spectre.iso", ["Windows 11 Pro"]).Evidence.Length == 1,
            "custom source branding is an indicator, not treated as authenticated OEM");
        check(SourcePreservation.Classify("Windows.iso", ["Windows 11 Pro"]).Classification == "Source origin unverified",
            "absence of modification markers never authenticates an OEM source");
        check(SourcePreservation.Classify("Source.iso",["WINDOWS 11 IoT ENT - COMPACT","WINDOWS 11 IoT ENT - SUPERLITE + DEF"]).Classification=="Custom-image indicators found","custom edition naming is detected even after the ISO is renamed");
        var ghostMount = Path.Combine(root, "deferred-ghost-toolbox");
        var ghostDesktop = Path.Combine(ghostMount, @"Users\Default\Desktop");
        Directory.CreateDirectory(ghostDesktop);
        var ghostShortcut = Path.Combine(ghostDesktop, "Ghost Toolbox.lnk");
        File.WriteAllText(ghostShortcut, "inert shortcut fixture");
        var ghostProfile = SourcePreservation.Classify("WIN11.IoT-ENT.iso", ["WINDOWS 11 IoT ENT - SUPERLITE + DEF"], ghostMount);
        check(ghostProfile.Classification == "Custom-image indicators found" && ghostProfile.Evidence.Any(e=>e.Contains("during setup"))
            && File.ReadAllText(ghostShortcut) == "inert shortcut fixture" && !Directory.Exists(Path.Combine(ghostMount, "Ghost Toolbox")),
            "deferred Ghost Toolbox is detected from its inherited shortcut without resolving or executing it");
        void Rejects(Action action, string name)
        {
            try { action(); } catch (IOException) { check(true, name); return; }
            throw new Exception("FAIL: " + name);
        }
        SourcePreservation.VerifyComponents([], [], [new("NetFx3", "Disabled with Payload Removed")], [new("NetFx3", "Disabled with Payload Removed")], []);
        check(true, "pre-stripped feature state is retained exactly");
        Rejects(() => SourcePreservation.VerifyComponents([], [new("Restored.App", "restored_1")], [], [], []), "reintroducing an absent source app is rejected");
        Rejects(() => SourcePreservation.VerifyComponents([new("Retained.App", "retained_1")], [], [], [], []), "removing an unselected source app is rejected");
        SourcePreservation.VerifyComponents([new("Microsoft.BingNews", "news_1")], [], [], [], [new("Appx", "Microsoft.BingNews")]);
        check(true, "explicitly selected app removal remains allowed");
        Rejects(() => SourcePreservation.VerifyComponents([], [], [new("NetFx3", "Disabled")], [new("NetFx3", "Enabled")], []), "reenabling an unselected disabled feature is rejected");
        Rejects(() => SourcePreservation.VerifyServices(new Dictionary<string,string>{{"CustomSvc", "disabled-config"}}, new Dictionary<string,string>{{"CustomSvc", "automatic-config"}}), "source service configuration cannot silently reset");
        SourcePreservation.VerifyServices(new Dictionary<string,string>{{"CustomSvc", "disabled-config"}}, new Dictionary<string,string>{{"CustomSvc", "disabled-config"}});
        check(true, "unchanged inherited service configuration is accepted");
        var configuration=new Dictionary<string,string>{{"service:Vendor","retained"},{"policy:Policies","retained"},{"default-user:NTUSER","original"}};
        var staged=new Dictionary<string,string>(configuration){["default-user:NTUSER"]="reviewed-policy"};
        SourcePreservation.VerifyStagedConfiguration(configuration,staged,"reviewed-policy");
        check(true,"explicit default-profile policy preparation permits its new hive hash in the final export baseline");
        Rejects(()=>SourcePreservation.VerifyStagedConfiguration(configuration,staged,null),"a default-profile change without reviewed policy preparation remains rejected");
        var changedUser=new Dictionary<string,string>(staged){["default-user:NTUSER"]="unexpected user change"};
        Rejects(()=>SourcePreservation.VerifyStagedConfiguration(configuration,changedUser,"reviewed-policy"),"a later user-profile change cannot be mistaken for the independently prepared policy hash");
        foreach(var key in new[]{"service:Vendor","policy:Policies"})
        {
            var changedConfiguration=new Dictionary<string,string>(staged){[key]="changed"};
            Rejects(()=>SourcePreservation.VerifyStagedConfiguration(configuration,changedConfiguration,"reviewed-policy"),"reviewed default-profile changes cannot mask unrelated configuration changes: "+key);
        }
        var runtimePayload = Path.Combine(root, "runtime-payload"); Directory.CreateDirectory(runtimePayload);
        File.WriteAllText(Path.Combine(runtimePayload, "TechLoungeTweaks.exe"), "inert executable");
        foreach (var includePrivate in new[] { false, true })
        {
            if (includePrivate)
            {
                var runtime = Path.Combine(runtimePayload, "WebView2Runtime"); Directory.CreateDirectory(runtime);
                foreach (var name in new[] { "msedgewebview2.exe", "msedge.dll", "icudtl.dat", "resources.pak" }) File.WriteAllText(Path.Combine(runtime, name), "inert fixture");
            }
            var runtimeOptions = options with { Index = 2, Remove = [], TweaksFolder = runtimePayload,
                Workspace = Path.Combine(root, "runtime-build-" + includePrivate), Output = Path.Combine(root, "runtime-build-" + includePrivate + ".iso") };
            var runtimeRunner = new FixtureRunner(runtimeOptions.Workspace);
            var completed = false;
            try { await new BuildEngine(_ => { }, _ => runtimeRunner, _ => new Dictionary<string,string>()).Build(runtimeOptions, CancellationToken.None); completed = true; }
            catch (IOException ex) when (!includePrivate && ex.Message.Contains("WebView2")) { }
            check(completed == includePrivate && File.Exists(runtimeOptions.Output) == includePrivate &&
                runtimeRunner.Commands.Any(c => c.Contains("/Commit")) == includePrivate &&
                (includePrivate || runtimeRunner.Commands.Any(c => c.Contains("/Discard"))),
                includePrivate ? "private runtime integration completes without adding a system runtime" : "missing Tweaks runtime blocks export and discards safely");
        }
        const string healthy = "No component store corruption detected.";
        const string repairable = "The component store is repairable.";
        var healthCases = new[] {
            (Name: "inherited-repairable", Before: repairable, After: repairable, Remove: false, Succeeds: true),
            (Name: "new-corruption", Before: healthy, After: repairable, Remove: false, Succeeds: false),
            (Name: "inherited-removal", Before: repairable, After: repairable, Remove: true, Succeeds: false),
            (Name: "unknown-source-health", Before: "Unrecognised output", After: healthy, Remove: false, Succeeds: false),
            (Name: "unrepairable-result", Before: repairable, After: "The component store cannot be repaired.", Remove: false, Succeeds: false)
        };
        foreach (var item in healthCases)
        {
            var healthOptions = options with { Index = 2, Workspace = Path.Combine(root, item.Name), Output = Path.Combine(root, item.Name + ".iso"),
                Remove = item.Remove ? [new("Appx", "Microsoft.BingNews")] : [] };
            var healthRunner = new FixtureRunner(healthOptions.Workspace, healthResults: new Queue<string>([item.Before, item.After]));
            var completed = false;
            try { await new BuildEngine(_ => { }, _ => healthRunner, _ => new Dictionary<string,string>()).Build(healthOptions, CancellationToken.None); completed = true; }
            catch (IOException) { }
            check(completed == item.Succeeds && File.Exists(healthOptions.Output) == item.Succeeds &&
                healthRunner.Commands.Any(c => c.Contains("/Commit")) == item.Succeeds &&
                !healthRunner.Commands.Any(c => c.Contains("/RestoreHealth") || c.Contains("/Remove-ProvisionedAppxPackage")) &&
                (item.Succeeds || healthRunner.Commands.Any(c => c.Contains("/Discard"))),
                item.Name + " preserves the source health boundary without repair or unsafe commit");
        }
        var sourceXml = System.Xml.Linq.XElement.Parse("<IMAGE INDEX='1'><FILECOUNT>10</FILECOUNT><WINDOWS><ARCH>9</ARCH><EDITIONID>Professional</EDITIONID><VERSION><BUILD>26200</BUILD></VERSION></WINDOWS><NAME>Source Pro</NAME><FLAGS>Professional</FLAGS></IMAGE>");
        var captureXml = System.Xml.Linq.XElement.Parse("<IMAGE INDEX='1'><FILECOUNT>99</FILECOUNT><TOTALBYTES>1234</TOTALBYTES><WINDOWS><ARCH>9</ARCH><VERSION><BUILD>26100</BUILD></VERSION></WINDOWS><NAME>Capture</NAME><CUSTOM>retained</CUSTOM></IMAGE>");
        var restored = WimMetadata.CopyIdentity(sourceXml, captureXml);
        check((string?)restored.Element("FLAGS") == "Professional" && (string?)restored.Element("WINDOWS")?.Element("VERSION")?.Element("BUILD") == "26200" &&
            (string?)restored.Element("FILECOUNT") == "99" && (string?)restored.Element("TOTALBYTES") == "1234" && (string?)restored.Element("CUSTOM") == "retained" &&
            (string?)captureXml.Element("NAME") == "Capture", "captured WIM keeps resource counts while restoring source identity without mutating its input");
        captureXml.Element("WINDOWS")!.Element("ARCH")!.Value = "12";
        Rejects(() => WimMetadata.CopyIdentity(sourceXml, captureXml), "WIM identity cannot be copied across different architectures");

        var evidence = Path.Combine(root, "export-evidence"); Directory.CreateDirectory(evidence);
        void Write<T>(string name, T value) => File.WriteAllText(Path.Combine(evidence, name), System.Text.Json.JsonSerializer.Serialize(value));
        File.WriteAllText(Path.Combine(evidence, "complete.txt"), "fixture completion");
        File.WriteAllText(Path.Combine(evidence, "TL-RESULT.txt"), "0");
        var auditOptions = options with { Remove = [] };
        ImageServicing.Package[] retained = [new("Retained.App", "Retained.App_1")];
        Write("appx-before.json", retained); Write("appx-after.json", retained);
        Write("features-before.json", Array.Empty<ImageServicing.Feature>()); Write("features-after.json", Array.Empty<ImageServicing.Feature>());
        Rejects(() => RealExport.VerifyEvidence(auditOptions, evidence), "VM export rejects completion markers without service audit evidence");
        Write("services-before.json", new Dictionary<string,string>{{"ControlSet001\\RetainedService", "unchanged"}});
        Write("services-after.json", new Dictionary<string,string>{{"ControlSet001\\RetainedService", "unchanged"}});
        RealExport.VerifyEvidence(auditOptions, evidence); check(true, "VM export accepts complete unchanged preservation evidence");
        Write("services-after.json", new Dictionary<string,string>{{"ControlSet001\\RetainedService", "changed"}});
        Rejects(() => RealExport.VerifyEvidence(auditOptions, evidence), "VM export rejects source service changes even with a successful worker marker");
        Write("services-after.json", new Dictionary<string,string>{{"ControlSet001\\RetainedService", "unchanged"}});
        Write("appx-after.json", Array.Empty<ImageServicing.Package>());
        Rejects(() => RealExport.VerifyEvidence(auditOptions, evidence), "VM export rejects loss of an unselected app");
        Write("appx-after.json", retained);
        File.WriteAllText(Path.Combine(evidence, "TL-RESULT.txt"), "255");
        Rejects(() => RealExport.VerifyEvidence(auditOptions, evidence), "VM export rejects a failed worker result");
        File.WriteAllText(Path.Combine(evidence, "TL-RESULT.txt"), "0");
        var finalizeOptions = auditOptions with { Workspace = Path.Combine(root, "inert-finalization") };
        var sources = Path.Combine(finalizeOptions.Workspace, "media", "sources"); Directory.CreateDirectory(sources);
        var originalMedia = Path.Combine(sources, "install.wim"); File.WriteAllText(originalMedia, "original media");
        var finalizeConfig = Path.Combine(root, "inert-finalization.json");
        File.WriteAllText(finalizeConfig, System.Text.Json.JsonSerializer.Serialize(finalizeOptions));
        File.WriteAllText(finalizeConfig + ".result.json", "{\"sourceBefore\":\"mismatched-source\"}");
        try { await RealExport.FinalizeVm(finalizeConfig, "missing-candidate.wim", evidence); throw new Exception("Expected source identity rejection"); }
        catch (IOException ex) when (ex.Message == "Source ISO changed.")
        {
            check(File.ReadAllText(originalMedia) == "original media" && !File.Exists(Path.Combine(finalizeOptions.Workspace, "original-install.wim")),
                "changed source is rejected before media mutation or ISO export");
        }

        var inherited = options with { Index = 2, Workspace = Path.Combine(root, "inherited-setup"), Output = Path.Combine(root, "inherited.iso") };
        await new BuildEngine(_ => { }, _ => new FixtureRunner(inherited.Workspace, inheritedSetup: true), _ => new Dictionary<string,string>()).Build(inherited, CancellationToken.None);
        check(File.ReadAllText(Path.Combine(inherited.Workspace, "media", "autounattend.xml")) == "<unattend />" && File.Exists(inherited.Output), "inherited unattended setup is retained and does not reject a modified source");
        var readOnly = options with { Index = 2, Workspace = Path.Combine(root, "readonly-media"), Output = Path.Combine(root, "readonly-media.iso") };
        var inputAttributes = File.GetAttributes(options.Iso);
        var readOnlyRunner = new FixtureRunner(readOnly.Workspace, afterDism: args =>
        {
            if (args.Contains("/Commit") && args.Contains("/MountDir:" + Path.Combine(readOnly.Workspace,"mount"))) File.SetAttributes(Path.Combine(readOnly.Workspace, "media", "sources", "install.esd"), FileAttributes.ReadOnly);
        });
        await new BuildEngine(_=>{}, _=>readOnlyRunner, _=>new Dictionary<string,string>()).Build(readOnly, CancellationToken.None);
        check(File.Exists(readOnly.Output) && File.GetAttributes(options.Iso) == inputAttributes,
            "read-only copied installation media can be replaced without changing the source ISO attributes");
        var changed = options with { Index = 2, Workspace = Path.Combine(root, "service-change"), Output = Path.Combine(root, "service-change.iso") };
        var runner = new FixtureRunner(changed.Workspace); var reads = 0;
        try
        {
            await new BuildEngine(_ => { }, _ => runner, _ => new Dictionary<string,string>{{"CustomSvc", ++reads == 1 ? "disabled" : "automatic"}}).Build(changed, CancellationToken.None);
            throw new Exception("Expected service preservation failure");
        }
        catch (IOException)
        {
            check(runner.Commands.Any(c => c.Contains("/Discard")) && !runner.Commands.Any(c => c.Contains("/Commit")) && !File.Exists(changed.Output), "a service configuration regression discards without publishing an ISO");
        }
    }
}
