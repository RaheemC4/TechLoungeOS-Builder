using System.Text.Json;
using TechLoungeOS;

// A bounded, owned-VM revision of the already audited Windows image.
// Only the two inert WinPE account templates and integration launchers change.
internal static class RegionalRevision
{
    internal static async Task Run(string sourceIso,string workspace,string destination,string report,string launcher,Action<string> log)
    {
        if(sourceIso!=@"S:\Ghost-full-748.iso" || workspace!=@"S:\ghost-full-740" || destination!=@"S:\Ghost-full-780.iso" || report!=@"C:\TLOS\regional-revision780.json" ||
            launcher==null || launcher.Length!=64 || !launcher.All(Uri.IsHexDigit))
            throw new IOException("Only the explicit owned748/780 revision is allowed.");
        var launcherFile=Path.Combine(AppContext.BaseDirectory,"FirstLogin.exe");
        if(BootableDrive.Hash(launcherFile)!=launcher)throw new IOException("Expected launcher differs from this tested package.");
        foreach(var path in new[]{sourceIso,workspace,destination,report})Policy.RequireUnlinkedPath(path);
        if(File.Exists(destination)||File.Exists(report))throw new IOException("Existing regional revision requires review before retrying.");
        using var previous=JsonDocument.Parse(File.ReadAllText(@"C:\TLOS\export-readback-748.json"));
        if(!previous.RootElement.GetProperty("readOnly").GetBoolean() || !previous.RootElement.GetProperty("configurationPreserved").GetBoolean())
            throw new IOException("The completed748 preservation audit is required.");
        using var buildLease=new FileStream(Path.Combine(workspace,"build.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        using var auditLease=new FileStream(Path.Combine(workspace,"export-readback.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        using var isoLease=new FileStream(sourceIso,FileMode.Open,FileAccess.Read,FileShare.Read);
        var originalSha=BootableDrive.Hash(sourceIso);
        if(originalSha!="9A90EF5293C62C4D5D81C553EB0789CFC3CBD13B004310C1C65E0A4643F29420")throw new IOException("Audited748 ISO changed.");
        var media=Path.Combine(workspace,"media");var windows=Path.Combine(media,@"sources\install.wim");
        const string sourceWindowsSha="83FF24AE42478FB61C92B84F80575B54C0F23837DAC4F4E58693404B9764EC01";
        if(BootableDrive.Hash(windows)!=sourceWindowsSha)throw new IOException("The audited Windows image changed before revision.");
        var revision=Path.Combine(workspace,"regional-revision780");var verification=@"S:\regional-audit780";
        if(Directory.Exists(revision)||Directory.Exists(verification))throw new IOException("Existing regional transaction requires recovery/review.");
        Directory.CreateDirectory(revision);
        var run=new ProcessRunner(log,Path.Combine(revision,"scratch"));
        var windowsMount=Path.Combine(revision,"windows-launcher-only");Directory.CreateDirectory(windowsMount);
        SetupRegional regional;var windowsMounted=false;
        try
        {
            windowsMounted=true;
            await run.Dism("/Mount-Image","/ImageFile:"+windows,"/Index:1","/MountDir:"+windowsMount,"/CheckIntegrity");
            var before=SourcePreservation.CaptureConfiguration(windowsMount);
            var tweaksExe=Path.Combine(windowsMount,@"ProgramData\TechLoungeOS\TechLoungeTweaks\TechLoungeTweaks.exe");
            SourcePreservation.RequireImageFile(tweaksExe,1024,256L<<20);var tweaksSha=BootableDrive.Hash(tweaksExe);
            regional=SetupRegional.FromImage(windowsMount,new BuildOptions("unused",1,"unused","unused","unused",[],[],false,false,null)
                {AnimatedWinPe=true,SetupRegion="en-GB",SetupKeyboard="00000809"});
            foreach(var relative in new[]{@"ProgramData\TechLoungeOS\FirstLogin.exe",@"Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\TechLoungeOS.exe"})
            {
                var path=Path.Combine(windowsMount,relative);SourcePreservation.RequireImageFile(path,1024,1<<20);
                if(BootableDrive.Hash(path)!="53C6E3CBD02B015ECCE1927FA21DEC3A557046E15A893B666A13DE8A28FE98AD")throw new IOException("Unexpected existing integration launcher: "+relative);
                File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly);File.WriteAllBytes(path,File.ReadAllBytes(launcherFile));
                if(BootableDrive.Hash(path)!=launcher)throw new IOException("Integration launcher copy failed.");
            }
            var integration=Path.Combine(windowsMount,@"ProgramData\TechLoungeOS");
            var runtimeBefore=Path.Combine(integration,@"TechLoungeTweaks\WebView2Runtime");
            var runtimeAfter=Path.Combine(integration,"WebView2Runtime");
            if(!Provisioning.HasPrivateRuntime(Path.Combine(integration,"TechLoungeTweaks")) || Directory.Exists(runtimeAfter))throw new IOException("Unexpected private runtime layout.");
            var runtimeFiles=Directory.EnumerateFiles(runtimeBefore,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(runtimeBefore,p),BootableDrive.Hash);
            Directory.Move(runtimeBefore,runtimeAfter);
            if(runtimeFiles.Any(p=>BootableDrive.Hash(Path.Combine(runtimeAfter,p.Key))!=p.Value))throw new IOException("Runtime relocation changed its files.");
            var script=Path.Combine(integration,"launch-tweaks.ps1");SourcePreservation.RequireImageFile(script,1,65536);
            File.SetAttributes(script,File.GetAttributes(script)&~FileAttributes.ReadOnly);File.WriteAllText(script,Provisioning.PrivateTweaksLauncher);
            var manifest=Path.Combine(integration,"payload-sha256.json");SourcePreservation.RequireImageFile(manifest,1,4<<20);
            var hashes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest))??throw new IOException("Missing integration manifest.");
            const string oldPrefix=@"TechLoungeTweaks\WebView2Runtime\";
            foreach(var key in hashes.Keys.Where(k=>k.StartsWith(oldPrefix,StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var digest=hashes[key];hashes.Remove(key);hashes.Add(@"WebView2Runtime\"+key[oldPrefix.Length..],digest);
            }
            hashes["FirstLogin.exe"]=launcher;hashes["launch-tweaks.ps1"]=BootableDrive.Hash(script);
            File.SetAttributes(manifest,File.GetAttributes(manifest)&~FileAttributes.ReadOnly);File.WriteAllText(manifest,JsonSerializer.Serialize(hashes,new JsonSerializerOptions{WriteIndented=true}));
            SourcePreservation.VerifyServices(before,SourcePreservation.CaptureConfiguration(windowsMount));
            if(BootableDrive.Hash(tweaksExe)!=tweaksSha)throw new IOException("The original Tweaks executable changed during the integration revision.");
            File.WriteAllText(Path.Combine(revision,"windows-preservation.json"),JsonSerializer.Serialize(new{configurationPreserved=before.Count,launcherSha256=launcher,tweaksSha256=tweaksSha,runtimeFilesPreserved=runtimeFiles.Count}));
            await run.Dism("/Unmount-Image","/MountDir:"+windowsMount,"/Commit","/CheckIntegrity");windowsMounted=false;
        }
        finally
        {
            if(windowsMounted)try{await run.Dism("/Unmount-Image","/MountDir:"+windowsMount,"/Discard");}catch(Exception ex){log("Windows mount needs recovery: "+ex.Message);}
        }
        var windowsSha=BootableDrive.Hash(windows);
        File.WriteAllText(Path.Combine(revision,"regional.json"),JsonSerializer.Serialize(regional));
        var boot=Path.Combine(media,@"sources\boot.wim");var bootSha=BootableDrive.Hash(boot);
        var candidate=Path.Combine(revision,"candidate-boot.wim");File.Copy(boot,candidate,false);File.SetAttributes(candidate,FileAttributes.Normal);
        var mount=Path.Combine(revision,"pe");Directory.CreateDirectory(mount);var mounted=false;
        var allowed=new[]{@"Windows\System32\TechLoungeOS\account-auto.xml",@"Windows\System32\TechLoungeOS\account-manual.xml"};
        try
        {
            mounted=true;await run.Dism("/Mount-Image","/ImageFile:"+candidate,"/Index:2","/MountDir:"+mount,"/CheckIntegrity");
            var before=WinPeBackground.Snapshot(mount,CancellationToken.None);var services=SourcePreservation.CaptureServices(mount);
            foreach(var name in allowed)
            {
                var path=Path.Combine(mount,name);SourcePreservation.RequireImageFile(path,1,2<<20);
                var text=File.ReadAllText(path);
                if(!text.Contains("TLOS_USER_7D91")||!text.Contains("TLOS_PASSWORD_7D91"))throw new IOException("Expected inert account template markers are absent.");
                var updated=SetupAnswerFile.WithRegional(text,regional);
                File.SetAttributes(path,FileAttributes.Normal);File.WriteAllText(path,updated);
            }
            SourcePreservation.VerifyServices(services,SourcePreservation.CaptureServices(mount));
            var after=WinPeBackground.Snapshot(mount,CancellationToken.None);
            if(before.Count!=after.Count || before.Any(p=>!after.TryGetValue(p.Key,out var hash)||(!allowed.Contains(p.Key,StringComparer.OrdinalIgnoreCase)&&p.Value!=hash)))
                throw new IOException("Regional revision changed an unrelated WinPE file.");
            File.WriteAllText(Path.Combine(revision,"pe-preservation.json"),JsonSerializer.Serialize(new{before,after,allowed,servicesPreserved=services.Count}));
            await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Commit","/CheckIntegrity");mounted=false;
        }
        finally
        {
            if(mounted)try{await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Discard");}catch(Exception ex){log("PE mount needs recovery: "+ex.Message);}
        }
        if(BootableDrive.Hash(boot)!=bootSha||BootableDrive.Hash(windows)!=windowsSha)throw new IOException("A source image changed during the private revision.");
        File.SetAttributes(boot,File.GetAttributes(boot)&~FileAttributes.ReadOnly);
        File.Replace(candidate,boot,Path.Combine(revision,"previous-boot.wim"));
        BootableDrive.Check16GbTarget(media,log);
        await IsoExport.Create(run,media,destination,@"D:\tools\oscdimg.exe",log,CancellationToken.None);
        Directory.CreateDirectory(verification);
        foreach(var name in new[]{"appx-after.json","features-after.json","configuration-staged.json","services-after.json"})File.Copy(Path.Combine(workspace,name),Path.Combine(verification,name),false);
        File.WriteAllText(Path.Combine(verification,"state.json"),JsonSerializer.Serialize(new{phase="Complete",options=new{Output=destination},origin="Regional PE templates and integration launcher only"}));
        var readback=await ExportReadback.Run(destination,verification,launcher,log);
        File.WriteAllText(@"C:\TLOS\export-readback-780.json",readback);
        if(BootableDrive.Hash(windows)!=windowsSha)throw new IOException("Windows image changed during export/readback.");
        using var data=JsonDocument.Parse(readback);
        File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,sourceIso,originalSha,destination,regional,sourceWindowsSha,windowsSha,launcherOnlyWindowsRevision=true,readback=data.RootElement.Clone()}));
        log("Regional/launcher ISO780 export and independent Windows configuration preservation readback passed.");
    }
}
