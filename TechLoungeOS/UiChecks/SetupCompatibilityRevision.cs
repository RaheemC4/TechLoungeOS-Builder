using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

// Owned-VM acceptance only, after the completed native740 export passed readback.
// Reuse its Windows image; do not repeat or fabricate offline Windows servicing.
internal static class SetupCompatibilityRevision
{
    internal static async Task Run(string sourceIso,string workspace,string destination,string report,string launcher,Action<string> log)
    {
        if(launcher==null||launcher.Length!=64||!launcher.All(Uri.IsHexDigit))throw new IOException("Expected package launcher digest required before revision.");
        if(Path.GetFullPath(sourceIso)!=@"S:\Ghost-full-740.iso" || Path.GetFullPath(workspace)!=@"S:\ghost-full-740" ||
            Path.GetFullPath(destination)!=@"S:\Ghost-full-748.iso" || Path.GetFullPath(report)!=@"C:\TLOS\boot-revision748.json")
            throw new IOException("Only the explicit native740/748 owned-VM revision is allowed.");
        foreach(var path in new[]{sourceIso,workspace,destination,report})Policy.RequireUnlinkedPath(path);
        if(File.Exists(destination)||File.Exists(report))throw new IOException("Retain existing revision evidence before retrying.");
        using var scope=JsonDocument.Parse(File.ReadAllText(@"C:\TLOS\full-build-scope-740.json"));
        using var previous=JsonDocument.Parse(File.ReadAllText(@"C:\TLOS\export-readback-740.json"));
        if(!scope.RootElement.GetProperty("buildComplete").GetBoolean()||!scope.RootElement.GetProperty("restored").GetBoolean()||
            !previous.RootElement.GetProperty("readOnly").GetBoolean()||!previous.RootElement.GetProperty("configurationPreserved").GetBoolean()||
            previous.RootElement.GetProperty("iso").GetString()!=sourceIso)throw new IOException("Audited completed native740 and restored VM test scope required.");
        using var buildLease=new FileStream(Path.Combine(workspace,"build.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        using var auditLease=new FileStream(Path.Combine(workspace,"export-readback.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        using var isoLease=new FileStream(sourceIso,FileMode.Open,FileAccess.Read,FileShare.Read);
        var originalSha256=Convert.ToHexString(SHA256.HashData(isoLease));
        if(originalSha256!=previous.RootElement.GetProperty("sourceSha256").GetString())throw new IOException("The verified native740 ISO changed.");
        var media=Path.Combine(workspace,"media");
        var sourceWim=Path.Combine(media,@"sources\install.wim");
        using var windowsLease=new FileStream(sourceWim,FileMode.Open,FileAccess.Read,FileShare.Read);
        var windowsSha256=Convert.ToHexString(SHA256.HashData(windowsLease));
        var revision=Path.Combine(workspace,"compatibility-revision748");
        var verification=@"S:\compatibility-audit748";
        if(Directory.Exists(revision)||Directory.Exists(verification))throw new IOException("Existing revision workspace needs explicit review.");
        Directory.CreateDirectory(revision);
        var run=new ProcessRunner(log,Path.Combine(revision,"scratch"));
        await SetupCompatibility.Prepare(run,media,revision,log,CancellationToken.None);
        windowsLease.Position=0;
        if(Convert.ToHexString(SHA256.HashData(windowsLease))!=windowsSha256)throw new IOException("Windows installation image changed during boot-only preparation.");
        BootableDrive.Check16GbTarget(media,log);
        await IsoExport.Create(run,media,destination,@"D:\tools\oscdimg.exe",log,CancellationToken.None);
        Directory.CreateDirectory(verification);
        foreach(var name in new[]{"appx-after.json","features-after.json","configuration-staged.json","services-after.json"})
            File.Copy(Path.Combine(workspace,name),Path.Combine(verification,name),false);
        File.WriteAllText(Path.Combine(verification,"state.json"),JsonSerializer.Serialize(new{phase="Complete",options=new{Output=destination},origin="Boot-only revision of audited native740; Windows image reused"}));
        var readback=await ExportReadback.Run(destination,verification,launcher,log);
        File.WriteAllText(@"C:\TLOS\export-readback-748.json",readback);
        using var data=JsonDocument.Parse(readback);
        using var compatibility=JsonDocument.Parse(File.ReadAllText(Path.Combine(revision,"setup-compatibility.json")));
        File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,sourceIso,originalSha256,destination,
            outputSha256=data.RootElement.GetProperty("sourceSha256").GetString(),windowsImageReused=true,windowsImagePreserved=true,windowsSha256,
            compatibility=compatibility.RootElement.Clone(),readback=data.RootElement.Clone()},new JsonSerializerOptions{WriteIndented=true}));
        log("Boot-only compatibility revision and independent exported-image readback passed.");
    }
}
