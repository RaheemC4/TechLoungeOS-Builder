using System.Text;
using System.Text.Json;
using TechLoungeOS;

// Explicitly owned test-image revision. The user's source ISO is never mounted here.
internal static class TweaksRevision
{
    internal static async Task Run(string sourceIso,string workspace,string destination,string report,string launcher,Action<string> log)
    {
        if(sourceIso!=@"S:\Ghost-full-780.iso"||workspace!=@"S:\ghost-full-740"||destination!=@"S:\Ghost-full-807.iso"||report!=@"C:\TLOS\tweaks-revision807.json")throw new IOException("Only the owned780/807 transaction is permitted.");
        foreach(var path in new[]{sourceIso,workspace,destination,report})Policy.RequireUnlinkedPath(path);
        if(File.Exists(destination)||File.Exists(report))throw new IOException("Existing revision807 requires review.");
        using var audit=JsonDocument.Parse(File.ReadAllText(@"C:\TLOS\export-readback-780.json"));
        if(!audit.RootElement.GetProperty("configurationPreserved").GetBoolean()||audit.RootElement.GetProperty("sourceSha256").GetString()!="BCBD791EAB4F5BEA01F0AA53322A173EACCB49E37CFCD7852F6325D9FDBFD8A8"||BootableDrive.Hash(sourceIso)!="BCBD791EAB4F5BEA01F0AA53322A173EACCB49E37CFCD7852F6325D9FDBFD8A8")throw new IOException("Completed780 ISO/readback required.");
        var launcherFile=Path.Combine(AppContext.BaseDirectory,"FirstLogin.exe");
        if(BootableDrive.Hash(launcherFile)!=launcher)throw new IOException("New package launcher differs.");
        using var buildLease=new FileStream(Path.Combine(workspace,"build.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        using var auditLease=new FileStream(Path.Combine(workspace,"export-readback.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        var revision=Path.Combine(workspace,"tweaks-revision807");
        if(Directory.Exists(revision))throw new IOException("Existing807 transaction requires recovery/review.");
        Directory.CreateDirectory(revision);
        var release=TweaksInstaller.ParseRelease(File.ReadAllText(@"E:\tweaks-release.json"));
        if(release.Version!="2026.10.23.174632"||release.Sha256!="7f42269fa18526ee14ff4f804bc3836dc57fd2e1575d10bdb2a7e80e6101a982")throw new IOException("Only the independently tested installer is permitted in this revision.");
        var prepared=Path.Combine(revision,"installer");var package=TweaksInstaller.Extract(@"E:\TechLoungeTweaks.zip",prepared,release);
        using var regionDoc=JsonDocument.Parse(File.ReadAllText(@"C:\TLOS\regional-revision780.json"));
        var regional=regionDoc.RootElement.GetProperty("regional").Deserialize<SetupRegional>()??throw new IOException("Missing780 regional choices.");
        var media=Path.Combine(workspace,"media");var windows=Path.Combine(media,@"sources\install.wim");var boot=Path.Combine(media,@"sources\boot.wim");
        if(BootableDrive.Hash(windows)!="F2743FB1AAC2C49D72B3F4C127C311CB74AB620B986275371399276DB4D7E5F8")throw new IOException("Unexpected780 Windows image.");
        var bootSha=BootableDrive.Hash(boot);var candidate=Path.Combine(revision,"candidate-boot.wim");File.Copy(boot,candidate,false);File.SetAttributes(candidate,FileAttributes.Normal);
        var run=new ProcessRunner(log,Path.Combine(revision,"scratch"));
        var pe=Path.Combine(revision,"pe");Directory.CreateDirectory(pe);var peMounted=false;
        try
        {
            peMounted=true;await run.Dism("/Mount-Image","/ImageFile:"+candidate,"/Index:2","/MountDir:"+pe,"/CheckIntegrity");
            var keyboard=WinPeBackground.KeyboardForImage(pe,regional)??throw new IOException("Explicit807 keyboard required.");
            var before=WinPeBackground.Snapshot(pe,CancellationToken.None);var services=SourcePreservation.CaptureServices(pe);
            const string shell=@"Windows\System32\winpeshl.ini";
            var path=Path.Combine(pe,shell);var text=Encoding.Latin1.GetString(File.ReadAllBytes(path));
            if(text.Split(WinPeBackground.LaunchLine).Length!=2||text.Contains("SetKeyboardLayout",StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected existing PE shell integration.");
            text=text.Replace(WinPeBackground.LaunchLine,"%SYSTEMROOT%\\System32\\wpeutil.exe, SetKeyboardLayout "+keyboard+"\r\n"+WinPeBackground.LaunchLine,StringComparison.Ordinal);
            File.SetAttributes(path,FileAttributes.Normal);File.WriteAllBytes(path,Encoding.Latin1.GetBytes(text));
            var after=WinPeBackground.Snapshot(pe,CancellationToken.None);
            if(before.Count!=after.Count||before.Any(p=>!after.TryGetValue(p.Key,out var hash)||(!p.Key.Equals(shell,StringComparison.OrdinalIgnoreCase)&&hash!=p.Value)))throw new IOException("An unrelated inherited PE tool/file changed.");
            SourcePreservation.VerifyServices(services,SourcePreservation.CaptureServices(pe));
            File.WriteAllText(Path.Combine(revision,"pe-preservation.json"),JsonSerializer.Serialize(new{before,after,changed=shell,keyboard,servicesPreserved=services.Count}));
            await run.Dism("/Unmount-Image","/MountDir:"+pe,"/Commit","/CheckIntegrity");peMounted=false;
        }
        finally{if(peMounted)try{await run.Dism("/Unmount-Image","/MountDir:"+pe,"/Discard");}catch(Exception ex){log("PE recovery required: "+ex.Message);}}
        var mount=Path.Combine(revision,"windows");Directory.CreateDirectory(mount);var mounted=false;
        try
        {
            mounted=true;await run.Dism("/Mount-Image","/ImageFile:"+windows,"/Index:1","/MountDir:"+mount,"/CheckIntegrity");
            var before=SourcePreservation.CaptureConfiguration(mount);var integration=Path.Combine(mount,@"ProgramData\TechLoungeOS");
            var manifest=Path.Combine(integration,"payload-sha256.json");
            var hashes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest))??throw new IOException("Missing owned integration manifest.");
            var legacy=Path.Combine(integration,"TechLoungeTweaks");
            // Retire only the Builder's prior integration copy, never the source
            // release. Check ownership by its exact manifest inventory, without
            // attempting to restore/read files rejected by antivirus.
            var ownedFiles=new List<string>();
            void Visit(string directory)
            {
                Policy.RequireUnlinkedPath(directory);
                foreach(var path in Directory.EnumerateFiles(directory)){Policy.RequireUnlinkedPath(path);ownedFiles.Add(path);}
                foreach(var path in Directory.EnumerateDirectories(directory))Visit(path);
            }
            Visit(legacy);var files=ownedFiles.ToArray();
            var keys=hashes.Keys.Where(k=>k.StartsWith(@"TechLoungeTweaks\",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(!files.Select(p=>Path.GetRelativePath(integration,p)).Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(keys.Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))throw new IOException("Prior integration inventory differs; review before replacement.");
            foreach(var path in files){Policy.RequireUnlinkedPath(path);File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly);}
            Directory.Delete(legacy,true);foreach(var key in keys)hashes.Remove(key);
            var wrapper=Path.Combine(integration,"launch-tweaks.ps1");File.SetAttributes(wrapper,FileAttributes.Normal);File.Delete(wrapper);hashes.Remove("launch-tweaks.ps1");
            if(!File.Exists(Path.Combine(integration,@"WebView2Runtime\msedgewebview2.exe")))throw new IOException("Stable private runtime missing.");
            Provisioning.CopyTree(prepared,Path.Combine(integration,"TweaksInstaller"));
            foreach(var path in Directory.EnumerateFiles(Path.Combine(integration,"TweaksInstaller")))hashes.Add(Path.GetRelativePath(integration,path),BootableDrive.Hash(path));
            using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state.json")));
            var options=state.RootElement.GetProperty("options").Deserialize<BuildOptions>()??throw new IOException("Original build choices missing.");
            var script=Path.Combine(integration,"first-login.ps1");
            if(BootableDrive.Hash(script)!=hashes["first-login.ps1"])throw new IOException("Prior first-login script differs from its verified payload.");
            if(options.WallpaperFile!=null)throw new IOException("807 expects the previously verified preset wallpaper.");
            var updated=Provisioning.Script(options.Apps,options.GameMode,options.DisableSuggestions,null,options.IncludeRuntimePack,options.IncludeStoreXbox,options.IncludeOffice,options.TweaksFolder!=null,NvidiaSetup.Enabled(options),options.IncludeAmdIntelGraphics,options.IncludeEdge,true,options.WallpaperPreset,true,true,true);
            File.SetAttributes(script,FileAttributes.Normal);File.WriteAllText(script,updated);hashes["first-login.ps1"]=BootableDrive.Hash(script);
            foreach(var relative in new[]{@"ProgramData\TechLoungeOS\FirstLogin.exe",@"Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\TechLoungeOS.exe"})
            {
                var path=Path.Combine(mount,relative);if(BootableDrive.Hash(path)!="58051DC3D8489C250225B763A9ABEC09F411992F687354086AB24B784A2DB93F")throw new IOException("Unexpected previous integration launcher.");
                File.SetAttributes(path,FileAttributes.Normal);File.WriteAllBytes(path,File.ReadAllBytes(launcherFile));
            }
            hashes["FirstLogin.exe"]=launcher;File.SetAttributes(manifest,FileAttributes.Normal);File.WriteAllText(manifest,JsonSerializer.Serialize(hashes,new JsonSerializerOptions{WriteIndented=true}));
            foreach(var entry in hashes)if(BootableDrive.Hash(Path.Combine(integration,entry.Key))!=entry.Value)throw new IOException("Updated integration integrity check failed: "+entry.Key);
            SourcePreservation.VerifyServices(before,SourcePreservation.CaptureConfiguration(mount));
            File.WriteAllText(Path.Combine(revision,"windows-preservation.json"),JsonSerializer.Serialize(new{configurationPreserved=before.Count,package,launcher,upstreamInstallerUnmodified=true,antivirusExceptionsAdded=false}));
            await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Commit","/CheckIntegrity");mounted=false;
        }
        finally{if(mounted)try{await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Discard");}catch(Exception ex){log("Windows recovery required: "+ex.Message);}}
        if(BootableDrive.Hash(boot)!=bootSha)throw new IOException("PE source changed during the private revision.");
        File.SetAttributes(boot,FileAttributes.Normal);File.Replace(candidate,boot,Path.Combine(revision,"previous-boot.wim"));
        BootableDrive.Check16GbTarget(media,log);await IsoExport.Create(run,media,destination,@"D:\tools\oscdimg.exe",log,CancellationToken.None);
        var verification=@"S:\tweaks-audit807";Directory.CreateDirectory(verification);
        foreach(var name in new[]{"appx-after.json","features-after.json","configuration-staged.json","services-after.json"})File.Copy(Path.Combine(workspace,name),Path.Combine(verification,name),false);
        File.WriteAllText(Path.Combine(verification,"state.json"),JsonSerializer.Serialize(new{phase="Complete",options=new{Output=destination}}));
        var readback=await ExportReadback.Run(destination,verification,launcher,log);File.WriteAllText(@"C:\TLOS\export-readback-807.json",readback);
        using var data=JsonDocument.Parse(readback);File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,sourceIso,destination,package,regional,readback=data.RootElement.Clone(),antivirusExceptionsAdded=false}));
        log("807 installer/keyboard revision exported and independently read back; fresh installation still required.");
    }
}
