using TechLoungeOS;
using System.Text.Json;

internal static class Export1257
{
    internal static async Task Run(bool finishExport=false)
    {
        var run=new ProcessRunner(Console.WriteLine);
        await run.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU' -or @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1176' -and $_.IsBoot}).Count -ne 1){throw 'Owned test installation required'}
            $disk=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$disk.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $disk.IsBoot -or $disk.IsSystem){throw 'Owned revision workspace required'}
            """);
        const string work=@"S:\wallpaper-revision1250",iso=@"S:\Ghost-full-1250.iso";
        using var lease=new FileStream(Path.Combine(work,"build.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        if(finishExport)
        {
            if(File.Exists(iso) || File.Exists(Path.Combine(work,"state.json")))throw new IOException("Review the existing export before retrying.");
            using var failed=JsonDocument.Parse(File.ReadAllText(@"C:\finish1257-failed.json"));
            if(failed.RootElement.GetProperty("action").GetString()!="finish1257-failed" ||
                !(failed.RootElement.GetProperty("result").GetProperty("detail").GetString()??"").Contains("Writing 1115 files in 139 directories to S:\\Ghost-full-1250.iso",StringComparison.Ordinal))
                throw new IOException("The completed outside-image verification checkpoint is required.");
            var media=Path.Combine(work,"media");var tool=Path.Combine(AppContext.BaseDirectory,"oscdimg.exe");
            if(BootableDrive.Hash(tool)!="D2709F8099DDD202BC41BCB399ECD5638503C6466F416D1A321769AF83A70C3A" ||
                BootableDrive.Hash(Path.Combine(media,@"sources\boot.wim"))!="F2074710A6B5C848461691775F910FC3039E99072D0633FF85417444FAA38BCB")throw new IOException("Verified export tool and WinPE image required.");
            using(var imageLease=new FileStream(Path.Combine(media,@"sources\install.wim"),FileMode.Open,FileAccess.Read,FileShare.Read))
                await IsoExport.Create(run,media,iso,tool,Console.WriteLine,CancellationToken.None);
            File.WriteAllText(Path.Combine(work,"state.json"),JsonSerializer.Serialize(new{phase="Complete",options=new{Output=iso,Apps=RecommendedApps.PresetIds,IncludeOffice=true}}));
        }
        var result=await ExportReadback.Run(iso,work,"50AD4786D872BF83BA8765DA28A3C5FF951EA99B853D13EE10A80260D02276AF",Console.WriteLine,"22384B8CF3A333E9F3143E12A87DB544655390A3EF9D5E4283F935ECDE986A0F");
        File.WriteAllText(Path.Combine(work,"export-readback1250.json"),result);
        File.WriteAllText(Path.Combine(work,"revision-result.json"),JsonSerializer.Serialize(new{complete=true,output=iso,sourceFiles=1115,unchangedOutsideInstallWim=true,bootWimSha256="F2074710A6B5C848461691775F910FC3039E99072D0633FF85417444FAA38BCB",launcherSha256="50AD4786D872BF83BA8765DA28A3C5FF951EA99B853D13EE10A80260D02276AF",wallpaperSha256="22384B8CF3A333E9F3143E12A87DB544655390A3EF9D5E4283F935ECDE986A0F",installedVmCaptured=false}));
        Console.WriteLine("1250 exported installer passed independent preservation and all payload checksums; fresh installation remains required.");
    }
}
