using System.Text.Json;
using TechLoungeOS;

// Native acceptance only. Never available from the shipped Builder UI.
internal static class NativeFeatureUpgrade
{
    internal static async Task ContinueApprovedRepair()
    {
        const string root=@"S:\feature-repair1609";
        const string mount=root+@"\mount";
        const string wim=@"R:\feature1586\selected.wim";
        var guard=new ProcessRunner(Console.WriteLine);
        await guard.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563' -or
              @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Exact owned VM required'}
            $r=Get-Partition -DriveLetter R|Get-Disk;$s=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$r.SerialNumber).Trim() -ne 'TLOS-DESKTOP1502' -or $r.IsBoot -or $r.IsSystem -or
              ([string]$s.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $s.IsBoot -or $s.IsSystem){throw 'Exact owned work disks required'}
            if(Get-Process dism,DismHost -ErrorAction SilentlyContinue){throw 'Wait for native servicing'}
            $m=@(Get-WindowsImage -Mounted)
            if($m.Count -ne 1 -or $m[0].Path.TrimEnd('\') -ine 'S:\feature-repair1609\mount' -or
              $m[0].ImagePath -ine 'R:\feature1586\selected.wim' -or $m[0].ImageIndex -ne 1 -or [string]$m[0].MountStatus -ne 'Ok'){throw 'Exact retained private mount required'}
            'Owned retained mount verified; no remount or export'
            """);
        using var prior=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"result.json")));
        if(prior.RootElement.GetProperty("passed").GetBoolean() || !prior.RootElement.GetProperty("mounted").GetBoolean() ||
            prior.RootElement.GetProperty("target").ValueKind!=JsonValueKind.Null || Directory.Exists(Path.Combine(root,"windows-update")) ||
            !prior.RootElement.GetProperty("error").GetString()!.Contains("img100.tlos-1609.tmp",StringComparison.Ordinal))
            throw new IOException("Only the retained pre-package staging-permission failure can continue.");
        var work=Path.Combine(root,"retry1619");if(Directory.Exists(work)||File.Exists(work))throw new IOException("New continuation evidence required.");
        Policy.RequireUnlinkedPath(work);Directory.CreateDirectory(work);
        using var log=new StreamWriter(Path.Combine(work,"native.log")){AutoFlush=true};
        var gate=new object();
        void Write(string text){lock(gate){log.WriteLine(DateTimeOffset.UtcNow.ToString("o")+" "+text);Console.WriteLine(text);}}
        var run=new ProcessRunner(Write,Path.Combine(work,"scratch"));bool mounted=true,repaired=false,passed=false;string? error=null;Version? target=null;
        try
        {
            var source=WindowsImageUpdates.ReadVersion(mount);if(source!=new Version(10,0,26200,9457))throw new IOException("Retained image version changed.");
            var apps=ImageServicing.Packages(await run.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages"));
            var features=ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
            var config=SourcePreservation.CaptureConfiguration(mount);var payloads=WindowsUpdatePreservation.Capture(mount);
            File.WriteAllText(Path.Combine(work,"original-baseline.json"),JsonSerializer.Serialize(new{apps,features,config,payloads}));
            WallpaperAssetRepair.Repair(mount,Path.Combine(work,"asset-repair"));repaired=true;
            SourcePreservation.VerifyServices(config,SourcePreservation.CaptureConfiguration(mount));
            SourcePreservation.VerifyComponents(apps,ImageServicing.Packages(await run.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages")),features,ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List")),[]);
            WindowsUpdatePreservation.Verify(mount,payloads);
            Write("Approved internal picture replacement preserved visible artwork, ACLs, apps, features and original configuration.");
            var packages=@"S:\WindowsUpdate1320\packages";var plan=WindowsImageUpdates.ReadPlan(packages);
            target=await WindowsImageUpdates.Apply(run,mount,work,packages,plan,source,Write,CancellationToken.None);
            await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Commit");mounted=false;
            WimVersionMetadata.Update(wim,Path.Combine(work,"metadata-scratch"),source,target);
            var actual=(await run.PowerShell("(Get-WindowsImage -ImagePath "+Policy.Ps(wim)+" -Index 1).Version.ToString()")).Trim();
            if(actual!=target.ToString())throw new IOException("Committed WIM metadata differs from verified target.");
            passed=true;
        }
        catch(Exception failure){error=failure.ToString();Write(error);}
        finally
        {
            if(mounted && repaired)
                try{await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Discard");mounted=false;}
                catch(Exception cleanup){error+="\nDiscard failed: "+cleanup;}
            File.WriteAllText(Path.Combine(work,"result.json"),JsonSerializer.Serialize(new{passed,error,mounted,repaired,target=target?.ToString(),originalReadOnly=true,installationValidated=false,utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
        }
        if(!passed)throw new IOException("Approved continuation stopped; retained mount state is recorded: "+work);
    }

    internal static async Task Run(string sourceWim,string workspace,bool reuse=false,string? offlinePackages=null,bool repairWallpaper=false)
    {
        if(repairWallpaper && (!reuse || offlinePackages!=@"S:\WindowsUpdate1320\packages"))throw new IOException("Explicit approved repair requires the exact owned cached transaction.");
        if(offlinePackages is not null && !reuse)throw new IOException("Offline validation must reuse the retained private image.");
        var guard=new ProcessRunner(Console.WriteLine);
        // Verify the owned virtual machine before creating files or starting servicing.
        await guard.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU' -or
              (Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563' -or
              @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Exact owned VM required'}
            $disk=Get-Partition -DriveLetter R|Get-Disk
            if(([string]$disk.SerialNumber).Trim() -ne 'TLOS-DESKTOP1502' -or $disk.IsBoot -or $disk.IsSystem){throw 'Owned workspace volume required'}
            if(Get-Process dism,DismHost,WimServ -ErrorAction SilentlyContinue){throw 'Wait for existing native servicing'}
            if(@(Get-WindowsImage -Mounted).Count){throw 'Existing mounts must be resolved first'}
            'Owned VM and idle servicing verified'
            """);
        workspace=Path.GetFullPath(workspace);
        var cachedMetadata=Path.Combine(workspace,"source-editions.json");
        if(offlinePackages is null)sourceWim=Path.GetFullPath(sourceWim);
        if(workspace!=@"R:\feature1586" || (!reuse && (Directory.Exists(workspace)||File.Exists(workspace))) ||
            (offlinePackages is null && (new DriveInfo(Path.GetPathRoot(sourceWim)!).DriveType!=DriveType.CDRom ||
            !sourceWim.EndsWith(@"\sources\install.wim",StringComparison.OrdinalIgnoreCase))))
            throw new IOException("A fresh owned workspace and read-only source CD are required.");
        Policy.RequireUnlinkedPath(workspace);
        if(offlinePackages is null)Policy.RequireUnlinkedPath(sourceWim);
        else
        {
            if(!Path.IsPathFullyQualified(offlinePackages))throw new IOException("An absolute offline package path is required.");
            if(offlinePackages==@"S:\WindowsUpdate1320\packages")
                await guard.PowerShell("""
                    $disk=Get-Partition -DriveLetter S|Get-Disk
                    if(([string]$disk.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $disk.IsBoot -or $disk.IsSystem){throw 'Exact owned package-cache disk required'}
                    'Owned existing package cache verified; packages will be copied and independently verified by the updater'
                    """);
            else if(new DriveInfo(Path.GetPathRoot(offlinePackages)!).DriveType!=DriveType.CDRom)
                throw new IOException("Offline packages require the read-only test CD or the exact owned cache.");
            Policy.RequireUnlinkedPath(offlinePackages);
            Policy.RequireUnlinkedPath(cachedMetadata);SourcePreservation.RequireImageFile(cachedMetadata,2,65536);
            _=WindowsImageUpdates.ReadPlan(offlinePackages);
        }
        var wim=Path.Combine(workspace,"selected.wim");
        if(reuse)
        {
            using var previous=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"result.json")));
            if(previous.RootElement.GetProperty("passed").GetBoolean() || previous.RootElement.GetProperty("mounted").GetBoolean() ||
                !previous.RootElement.GetProperty("originalReadOnly").GetBoolean() || previous.RootElement.GetProperty("target").ValueKind!=JsonValueKind.Null ||
                !previous.RootElement.GetProperty("error").GetString()!.Contains("inherited component-store damage",StringComparison.Ordinal) ||
                Directory.Exists(Path.Combine(workspace,"windows-update")) || Directory.Exists(Path.Combine(workspace,"downloads")))
                throw new IOException("Reuse requires the discarded pre-package health-flag rejection, with no earlier update attempt.");
            Policy.RequireUnlinkedPath(wim);SourcePreservation.RequireImageFile(wim,1,20L*1024*1024*1024);
            if(repairWallpaper)
            {
                var rejected=Path.Combine(workspace,@"offline1593\windows-update\result.json");
                using var result=JsonDocument.Parse(File.ReadAllText(rejected));
                if(result.RootElement.GetProperty("packageOperationStarted").GetBoolean() || result.RootElement.GetProperty("actual").GetString()!="10.0.26200.9457")throw new IOException("A pre-package source health rejection is required.");
                workspace=@"S:\feature-repair1609";
            }
            else workspace=Path.Combine(workspace,offlinePackages is null?"rescan1591":"offline1593");
            if(Directory.Exists(workspace)||File.Exists(workspace))throw new IOException("Full-scan evidence must be new.");
        }
        if(new DriveInfo(repairWallpaper?"S":"R").AvailableFreeSpace<(repairWallpaper?35L:reuse?45L:55L)*1024*1024*1024)
            throw new IOException("Insufficient reserved space for the bounded feature-upgrade test.");
        Directory.CreateDirectory(workspace);
        using var log=new StreamWriter(Path.Combine(workspace,"native.log")){AutoFlush=true};
        var gate=new object();
        void Write(string text){lock(gate){log.WriteLine(DateTimeOffset.UtcNow.ToString("o")+" "+text);Console.WriteLine(text);}}
        var runner=new ProcessRunner(Write,Path.Combine(workspace,"scratch"));
        var mount=Path.Combine(workspace,"mount");Directory.CreateDirectory(mount);
        bool mounted=false,passed=false;string? error=null;Version? target=null;
        void SaveTails()
        {
            // Keep bounded diagnostic tails before discard. These are evidence, never repair inputs.
            foreach(var item in new[]{(Path.Combine(mount,@"Windows\Logs\CBS\CBS.log"),"offline-cbs-tail.log"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"Logs\CBS\CBS.log"),"guest-cbs-tail.log")})
            {
                try
                {
                    if(!File.Exists(item.Item1))continue;
                    using var input=new FileStream(item.Item1,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    if(input.Length>4*1024*1024)input.Seek(-4*1024*1024,SeekOrigin.End);
                    using var output=new FileStream(Path.Combine(workspace,item.Item2),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                    input.CopyTo(output);
                }
                catch(Exception diagnostic) when(diagnostic is IOException or UnauthorizedAccessException)
                {Write("Diagnostic tail unavailable: "+diagnostic.Message);}
            }
        }
        try
        {
            var json=offlinePackages is not null?File.ReadAllText(cachedMetadata):await runner.PowerShell("@((Get-WindowsImage -ImagePath "+Policy.Ps(sourceWim)+
                ")|ForEach-Object {$detail=Get-WindowsImage -ImagePath "+Policy.Ps(sourceWim)+
                " -Index $_.ImageIndex;[pscustomobject]@{ImageIndex=$detail.ImageIndex;ImageName=$detail.ImageName;Architecture=[string]$detail.Architecture;Version=$detail.Version.ToString()}})|ConvertTo-Json -Compress");
            var editions=JsonSerializer.Deserialize<Edition[]>(json)??throw new IOException("Source metadata missing.");
            var edition=editions.Single(e=>e.ImageName.Equals("WINDOWS 11 IoT ENT - COMPACT + DEF",StringComparison.OrdinalIgnoreCase));
            if(edition.Architecture!="9" || edition.Version!="10.0.26200.9457")throw new IOException("Unexpected original source image.");
            File.WriteAllText(Path.Combine(workspace,"source-editions.json"),json);
            if(!reuse)await runner.Dism("/Export-Image","/SourceImageFile:"+sourceWim,"/SourceIndex:"+edition.ImageIndex,
                "/DestinationImageFile:"+wim,"/Compress:fast","/CheckIntegrity");
            // A failed mount command may still leave a private mount requiring recovery.
            mounted=true;
            // Optimise initial directory population on the second mount; DISM's full health scan still reads the components.
            await runner.Dism("/Mount-Image","/ImageFile:"+wim,"/Index:1","/MountDir:"+mount,"/Optimize","/CheckIntegrity");
            var source=WindowsImageUpdates.ReadVersion(mount);var editionId=WindowsImageUpdates.ReadEdition(mount);
            if(source!=Version.Parse(edition.Version))throw new IOException("Private image version differs from the original selected source.");
            WindowsImageUpdates.Plan plan;
            if(offlinePackages is null)
            {
            var report=await WindowsUpdateDownload.CheckDetails(source,CancellationToken.None);
            var offer=report.Upgrade??throw new IOException("No supported live Windows feature-upgrade offer.");
            WindowsUpgradeRules.RequireEdition(offer.RuleId,editionId);
            File.WriteAllText(Path.Combine(workspace,"discovery.json"),JsonSerializer.Serialize(new{source=source.ToString(),editionId,report}));
            var downloads=Path.Combine(workspace,"downloads");
            plan=await WindowsFeatureUpdateDownload.Prepare(downloads,source,offer,Write,CancellationToken.None);
            offlinePackages=downloads;
            }
            else
            {
                plan=WindowsImageUpdates.ReadPlan(offlinePackages);
                if(plan.Feature?.RuleId!="win11-26h2-x64")throw new IOException("An exact reviewed 26H2 plan is required.");
                Write("Using locally cached Microsoft packages. No metadata request, internet download or repair will run.");
                File.WriteAllText(Path.Combine(workspace,"offline-plan.json"),JsonSerializer.Serialize(plan));
            }
            if(repairWallpaper)
            {
                var apps=ImageServicing.Packages(await runner.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages"));
                var features=ImageServicing.Features(await runner.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
                var config=SourcePreservation.CaptureConfiguration(mount);var payloads=WindowsUpdatePreservation.Capture(mount);
                WallpaperAssetRepair.Repair(mount,Path.Combine(workspace,"asset-repair"));
                SourcePreservation.VerifyServices(config,SourcePreservation.CaptureConfiguration(mount));
                SourcePreservation.VerifyComponents(apps,ImageServicing.Packages(await runner.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages")),features,ImageServicing.Features(await runner.Dism("/Image:"+mount,"/Get-Features","/Format:List")),[]);
                WindowsUpdatePreservation.Verify(mount,payloads);
                Write("Approved one-file component backup repair passed artwork, ACL and original configuration checks. The production updater still requires a full healthy scan.");
            }
            target=await WindowsImageUpdates.Apply(runner,mount,workspace,offlinePackages,plan,source,Write,CancellationToken.None);
            await runner.Dism("/Unmount-Image","/MountDir:"+mount,"/Commit");mounted=false;
            WimVersionMetadata.Update(wim,Path.Combine(workspace,"metadata-scratch"),source,target);
            var version=(await runner.PowerShell("(Get-WindowsImage -ImagePath "+Policy.Ps(wim)+" -Index 1).Version.ToString()" )).Trim();
            if(version!=target.ToString())throw new IOException("Native WIM metadata readback mismatch.");
            passed=true;
        }
        catch(Exception exception)
        {
            error=exception.ToString();Write(error);
            SaveTails();
        }
        finally
        {
            if(mounted && !(repairWallpaper && !File.Exists(Path.Combine(workspace,@"windows-update\verified-plan.json")) && !File.Exists(Path.Combine(workspace,@"asset-repair\result.json"))))
            {
                try{await runner.Dism("/Unmount-Image","/MountDir:"+mount,"/Discard");mounted=false;}
                catch(Exception cleanup){error+="\nDiscard failed: "+cleanup;Write("Private mount retained for explicit recovery: "+mount);}
            }
            File.WriteAllText(Path.Combine(workspace,"result.json"),JsonSerializer.Serialize(new{passed,error,mounted,
                target=target?.ToString(),originalReadOnly=true,installationValidated=false,utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
        }
        if(!passed)throw new IOException("Native upgrade acceptance failed. Inspect retained result and logs: "+workspace);
    }
}
