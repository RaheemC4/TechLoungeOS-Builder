using System.Text.Json;
using System.Text.Json.Nodes;
using TechLoungeOS;

// Explicit owned-VM checks. The small fixtures are never booted or installed.
internal static class IsoExportVerificationCheck
{
    internal static async Task<string> Run(bool acceptedImage)
    {
        var runner=new ProcessRunner(Console.WriteLine);
        await runner.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU' -or @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Owned fresh1263 required'}
            $scratch=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$scratch.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $scratch.IsBoot -or $scratch.IsSystem){throw 'Owned scratch required'}
            """);
        var root=Path.Combine(@"S:\IsoVerifierTests",Guid.NewGuid().ToString("N"));
        Policy.RequireUnlinkedPath(root);
        if(Directory.Exists(root)||File.Exists(root))throw new IOException("Use the new owned verification folder; inspect any previous result first.");
        Directory.CreateDirectory(root);
        if(acceptedImage)
        {
            const string iso=@"S:\Ghost-full-1250.iso",media=@"S:\wallpaper-revision1250\media";
            if(BootableDrive.Hash(iso)!="CDBB0604EB3F625B7E923D05792F07E48C86A2601361F4B1905F16647A95CDB8")throw new IOException("Accepted ISO changed.");
            if((await runner.PowerShell("(Get-DiskImage -ImagePath 'S:\\Ghost-full-1250.iso').Attached")).Trim()!="False")throw new IOException("Leave the existing source mount alone.");
            var expected=IsoMediaVerification.Snapshot(media,CancellationToken.None);
            if(expected.Count!=1115)throw new IOException($"Accepted build-media file count differs: {expected.Count}; the original export recorded 1115 total files.");
            var manifest=Path.Combine(root,"expected.json");
            using(var file=new FileStream(manifest,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(file,expected);
            await IsoMediaVerification.Verify(runner,iso,manifest,CancellationToken.None);
            if((await runner.PowerShell("(Get-DiskImage -ImagePath 'S:\\Ghost-full-1250.iso').Attached")).Trim()!="False")throw new IOException("Readback did not release its source mount.");
            File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,passed=true,acceptedIsoUnchanged=true,files=expected.Count,bytes=expected.Sum(e=>e.Length),allFilesIncludingWimVerified=true,readOnly=true}));
            return Path.Combine(root,"result.json");
        }
        var tool=Path.Combine(AppContext.BaseDirectory,"oscdimg.exe");
        if(BootableDrive.Hash(tool)!="D2709F8099DDD202BC41BCB399ECD5638503C6466F416D1A321769AF83A70C3A")throw new IOException("Expected previously verified Microsoft ISO tool required.");
        var fixture=Path.Combine(root,"media");
        foreach(var relative in new[]{@"boot\etfsboot.com",@"efi\microsoft\boot\efisys.bin",@"sources\install.wim","marker.txt"})
        {
            var path=Path.Combine(fixture,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path,relative.EndsWith(".wim",StringComparison.Ordinal)?Enumerable.Range(0,65536).Select(i=>(byte)(i*131+17)).ToArray():new byte[2048]);
        }
        var output=Path.Combine(root,"fixture.iso");
        await IsoExport.Create(runner,fixture,output,tool,Console.WriteLine,CancellationToken.None);
        if(!File.Exists(output)||(await runner.PowerShell("(Get-DiskImage -ImagePath "+Policy.Ps(output)+").Attached")).Trim()!="False")throw new IOException("Native export did not finish or release its readback mount.");
        var negative=Path.Combine(root,"must-not-publish.iso");
        var corrupt=new ChangedManifestRunner(runner,root);
        try{await IsoExport.Create(corrupt,fixture,negative,tool,Console.WriteLine,CancellationToken.None);throw new Exception("A mismatched export manifest was accepted.");}
        catch(IOException ex) when(ex.Message.Contains("size/checksum verification",StringComparison.Ordinal))
        {
            if(File.Exists(negative)||Directory.EnumerateFiles(root,"*.partial*").Any())throw new IOException("Failed native verification retained a final ISO, partial or mounted file.");
        }
        var retry=new TransientReleaseRunner(runner);
        var recovered=Path.Combine(root,"retry.iso");
        await IsoExport.Create(retry,fixture,recovered,tool,Console.WriteLine,CancellationToken.None);
        if(!retry.Exercised||!File.Exists(recovered)||Directory.EnumerateFiles(root,"*.partial*").Any())throw new IOException("Transient private-mount release did not recover cleanly.");
        File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,passed=true,realOscdimg=true,realReadOnlyMount=true,positiveVerified=true,mismatchRejected=true,failedOutputAbsent=true,partialAndManifestReleased=true,transientReleaseRecovered=true,inertFixtureNeverBooted=true}));
        return Path.Combine(root,"result.json");
    }

    private sealed class ChangedManifestRunner(ICommandRunner inner,string root):ICommandRunner
    {
        public Task<string> Dism(params string[] args)=>throw new InvalidOperationException("No Windows servicing belongs in this export fixture.");
        public Task<string> PowerShell(string script,CancellationToken ct=default)=>inner.PowerShell(script,ct);
        public async Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)
        {
            var text=await inner.Run(exe,args,ct);
            var manifest=Directory.EnumerateFiles(root,"*.files.json").Single();
            var records=JsonNode.Parse(File.ReadAllText(manifest))!.AsArray();
            records[0]!["Sha256"]=new string('0',64);
            File.WriteAllText(manifest,records.ToJsonString());
            return text;
        }
    }

    private sealed class TransientReleaseRunner(ICommandRunner inner):ICommandRunner
    {
        internal bool Exercised { get; private set; }
        public Task<string> Dism(params string[] args)=>throw new InvalidOperationException("No Windows servicing belongs in this fixture.");
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)=>inner.Run(exe,args,ct);
        public async Task<string> PowerShell(string script,CancellationToken ct=default)
        {
            var text=await inner.PowerShell("""
                Import-Module Storage -ErrorAction Stop
                $script:releaseProbe=0
                function Dismount-DiskImage {
                    param([string]$DevicePath)
                    $script:releaseProbe++
                    if($script:releaseProbe -eq 1){throw 'Injected one-time private ISO release failure'}
                    Storage\Dismount-DiskImage -DevicePath $DevicePath
                }
                """+"\n"+script+"\nif($script:releaseProbe -ne 2){throw ('Expected exactly two private ISO release attempts; observed '+$script:releaseProbe)}; 'Private ISO release retry exercised.'",ct);
            Exercised=text.Contains("Private ISO release retry exercised.",StringComparison.Ordinal);
            return text;
        }
    }
}
