using System.Text.Json;
using TechLoungeOS;

// Explicit VM acceptance only. The caller checks the owned VM/disk serials.
internal static class ExportReadback
{
    internal static void CheckRejectedInputs(string folder)
    {
        var work=Path.Combine(folder,"readback-input-checks");Directory.CreateDirectory(work);
        var iso=Path.Combine(work,"fixture.iso");File.WriteAllText(iso,"inert fixture, never mounted");
        void Journal(string phase,string output)=>File.WriteAllText(Path.Combine(work,"state.json"),JsonSerializer.Serialize(new{phase,options=new{Output=output}}));
        void Reject(bool sharing)
        {
            try{Run(iso,work,new string('0',64),_=>{}).GetAwaiter().GetResult();throw new Exception("Unsafe readback input was accepted.");}
            catch(IOException ex) when(sharing ? (ex.HResult&0xffff) is 32 or 33 : ex.Message.Contains("Readback requires the completed output")) { }
            if(Directory.EnumerateDirectories(work).Any()||File.Exists(Path.Combine(work,"export-readback-state.json")))throw new Exception("Rejected readback input started a mount transaction.");
        }
        Journal("Failed",iso);Reject(false);
        Journal("Complete",Path.Combine(work,"different.iso"));Reject(false);
        Journal("Complete",iso);
        using(var held=new FileStream(Path.Combine(work,"export-readback.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))Reject(true);
        Console.WriteLine("Readback rejects incomplete/mismatched exports and concurrent audits before creating a mount transaction. No ISO was mounted.");
    }

    internal static async Task<string> Run(string iso,string workspace,string expectedLauncherSha256,Action<string> log,string? expectedPersonaliseSha256=null,string? imageWorkspace=null,Action<string>? inspectImage=null)
    {
        if(expectedLauncherSha256.Length!=64 || !expectedLauncherSha256.All(Uri.IsHexDigit))throw new IOException("Supply the quiet launcher's SHA-256 from the package used to build this ISO.");
        if(expectedPersonaliseSha256!=null && (expectedPersonaliseSha256.Length!=64 || !expectedPersonaliseSha256.All(Uri.IsHexDigit)))throw new IOException("Supply the wallpaper helper's SHA-256 from the tested package.");
        Policy.RequireUnlinkedPath(iso);Policy.RequireUnlinkedPath(workspace);
        using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state.json")));
        if(state.RootElement.GetProperty("phase").GetString()!="Complete" ||
            !Path.GetFullPath(iso).Equals(Path.GetFullPath(state.RootElement.GetProperty("options").GetProperty("Output").GetString()!),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Readback requires the completed output named by this build journal.");
        using var lease=new FileStream(Path.Combine(workspace,"export-readback.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var isoLease=new FileStream(iso,FileMode.Open,FileAccess.Read,FileShare.Read);
        var sourceSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(isoLease));
        var mount=Path.Combine(workspace,"readback-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mount);
        var run=new ProcessRunner(log,Path.Combine(workspace,"readback-scratch"));
        bool mounted=false,ownedIso=false;Exception? failure=null;string? result=null,expanded=null;
        File.WriteAllText(Path.Combine(workspace,"export-readback-state.json"),JsonSerializer.Serialize(new{iso,mount,phase="Preparing read-only mount"}));
        try
        {
            var disk=await run.PowerShell($$"""
                $d=Get-DiskImage -ImagePath {{Policy.Ps(iso)}}
                $owned=!$d.Attached
                try {
                  if($owned){$d=Mount-DiskImage -ImagePath {{Policy.Ps(iso)}} -PassThru}
                  $v=$d|Get-Volume
                  if(!$v.DriveLetter){throw 'Output ISO has no accessible volume'}
                  $images=@('install.wim','install.esd')|ForEach-Object {Join-Path ($v.DriveLetter+':\sources') $_}|Where-Object {Test-Path -LiteralPath $_}
                  if(@($images).Count -ne 1){throw 'Exactly one installation image is required'}
                  [pscustomobject]@{owned=$owned;wim=[string]$images}|ConvertTo-Json -Compress
                }catch{if($owned){Dismount-DiskImage -ImagePath {{Policy.Ps(iso)}}|Out-Null};throw}
                """);
            using var metadata=JsonDocument.Parse(disk.Trim());
            ownedIso=metadata.RootElement.GetProperty("owned").GetBoolean();
            var wim=metadata.RootElement.GetProperty("wim").GetString()!;
            if(Path.GetExtension(wim).Equals(".esd",StringComparison.OrdinalIgnoreCase))
            {
                // DISM cannot mount solid ESD. Re-export its actual ISO bytes without servicing.
                var expansion=Path.GetFullPath(imageWorkspace??workspace);Policy.RequireUnlinkedPath(expansion);
                var drive=new DriveInfo(Path.GetPathRoot(expansion)!);
                if(drive.DriveType==DriveType.Network)throw new IOException("Use a local NTFS readback workspace.");
                Policy.ValidateStorage(drive.DriveFormat,drive.AvailableFreeSpace,checked(new FileInfo(wim).Length*3+(1L<<30)),true);
                Directory.CreateDirectory(expansion);expanded=Path.Combine(expansion,"readback-image-"+Guid.NewGuid().ToString("N")+".wim");
                File.WriteAllText(Path.Combine(workspace,"export-readback-state.json"),JsonSerializer.Serialize(new{iso,mount,expanded,phase="Expanding the exported ESD for read-only verification"}));
                await run.Dism("/Export-Image","/SourceImageFile:"+wim,"/SourceIndex:1","/DestinationImageFile:"+expanded,"/Compress:fast","/CheckIntegrity");
                wim=expanded;
            }
            mounted=true;
            await run.Dism("/Mount-Image","/ImageFile:"+wim,"/Index:1","/MountDir:"+mount,"/ReadOnly","/CheckIntegrity");
            var windowsVersion=WindowsImageUpdates.ReadVersion(mount).ToString();
            if(File.Exists(Path.Combine(workspace,"image-packing.json")))
            {
                using var packing=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"image-packing.json")));
                if(packing.RootElement.GetProperty("version").GetString()!=windowsVersion)throw new IOException("Exported Windows version differs from the accepted packed image.");
            }
            T Read<T>(string name)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(workspace,name)))??throw new IOException("Missing readback baseline: "+name);
            var apps=ImageServicing.Packages(await run.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages"));
            var features=ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
            if(!apps.OrderBy(p=>p.PackageName,StringComparer.Ordinal).SequenceEqual(Read<ImageServicing.Package[]>("appx-after.json").OrderBy(p=>p.PackageName,StringComparer.Ordinal)) ||
                !features.OrderBy(f=>f.FeatureName,StringComparer.Ordinal).SequenceEqual(Read<ImageServicing.Feature[]>("features-after.json").OrderBy(f=>f.FeatureName,StringComparer.Ordinal)))
                throw new IOException("Exported app or optional-feature state differs from the verified build.");
            var configuration=SourcePreservation.CaptureConfiguration(mount);
            // Retain exact hashes before a preservation rejection releases the read-only mount.
            File.WriteAllText(Path.Combine(workspace,"configuration-exported.json"),JsonSerializer.Serialize(configuration));
            var staged=File.Exists(Path.Combine(workspace,"configuration-staged.json"));
            SourcePreservation.VerifyServices(Read<Dictionary<string,string>>(staged?"configuration-staged.json":"services-after.json"),configuration);
            // The legacy compiler embeds build-specific bytes. Compare against
            // the package used for this ISO, not a later verifier rebuild.
            var expected=expectedLauncherSha256.ToUpperInvariant();
            foreach(var relative in new[]{@"ProgramData\TechLoungeOS\FirstLogin.exe",@"Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\TechLoungeOS.exe"})
                if(BootableDrive.Hash(Path.Combine(mount,relative))!=expected)throw new IOException("Exported quiet launcher differs: "+relative);
            if(expectedPersonaliseSha256!=null)
            {
                var common=Path.Combine(mount,@"ProgramData\TechLoungeOS");
                if(!BootableDrive.Hash(Path.Combine(common,@"Wallpapers\Personalise.exe")).Equals(expectedPersonaliseSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Exported wallpaper helper differs from the native-tested package.");
                var payload=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(common,"payload-sha256.json")))??throw new IOException("Exported integration manifest missing.");
                foreach(var entry in payload)
                {
                    var file=Path.GetFullPath(Path.Combine(common,entry.Key));
                    if(Path.IsPathRooted(entry.Key) || !file.StartsWith(common+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Exported integration path leaves its payload folder.");
                    Policy.RequireUnlinkedPath(file);
                    if(!BootableDrive.Hash(file).Equals(entry.Value,StringComparison.OrdinalIgnoreCase))throw new IOException("Exported integration checksum differs: "+entry.Key);
                }
            }
            foreach(var vendor in new[]{"TechLoungeOS-Nvidia","TechLoungeOS-Graphics"})
                if(Directory.Exists(Path.Combine(mount,"Windows","Setup",vendor,"state")))throw new IOException("Target GPU detection state was baked into the ISO.");
            var options=state.RootElement.GetProperty("options");
            PostAppReadback.Check(mount,options.GetProperty("Apps").Deserialize<string[]>()??[],options.GetProperty("IncludeOffice").GetBoolean());
            var exportedBoot=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(metadata.RootElement.GetProperty("wim").GetString()!))!,@"sources\boot.wim");
            var bootWimSha256=BootableDrive.Hash(exportedBoot);
            if(bootWimSha256!=BootableDrive.Hash(Path.Combine(workspace,@"media\sources\boot.wim")))throw new IOException("Exported WinPE image differs from the verified build media.");
            inspectImage?.Invoke(mount);
            result=JsonSerializer.Serialize(new{iso,sourceSha256,windowsVersion,apps=apps.Length,features=features.Length,configuration=configuration.Count,configurationPreserved=true,quietLauncherSha256=expected,wallpaperHelperSha256=expectedPersonaliseSha256,targetDetectionStateAbsent=true,postAppFilesAndOrderVerified=true,bootWimSha256,bootImageMatches=true,readOnly=true});
        }
        catch(Exception ex){failure=ex;}
        finally
        {
            if(mounted)try{await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Discard");mounted=false;}catch(Exception ex){failure=failure==null?ex:new AggregateException(failure,ex);}
            // Retain the ISO if its read-only WIM mount still needs recovery.
            if(ownedIso&&!mounted)try{await run.PowerShell("Dismount-DiskImage -ImagePath "+Policy.Ps(iso)+" | Out-Null");}catch(Exception ex){failure=failure==null?ex:new AggregateException(failure,ex);}
            if(expanded!=null&&!mounted)try{if(File.Exists(expanded))File.Delete(expanded);}catch(Exception ex){failure=failure==null?ex:new AggregateException(failure,ex);}
            File.WriteAllText(Path.Combine(workspace,"export-readback-state.json"),JsonSerializer.Serialize(new{iso,mount,expanded,phase=failure==null?"Complete":"Failed; inspect errors and mount before recovery",error=failure?.ToString()}));
        }
        if(failure!=null)throw new IOException("Export readback failed. Recovery journal: "+Path.Combine(workspace,"export-readback-state.json"),failure);
        return result??throw new IOException("Export readback produced no result.");
    }
}
