using System.Diagnostics;
using System.Security.Cryptography;
using TechLoungeOS;

internal static class UsbTransferChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"usb-transfer");Directory.CreateDirectory(folder);
        var source=Path.Combine(folder,"source.bin");var copied=Path.Combine(folder,"copied.bin");var baseline=Path.Combine(folder,"baseline.bin");
        var data=RandomNumberGenerator.GetBytes(16<<20);var expected=Convert.ToHexString(SHA256.HashData(data));File.WriteAllBytes(source,data);
        var timestamp=new DateTime(2025,1,2,3,4,6,DateTimeKind.Utc);File.SetLastWriteTimeUtc(source,timestamp);
        var watch=Stopwatch.StartNew();File.Copy(source,baseline);watch.Stop();var oldMs=watch.Elapsed.TotalMilliseconds;
        var copiedProgress=new List<long>();watch.Restart();UsbFileTransfer.Copy(source,copied,data.Length,CancellationToken.None,copiedProgress.Add);watch.Stop();
        check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copied)))==expected&&File.GetLastWriteTimeUtc(copied)==timestamp&&
            copiedProgress.Count==16&&copiedProgress[0]==1<<20&&copiedProgress[^1]==data.Length&&copiedProgress.Zip(copiedProgress.Skip(1)).All(p=>p.First<p.Second),
            "Chunked copying preserves exact bytes/timestamp and reports monotonically increasing transferred bytes (inert local files)");
        Console.WriteLine($"Inert 16 MiB local cached-copy sample: File.Copy {oldMs:F2} ms; progress copy {watch.Elapsed.TotalMilliseconds:F2} ms. Not a USB or whole-build speed measurement.");
        var verified=new List<long>();
        check(UsbFileTransfer.Hash(copied,CancellationToken.None,verified.Add)==expected&&verified.Count==16&&verified[^1]==data.Length,
            "Chunked verification produces the independent expected SHA256 and counts actual checked bytes");
        try{UsbFileTransfer.Copy(source,copied,data.Length,CancellationToken.None,_=>{});throw new Exception("Existing destination overwritten");}
        catch(IOException){check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copied)))==expected,"A pre-existing destination is never overwritten");}
        var wrong=Path.Combine(folder,"wrong.bin");
        try{UsbFileTransfer.Copy(source,wrong,data.Length-1,CancellationToken.None,_=>{});throw new Exception("Changed source length accepted");}
        catch(IOException){check(!File.Exists(wrong),"A changed source size is rejected before creating its destination");}
        using(var cancelled=new CancellationTokenSource())
        {
            var partial=Path.Combine(folder,"partial.bin");
            try{UsbFileTransfer.Copy(source,partial,data.Length,cancelled.Token,_=>cancelled.Cancel());throw new Exception("Copy ignored cancellation");}
            catch(OperationCanceledException){check(new FileInfo(partial).Length==1<<20&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))==expected,"Copy cancellation takes effect between chunks and leaves the source unchanged");}
        }
        using(var cancelled=new CancellationTokenSource())
        {
            long bytes=0;
            try{UsbFileTransfer.Hash(source,cancelled.Token,n=>{bytes=n;cancelled.Cancel();});throw new Exception("Hash ignored cancellation");}
            catch(OperationCanceledException){check(bytes==1<<20,"Verification cancellation stops after its reported chunk");}
        }
        var empty=Path.Combine(folder,"empty.bin");File.WriteAllBytes(empty,[]);
        check(UsbFileTransfer.Hash(empty,CancellationToken.None,_=>{})=="E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            "Empty media files retain their standard SHA256");
        check(new UsbTransferProgress(false,6_500_000_000,13_000_000_000).Percent==50&&new UsbTransferProgress(true,13_000_000_000,13_000_000_000).Percent==100,
            "USB progress uses actual byte totals for copying and independent verification");
    }
}
