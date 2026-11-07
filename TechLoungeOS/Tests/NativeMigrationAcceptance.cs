using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

// Continue the retained transaction; never reapply packages or accept unexplained differences.
internal static class NativeMigrationAcceptance
{
    internal static async Task Run()
    {
        const string root=@"S:\feature-repair1609\migration1651";
        const string work=root+@"\accept1726";
        const string mount=@"S:\feature-repair1609\mount";
        const string wim=@"R:\feature1586\selected.wim";
        var guard=new ProcessRunner(Console.WriteLine);
        await guard.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563' -or
              @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Exact owned VM required'}
            $r=Get-Partition -DriveLetter R|Get-Disk;$s=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$r.SerialNumber).Trim() -ne 'TLOS-DESKTOP1502' -or $r.IsBoot -or $r.IsSystem -or
              ([string]$s.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $s.IsBoot -or $s.IsSystem){throw 'Exact owned work disks required'}
            $owner=(Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
            if(Get-Process dism,DismHost,Tests,MigrationTools* -ErrorAction SilentlyContinue|Where-Object {($_.ProcessName -ne 'Tests' -and $_.ProcessName -notlike 'MigrationTools*') -or $_.Id -ne $owner}){throw 'Wait for servicing/review to finish'}
            $memory=Get-CimInstance Win32_OperatingSystem
            if($memory.FreePhysicalMemory -lt 1572864 -or $memory.FreeVirtualMemory -lt 2097152){throw 'Release diagnostic memory before the final health scan'}
            $m=@(Get-WindowsImage -Mounted)
            if($m.Count -ne 1 -or $m[0].Path.TrimEnd('\') -ine 'S:\feature-repair1609\mount' -or
              $m[0].ImagePath -ine 'R:\feature1586\selected.wim' -or $m[0].ImageIndex -ne 1 -or [string]$m[0].MountStatus -ne 'Ok'){throw 'Exact retained mount required'}
            'Exact owned retained migration mount verified'
            """);
        using var prior=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"result.json")));
        using var previousUpdate=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,@"windows-update\result.json")));
        if(prior.RootElement.GetProperty("accepted").GetBoolean() || !prior.RootElement.GetProperty("mounted").GetBoolean() ||
            !previousUpdate.RootElement.GetProperty("packageCommandSucceeded").GetBoolean() ||
            previousUpdate.RootElement.GetProperty("actual").GetString()!="10.0.26300.9725" ||
            previousUpdate.RootElement.GetProperty("preservationPassed").GetBoolean())throw new IOException("Exact retained unaccepted update required.");
        if(Directory.Exists(work)||File.Exists(work)||new DriveInfo("R").AvailableFreeSpace<15L*1024*1024*1024 ||
            new DriveInfo("S").AvailableFreeSpace<8L*1024*1024*1024)throw new IOException("Fresh evidence folder and reserved commit space required.");
        Policy.RequireUnlinkedPath(work);Directory.CreateDirectory(work);
        using var log=new StreamWriter(Path.Combine(work,"native.log")){AutoFlush=true};var gate=new object();
        void Write(string text){lock(gate){log.WriteLine(DateTimeOffset.UtcNow.ToString("o")+" "+text);Console.WriteLine(text);}}
        var run=new ProcessRunner(Write,Path.Combine(work,"scratch"));var held=new List<FileStream>();
        bool validated=false,committed=false,metadataVerified=false;string? failure=null;
        try
        {
            var plan=WindowsImageUpdates.ReadPlan(@"S:\WindowsUpdate1320\packages");
            var route=WindowsUpdateMigrations.Find(plan)??throw new IOException("This package sequence has no reviewed migration rule.");
            using(var recorded=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,@"windows-update\verified-plan.json"))))
                if(JsonSerializer.Serialize(plan)!=JsonSerializer.Serialize(JsonSerializer.Deserialize<WindowsImageUpdates.Plan>(recorded.RootElement.GetRawText())))
                    throw new IOException("The native signature-verified transaction used a different update plan.");
            // Original native execution already verified Microsoft signatures before applying packages.
            // Deny mutation and rehash those exact staged bytes before accepting its continuation.
            foreach(var package in plan.Packages)
            {
                var path=Path.Combine(root,"windows-update",package==plan.Packages[^1]?"enablement":"cumulative",package.File);
                Policy.RequireUnlinkedPath(path);var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);held.Add(file);
                if(file.Length!=package.Bytes || Convert.ToHexString(await SHA256.HashDataAsync(file))!=package.Sha256)
                    throw new IOException("Previously signature-verified package bytes changed.");
            }
            var sourceSystem=Path.Combine(root,"before-SYSTEM.hive");var sourceSoftware=Path.Combine(root,"before-SOFTWARE.hive");
            foreach(var path in new[]{sourceSystem,sourceSoftware})
            {
                Policy.RequireUnlinkedPath(path);SourcePreservation.RequireImageFile(path,4096,268435456);
                using var input=File.OpenRead(path);
                if(Convert.ToHexString(SHA256.HashData(input))!=File.ReadAllText(path+".sha256"))throw new IOException("Source snapshot evidence changed.");
            }
            if(SourcePreservation.ReadStringValue(sourceSoftware,@"Microsoft\Windows NT\CurrentVersion","CurrentBuildNumber")!="26200" ||
                SourcePreservation.ReadDwordValue(sourceSoftware,@"Microsoft\Windows NT\CurrentVersion","UBR")!=9457 ||
                WindowsImageUpdates.ReadVersion(mount)!=new Version(10,0,26300,9725) ||
                WindowsImageUpdates.ReadEdition(mount)!=SourcePreservation.ReadStringValue(sourceSoftware,@"Microsoft\Windows NT\CurrentVersion","EditionID") ||
                SourcePreservation.ReadStringValue(Path.Combine(mount,@"Windows\System32\config\SOFTWARE"),@"Microsoft\Windows NT\CurrentVersion","DisplayVersion")!="26H2")
                throw new IOException("Independent source/target release or retained edition differs.");
            using var baseline=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,@"windows-update\before.json")));
            var beforeApps=JsonSerializer.Deserialize<ImageServicing.Package[]>(baseline.RootElement.GetProperty("apps").GetRawText())!;
            var beforeFeatures=JsonSerializer.Deserialize<ImageServicing.Feature[]>(baseline.RootElement.GetProperty("features").GetRawText())!;
            var beforeConfig=new Dictionary<string,string>(JsonSerializer.Deserialize<Dictionary<string,string>>(baseline.RootElement.GetProperty("configuration").GetRawText())!,StringComparer.OrdinalIgnoreCase);
            SourcePreservation.VerifyServices(beforeConfig.Where(p=>p.Key.StartsWith("ControlSet",StringComparison.Ordinal)).ToDictionary(),SourcePreservation.CaptureServicesHive(sourceSystem));
            foreach(var preference in SourcePreservation.CaptureUpdatePreferences(sourceSystem,sourceSoftware))beforeConfig.Add(preference.Key,preference.Value);
            var beforePayloads=JsonSerializer.Deserialize<WindowsUpdatePreservation.Snapshot>(baseline.RootElement.GetProperty("removedPayloads").GetRawText())!;
            var afterFeatures=ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
            // A resource-exhausted final scan can leave the already-updated image before
            // camera correction. Continue that same private transaction, never reapply packages.
            if(await WindowsUpdateMigrations.DisableNewCamera(run,mount,route,beforeFeatures,afterFeatures,CancellationToken.None))
                afterFeatures=ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
            var afterApps=ImageServicing.Packages(await run.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages"));
            var afterConfig=SourcePreservation.CaptureUpdateConfiguration(mount);
            File.WriteAllText(Path.Combine(work,"differences.json"),JsonSerializer.Serialize(WindowsUpdatePreservation.Differences(beforeApps,afterApps,beforeFeatures,afterFeatures,beforeConfig,afterConfig,beforePayloads,WindowsUpdatePreservation.Capture(mount)),new JsonSerializerOptions{WriteIndented=true}));
            SourcePreservation.VerifyComponents(beforeApps,afterApps,beforeFeatures,WindowsUpdateMigrations.ExistingFeatures(route,beforeFeatures,afterFeatures),[]);
            WindowsUpdateMigrations.VerifyConfiguration(route,beforeConfig,afterConfig);
            WindowsUpdatePreservation.Verify(mount,beforePayloads);
            using(var artwork=File.OpenRead(Path.Combine(mount,WallpaperAssetRepair.Active)))
                if(Convert.ToHexString(SHA256.HashData(artwork))!=WallpaperAssetRepair.GhostHash)throw new IOException("Visible Ghost artwork changed.");
            var health=await run.Dism("/Image:"+mount,"/Cleanup-Image","/ScanHealth");File.WriteAllText(Path.Combine(work,"health.txt"),health);
            if(!health.Contains("No component store corruption detected",StringComparison.OrdinalIgnoreCase))throw new IOException("Final corrected image is not healthy.");
            // Re-read settings after the scan before committing the accepted private image.
            WindowsUpdateMigrations.VerifyConfiguration(route,beforeConfig,SourcePreservation.CaptureUpdateConfiguration(mount));
            validated=true;
            await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Commit");committed=true;
            WimVersionMetadata.Update(wim,Path.Combine(work,"metadata-scratch"),new Version(10,0,26200,9457),new Version(10,0,26300,9725));
            var version=(await run.PowerShell("(Get-WindowsImage -ImagePath "+Policy.Ps(wim)+" -Index 1).Version.ToString()")).Trim();
            if(version!="10.0.26300.9725")throw new IOException("Independent committed WIM metadata readback differs.");
            metadataVerified=true;Write("Native migration, private commit and independent 26H2 WIM metadata passed. Export/boot are still pending.");
        }
        catch(Exception error){failure=error.ToString();Write(failure);throw;}
        finally
        {
            foreach(var file in held)file.Dispose();
            File.WriteAllText(Path.Combine(work,"result.json"),JsonSerializer.Serialize(new{validated,committed,metadataVerified,mounted=!committed,failure,originalReadOnly=true,installationValidated=false,utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
