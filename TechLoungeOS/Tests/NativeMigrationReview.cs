using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

// Exact owned-VM investigation. A retained image is unaccepted and cannot be exported.
internal static class NativeMigrationReview
{
    internal static async Task Run()
    {
        const string root=@"S:\feature-repair1609";
        const string work=root+@"\migration1651";
        const string mount=root+@"\mount";
        const string wim=@"R:\feature1586\selected.wim";
        var guard=new ProcessRunner(Console.WriteLine);
        await guard.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563' -or
              @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Exact owned VM required'}
            $r=Get-Partition -DriveLetter R|Get-Disk;$s=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$r.SerialNumber).Trim() -ne 'TLOS-DESKTOP1502' -or $r.IsBoot -or $r.IsSystem -or
              ([string]$s.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $s.IsBoot -or $s.IsSystem){throw 'Exact owned work disks required'}
            $owner=(Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
            if(Get-Process dism,DismHost,WimServ,Tests -ErrorAction SilentlyContinue|Where-Object {$_.ProcessName -ne 'Tests' -or $_.Id -ne $owner}){throw 'Wait for native servicing'}
            if(@(Get-WindowsImage -Mounted).Count){throw 'Existing mounts must be resolved first'}
            'Owned disks and idle servicing verified; no physical media is attached'
            """);
        using var prior=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,@"retry1619\result.json")));
        using var update=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,@"retry1619\windows-update\result.json")));
        if(prior.RootElement.GetProperty("passed").GetBoolean() || prior.RootElement.GetProperty("mounted").GetBoolean() ||
            update.RootElement.GetProperty("actual").GetString()!="10.0.26300.9725" ||
            update.RootElement.GetProperty("preservationPassed").GetBoolean() ||
            update.RootElement.GetProperty("error").GetString()!="Preservation check: a feature absent from the source appeared: Microsoft-Windows-Camera-CCX-Deployment")
            throw new IOException("The completed and discarded exact preservation rejection is required.");
        if(Directory.Exists(work)||File.Exists(work)||new DriveInfo("S").AvailableFreeSpace<35L*1024*1024*1024)
            throw new IOException("Fresh bounded investigation storage required.");
        Policy.RequireUnlinkedPath(work);Policy.RequireUnlinkedPath(wim);
        Directory.CreateDirectory(work);
        using var log=new StreamWriter(Path.Combine(work,"native.log")){AutoFlush=true};
        var gate=new object();
        void Write(string text){lock(gate){log.WriteLine(DateTimeOffset.UtcNow.ToString("o")+" "+text);Console.WriteLine(text);}}
        var run=new ProcessRunner(Write,Path.Combine(work,"scratch"));
        bool mounted=false;string? failure=null;bool cameraDisabled=false;
        try
        {
            Directory.CreateDirectory(mount);
            await run.Dism("/Mount-Image","/ImageFile:"+wim,"/Index:1","/MountDir:"+mount);mounted=true;
            if(WindowsImageUpdates.ReadVersion(mount)!=new Version(10,0,26200,9457))throw new IOException("Original private WIM version changed.");
            var apps=ImageServicing.Packages(await run.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages"));
            var features=ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
            var config=SourcePreservation.CaptureConfiguration(mount);
            using(var original=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,@"retry1619\original-baseline.json"))))
            {
                var baseline=original.RootElement;
                SourcePreservation.VerifyServices(JsonSerializer.Deserialize<Dictionary<string,string>>(baseline.GetProperty("config").GetRawText())!,config);
                SourcePreservation.VerifyComponents(JsonSerializer.Deserialize<ImageServicing.Package[]>(baseline.GetProperty("apps").GetRawText())!,apps,
                    JsonSerializer.Deserialize<ImageServicing.Feature[]>(baseline.GetProperty("features").GetRawText())!,features,[]);
            }
            Snapshot("before");
            WallpaperAssetRepair.Repair(mount,Path.Combine(work,"asset-repair"));
            SourcePreservation.VerifyServices(config,SourcePreservation.CaptureConfiguration(mount));
            var packages=@"S:\WindowsUpdate1320\packages";
            try
            {
                await WindowsImageUpdates.Apply(run,mount,work,packages,WindowsImageUpdates.ReadPlan(packages),new Version(10,0,26200,9457),Write,CancellationToken.None);
                throw new IOException("Expected the strict preservation gate to reject the unreviewed migrations.");
            }
            catch(IOException error)
            {
                using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(work,@"windows-update\result.json")));
                if(!result.RootElement.GetProperty("packageCommandSucceeded").GetBoolean() ||
                    result.RootElement.GetProperty("actual").GetString()!="10.0.26300.9725" ||
                    !error.Message.StartsWith("Preservation check:",StringComparison.Ordinal))throw;
                failure=error.Message;
            }
            const string camera="Microsoft-Windows-Camera-CCX-Deployment";
            if(features.Any(f=>f.FeatureName==camera))throw new IOException("Do not disable a camera feature that existed in the original source.");
            await run.Dism("/Image:"+mount,"/Disable-Feature","/FeatureName:"+camera,"/Remove","/NoRestart");
            var actualFeatures=ImageServicing.Features(await run.Dism("/Image:"+mount,"/Get-Features","/Format:List"));
            cameraDisabled=actualFeatures.Any(f=>f.FeatureName==camera && f.State.Equals("Disabled with Payload Removed",StringComparison.OrdinalIgnoreCase));
            if(!cameraDisabled)throw new IOException("The new camera feature was not disabled with its payload removed.");
            // Existing source features/apps must retain their states, even during investigation.
            SourcePreservation.VerifyComponents(apps,ImageServicing.Packages(await run.Dism("/Image:"+mount,"/Get-ProvisionedAppxPackages")),features,
                actualFeatures.Where(f=>f.FeatureName!=camera).ToArray(),[]);
            Snapshot("after");
            File.WriteAllText(Path.Combine(work,"after-camera.json"),JsonSerializer.Serialize(new{features=actualFeatures,configuration=SourcePreservation.CaptureConfiguration(mount),payloads=WindowsUpdatePreservation.Capture(mount)}));
            Write("Unaccepted updated private mount retained for migration review. No commit, export or installation occurred.");
        }
        catch(Exception error){failure=error.ToString();Write(failure);throw;}
        finally
        {
            // Keep this owned mount for bounded correction, avoiding repeated update application.
            File.WriteAllText(Path.Combine(work,"result.json"),JsonSerializer.Serialize(new{accepted=false,mounted,cameraDisabled,failure,originalReadOnly=true,utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
        }

        void Snapshot(string phase)
        {
            foreach(var name in new[]{"SYSTEM","SOFTWARE"})
            {
                var source=Path.Combine(mount,@"Windows\System32\config",name);
                SourcePreservation.RequireImageFile(source,4096,268435456);
                var target=Path.Combine(work,phase+"-"+name+".hive");
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
                using var file=File.OpenRead(target);
                File.WriteAllText(target+".sha256",Convert.ToHexString(SHA256.HashData(file)));
            }
        }
    }
}
