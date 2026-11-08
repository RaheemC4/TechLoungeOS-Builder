namespace TechLoungeOS;

// Re-export the accepted updated image to omit obsolete WIM resources, without servicing its contents.
internal static class WimPacking
{
    internal static bool Needed(BuildOptions options,long projectedBytes)
    {
        if(projectedBytes<0)throw new IOException("Invalid prepared media size.");
        var limit=options.Target16GbUsb?BootableDrive.Target16GbBytes:long.MaxValue;
        if(options.UsbTargetBytes is { } capacity)limit=Math.Min(limit,capacity);
        return projectedBytes>limit;
    }
    private static readonly Dictionary<string,string> Payloads=new(StringComparer.Ordinal)
    {
        ["wimlib-imagex.exe"]="34C0C4165591AD1F592837ED99D08273C58D6ED3FE0ED6360CF34E7B0739B353",
        ["libwim-15.dll"]="BA853EE1E3FC5F5798581F02E8E066BA07A0A2375F0BF444FE981431FD508495",
        ["source.tar.gz"]="84221A3ABD5B91228F15F8E6065C335A336237B5738197B75BF419EEA561A194",
        ["COPYING.txt"]="ED5CC16630B6AA5EDAF9CFD440DBF5EDA8E2257471752EAD93634AC8EB102583",
        ["COPYING.GPLv3.txt"]="230184F60BAE2FEAF244F10A8BAC053C8FF33A183BCC365B4D8B876D2B7F4809",
        ["COPYING.LGPL.txt"]="B14C366C41CC9F62DC20EC1A0885F278CF9681076D98027012A910FCA5E89639",
        ["COPYING.libdivsufsort-lite.txt"]="6783C2BE541A8871BCCFD0E9A26253DEF3A32922FB1DA5344B6BBD43CBAB851F",
        ["README.txt"]="43854A34DB76EF7EB373E290179ADCC3BB11377B5EBB481E166FACDD2C9CDF78",
        ["README.WINDOWS.txt"]="912314E6258926AB9EBAE5CAE5D30AF515959DFEB6BF6D8C538A1D3748FABE32"
    };
    private static byte[] Read(string name,string expected)
    {
        using var input=typeof(WimPacking).Assembly.GetManifestResourceStream("TechLoungeOS.WimPacking."+name)
            ?? throw new IOException("The verified Windows image compressor was not packaged: "+name);
        if(input.Length is <1 or > (4<<20))throw new IOException("Invalid image-compressor payload size.");
        using var memory=new MemoryStream();input.CopyTo(memory);var bytes=memory.ToArray();
        if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))!=expected)throw new IOException("Image-compressor payload checksum differs: "+name);
        return bytes;
    }
    internal static void ValidatePackage(){foreach(var file in Payloads)Read(file.Key,file.Value);}
    internal static async Task<string> Compact(ICommandRunner run,string source,string workspace,Version expected,Action<string> log,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var resources=BuildResources.CompressionPlan();
        log($"Image compression: {resources.Threads} CPU threads, {resources.AvailableBytes/(double)(1L<<30):F1} GiB available RAM. The memory budget leaves headroom for Windows and other apps.");
        source=Path.GetFullPath(source);workspace=Path.GetFullPath(workspace);
        Policy.RequireUnlinkedPath(source);Policy.RequireUnlinkedPath(workspace);
        var packed=Path.Combine(workspace,"packed-install.esd");var tools=Path.Combine(workspace,"wim-packing-tools");
        if(source.Equals(packed,StringComparison.OrdinalIgnoreCase)||File.Exists(packed)||Directory.Exists(packed))
            throw new IOException("A fresh packed-image destination is required; existing files were retained.");
        var before=new FileInfo(source).Length;
        if(new DriveInfo(Path.GetPathRoot(workspace)!).AvailableFreeSpace<checked(before+(1L<<30)))
            throw new IOException("Not enough temporary space to compress the updated image safely. The accepted image was retained.");
        Policy.RequireUnlinkedPath(tools);
        if(File.Exists(tools)||Directory.Exists(tools))throw new IOException("A fresh compressor folder is required; existing files were retained.");
        var payloads=Payloads.ToDictionary(f=>f.Key,f=>Read(f.Key,f.Value),StringComparer.Ordinal);
        Directory.CreateDirectory(tools);foreach(var file in payloads)File.WriteAllBytes(Path.Combine(tools,file.Key),file.Value);
        // Keep the executable and its only non-system DLL immutable while the child runs.
        using(var executable=new FileStream(Path.Combine(tools,"wimlib-imagex.exe"),FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var library=new FileStream(Path.Combine(tools,"libwim-15.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))
            await run.Run(Path.Combine(tools,"wimlib-imagex.exe"),["export",source,"1",packed,"--solid","--threads="+resources.Threads,"--check"],ct);
        ct.ThrowIfCancellationRequested();
        var metadata=(await run.PowerShell("[string](Get-WindowsImage -ImagePath "+Policy.Ps(packed)+" -Index 1).Version")).Trim();
        if(!File.Exists(packed)||new FileInfo(packed).Length==0||!Version.TryParse(metadata,out var actual)||actual!=expected)
            throw new IOException("Packed image readback differs from the accepted Windows version. No ISO was exported; the accepted image was retained.");
        File.WriteAllText(Path.Combine(workspace,"image-packing.json"),System.Text.Json.JsonSerializer.Serialize(new{source,packed,before,after=new FileInfo(packed).Length,version=actual.ToString(),componentsRemoved=false,compression="solid LZMS, default64MiB chunks",threads=resources.Threads,availableMemoryBytes=resources.AvailableBytes,tool="wimlib1.14.5"}));
        log("Compressed the updated installation image without changing its components.");return packed;
    }
}
