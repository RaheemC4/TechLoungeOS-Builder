using System.Text.Json;
using TechLoungeOS;

internal static class IsoVerificationChecks
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var folder=Path.Combine(root,"iso verification 'accenté'");Directory.CreateDirectory(folder);
        var source=Path.Combine(folder,"media");var exported=Path.Combine(folder,"readback");
        Directory.CreateDirectory(Path.Combine(source,"sources"));Directory.CreateDirectory(Path.Combine(exported,"sources"));
        var original=System.Text.Encoding.UTF8.GetBytes("Independent Windows image fixture.");
        File.WriteAllBytes(Path.Combine(source,@"sources\install.wim"),original);
        File.WriteAllBytes(Path.Combine(source,"empty.txt"),[]);
        File.WriteAllBytes(Path.Combine(exported,@"sources\install.wim"),original);
        File.WriteAllBytes(Path.Combine(exported,"empty.txt"),[]);
        var expected=IsoMediaVerification.Snapshot(source,CancellationToken.None);
        var manifest=Path.Combine(folder,"expected.json");File.WriteAllText(manifest,JsonSerializer.Serialize(expected));
        var runner=new ProcessRunner(_=>{});
        async Task<string> Verify()=>await runner.PowerShell(IsoMediaVerification.DirectoryScript(exported,manifest));
        var positive=await Verify();
        check(positive.Contains("verified 2 files / 34 bytes.",StringComparison.Ordinal),"actual PowerShell verifies every file, including empty files, through quoted Unicode fixture paths");
        async Task Reject(string expectedMessage,string label)
        {
            try{await Verify();throw new Exception("Unsafe exported tree accepted");}
            catch(IOException ex) when(ex.Message.Contains(expectedMessage,StringComparison.Ordinal)){check(true,label);}
        }
        var changed=original.ToArray();changed[7]^=0x10;File.WriteAllBytes(Path.Combine(exported,@"sources\install.wim"),changed);
        await Reject("size/checksum verification","one-bit corruption in an equal-length installation image is rejected");
        File.WriteAllBytes(Path.Combine(exported,@"sources\install.wim"),original);
        File.Delete(Path.Combine(exported,"empty.txt"));
        await Reject("missing 1 source files","an omitted zero-byte media file is rejected");
        File.WriteAllBytes(Path.Combine(exported,"empty.txt"),[]);
        File.WriteAllText(Path.Combine(exported,"extra.txt"),"unexpected export data");
        await Reject("Unexpected exported file","extra ISO files cannot silently bypass the captured source manifest");
        File.Delete(Path.Combine(exported,"extra.txt"));
        File.WriteAllText(Path.Combine(source,"PSObject"),"ordinary ISO file with a PowerShell member name");
        File.WriteAllText(Path.Combine(exported,"PSObject"),"ordinary ISO file with a PowerShell member name");
        File.WriteAllText(manifest,JsonSerializer.Serialize(IsoMediaVerification.Snapshot(source,CancellationToken.None)));
        check((await Verify()).Contains("verified 3 files",StringComparison.Ordinal),"ordinary filenames matching PowerShell members retain their checksum verification");
        var large=Path.Combine(source,"large.bin");var largeExport=Path.Combine(exported,"large.bin");
        var bytes=System.Security.Cryptography.RandomNumberGenerator.GetBytes(17<<20);File.WriteAllBytes(large,bytes);File.Copy(large,largeExport);
        File.WriteAllText(manifest,JsonSerializer.Serialize(IsoMediaVerification.Snapshot(source,CancellationToken.None)));
        check((await Verify()).Contains("verified 4 files",StringComparison.Ordinal),"large buffered readback and small/empty files all retain full SHA256 verification");
        using(var changedLarge=new FileStream(largeExport,FileMode.Open,FileAccess.Write,FileShare.None)){changedLarge.Position=(16<<20)+123;changedLarge.WriteByte((byte)(bytes[(16<<20)+123]^1));}
        await Reject("size/checksum verification","one-bit corruption beyond the large-buffer boundary is rejected before publication");
        File.Delete(large);File.Delete(largeExport);File.WriteAllText(manifest,JsonSerializer.Serialize(IsoMediaVerification.Snapshot(source,CancellationToken.None)));
        var empty=Path.Combine(folder,"empty-media");Directory.CreateDirectory(empty);
        try{IsoMediaVerification.Snapshot(empty,CancellationToken.None);throw new Exception("Empty source accepted");}
        catch(IOException){check(true,"empty media cannot produce a meaningless successful verification");}
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try{IsoMediaVerification.Snapshot(source,cancelled.Token);throw new Exception("Cancelled snapshot accepted");}
            catch(OperationCanceledException){check(true,"cancellation stops source hashing before export");}
        }
        Directory.CreateDirectory(Path.Combine(source,"boot"));Directory.CreateDirectory(Path.Combine(source,@"efi\microsoft\boot"));
        File.WriteAllBytes(Path.Combine(source,@"boot\etfsboot.com"),new byte[2048]);
        File.WriteAllBytes(Path.Combine(source,@"efi\microsoft\boot\efisys.bin"),new byte[2048]);
        var success=Path.Combine(folder,"verified.iso");
        var successful=new FixtureRunner(folder);
        await IsoExport.Create(successful,source,success,"inert-oscdimg-fixture",_=>{},CancellationToken.None);
        var temporary=successful.Commands.Single()[^1];
        check(Path.GetExtension(temporary)==".iso" && temporary!=success &&
            temporary.StartsWith(success+".",StringComparison.Ordinal),
            "staged export retains its recognised ISO extension and a distinct private path before publication");
        check(File.ReadAllText(success)=="fixture ISO" && !Directory.EnumerateFiles(folder,"*.partial*").Any(),
            "verified export finalises the intended bytes and releases its own temporary ISO and manifest");
        var output=Path.Combine(folder,"must-not-exist.iso");
        var failure=new FixtureRunner(folder,failVerification:true);
        try{await IsoExport.Create(failure,source,output,"inert-oscdimg-fixture",_=>{},CancellationToken.None);throw new Exception("Failed verification published an ISO");}
        catch(IOException ex) when(ex.Message=="Injected export verification failure")
        {
            check(!File.Exists(output)&&!Directory.EnumerateFiles(folder,"*.partial*").Any(),"failed post-export verification publishes no final ISO and removes its own partial and manifest");
        }
    }
}
