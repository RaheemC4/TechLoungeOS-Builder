using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

// Explicit opt-in, bounded native archive benchmark. Only inert local test files;
// no Windows ISO, image mount, physical USB or installed-Windows settings are used.
internal static class PipelinePerformanceChecks
{
    internal static void Hash(string path)
    {
        path=Path.GetFullPath(path);Policy.RequireUnlinkedPath(path);
        if(!path.Contains("compression-scaling",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(path)!=".bin")
            throw new IOException("Use an owned inert compression-scaling fixture file for the hash comparison.");
        using var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(lease.Length<16L<<20||lease.Length>1L<<30)throw new IOException("The bounded hash fixture must be16MiB to1GiB.");
        string expected;
        using(var warmup=File.OpenRead(path))expected=Convert.ToHexString(SHA256.HashData(warmup));
        if(UsbFileTransfer.Hash(path,CancellationToken.None,_=>{})!=expected)throw new IOException("Hash warm-up differs.");
        var results=new List<object>();
        foreach(var chunked in new[]{false,true,true,false})
        {
            var watch=Stopwatch.StartNew();string hash;
            if(chunked)hash=UsbFileTransfer.Hash(path,CancellationToken.None,_=>{});
            else{using var input=File.OpenRead(path);hash=Convert.ToHexString(SHA256.HashData(input));}
            watch.Stop();if(hash!=expected)throw new IOException("Hash implementations disagree.");
            results.Add(new{chunked,seconds=watch.Elapsed.TotalSeconds});
        }
        var report=new{path,bytes=lease.Length,results,hash=expected,scope="Read-only warm local fixture; not USB speed or whole-build timing"};
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!,"hash-results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    internal static async Task Run(string root,bool scaling=false)
    {
        root=Path.GetFullPath(root);Policy.RequireUnlinkedPath(root);
        if(Directory.Exists(root)||File.Exists(root))throw new IOException("A new private benchmark folder is required.");
        var drive=new DriveInfo(Path.GetPathRoot(root)!);
        if(drive.DriveType!=DriveType.Fixed||drive.DriveFormat!="NTFS"||drive.AvailableFreeSpace<(scaling?112L:104L)*1024*1024*1024)throw new IOException("A local NTFS drive with100GiB headroom plus room for the bounded fixtures is required.");
        var resources=BuildResources.CompressionPlan();
        if(scaling&&(Environment.ProcessorCount<12||resources.AvailableBytes<(13UL<<30)))throw new IOException("The optional 8/12-thread comparison needs twelve CPU threads and at least13GiB currently free RAM.");
        Directory.CreateDirectory(root);
        using var transcript=new StreamWriter(Path.Combine(root,"native.log")){AutoFlush=true};
        void Log(string text){transcript.WriteLine(text);if(text.StartsWith('>'))Console.WriteLine(text);}
        var run=new ProcessRunner(Log);var tools=Path.Combine(AppContext.BaseDirectory,"ThirdParty","Wimlib");
        var exe=Path.Combine(tools,"wimlib-imagex.exe");var dll=Path.Combine(tools,"libwim-15.dll");
        if(BootableDrive.Hash(exe)!="34C0C4165591AD1F592837ED99D08273C58D6ED3FE0ED6360CF34E7B0739B353"||BootableDrive.Hash(dll)!="BA853EE1E3FC5F5798581F02E8E066BA07A0A2375F0BF444FE981431FD508495")throw new IOException("Pinned native compressor required.");
        using var executable=new FileStream(exe,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var library=new FileStream(dll,FileMode.Open,FileAccess.Read,FileShare.Read);
        var input=Path.Combine(root,"input");Directory.CreateDirectory(input);
        var random=new Random(3018);var expected=new Dictionary<string,string>();var block=new byte[1<<20];
        for(var file=0;file<4;file++)
        {
            var path=Path.Combine(input,file+".bin");using(var output=File.Create(path))
                for(var chunk=0;chunk<(scaling?256:64);chunk++)
                {
                    random.NextBytes(block);
                    for(var offset=0;offset<block.Length;offset+=4096)Array.Fill(block,(byte)(chunk+file),offset,2048);
                    output.Write(block);
                }
            expected.Add(Path.GetFileName(path),BootableDrive.Hash(path));
        }
        var source=Path.Combine(root,"input.wim");await run.Run(exe,["capture",input,source,"Inert performance fixture","--compress=none","--check"]);
        var results=new List<object>();var baseline=scaling?8:2;var candidate=scaling?12:resources.Threads;
        // Alternate order on the second round to avoid always favouring the warm run.
        foreach(var sample in new[]{(Round:1,Threads:baseline),(Round:1,Threads:candidate),(Round:2,Threads:candidate),(Round:2,Threads:baseline)})
        {
            var destination=Path.Combine(root,$"solid-{results.Count}.esd");var watch=Stopwatch.StartNew();
            await run.Run(exe,["export",source,"1",destination,"--solid","--threads="+sample.Threads,"--check"]);watch.Stop();
            var extract=Path.Combine(root,"extract-"+results.Count);
            await run.Run(exe,["apply",destination,"1",extract,"--check"]);
            foreach(var file in expected)if(BootableDrive.Hash(Path.Combine(extract,file.Key))!=file.Value)throw new IOException("Native archive readback differs: "+file.Key);
            results.Add(new{sample.Round,sample.Threads,seconds=watch.Elapsed.TotalSeconds,bytes=new FileInfo(destination).Length,allFourFileHashesMatched=true});
            Console.WriteLine(JsonSerializer.Serialize(results[^1]));
        }
        var report=new{utc=DateTimeOffset.UtcNow,fixtureBytes=(scaling?1024L:256L)<<20,baselineThreads=baseline,candidateThreads=candidate,resources.AvailableBytes,results,scope="Warm/local synthetic archive samples; not Windows servicing, physical USB or end-to-end timing",physicalDeviceWritten=false};
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Bounded native archive performance and complete data readback passed: "+root);
    }
}
