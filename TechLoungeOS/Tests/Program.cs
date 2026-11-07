using TechLoungeOS;
if(args is ["--work-progress-checks"])
{
    WorkProgressChecks.Run((ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});return;
}
if(args is ["--microsoft-iso-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-microsoft-iso-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await MicrosoftIsoChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}

if(args is ["--smb-source-native",var nativeSourceWim])
{
    var trial=Path.Combine(Path.GetTempPath(),"TechLoungeOS-smb-readonly-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(trial);
    var mount=Path.Combine(trial,"mount");Directory.CreateDirectory(mount);
    Policy.RequireUnlinkedPath(nativeSourceWim);
    var runner=new ProcessRunner(Console.WriteLine,Path.Combine(trial,"scratch"));var mounted=false;
    try
    {
        using(var lease=new FileStream(nativeSourceWim,FileMode.Open,FileAccess.Read,FileShare.None)){}
        await runner.Dism("/Mount-Image","/ImageFile:"+nativeSourceWim,"/Index:1","/MountDir:"+mount,"/CheckIntegrity");mounted=true;
        await LegacyNetwork.ValidateSource(runner,mount);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{nativeSmbQueryPassed=true,queriesOnly=true,discardAfterQuery=true,source=nativeSourceWim,trial,noPhysicalUsbWritten=true}));
    }
    finally{if(mounted)await runner.Dism("/Unmount-Image","/MountDir:"+mount,"/Discard");}return;
}
if(args is ["--legacy-network-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-smb-inert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await LegacyNetworkChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
var count = 0;
ImageMetadataChecks.Run((ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);});
if(args is ["--restart-notice-checks"])
{
    var parent=Path.GetFullPath(Path.GetTempPath());
    var fixture=Path.GetFullPath(Path.Combine(parent,"TLOS-restart-notice-"+Guid.NewGuid().ToString("N")));
    if(!fixture.StartsWith(Path.Combine(parent,"TLOS-restart-notice-"),StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected fixture path");
    Directory.CreateDirectory(fixture);
    try{await RestartNoticeChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--task-identity-checks"])
{
    TaskCacheIdentityChecks.Run((ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});
    Console.WriteLine("Task identity checks passed. No host registry or servicing changed.");return;
}
if(args is ["--quiet-launch-checks",var quietDestination])
{
    Policy.RequireUnlinkedPath(quietDestination);
    if(Path.Exists(quietDestination))throw new IOException("A fresh harmless-launch fixture folder is required.");
    Directory.CreateDirectory(quietDestination);
    await QuietLaunchChecks.Run(quietDestination,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});
    Console.WriteLine("Quiet launcher checks passed. No app installer or host servicing ran.");return;
}
if(args is ["--emit-post-app-fixture",var postAppDestination])
{
    Policy.RequireUnlinkedPath(postAppDestination);
    if(Directory.Exists(postAppDestination)||File.Exists(postAppDestination))throw new IOException("A fresh post-app fixture folder is required.");
    string Resource(string name){using var input=typeof(BootOptimizer).Assembly.GetManifestResourceStream(name)??throw new IOException("Missing fixture resource: "+name);using var reader=new StreamReader(input);return reader.ReadToEnd();}
    Directory.CreateDirectory(postAppDestination);
    var scripts=new Dictionary<string,string>{["app-readiness.ps1"]=Resource("TechLoungeOS.PostAppReadiness.ps1"),["boot-worker.ps1"]=Resource("TechLoungeOS.BootOptimizerWorker.ps1"),["power-worker.ps1"]=PowerDefaults.Worker,["boot-first-login.ps1"]=BootOptimizer.FirstLogin(RecommendedApps.ForBuild(RecommendedApps.PresetIds)),["power-first-login.ps1"]=PowerDefaults.FirstLogin};
    scripts["hatch-shortcut.ps1"]=AppRecipes.HatchZipShortcut;
    scripts["hatch-first-login.ps1"]=AppRecipes.Script(RecommendedApps.HatchZip);
    scripts["runtime-policy.ps1"]=TweaksRuntime.Worker;
    scripts["machine-worker.ps1"]=DesktopSetup.Worker;
    scripts["tweaks-first-login.ps1"]=TweaksInstaller.FirstLogin;
    scripts["chatgpt-first-login.ps1"]=AppRecipes.ChatGPTScript;
    foreach(var item in scripts)File.WriteAllText(Path.Combine(postAppDestination,item.Key),item.Value,new System.Text.UTF8Encoding(false));
    File.WriteAllText(Path.Combine(postAppDestination,"manifest.json"),System.Text.Json.JsonSerializer.Serialize(scripts.Keys.ToDictionary(n=>n,n=>RuntimePack.Hash(Path.Combine(postAppDestination,n)))));
    Console.WriteLine("Post-app fixture scripts emitted. No task registered or installer run.");return;
}
if(args is ["--owned-final-26h2-usb"]){await NativeFinalUsb.Run();return;}
if(args is ["--wim-packing-checks"]) {await WimPackingChecks.Run();return;}
if(args is ["--prepare-default-recipes",var recipeDestination])
{
    if(Directory.Exists(recipeDestination)||File.Exists(recipeDestination))throw new IOException("A fresh recipe folder is required.");
    await AppRecipes.Prepare([RecommendedApps.Discord,RecommendedApps.HatchZip],recipeDestination,CancellationToken.None);
    Console.WriteLine("Current publisher-verified Discord/OpenAsar and HatchZip payloads prepared; no apps installed.");return;
}
if(args is ["--runtime-support-checks"])
{
    if(!RuntimePack.Net8Supported(new DateOnly(2026,11,3)) || !RuntimePack.Net8Supported(new DateOnly(2026,11,10)) ||
        RuntimePack.Net8Supported(new DateOnly(2026,11,11)) || RuntimePack.Net8Supported(new DateOnly(2027,1,1)) ||
        RuntimePack.Packages(false).Any(p=>p.Id.StartsWith("Microsoft.DotNet.DesktopRuntime.8",StringComparison.Ordinal)) ||
        !RuntimePack.Packages(false).Any(p=>p.Id=="Microsoft.DotNet.DesktopRuntime.10"))
        throw new Exception("Runtime support boundary or supported replacement failed.");
    Console.WriteLine("Runtime support dates and retained .NET 10 packages passed; no installer ran.");return;
}
if(args is ["--owned-final-26h2-cache-check"]) {await NativeFinalIntegration.CheckCache();return;}
if(args is ["--owned-final-26h2-integration"]) {await NativeFinalIntegration.Run();return;}
if(args is ["--owned-final-26h2-resume"]) {await NativeFinalIntegration.Run(true);return;}
if(args is ["--wallpaper-preservation-checks"]) {KnownWallpaperChecks.PreservationFixtures();return;}
if(args is ["--deployment-package-checks"]) {Preflight.ValidatePackage();Console.WriteLine("Deployment helpers and all wallpaper variants passed before any servicing.");return;}
if(args is ["--checked-wallpaper-native-fixtures"] or ["--owned-migration-accept-1665"])
{
    try{if(args[0]=="--checked-wallpaper-native-fixtures")await KnownWallpaperChecks.NativeFixtures();else await NativeMigrationAcceptance.Run();}
    catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    return;
}
if(args is ["--windows-migration-checks"]) {await WindowsMigrationChecks.Run((ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});return;}
if(args is ["--owned-migration-review-1651"]) {await NativeMigrationReview.Run();return;}
if(args is ["--asset-security-fixtures"]) {AssetWriteAccess.SecurityFixtures();return;}
if(args is ["--asset-security-diagnostics"]) {await AssetWriteAccess.Diagnose();return;}
if(args is ["--continue-approved-wallpaper-repair"])
{ await NativeFeatureUpgrade.ContinueApprovedRepair();return; }

if(args is ["--wallpaper-asset-fixture",var assetFixture])
{ WallpaperAssetRepair.Fixture(assetFixture);return; }
if(args is ["--native-feature-repair-wallpaper-existing",var repairWork,var repairPackages])
{ await NativeFeatureUpgrade.Run("",repairWork,true,repairPackages,true);return; }

if(args is ["--native-feature-upgrade",var nativeSource,var nativeWork])
{ await NativeFeatureUpgrade.Run(nativeSource,nativeWork); return; }
if(args is ["--native-feature-upgrade-existing",var existingSource,var existingWork])
{ await NativeFeatureUpgrade.Run(existingSource,existingWork,true); return; }
if(args is ["--native-feature-offline-existing",var offlineWork,var offlinePackages])
{ await NativeFeatureUpgrade.Run("",offlineWork,true,offlinePackages); return; }
if(args is ["--read-windows-update-plan",var planFolder])
{ Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(WindowsImageUpdates.ReadPlan(planFolder))); return; }
if(args is ["--usb-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-usb-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await UsbChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--app-selection-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-app-selection-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await AppSelectionChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--discord-install-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-discord-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await DiscordInstallChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--write-discord-recipe",var discordRecipe]){File.WriteAllText(discordRecipe,Provisioning.WinGetInstall+"\n"+AppRecipes.DiscordScript);return;}
if(args is ["--tweaks-installer-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-tweaks-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{TweaksInstallerChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--windows-feature-upgrade-discovery",var discoveryFolder])
{
    var report=await WindowsUpdateDownload.CheckDetails(new Version(10,0,26200,9457),CancellationToken.None);
    var offer=report.Upgrade??throw new IOException("No reviewed enablement route was found in current Microsoft metadata.");
    // Metadata/download smoke only: assume its prerequisite version, so no large CU is downloaded or serviced.
    var plan=await WindowsFeatureUpdateDownload.Prepare(discoveryFolder,offer.Baseline.Version,offer,Console.WriteLine,CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{report.Upgrade,plan}));return;
}
if(args is ["--windows-feature-upgrade-checks",var enablementFixture])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-feature-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await WindowsFeatureUpdateChecks.Run(fixture,enablementFixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--windows-update-download-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-update-download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await WindowsUpdateDownloadChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--windows-release-info",var windowsReleaseFile,var sourceVersion])
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(WindowsReleaseCatalog.LatestCompatible(WindowsReleaseCatalog.Parse(File.ReadAllText(windowsReleaseFile)),Version.Parse(sourceVersion),DateOnly.FromDateTime(DateTime.UtcNow))));return;
}
if(args is ["--windows-update-metadata",var searchFile,var metadataFile])
{
    var release=new WindowsReleaseCatalog.Release("25H2",new Version(10,0,26200,9725),new DateOnly(2026,10,26),"2026-10 OOB","KB5134133");
    var id=WindowsUpdateDownload.FindUpdate(File.ReadAllText(searchFile),release);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{id,files=WindowsUpdateDownload.ParseFiles(File.ReadAllText(metadataFile),id,release.KnowledgeBase)}));return;
}
if(args is ["--power-default-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-power-defaults-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await PowerDefaultsChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--windows-image-update-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-windows-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await WindowsImageUpdateChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--desktop-setup-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-desktop-setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await DesktopSetupChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--process-output-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TLOS-process-output-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await ProcessOutputChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--pe-startup-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-pe-startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{PeStartupChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--pe-metadata-native" or "--pe-metadata-existing",var originalPe,var stagedPe])
{
    var nativePeFolder=Path.Combine(Path.GetTempPath(),"TechLoungeOS-pe-metadata-"+Guid.NewGuid().ToString("N"));
    if(new DriveInfo(Path.GetPathRoot(nativePeFolder)!).AvailableFreeSpace<(100L<<30)+new FileInfo(stagedPe).Length)throw new IOException("Keep host storage headroom before copying the native fixture");
    string copy;
    if(args[0]=="--pe-metadata-existing")
    {
        copy=Path.GetFullPath(stagedPe);var approvedPrefix=Path.Combine(Path.GetTempPath(),"TechLoungeOS-pe-metadata-");
        if(!copy.StartsWith(approvedPrefix,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(copy)!="boot.wim")throw new IOException("Only an owned native metadata fixture can be retried");
    }
    else{Directory.CreateDirectory(nativePeFolder);copy=Path.Combine(nativePeFolder,"boot.wim");File.Copy(stagedPe,copy,false);}
    WinPeImage.PreserveWindowsMetadata(originalPe,copy);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{path=copy,bootIndex=WinPeImage.ReadBootIndex(copy),originalChanged=false,onlyGeneratedCopyChanged=true}));return;
}
if(args is ["--pe-image-info",var bootImage]){Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{bootIndex=WinPeImage.ReadBootIndex(bootImage)}));return;}
if(args is ["--pe-image-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-pe-image-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{PeImageChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--iso-verification-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-iso-verification-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await IsoVerificationChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--standalone-login-script",var standaloneScript]){File.WriteAllText(standaloneScript,Standalone.FirstLogin(RecommendedApps.PresetIds));return;}
if(args is ["--stage-standalone",var standaloneMount]){Standalone.Stage(standaloneMount,RecommendedApps.PresetIds,true);return;}
if(args is ["--standalone-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-standalone-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await StandaloneChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--boot-login-script",var bootScript]){File.WriteAllText(bootScript,BootOptimizer.FirstLogin(RecommendedApps.PresetIds));return;}
if(args is ["--stage-boot",var bootMount]){BootOptimizer.Stage(bootMount,RecommendedApps.PresetIds);return;}
if(args is ["--boot-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-boot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await BootOptimizerChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--hwinfo-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-hwinfo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await HwInfoChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--prepare-hwinfo",var hwinfoDestination]){await HwInfoPack.Prepare(new ProcessRunner(Console.WriteLine),hwinfoDestination,CancellationToken.None);return;}
if(args is ["--quiet-functions",var quietFunctions]){File.WriteAllText(quietFunctions,QuietApps.Functions);return;}
if(args is ["--edge-checks"])
{
    var fixture=Path.Combine(Path.GetTempPath(),"TechLoungeOS-edge-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    try{await EdgeChecks.Run(fixture,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(fixture,true);}return;
}
if(args is ["--desktop-checks"])
{
    var desktopFixture=Path.Combine(Path.GetTempPath(),"TLOS-desktop-checks-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(desktopFixture);
    try {
        await DesktopChecks.Run(desktopFixture,Check);
        await DesktopSurfaceChecks.Run(desktopFixture,Check);
        DefaultPolicyChecks.Run(desktopFixture,Check);
        Console.WriteLine($"{count} desktop checks passed. No host desktop settings changed.");
    }finally{Directory.Delete(desktopFixture,true);}
    return;
}
if(args is ["--export-desktop-cleanup",var desktopScripts])
{
    if(Directory.Exists(desktopScripts)||File.Exists(desktopScripts))throw new IOException("Desktop script export folder must be new.");
    Directory.CreateDirectory(desktopScripts);
    File.WriteAllText(Path.Combine(desktopScripts,"desktop-machine.ps1"),DesktopDefaults.Script(true)+"\n"+DesktopSurface.MachineScript);
    File.WriteAllText(Path.Combine(desktopScripts,"desktop-settings.ps1"),DesktopDefaults.Script(false));
    File.WriteAllText(Path.Combine(desktopScripts,"desktop-user-cleanup.ps1"),DesktopSurface.FirstLogin(true));
    Console.WriteLine("Desktop scripts exported for owned-VM validation; no host desktop settings changed.");return;
}
if(args is ["--prepare-tweaks",var tweaksDestination])
{
    await TweaksInstaller.Prepare(tweaksDestination,CancellationToken.None);
    Console.WriteLine("Current release archive and installer verified; no installer was executed.");return;
}
if(args is ["--parse-tweaks-release",var releaseFile])
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(TweaksInstaller.ParseRelease(File.ReadAllText(releaseFile))));return;
}
if(args is ["--export-account-templates",var templateFolder])
{
    if(Directory.Exists(templateFolder)||File.Exists(templateFolder))throw new IOException("Template check destination must be new.");
    Directory.CreateDirectory(templateFolder);
    File.WriteAllText(Path.Combine(templateFolder,"account-auto.xml"),SetupAnswerFile.Template(true),new System.Text.UTF8Encoding(false));
    File.WriteAllText(Path.Combine(templateFolder,"account-manual.xml"),SetupAnswerFile.Template(false),new System.Text.UTF8Encoding(false));return;
}
if(args is ["--write-recovery-script",var recoveryScript]){File.WriteAllText(recoveryScript,WindowsRecovery.Script);return;}
if(args is ["--verify-store",var storeSource,var storeDestination])
{
    await StoreXbox.Prepare(new ProcessRunner(Console.WriteLine),storeDestination,storeSource,CancellationToken.None);
    Console.WriteLine("All six offline Store/Xbox component sets verified; no host deployment performed.");return;
}
if(args is ["--download-store",var storeProduct,var storeDownload])
{
    var component=StoreXbox.Products.Single(p=>p.Product==storeProduct);
    Directory.CreateDirectory(storeDownload);
    await StorePackageDownload.Acquire(component.Product,component.Identity,storeDownload,new Version(10,0,26200,9457),CancellationToken.None);
    Console.WriteLine("Original Microsoft component archives downloaded; signature/identity verification remains in Prepare.");return;
}
if(args is ["--prepare-store",var storePrepared])
{
    await StoreXbox.Prepare(new ProcessRunner(Console.WriteLine),storePrepared,null,CancellationToken.None,
        imageVersion:new Version(10,0,26200,9457),download:StorePackageDownload.Acquire);
    Console.WriteLine("Offline Store/Xbox payload verified; no host deployment performed.");return;
}
if(args is ["--edge-script",var edgeScript]){File.WriteAllText(edgeScript,EdgePack.Script);return;}
if(args is ["--prepare-edge",var edgePrepared])
{
    await EdgePack.Prepare(new ProcessRunner(Console.WriteLine),edgePrepared,CancellationToken.None);
    Console.WriteLine("Offline Edge payload verified; no browser installed on the build host.");return;
}
if(args is ["--quiet-app-checks"])
{
    var quietRoot=Path.Combine(Path.GetTempPath(),"TechLoungeOS-quiet-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(quietRoot);
    try{await QuietAppChecks.Run(quietRoot,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(quietRoot,true);}return;
}
if(args is ["--write-quiet-login-script",var quietScript])
{
    File.WriteAllText(quietScript,Provisioning.Script(["7zip.7zip","SumatraPDF.SumatraPDF",RecommendedApps.Discord],true,false,"wallpaper.bmp",recommended:true));
    return;
}
if (args is ["--stage-graphics",var graphicsMount])
{
    GraphicsSetup.Stage(graphicsMount);
    Console.WriteLine("Staged graphics scripts only; no tasks or drivers ran.");return;
}
if (args is ["--stage-recommended",var recommendedStageTarget])
{
    Directory.CreateDirectory(Path.Combine(recommendedStageTarget,@"ProgramData\TechLoungeOS"));
    RecommendedProfile.StageMachine(recommendedStageTarget);
    File.WriteAllText(Path.Combine(recommendedStageTarget,@"ProgramData\TechLoungeOS\recommended-user.ps1"),"$state=Join-Path $env:LOCALAPPDATA 'TechLoungeOS-Accept327'\nNew-Item $state -ItemType Directory -Force|Out-Null\n"+RecommendedProfile.Script(false));
    Console.WriteLine("Staged Recommended scripts only; no registry values changed.");return;
}
if (args is ["--graphics-checks"])
{
    var graphicsRoot=Path.Combine(Path.GetTempPath(),"TechLoungeOS-graphics-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(graphicsRoot);
    try{await GraphicsChecks.Run(graphicsRoot,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});}
    finally{Directory.Delete(graphicsRoot,true);}return;
}
if (args is ["--prepare-nvidia", var nvidiaPrepared])
{
    await NvidiaSetup.Prepare(nvidiaPrepared,CancellationToken.None);
    Console.WriteLine("Prepared extractor and licence only; no GPU driver downloaded or installed.");
    return;
}
if (args is ["--stage-nvidia", var nvidiaMount, var nvidiaTweaks, var nvidiaTools])
{
    NvidiaSetup.Stage(nvidiaMount,nvidiaTweaks,nvidiaTools);
    Console.WriteLine("Staged NVIDIA setup files only; no task registered or driver installed.");
    return;
}
if (args is ["--nvidia-checks"])
{
    var nvidiaRoot = Path.Combine(Path.GetTempPath(), "TechLoungeOS-nvidia-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(nvidiaRoot);
    try { await NvidiaChecks.Run(nvidiaRoot,(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}); }
    finally { Directory.Delete(nvidiaRoot,true); }
    return;
}
if (args is ["--recommended-checks"])
{
    var recommendedRoot = Path.Combine(Path.GetTempPath(), "TechLoungeOS-recommended-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(recommendedRoot);
    try { await RecommendedChecks.Run(recommendedRoot, (ok, name) => { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }); }
    finally { Directory.Delete(recommendedRoot, true); }
    return;
}
if (args is ["--prepare-office", var officeFolder])
{
    await OfficePack.Prepare(new ProcessRunner(Console.WriteLine), officeFolder, null, CancellationToken.None);
    return;
}
if (args is ["--prepare-runtimes", var runtimeFolder])
{
    await RuntimePack.Prepare(new ProcessRunner(Console.WriteLine), runtimeFolder, false, CancellationToken.None);
    return;
}
if (args is ["--configuration", var configurationMount, var configurationOutput])
{
    var state = SourcePreservation.CaptureConfiguration(configurationMount);
    using var output = new FileStream(configurationOutput, FileMode.CreateNew, FileAccess.Write);
    System.Text.Json.JsonSerializer.Serialize(output, state);
    Console.WriteLine($"Read {state.Count} service and settings entries without modifying source hives.");
    return;
}
if (args is ["--restore-wim-metadata", var originalWim, var capturedWim, var metadataScratch])
{
    WimMetadata.Restore(originalWim, capturedWim, metadataScratch);
    Console.WriteLine("Original selected-image identity restored; capture resource counts retained.");
    return;
}
if (args is ["--pe-verify-completed", var completedConfig, var completedDrive])
{
    await PeServicing.VerifyCompleted(completedConfig, completedDrive);
    return;
}
if (args is ["--services", var serviceMount])
{
    var state = SourcePreservation.CaptureServices(serviceMount);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { services = state.Count, verifiedRead = true }));
    return;
}
if (args is ["--source-apps", var scanIso, var scanIndex])
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await InheritedApps.Scan(scanIso, int.Parse(scanIndex), Console.Error.WriteLine)));
    return;
}
if (args is ["--search", var query])
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await AppCatalog.Search(query)));
    return;
}
if (args is ["--pe-applied-service", var appliedConfig, var appliedDrive])
{
    await PeServicing.Run(appliedConfig, appliedDrive, applied: true);
    return;
}
if (args is ["--finalize-vm", var finalizeConfig, var servicedWim, var vmEvidence])
{
    await RealExport.FinalizeVm(finalizeConfig, servicedWim, vmEvidence);
    return;
}
if (args is ["--pe-service", var peConfig, var virtualDrive])
{
    await PeServicing.Run(peConfig, virtualDrive);
    return;
}
if (args is ["--build", var buildConfig])
{
    await RealExport.Run(buildConfig);
    return;
}
if (args is ["--import", var sourceIso])
{
    var editions = await new BuildEngine(Console.WriteLine).Import(sourceIso);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(editions, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}
if(args.Length>0)throw new ArgumentException("Unknown validation command: "+args[0]);
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
void Throws(Action action, string name) { try { action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var catalog = AppCatalog.Parse("Name                Id                         Version Match\n-------------------------------------------------------------------\nVLC media player    VideoLAN.VLC               3.0.24  Moniker: vlc\n");
Check(catalog is [{ Name: "VLC media player", Id: "VideoLAN.VLC", Version: "3.0.24" }], "real WinGet table maps name, exact identity and version");
Check(!AppCatalog.ValidId("Vendor.App/../../evil") && !AppCatalog.ValidId("Vendor.App' ;exit") && !AppCatalog.ValidId("--override"), "catalog IDs reject path and script injection");
Check(AppCatalog.ValidId("Mozilla.Firefox") && AppCatalog.ValidId("7zip.7zip"), "catalog supports apps outside former preset list");
Check(AppCatalog.ExplicitArmVariant(new("Discord (arm64)","Discord.Discord.arm64","1"))&&!AppCatalog.ExplicitArmVariant(new("Discord","Discord.Discord","1"))&&!AppCatalog.ExplicitArmVariant(new("ARM64 development tools","Vendor.Compiler","1")),"explicit ARM package variants are distinguished from standard apps and generic ARM developer tools");
Check(AppCatalog.Parse("Name                Id                         Version Match\n-------------------------------------------------------------------\nTruncated app       Vendor.Trunc…              1.0     Tag: x\n").Length == 0, "truncated identity never becomes an install selection");
try { AppCatalog.Parse("unexpected output"); throw new Exception("Expected parse failure"); }
catch (InvalidDataException) { Check(true, "unknown catalog format fails explicitly"); }
var inherited = InheritedApps.Parse([
    new("image:SetupComplete.cmd", "winget install --id Mozilla.Firefox --exact\nrem winget install --id Fake.Comment\necho winget install --id Fake.Echo\nwinget install --id $dynamic\n"),
    new("image:first-login.ps1", "& $winget.Source install --id 'Valve.Steam' --exact\nwinget install --id Mozilla.Firefox --exact"),
    new("media:apps.json", "{\"Sources\":[{\"SourceDetails\":{\"Name\":\"winget\"},\"Packages\":[{\"PackageIdentifier\":\"Discord.Discord\"}]}]}"),
    new("media:install-custom.exe", ""), new("media:broken.json", "invalid")]);
Check(inherited.Apps.Select(a => a.Id).SequenceEqual(["Mozilla.Firefox", "Valve.Steam", "Discord.Discord"]), "source deployment scan identifies literal IDs and deduplicates references");
Check(inherited.ReviewFiles.Length == 5 && inherited.Apps.All(a => a.Evidence.Contains("not confirmed")), "unknown installers and unconfirmed script execution remain visible for review");
Check(InheritedApps.Parse([new("media:setup.cmd", "# winget install --id Fake.Comment\nwinget install --id '../evil'\nwinget install --id $env:APP")]).Apps.Length == 0, "source scanner ignores comments, dynamic IDs and unsafe identifiers");
Check(!Policy.CanRemove(new("Appx", "Microsoft.WindowsStore")), "Store protected");
Check(!Policy.CanRemove(new("Appx", "Microsoft.XboxGamingOverlay")), "Xbox protected");
Check(!Policy.CanRemove(new("Feature", "Windows-Defender")), "Defender protected");
Check(!Policy.CanRemove(new("Feature", "NetFx3")), "gaming framework protected");
Check(!Policy.CanRemove(new("Feature", "Microsoft.BingNews")), "component kind cannot bypass policy");
Check(Policy.CanRemove(new("Appx", "Microsoft.BingNews")), "optional app removable");
Check(Policy.CanRemove(new("Feature", "WorkFolders-Client")), "optional feature removable");
Check(Policy.Ps("D:\\O'Brien\\file.iso") == "'D:\\O''Brien\\file.iso'", "PowerShell path quoting");
Throws(() => Policy.ValidateStorage("exFAT", 1000, 100, true), "non-NTFS servicing workspace rejected");
Throws(() => Policy.ValidateStorage("FAT32", 1000, 100, false), "FAT32 ISO destination rejected");
Throws(() => Policy.ValidateStorage("NTFS", 99, 100, true), "insufficient disk space rejected");
Policy.ValidateStorage("NTFS", 100, 100, true); Check(true, "exact required capacity accepted");
Policy.ValidateStorage("exFAT", 100, 100, false); Check(true, "large-file exFAT output accepted");
var packages = ImageServicing.Packages("DisplayName : Microsoft.BingNews\nPackageName : Microsoft.BingNews_1.0_x64__abc\nDisplayName : Microsoft.WindowsStore\nPackageName : store\nThe operation completed successfully.");
Check(packages.Where(p => p.DisplayName == "Microsoft.BingNews").Select(p => p.PackageName).SequenceEqual(["Microsoft.BingNews_1.0_x64__abc"]), "package identity cannot cross native record boundaries");
Check(!packages.Any(p => p.DisplayName == "Microsoft.People"), "missing component is explicit no-op");
var nativePackages = ImageServicing.Packages("DisplayName : Microsoft.BingNews\r\nVersion : 1.0\r\nPackageName : Microsoft.BingNews_1.0_x64__abc\r\nThe operation completed successfully.");
Check(nativePackages is [{ DisplayName: "Microsoft.BingNews", PackageName: "Microsoft.BingNews_1.0_x64__abc" }], "native DISM inventory preserves exact package identity");
Check(ImageServicing.Features("Feature Name : WorkFolders-Client\nState : Disabled\nThe operation completed successfully.") is [{ FeatureName: "WorkFolders-Client", State: "Disabled" }], "native DISM feature state parsed");
foreach (var malformed in new[] { "DisplayName : Incomplete\nThe operation completed successfully.", "PackageName : orphan\nThe operation completed successfully.", "DisplayName : bad\nPackageName : id\n" })
{
    try { ImageServicing.Packages(malformed); throw new Exception("Expected incomplete inventory rejection"); }
    catch (InvalidDataException) { Check(true, "incomplete native inventory rejected"); }
}
var root = Path.Combine(Path.GetTempPath(), "TechLoungeOS-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var iso = Path.Combine(root, "source.iso"); File.WriteAllText(iso, "fixture");
    var tool = Path.Combine(root, "oscdimg.exe"); File.WriteAllText(tool, "fixture, never executed");
    var options = new BuildOptions(iso, 1, Path.Combine(root, "work"), Path.Combine(root, "out.iso"), tool, [], [], true, false, null) { IncludeEdge = false, IncludeRuntimePack = false, IncludeStoreXbox = false, IncludeOffice = false, IncludeNvidiaSetup = false };
    Policy.Validate(options); Check(true, "valid new build destinations");
    await SetupChecks.Run(root, options, Check);
    await UsbChecks.Run(root,Check);
    await UpdateChecks.Run(root,Check);
    PeBackgroundChecks.Run(root,Check);
    PeStartupChecks.Run(root,Check);
    PeImageChecks.Run(root,Check);
    await IsoVerificationChecks.Run(root,Check);
    await SetupCompatibilityChecks.Run(root,Check);
    await OfficeChecks.Run(root, Check);
    StoreDownloadChecks.Run(Check);
    await StoreChecks.Run(root, Check);
    await EdgeChecks.Run(root, Check);
    await PreservationChecks.Run(root, options, Check);
    await LaunchChecks.Run(root, Check);
    await WallpaperChecks.Run(root, options, Check);
    Policy.Validate(options with { Apps = ["Mozilla.Firefox"] }); Check(true, "searched app ID passes build validation");
    Throws(() => Policy.Validate(options with { Apps = ["Mozilla.Firefox", "mozilla.firefox"] }), "duplicate app IDs cannot create duplicate provisioning steps");
    Throws(() => Policy.Validate(options with { Output = "" }), "blank output rejected before servicing");
    Throws(() => Policy.Validate(options with { Workspace = "relative-workspace" }), "ambiguous relative workspace rejected");
    Throws(() => Policy.Validate(options with { Workspace = @"\\server\share\work" }), "network servicing workspace rejected");
    Throws(() => Policy.Validate(options with { Workspace = Path.Combine(root, "work#boot") }), "boot-entry delimiter path rejected");
    using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            Throws(() => Preflight.Check(options), "real non-elevated host gets actionable preflight rejection");
    Throws(() => Policy.Validate(options with { Remove = [new("Feature", "Windows-Defender")] }), "unsafe servicing rejected");
    Throws(() => Policy.Validate(options with { Apps = ["arbitrary;command"] }), "unknown app rejected");
    Throws(() => Policy.Validate(options with { Output = iso }), "source overwrite rejected");
    Throws(() => Policy.Validate(options with { Output = iso + ":export.iso" }), "alternate stream output cannot modify the source file");
    Throws(() => Policy.Validate(options with { Output = Path.Combine(options.Workspace, "out.iso") }), "nested output rejected");
    var payload = Path.Combine(root, "release"); Directory.CreateDirectory(payload);
    File.WriteAllText(Path.Combine(payload, "TechLoungeTweaks.exe"), "fixture executable, never launched");
    var payloadBefore = File.ReadAllBytes(Path.Combine(payload, "TechLoungeTweaks.exe"));
    Provisioning.ValidatePayload(payload, CancellationToken.None);
    Check(File.ReadAllBytes(Path.Combine(payload, "TechLoungeTweaks.exe")).SequenceEqual(payloadBefore), "payload preflight reads without modifying or launching the supplied app");
    var blockedOptions = options with { TweaksFolder = payload, Workspace = Path.Combine(root, "unreadable-payload") };
    var blockedFixture = new FixtureRunner(blockedOptions.Workspace);
    using (var locked = new FileStream(Path.Combine(payload, "TechLoungeTweaks.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
        try { await new BuildEngine(_ => { }, _ => blockedFixture).Build(blockedOptions, CancellationToken.None); throw new Exception("Expected unreadable payload rejection"); }
        catch (IOException ex) when (ex.Message.Contains("Cannot read the TechLoungeTweaks bundle file"))
        { Check(blockedFixture.Scripts.Count == 0 && blockedFixture.Commands.Count == 0 && !Directory.Exists(blockedOptions.Workspace), "unreadable integration fails before ISO import, downloads, workspace creation or servicing"); }
    }
    Throws(() => Policy.Validate(options with { Workspace = Path.Combine(payload, "work"), TweaksFolder = payload }), "recursive payload workspace rejected");
    Directory.CreateDirectory(options.Workspace);
    Throws(() => Policy.Validate(options), "existing workspace rejected");
    var mount = Path.Combine(root, "fixture-mount"); Directory.CreateDirectory(mount);
    Provisioning.Stage(mount, options with { Apps = ["Valve.Steam"], TweaksFolder = payload });
    Check(File.ReadAllText(Path.Combine(mount, @"ProgramData\TechLoungeOS\TechLoungeTweaks\TechLoungeTweaks.exe")) == "fixture executable, never launched", "TechLoungeTweaks payload copied intact");
    Check(File.Exists(Path.Combine(mount, @"ProgramData\TechLoungeOS\payload-sha256.json")), "provisioning staged with hashes");
    Check(!Provisioning.HasImageRuntime(mount) && !Provisioning.HasPrivateRuntime(payload), "missing runtime files are not inferred from registration or folder names");
    var privateRuntime = Path.Combine(payload, "WebView2Runtime"); Directory.CreateDirectory(privateRuntime);
    File.WriteAllText(Path.Combine(privateRuntime, "msedgewebview2.exe"), "inert runtime fixture");
    try { Provisioning.HasPrivateRuntime(payload); throw new Exception("Expected incomplete runtime rejection"); }
    catch (IOException) { Check(true, "incomplete private runtime is rejected"); }
    foreach (var name in new[] { "msedge.dll", "icudtl.dat", "resources.pak" }) File.WriteAllText(Path.Combine(privateRuntime, name), "fixture");
    var privateMount = Path.Combine(root, "private runtime O'Brien 测试"); Directory.CreateDirectory(privateMount);
    Provisioning.Stage(privateMount, options with { TweaksFolder = payload });
    var privateFolder = Path.Combine(privateMount, @"ProgramData\TechLoungeOS");
    var launcher = Path.Combine(privateFolder, "launch-tweaks.ps1");
    Check(File.Exists(launcher) && File.ReadAllText(Path.Combine(privateFolder, @"WebView2Runtime\msedgewebview2.exe")) == "inert runtime fixture" && !Directory.Exists(Path.Combine(privateFolder,@"TechLoungeTweaks\WebView2Runtime")),
        "complete private runtime is copied beside unchanged Tweaks and included in its launcher payload");
    var launchRunner = new ProcessRunner(_ => { });
    var launchResult = await launchRunner.PowerShell("function Start-Process { param($FilePath,$WorkingDirectory,$ArgumentList) [pscustomobject]@{exe=$FilePath;folder=$WorkingDirectory;arguments=$ArgumentList}|ConvertTo-Json -Compress }; & " + Policy.Ps(launcher));
    using (var launchJson = System.Text.Json.JsonDocument.Parse(launchResult.Trim()))
        Check(launchJson.RootElement.GetProperty("exe").GetString() == Path.Combine(privateFolder,"FirstLogin.exe") &&
            launchJson.RootElement.GetProperty("arguments").GetString() == "--open-tweaks" && launchJson.RootElement.GetProperty("folder").GetString()==privateFolder,
            "legacy script forwards the explicit Tweaks click to the same elevation-aware GUI launcher without launching fixture executables");
    var runtimeVersion = Path.Combine(mount, @"Program Files (x86)\Microsoft\EdgeWebView\Application\145.0"); Directory.CreateDirectory(runtimeVersion);
    Check(!Provisioning.HasImageRuntime(mount), "a runtime version folder without its executable is rejected");
    File.WriteAllText(Path.Combine(runtimeVersion, "msedgewebview2.exe"), "fixture");
    Check(Provisioning.HasImageRuntime(mount), "runtime executable at a supported image path is detected");
    var script = File.ReadAllText(Path.Combine(mount, @"ProgramData\TechLoungeOS\first-login.ps1"));
    Check(script.Contains("AutoGameModeEnabled -Value 1") && script.Contains("Install-WinGetApp 'Valve.Steam'"), "selected settings and app staged");
    Check(!script.Contains("pause_windows_updates") && !script.Contains("Apply Recommended") && System.Text.RegularExpressions.Regex.Matches(script,"Start-Process").Count==2 && script.Contains("Start-Process -FilePath $helper") && script.Contains("Start-Process -FilePath 'ms-settings:signinoptions'"), "no bulk Tweaks preset or implicit Tweaks launch; process targets are the selected wallpaper helper and conditional Hello invitation");
    Check(script.Contains("Invoke-Step 'Valve.Steam'") && script.Contains("$lock.Dispose()"), "per-step retries and exclusive state lock present");
    var runner = new ProcessRunner(_ => { });
    var parsed = await runner.PowerShell("$tokens=$null; $errors=$null; [System.Management.Automation.Language.Parser]::ParseFile(" + Policy.Ps(Path.Combine(mount,@"ProgramData\TechLoungeOS\first-login.ps1")) + ", [ref]$tokens, [ref]$errors) | Out-Null; if ($errors.Count) { throw ($errors | Out-String) }; 'parsed'");
    Check(parsed.Trim() == "parsed", "generated provisioning parses in Windows PowerShell 5.1");
    var status = await runner.PowerShell("[pscustomobject]@{ answer = 42 } | ConvertTo-Json -Compress");
    Check(status.Trim() == "{\"answer\":42}", "real process output captured");
    await ProcessOutputChecks.Run(root,Check);
    await MicrosoftIsoChecks.Run(root,Check);
    await IsoToolChecks.Run(root,Check);
    try { await runner.PowerShell("throw 'Synthetic servicing error'"); throw new Exception("Expected PowerShell failure"); }
    catch (IOException ex) { Check(ex.Message.Contains("Synthetic servicing error") && !ex.Message.Contains("#< CLIXML"), "PowerShell failures are readable without encoded error markup"); }
    var drained = Path.Combine(root, "drained.txt");
    try
    {
        await new ProcessRunner(_ => throw new IOException("Synthetic log storage failure")).PowerShell(
            "1..2000 | ForEach-Object { 'line of synthetic output ' + $_ }; Set-Content -LiteralPath " + Policy.Ps(drained) + " -Value 'finished'");
        throw new Exception("Expected logging failure");
    }
    catch (IOException ex) when (ex.InnerException?.Message == "Synthetic log storage failure")
    {
        Check(File.ReadAllText(drained).Trim() == "finished", "failed log storage does not block subprocess output or completion");
    }
    var success = options with { Apps = ["Mozilla.Firefox"], Index = 2, Workspace = Path.Combine(root, "successful-build"), Remove = [new("Appx", "Microsoft.BingNews"), new("Feature", "WorkFolders-Client")] };
    var watched = success with { Workspace = Path.Combine(root, "journal-reader"), Output = Path.Combine(root, "journal-reader.iso") };
    FileStream? journalReader = null;
    try
    {
        var watchedRunner = new FixtureRunner(watched.Workspace, afterDism: a => {
            if (a.Contains("/Export-Image")) journalReader = new FileStream(Path.Combine(watched.Workspace, "state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        });
        var progress = new RecordingBuildProgress();
        await new BuildEngine(_ => { }, _ => watchedRunner, _ => new Dictionary<string, string>(), progress).Build(watched, CancellationToken.None);
        using var snapshot = new StreamReader(journalReader!, leaveOpen: true);
        using var before = System.Text.Json.JsonDocument.Parse(snapshot.ReadToEnd());
        using var after = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(watched.Workspace, "state.json")));
        Check(before.RootElement.GetProperty("phase").GetString() == "Exporting selected edition" && after.RootElement.GetProperty("phase").GetString() == "Complete" && File.ReadAllText(watched.Output) == "fixture ISO", "an external delete-sharing journal reader retains its snapshot while the build atomically reaches Complete");
        Check(progress.Phases[0] == "Preparing build" && progress.Phases[^1] == "Complete" && progress.Phases.IndexOf("Committing") >= 0 && progress.Phases.IndexOf("Committing") < progress.Phases.IndexOf("Creating ISO") && progress.Phases.Contains("Servicing"), "the public build reports its actual saving/export stages through completion");
    }
    finally { journalReader?.Dispose(); }
    var journalFailure = success with { Workspace = Path.Combine(root, "journal-failure"), Output = Path.Combine(root, "journal-failure.iso") };
    FileStream? blockedJournal = null;
    try
    {
        var brokenJournal = new FixtureRunner(journalFailure.Workspace, failRemoval: true, afterDism: a => {
            if (a.Contains("/ScanHealth")) blockedJournal = new FileStream(Path.Combine(journalFailure.Workspace, "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        });
        try { await new BuildEngine(_ => { }, _ => brokenJournal, _ => new Dictionary<string, string>()).Build(journalFailure, CancellationToken.None); throw new Exception("Expected removal failure"); }
        catch (IOException ex) when (ex.Message == "Injected removal failure")
        {
            var log = File.ReadAllText(Path.Combine(journalFailure.Workspace, "build.log"));
            Check(log.Contains("Injected removal failure") && log.Contains("Could not record failure in build journal") && brokenJournal.Commands.Any(c => c.Contains("/Discard")) && !File.Exists(journalFailure.Output), "an unreadable failure journal does not mask the original servicing error or prevent discard");
        }
    }
    finally { blockedJournal?.Dispose(); }
    var fixture = new FixtureRunner(success.Workspace);
    var cancelMount=options with{Index=2,Workspace=Path.Combine(root,"cancel-after-mount"),Output=Path.Combine(root,"cancel-after-mount.iso"),Remove=[]};
    using(var cancelSource=new CancellationTokenSource())
    {
        var cancelRunner=new FixtureRunner(cancelMount.Workspace,afterDism:a=>{if(a.Contains("/Mount-Image"))cancelSource.Cancel();});
        try{await new BuildEngine(_=>{},_=>cancelRunner,_=>new Dictionary<string,string>()).Build(cancelMount,cancelSource.Token);throw new Exception("Expected cancellation after mount");}
        catch(OperationCanceledException){Check(cancelRunner.Commands.Any(c=>c.Contains("/Discard"))&&!cancelRunner.Commands.Any(c=>c.Contains("/Get-ProvisionedAppxPackages"))&&!File.Exists(cancelMount.Output)&&!Directory.Exists(Path.Combine(cancelMount.Workspace,@"mount\ProgramData\TechLoungeOS")),"cancellation during mounting stops at that operation boundary before inventory/provisioning and discards its private mount");}
    }
    foreach(var scanToCancel in new[]{1,2})
    {
        using var cancelledScan=new CancellationTokenSource();var scans=0;var reads=0;
        var cancelledOptions=cancelMount with{Workspace=Path.Combine(root,"cancel-scan-"+scanToCancel),Output=Path.Combine(root,"cancel-scan-"+scanToCancel+".iso")};
        var cancelledRunner=new FixtureRunner(cancelledOptions.Workspace,afterDism:a=>{if(a.Contains("/ScanHealth")&&++scans==scanToCancel)cancelledScan.Cancel();});
        try{await new BuildEngine(_=>{},_=>cancelledRunner,_=>{reads++;return new Dictionary<string,string>();}).Build(cancelledOptions,cancelledScan.Token);throw new Exception("Expected scan cancellation");}
        catch(OperationCanceledException){Check(scans==scanToCancel&&reads==scanToCancel-1&&cancelledRunner.Commands.Any(c=>c.Contains("/Discard"))&&!cancelledRunner.Commands.Any(c=>c.Contains("/Commit"))&&!File.Exists(cancelledOptions.Output),"cancellation after health scan "+scanToCancel+" skips subsequent configuration reads/commit and discards the private image");}
    }
    var noFramework=options with{Index=2,Workspace=Path.Combine(root,"missing-framework"),Output=Path.Combine(root,"missing-framework.iso")};
    var noFrameworkRunner=new FixtureRunner(noFramework.Workspace,missingFramework:true);
    try{await new BuildEngine(_=>{},_=>noFrameworkRunner,_=>new Dictionary<string,string>()).Build(noFramework,CancellationToken.None);throw new Exception("Expected missing Framework rejection");}
    catch(IOException ex)when(ex.Message.Contains("Quiet first-login setup requires")){
        Check(!File.Exists(noFramework.Output)&&noFrameworkRunner.Commands.Any(c=>c.Contains("/Discard"))&&!noFrameworkRunner.Commands.Any(c=>c.Contains("/Get-ProvisionedAppxPackages")),"a source missing the quiet launcher's Windows Framework prerequisite fails before removals and discards its private mount");
    }
    await new BuildEngine(_ => { }, _ => fixture, _ => new Dictionary<string, string>()).Build(success, CancellationToken.None);
    Check(File.ReadAllText(success.Output) == "fixture ISO", "fixture pipeline publishes output");
    Check(File.ReadAllText(Path.Combine(success.Workspace, "mount", "ProgramData", "TechLoungeOS", "first-login.ps1")).Contains("Mozilla.Firefox"), "searched app survives full servicing pipeline and reaches provisioning");
    var recommendedBuild = options with { Index = 2, TweaksFolder = payload, Workspace = Path.Combine(root,"recommended-build"), Output = Path.Combine(root,"recommended.iso") };
    var recommendedFixture = new FixtureRunner(recommendedBuild.Workspace);
    await new BuildEngine(_ => { }, _ => recommendedFixture, _ => new Dictionary<string,string>()).Build(recommendedBuild, CancellationToken.None);
    var recommendedMount = Path.Combine(recommendedBuild.Workspace,"mount");
    Check(File.Exists(Path.Combine(recommendedMount,@"ProgramData\TechLoungeOS\recommended-profile.json")) &&
        File.ReadAllText(SetupHooks.ImageHook(recommendedMount)).Contains("recommended-machine.ps1") &&
        File.ReadAllText(Path.Combine(recommendedMount,@"ProgramData\TechLoungeOS\first-login.ps1")).Contains("Invoke-Step 'recommended-user'") &&
        File.ReadAllText(recommendedBuild.Output) == "fixture ISO",
        "full fixture export stages both Recommended phases when only Tweaks is selected, without requiring Office, runtimes or Store setup");
    Check(fixture.Commands.Any(c => c.Contains("/SourceIndex:2")) && fixture.Commands.Any(c => c.Contains("/Index:1")), "selected edition exported and remapped to index one");
    Check(fixture.Commands.Any(c => c.Contains("/PackageName:Microsoft.BingNews_1.0_x64__abc")), "exact inventory package serviced");
    Check(fixture.Commands.Any(c => c.Contains("/FeatureName:WorkFolders-Client")), "selected optional feature serviced");
    Check(fixture.Commands.Any(c => c.Contains("/Commit")) && !fixture.Commands.Any(c => c.Contains("/Discard")), "successful fixture commits once without discard");
    Check(fixture.Commands[^1].Any(a => a.StartsWith("-bootdata:2#p0,e,b") && a.Contains("#pEF,e,b")), "both BIOS and UEFI boot entries exported");
    Check(File.ReadAllText(iso) == "fixture", "pipeline leaves original ISO unchanged");
    var failure = success with { Workspace = Path.Combine(root, "failed-build"), Output = Path.Combine(root, "failed.iso") };
    var broken = new FixtureRunner(failure.Workspace, true);
    try { await new BuildEngine(_ => { }, _ => broken, _ => new Dictionary<string, string>()).Build(failure, CancellationToken.None); throw new Exception("Expected failure"); }
    catch (IOException ex) when (ex.Message == "Injected removal failure") { Check(true, "servicing failure propagates"); }
    Check(broken.Commands.Any(c => c.Contains("/Discard")) && !broken.Commands.Any(c => c.Contains("/Commit")), "failed fixture discards without commit");
    Check(!File.Exists(failure.Output) && File.ReadAllText(Path.Combine(failure.Workspace, "state.json")).Contains("Failed:"), "failure leaves journal and no output ISO");
    var inventoryFailure = failure with { Workspace = Path.Combine(root, "broken-inventory") };
    var noInventory = new FixtureRunner(inventoryFailure.Workspace, failInventory: true);
    try { await new BuildEngine(_ => { }, _ => noInventory, _ => new Dictionary<string, string>()).Build(inventoryFailure, CancellationToken.None); throw new Exception("Expected inventory failure"); }
    catch (IOException ex) when (ex.Message.StartsWith("Offline Windows inventory failed"))
    {
        Check(noInventory.Commands.Any(c => c.Contains("/Discard")) && !noInventory.Commands.Any(c => c.Contains("/Remove-ProvisionedAppxPackage")), "real inventory failure regression discards before any removals");
    }
    var postFailure = failure with { Workspace = Path.Combine(root, "post-inventory-failure") };
    var postInventory = new FixtureRunner(postFailure.Workspace, failPostFeatureInventory: true);
    try { await new BuildEngine(_ => { }, _ => postInventory, _ => new Dictionary<string, string>()).Build(postFailure, CancellationToken.None); throw new Exception("Expected post-servicing inventory failure"); }
    catch (IOException ex) when (ex.Message == "Post-servicing feature inventory failed (87)")
    {
        Check(postInventory.Commands.Any(c => c.Contains("/Remove-ProvisionedAppxPackage")) && postInventory.Commands.Any(c => c.Contains("/Discard"))
            && !postInventory.Commands.Any(c => c.Contains("/Commit")) && !File.Exists(postFailure.Output), "post-servicing inventory failure discards changes and publishes no ISO");
    }
    var ineffective = failure with { Workspace = Path.Combine(root, "ineffective-removal") };
    var unchanged = new FixtureRunner(ineffective.Workspace, ignoreRemoval: true);
    try { await new BuildEngine(_ => { }, _ => unchanged, _ => new Dictionary<string, string>()).Build(ineffective, CancellationToken.None); throw new Exception("Expected unchanged inventory rejection"); }
    catch (IOException ex) when (ex.Message.StartsWith("Selected app is still provisioned:"))
    {
        Check(unchanged.Commands.Any(c => c.Contains("/Discard")) && !unchanged.Commands.Any(c => c.Contains("/Commit")), "successful DISM exit cannot hide an ineffective removal");
    }
    var cancel = success with { Workspace = Path.Combine(root, "cancelled-build"), Output = Path.Combine(root, "cancelled.iso") };
    var unsupportedPe=cancel with{Workspace=Path.Combine(root,"unsupported-pe"),AnimatedWinPe=true};
    var peFixture=new FixtureRunner(unsupportedPe.Workspace);
    try{await new BuildEngine(_=>{},_=>peFixture,_=>new Dictionary<string,string>()).Build(unsupportedPe,CancellationToken.None);throw new Exception("Expected unavailable PE payload rejection");}
    catch(DirectoryNotFoundException)
    {
        Check(peFixture.Commands.Any(c=>c.Contains("/Discard"))&&!peFixture.Commands.Any(c=>c.Contains("/Remove-ProvisionedAppxPackage")||c.Contains("/Commit"))&&!File.Exists(unsupportedPe.Output)&&File.ReadAllText(Path.Combine(unsupportedPe.Workspace,@"media\sources\boot.wim"))=="fixture","missing source regional prerequisites discard the private Windows mount before removals and preserve copied boot media");
    }
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await new BuildEngine(_ => { }, _ => new FixtureRunner(cancel.Workspace), _ => new Dictionary<string, string>()).Build(cancel, cancelled.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(!Directory.Exists(cancel.Workspace), "early cancellation creates no workspace"); }
    using var midCancel = new CancellationTokenSource();
    var mid = cancel with { Workspace = Path.Combine(root, "mid-cancel") };
    var midFixture = new FixtureRunner(mid.Workspace, afterDism: a => { if (a.Contains("/Mount-Image")) midCancel.Cancel(); });
    try { await new BuildEngine(_ => { }, _ => midFixture, _ => new Dictionary<string, string>()).Build(mid, midCancel.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(midFixture.Commands.Any(c => c.Contains("/Discard")) && !File.Exists(mid.Output), "cancellation after mount discards and publishes nothing"); }
    var failedExport = cancel with { Workspace = Path.Combine(root, "failed-export") };
    var exportFixture = new FixtureRunner(failedExport.Workspace, failExport: true);
    try { await new BuildEngine(_ => { }, _ => exportFixture, _ => new Dictionary<string, string>()).Build(failedExport, CancellationToken.None); throw new Exception("Expected ISO failure"); }
    catch (IOException ex) when (ex.Message == "Injected ISO failure") { Check(!File.Exists(failedExport.Output) && !Directory.EnumerateFiles(failedExport.Workspace, "*.partial*",SearchOption.AllDirectories).Any(), "export failure removes partial ISO and manifest from its own workspace"); }
    foreach (var generated in fixture.Scripts)
    {
        await runner.PowerShell("$t=$null;$e=$null;[System.Management.Automation.Language.Parser]::ParseInput(" + Policy.Ps(generated) + ",[ref]$t,[ref]$e)|Out-Null; if($e.Count){throw ($e|Out-String)}");
    }
    Check(true, "all generated pipeline scripts parse in Windows PowerShell 5.1");
    await ProvisioningChecks.Run(root, Check);
    SetupAnswerChecks.Run(Check);
    SetupRegionalChecks.Run(root,Check);
    TweaksInstallerChecks.Run(root,Check);
    await AppSelectionChecks.Run(root,Check);
    await WindowsRecoveryChecks.Run(root,Check);
    await DesktopChecks.Run(root,Check);
    await DesktopSurfaceChecks.Run(root,Check);
    await InputChecks.Run(root,Check);
    await HelloChecks.Run(root,Check);
    await QuietAppChecks.Run(root,Check);
    await DiscordInstallChecks.Run(root,Check);
    await HwInfoChecks.Run(root,Check);
    await BootOptimizerChecks.Run(root,Check);
    await StandaloneChecks.Run(root,Check);
    await LegacyNetworkChecks.Run(root,Check);
    DefaultPolicyChecks.Run(root,Check);
    await RecommendedChecks.Run(root, Check);
    await NvidiaChecks.Run(root, Check);
    await GraphicsChecks.Run(root, Check);
    await QuietLaunchChecks.Run(root, Check);
    await WindowsImageUpdateChecks.Run(root, Check);
await WindowsMigrationChecks.Run(Check);
    TaskCacheIdentityChecks.Run(Check);
    WindowsUpdateMetadataChecks.Run(root,Check);
    WindowsMetadataMigrationChecks.Run(root,Check);
    await WindowsUpdateDownloadChecks.Run(root, Check);
    await PowerDefaultsChecks.Run(root, Check);
    await DesktopSetupChecks.Run(root, Check);
    await RestartNoticeChecks.Run(root,Check);
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"{count} tests passed. No host servicing performed.");
