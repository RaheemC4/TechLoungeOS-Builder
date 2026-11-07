using System.Text.Json;
using TechLoungeOS;

// Destructive acceptance is restricted to the exact owned disposable USB in the isolated VM.
internal static class NativeFinalUsb
{
    internal static async Task Run()
    {
        var run=new ProcessRunner(Console.WriteLine);
        await run.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563' -or
              @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Exact owned VM required'}
            $r=Get-Partition -DriveLetter R|Get-Disk;$s=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$r.SerialNumber).Trim() -notmatch '^TLOS-DESKTOP1502(?:\s+_00000001\.)?$' -or $r.IsBoot -or $r.IsSystem -or
              ([string]$s.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $s.IsBoot -or $s.IsSystem){throw 'Exact private source and workspace required'}
            if(Get-Process FinalIntegration1759,dism,DismHost,oscdimg -ErrorAction SilentlyContinue){throw 'Finish integration and readback first'}
            if(@(Get-WindowsImage -Mounted).Count){throw 'Existing image mounts retained'}
            'Owned USB acceptance boundary passed'
            """);
        const string root=@"R:\ghost-final1758",iso=@"R:\Ghost-26H2-final1758.iso",work=@"S:\usb26-final1772",report=@"S:\usb26-final1772-result.json";
        if(Directory.Exists(work)||File.Exists(work)||File.Exists(report))throw new IOException("Review an existing USB transaction before retrying.");
        using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-result.json")));var done=result.RootElement;
        if(!done.GetProperty("complete").GetBoolean()||!done.GetProperty("committed").GetBoolean()||done.GetProperty("mounted").GetBoolean()||done.GetProperty("failure").ValueKind!=JsonValueKind.Null)throw new IOException("A completed integrated ISO is required.");
        using var readback=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"export-readback.json")));var proof=readback.RootElement;
        if(proof.GetProperty("iso").GetString()!=iso||!proof.GetProperty("readOnly").GetBoolean()||!proof.GetProperty("configurationPreserved").GetBoolean()||!proof.GetProperty("postAppFilesAndOrderVerified").GetBoolean()||!proof.GetProperty("bootImageMatches").GetBoolean())throw new IOException("Independent ISO readback must pass first.");
        var engine=new BootableDrive(run,Console.WriteLine);
        var usb=(await engine.List()).Single(d=>d.SerialNumber=="TLOS-USB-1255");
        if(usb.Size!=16000000000||usb.LogicalSectorSize!=512||BootableDrive.Rejection(usb) is not null)throw new IOException("Only the owned disposable 16GB USB is permitted.");
        var prepared=await engine.Prepare(iso,work,usb,CancellationToken.None);
        if(prepared.IsoSha256!=proof.GetProperty("sourceSha256").GetString())throw new IOException("ISO changed after readback. No drive was erased.");
        const string label="TLOS26H2";
        await engine.Write(prepared,usb,"ERASE "+usb.Number,CancellationToken.None,label);
        var actual=(await run.PowerShell("[string](((Get-Partition -DiskNumber "+usb.Number+")|Get-Volume|Where-Object FileSystem -eq '"+(BootableDrive.SeparateData(prepared.Files)?"NTFS":"FAT32")+"').FileSystemLabel)")).Trim();
        if(actual!=label)throw new IOException("USB label readback differs.");
        File.WriteAllText(report,JsonSerializer.Serialize(new{verified=true,physicalDevice=false,usb,source=iso,sourceSha256=prepared.IsoSha256,files=prepared.Files.Length,prepared.Bytes,volumeLabel=actual,bootValidated=false,utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Final ISO passed the owned 16GB virtual USB write, complete file readback and custom volume-label check. Boot/install acceptance follows.");
    }
}
