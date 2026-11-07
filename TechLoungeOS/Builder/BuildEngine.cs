using System.Text.Json;

namespace TechLoungeOS;

public sealed class BuildEngine(Action<string> log, Func<Action<string>, ICommandRunner>? runnerFactory = null, Func<string, IReadOnlyDictionary<string, string>>? serviceReader = null, IProgress<string>? progress = null)
{
    private readonly ICommandRunner runner = runnerFactory?.Invoke(log) ?? new ProcessRunner(log);
    private static string Q(string value) => Policy.Ps(value);

    public async Task<Edition[]> Import(string iso)
    {
        if (!File.Exists(iso)) throw new FileNotFoundException("ISO not found.", iso);
        var json = await runner.PowerShell($$"""
            $iso = {{Q(Path.GetFullPath(iso))}}
            $disk = Get-DiskImage -ImagePath $iso
            $owned = !$disk.Attached
            try {
              if ($owned) { $disk = Mount-DiskImage -ImagePath $iso -PassThru }
              $volume = $disk | Get-Volume
              if (!$volume.DriveLetter) { throw 'ISO has no accessible volume.' }
              $root = "$($volume.DriveLetter):\"
              $image = @('sources\install.wim','sources\install.esd') | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
              if (!$image) { throw 'No install.wim/install.esd. Split SWM and nonstandard media are not supported yet.' }
              $items = @(Get-WindowsImage -ImagePath $image | ForEach-Object {
                $detail = Get-WindowsImage -ImagePath $image -Index $_.ImageIndex
                [pscustomobject]@{ ImageIndex = [int]$_.ImageIndex; ImageName = [string]$_.ImageName; Architecture = [string]([int]$detail.Architecture); Version = [string]$detail.Version; EditionId = [string]$detail.EditionId }
              })
              ConvertTo-Json -InputObject $items -Compress
            } finally { if ($owned) { Dismount-DiskImage -ImagePath $iso | Out-Null } }
            """);
        return JsonSerializer.Deserialize<Edition[]>(json.Trim()) ?? throw new IOException("No editions found.");
    }

    public async Task Build(BuildOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Owned command-runner fixtures can omit downloads. Every real build enforces the standard inclusions.
        options=runnerFactory is null
            ? options with{Apps=RecommendedApps.ForBuild(options.Apps),Target16GbUsb=true,IncludeNet8=RuntimePack.Net8Supported(DateOnly.FromDateTime(DateTime.UtcNow))}
            : options with{Apps=RecommendedApps.NormalizeIds(options.Apps)};
        // Only controlled command-runner fixtures may omit the mandatory Office payload.
        if (runnerFactory == null && !options.IncludeOffice)
            throw new InvalidOperationException("Office is required in every app-created ISO.");
        if (runnerFactory == null && !options.IncludeEdge)
            throw new InvalidOperationException("The offline Edge browser is required in every app-created ISO.");
        if (runnerFactory == null && (!options.IncludeRuntimePack || !options.IncludeStoreXbox))
            throw new InvalidOperationException("Runtimes, Microsoft Store and Xbox are required in every app-created ISO.");
        if (runnerFactory == null && (!options.AnimatedWinPe || !options.GameMode || !options.DisableSuggestions || !options.UseCurrentTweaksInstaller || !options.IncludeAmdIntelGraphics || !options.IncludeNvidiaSetup))
            throw new InvalidOperationException("The setup wizard, gaming defaults, hardware-matched graphics queues and current Tweaks integration are required in every app-created ISO.");
        if (runnerFactory == null && string.IsNullOrWhiteSpace(options.TweaksFolder))
            throw new InvalidOperationException("TechLoungeTweaks integration is required. Extract the complete Builder package or choose its support folder.");
        Policy.Validate(options);
        if(runnerFactory is null)Preflight.ValidatePackage();
        if (options.TweaksFolder != null)
        {
            log("Checking TechLoungeTweaks files before servicing. Large bundles can take several minutes; the original files stay unchanged.");
            Provisioning.ValidatePayload(options.UseCurrentTweaksInstaller ? Path.Combine(options.TweaksFolder,"WebView2Runtime") : options.TweaksFolder, ct);
            log("TechLoungeTweaks files are readable. Checking the selected Windows image next.");
        }
        if (NvidiaSetup.Enabled(options)) NvidiaSetup.Validate(options.TweaksFolder!);
        if (runnerFactory == null) Preflight.Check(options);
        // Re-read the selected image rather than trusting stale UI metadata.
        var editions = await Import(options.Iso);
        var edition = editions.SingleOrDefault(e => e.ImageIndex == options.Index)
            ?? throw new IOException("Selected edition no longer exists.");
        if (!Version.TryParse(edition.Version, out var version) || version.Major != 10 || version.Build < 22000 || edition.Architecture != "9")
            throw new IOException("This milestone supports Windows 11 x64 images only.");
        var updatePlan = options.WindowsUpdateFolder == null ? null : WindowsImageUpdates.ReadPlan(options.WindowsUpdateFolder);
        var updateFolder = options.WindowsUpdateFolder;
        if(updatePlan != null && Version.Parse(updatePlan.SourceVersion) != version)
            throw new IOException("The Windows update plan does not match the selected edition version. Choose a matching plan before building.");
        ct.ThrowIfCancellationRequested();
        var work = Path.GetFullPath(options.Workspace);
        Directory.CreateDirectory(work);
        using var exclusive = new FileStream(Path.Combine(work, "build.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var logFile = new StreamWriter(Path.Combine(work, "build.log")) { AutoFlush = true };
        var logGate = new object();
        void Write(string text) { lock (logGate) logFile.WriteLine($"{DateTimeOffset.Now:O} {text}"); log(text); }
        var run = runnerFactory?.Invoke(Write) ?? new ProcessRunner(Write, Path.Combine(work, "scratch"));
        var media = Path.Combine(work, "media");
        var mount = Path.Combine(work, "mount");
        var wim = Path.Combine(work, "selected.wim");
        var journal = Path.Combine(work, "state.json");
        void State(string phase)
        {
            File.WriteAllText(journal + ".tmp", JsonSerializer.Serialize(new { phase, mount, wim, options, edition }, new JsonSerializerOptions { WriteIndented = true }));
            // MoveFileEx overwrite can reject a reader even with delete sharing.
            if (File.Exists(journal)) File.Replace(journal + ".tmp", journal, null);
            else File.Move(journal + ".tmp", journal);
            progress?.Report(phase);
        }
        State("Preparing build");
        Directory.CreateDirectory(mount);
        var mounted = false;
        try
        {
            if(options.UpdateWindows)
            {
                State("Checking Microsoft Windows updates for the selected ISO");
                var report=WindowsUpgradeRules.ForEdition(await WindowsUpdateDownload.CheckDetails(version,ct),edition.EditionId);var release=report.LatestCompatible;
                File.WriteAllText(Path.Combine(work,"windows-release.json"),JsonSerializer.Serialize(report));
                if(report.Upgrade is { } upgrade)
                {
                    State("Downloading and verifying Windows release upgrade packages");
                    updateFolder=Path.Combine(work,"windows-update-download");
                    updatePlan=await WindowsFeatureUpdateDownload.Prepare(updateFolder,version,upgrade,Write,ct);
                }
                else if(release.Version>version)
                {
                    State("Downloading and verifying Windows cumulative update packages");
                    updateFolder=Path.Combine(work,"windows-update-download");
                    updatePlan=await WindowsUpdateDownload.Prepare(updateFolder,version,release,Write,ct);
                }
                else Write("The source Windows version is at or above the latest offered stable cumulative update. No downgrade or unreviewed feature upgrade will be applied.");
            }
            if(options.UseCurrentTweaksInstaller)
            {
                State("Downloading and verifying the current official TechLoungeTweaks installer");
                await TweaksInstaller.Prepare(Path.Combine(work,"tweaks-installer"),ct);
            }
            var nvidia = Path.Combine(work, "nvidia-setup");
            if (NvidiaSetup.Enabled(options))
            {
                State("Preparing compact NVIDIA setup tools; drivers download after installation");
                await NvidiaSetup.Prepare(nvidia, ct);
            }
            var office = Path.Combine(work, "office-pack");
            if (options.IncludeOffice)
            {
                State("Downloading and verifying offline Office payload");
                await OfficePack.Prepare(run, office, options.OfficeSource, ct);
            }
            var runtimes = Path.Combine(work, "runtime-pack");
            if (options.IncludeRuntimePack)
            {
                State("Downloading and verifying offline runtime installers");
                await RuntimePack.Prepare(run, runtimes, options.IncludeNet8, ct);
            }
            var edge = Path.Combine(work, "edge-pack");
            if (options.IncludeEdge)
            {
                State("Downloading and verifying offline Microsoft Edge");
                await EdgePack.Prepare(run, edge, ct);
            }
            var recommended = Path.Combine(work, "recommended-apps");
            await AppRecipes.Prepare(options.Apps, recommended, ct);
            var hwinfo=Path.Combine(work,"hwinfo-pack");
            if(options.Apps.Any(HwInfoPack.Is))
            {
                State("Downloading and verifying selected HWiNFO for quiet Windows Setup installation");
                await HwInfoPack.Prepare(run,hwinfo,ct);
            }
            State("Copying Windows installation media");
            await run.PowerShell($$"""
                $iso = {{Q(Path.GetFullPath(options.Iso))}}
                $disk = Get-DiskImage -ImagePath $iso
                $owned = !$disk.Attached
                try {
                  if ($owned) { $disk = Mount-DiskImage -ImagePath $iso -PassThru }
                  $v = $disk | Get-Volume
                  if (!$v.DriveLetter) { throw 'ISO has no drive letter.' }
                  & "$env:SystemRoot\System32\robocopy.exe" "$($v.DriveLetter):\" {{Q(media)}} /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ /NP /NFL /NDL
                  if ($LASTEXITCODE -ge 8) { throw "ISO copy failed: $LASTEXITCODE" }
                  Get-ChildItem -LiteralPath {{Q(media)}} -File -Recurse | ForEach-Object { $_.IsReadOnly = $false }
                } finally { if ($owned) { Dismount-DiskImage -ImagePath $iso | Out-Null } }
                """);
            foreach (var required in new[] { @"boot\etfsboot.com", @"efi\microsoft\boot\efisys.bin", @"sources\boot.wim", "setup.exe" })
                if (!File.Exists(Path.Combine(media, required))) throw new IOException("Missing boot/setup prerequisite: " + required);
            foreach (var inherited in new[] { "autounattend.xml", @"sources\unattend.xml", @"sources\PID.txt" })
                if (File.Exists(Path.Combine(media, inherited))) Write("Preserving inherited setup file: " + inherited + ". Its deployment behaviour still requires VM validation.");
            if (Directory.EnumerateFiles(Path.Combine(media, "sources"), "install*.swm").Any()
                || (File.Exists(Path.Combine(media, @"sources\install.wim")) && File.Exists(Path.Combine(media, @"sources\install.esd"))))
                throw new IOException("Source contains split or ambiguous installation images.");
            ct.ThrowIfCancellationRequested();
            var source = Path.Combine(media, @"sources\install.wim");
            if (!File.Exists(source)) source = Path.Combine(media, @"sources\install.esd");
            State("Exporting selected edition");
            await run.Dism("/Export-Image", "/SourceImageFile:" + source, "/SourceIndex:" + options.Index,
                "/DestinationImageFile:" + wim, "/Compress:max", "/CheckIntegrity");
            ct.ThrowIfCancellationRequested();
            State("Mounting image");
            // Mark intent first: a failed mount may still require explicit recovery.
            mounted = true;
            await run.Dism("/Mount-Image", "/ImageFile:" + wim, "/Index:1", "/MountDir:" + mount, "/CheckIntegrity");
            ct.ThrowIfCancellationRequested();
            State("Servicing");
            version=await ServiceAndStage(run,options,edition,media,mount,version,updatePlan,updateFolder,Write,State,ct,serviceReader);
            ct.ThrowIfCancellationRequested();
            State("Committing");
            await run.Dism("/Unmount-Image", "/MountDir:" + mount, "/Commit", "/CheckIntegrity");
            mounted = false;
            if(updatePlan is not null)
            {
                WimVersionMetadata.Update(wim,Path.Combine(work,"scratch"),Version.Parse(edition.Version),version);
                var metadata=await run.PowerShell("$image=Get-WindowsImage -ImagePath "+Q(wim)+" -Index 1; [string]$image.Version");
                if(!Version.TryParse(metadata.Trim(),out var reported) || reported!=version)
                    throw new IOException("The serviced image metadata does not match its verified Windows version. No ISO will be exported.");
            }
            var completedWim=wim;
            if(updatePlan is not null || Path.GetExtension(source).Equals(".esd",StringComparison.OrdinalIgnoreCase) ||
                (options.Target16GbUsb && BootableDrive.ProjectedUsbBytes(media,source,wim)>BootableDrive.Target16GbBytes))
            {
                State("Compressing the installation image");
                completedWim=await WimPacking.Compact(run,wim,work,version,Write,ct);
            }
            // ISO files may carry the read-only attribute into our private media copy.
            File.SetAttributes(source, File.GetAttributes(source) & ~FileAttributes.ReadOnly);
            File.Delete(source);
            File.Move(completedWim, Path.Combine(media,"sources",Path.GetExtension(completedWim).Equals(".esd",StringComparison.OrdinalIgnoreCase)?"install.esd":"install.wim"));
            State("Preparing Windows Setup compatibility defaults");
            await SetupCompatibility.Prepare(run, media, work, Write, ct);
            State("Creating ISO");
            ct.ThrowIfCancellationRequested();
            if (options.Target16GbUsb) BootableDrive.Check16GbTarget(media, Write);
            await IsoExport.Create(run, media, options.Output, options.Oscdimg, Write, ct, State);
            if(completedWim!=wim)
            {
                try{File.Delete(wim);}
                catch(Exception cleanup) when(cleanup is IOException or UnauthorizedAccessException)
                {Write("The ISO passed readback; its temporary unpacked WIM could not be removed: "+cleanup.Message);}
            }
            State("Complete");
            Write("ISO exported. Installation and Secure Boot must still be validated in a VM.");
        }
        catch (Exception ex)
        {
            Write(ex.ToString());
            try { State("Failed: " + ex.Message); }
            catch (Exception journalFailure) when (journalFailure is IOException or UnauthorizedAccessException)
            { Write("Could not record failure in build journal: " + journalFailure.Message); }
            throw;
        }
        finally
        {
            if (mounted)
            {
                try { await run.Dism("/Unmount-Image", "/MountDir:" + mount, "/Discard"); Write("Uncommitted image discarded."); }
                catch (Exception ex) { Write("Recovery required for " + mount + ": " + ex.Message); }
            }
        }
    }

    // Shared by the ordinary build and owned-VM continuation after verified Windows servicing.
    // The caller owns mounting, commit, export and recovery; this stage never publishes an image.
    internal static async Task<Version> ServiceAndStage(ICommandRunner run,BuildOptions options,Edition edition,
        string media,string mount,Version version,WindowsImageUpdates.Plan? updatePlan,string? updateFolder,
        Action<string> Write,Action<string> State,CancellationToken ct,
        Func<string,IReadOnlyDictionary<string,string>>? serviceReader=null,bool prepareWinPe=true)
    {
        var work=Path.GetFullPath(options.Workspace);
        var office=Path.Combine(work,"office-pack");var runtimes=Path.Combine(work,"runtime-pack");
        var edge=Path.Combine(work,"edge-pack");var recommended=Path.Combine(work,"recommended-apps");
        var hwinfo=Path.Combine(work,"hwinfo-pack");var nvidia=Path.Combine(work,"nvidia-setup");
        if(options.AnimatedWinPe && prepareWinPe)
        {
            var regional=SetupRegional.FromImage(mount,options);
            File.WriteAllText(Path.Combine(work,"setup-regional.json"),JsonSerializer.Serialize(regional));
            State("Checking and preparing the WinPE setup and tools interface");
            await WinPeBackground.Prepare(run,media,work,Write,ct,regional);
            State("Servicing");
        }
        var profile = SourcePreservation.Classify(options.Iso, [edition.ImageName], mount);
        if (!File.Exists(Path.Combine(mount, @"Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll")))
            throw new IOException("Quiet first-login setup requires the Windows .NET Framework 4 files. This source removed them; use a source retaining that Windows dependency.");
        foreach(var dependency in new[]{"System.Drawing.dll","System.Windows.Forms.dll","System.Web.Extensions.dll"})
            if(!File.Exists(Path.Combine(mount,@"Windows\Microsoft.NET\Framework64\v4.0.30319",dependency)))
                throw new IOException("The installed wallpaper picker requires the Windows .NET Framework desktop library "+dependency+". This source removed it.");
        File.WriteAllText(Path.Combine(work, "source-profile.json"), JsonSerializer.Serialize(profile));
        Write(profile.Classification + ". Existing source configuration is the preservation baseline.");
        PowerDefaults.ValidateSource(mount);
        var dependencies = new Dictionary<string, bool>
        {
            ["Windows Update library"] = File.Exists(Path.Combine(mount, @"Windows\System32\wuaueng.dll")),
            ["Recovery image (may live on a separate partition)"] = File.Exists(Path.Combine(mount, @"Windows\System32\Recovery\Winre.wim")),
            ["Defender built-in folder"] = Directory.Exists(Path.Combine(mount, @"Program Files\Windows Defender")),
            ["WebView2 runtime at standard image paths"] = Provisioning.HasImageRuntime(mount)
        };
        File.WriteAllText(Path.Combine(work, "dependency-presence.json"), JsonSerializer.Serialize(dependencies, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var dependency in dependencies.Where(d => !d.Value))
            Write("Source warning: " + dependency.Key + " not found at its standard path. Presence checks do not establish health; this builder cannot restore stripped dependencies.");
        if (options.TweaksFolder != null && !Provisioning.HasImageRuntime(mount) && !Provisioning.HasPrivateRuntime(options.TweaksFolder))
            throw new IOException("This source has no WebView2 runtime files at the supported image paths. Choose a trusted TechLoungeTweaks integration bundle containing a complete x64 WebView2Runtime folder, or deselect Tweaks integration. System components will not be restored automatically.");
        if(updatePlan != null)
        {
            State("Updating Windows and checking source preservation");
            version = await WindowsImageUpdates.Apply(run,mount,work,updateFolder!,updatePlan,version,Write,ct,serviceReader);
            State("Servicing");
        }
        await WindowsRecovery.ValidateSource(run,mount);
        await LegacyNetwork.ValidateSource(run,mount);
        await ImageServicing.Run(run, mount, options, Write, ct, serviceReader);
        var storeXbox = Path.Combine(work, "store-xbox");
        if (options.IncludeStoreXbox)
        {
            using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(work, "appx-before.json")));
            var present = inventory.RootElement.EnumerateArray().Select(p => p.GetProperty("DisplayName").GetString()!).ToArray();
            State("Acquiring missing offline Store and Xbox packages");
            await StoreXbox.Prepare(run, storeXbox, options.StoreXboxPackages, ct, present, version);
        }
        if (options.Apps.Any(AppRecipes.NeedsPayload))
        {
            Provisioning.CopyTree(recommended, Path.Combine(mount, @"ProgramData\TechLoungeOS\Recommended"));
            Provisioning.WriteHashes(Path.Combine(mount, @"ProgramData\TechLoungeOS"));
        }
        const bool setupRequested = true; // Global desktop defaults also require the preserved setup hook.
        var hadOemHook = setupRequested && SetupHooks.Prepare(media, mount);
        var desktopInstallers = new List<DesktopSetup.Step>();
        if (options.IncludeOffice)
        {
            foreach (var file in new[] { @"Windows\System32\fltmc.exe", @"Windows\System32\drivers\fltmgr.sys" })
                if (!File.Exists(Path.Combine(mount, file))) throw new IOException("Office's requested setup guard is unavailable in this source: " + file);
            desktopInstallers.Add(OfficePack.Stage(media, mount, office));
        }
        if (options.IncludeEdge) desktopInstallers.Add(await EdgePack.Stage(run, mount, edge, ct));
        if(options.Apps.Any(HwInfoPack.Is)) desktopInstallers.Add(RuntimePack.AddSetupCommand(mount,"TechLoungeOS-HWiNFO",hwinfo));
        if (options.IncludeRuntimePack) desktopInstallers.Add(RuntimePack.Stage(mount, runtimes));
        if (options.IncludeStoreXbox) desktopInstallers.Add(RuntimePack.AddSetupCommand(mount, "TechLoungeOS-StoreXbox", storeXbox));
        var preparedUserPolicyHash=RecommendedDefaultUser.Stage(mount,options.TweaksFolder!=null);
        DesktopDefaults.StageMachine(mount);
        desktopInstallers.Add(LegacyNetwork.Stage(mount));
        desktopInstallers.Add(WindowsRecovery.Stage(mount));
        DesktopSetup.Stage(mount,desktopInstallers);
        BootOptimizer.Stage(mount,options.Apps);
        PowerDefaults.Stage(mount,options.Apps);
        if (options.TweaksFolder != null)
        {
            RecommendedProfile.StageMachine(mount);
        }
        if (NvidiaSetup.Enabled(options)) NvidiaSetup.Stage(mount, options.TweaksFolder!, nvidia);
        if (options.IncludeAmdIntelGraphics) GraphicsSetup.Stage(mount);
        if (options.IncludeOffice || hadOemHook) SetupHooks.FinalizeOem(media, mount);
        if (setupRequested) Provisioning.WriteHashes(Path.Combine(mount, @"ProgramData\TechLoungeOS"));
        var stagedConfiguration=serviceReader?.Invoke(mount) ?? SourcePreservation.CaptureConfiguration(mount);
        var preservedConfiguration=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(work,"services-after.json")))
            ?? throw new IOException("Missing verified configuration baseline.");
        SourcePreservation.VerifyStagedConfiguration(preservedConfiguration,stagedConfiguration,preparedUserPolicyHash);
        File.WriteAllText(Path.Combine(work,"configuration-staged.json"),JsonSerializer.Serialize(stagedConfiguration));
        return version;
    }
}
