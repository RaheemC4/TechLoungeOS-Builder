using System.Text.Json;
using TechLoungeOS;

internal static class RevisionUsb
{
    internal static async Task Run()
    {
        var runner = new ProcessRunner(Console.WriteLine);
        await runner.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU'){throw 'Owned VM required'}
            if(@(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1176' -and $_.IsBoot}).Count -ne 1){throw 'Owned test installation required'}
            $workspace=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$workspace.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $workspace.IsBoot -or $workspace.IsSystem){throw 'Owned workspace required'}
            """);
        const string iso = @"S:\Ghost-full-1250.iso", workspace = @"S:\usb1255", report = @"S:\usb1255-result.json";
        if (Directory.Exists(workspace) || File.Exists(report)) throw new IOException("Inspect the previous USB transaction before retrying.");
        using var audit = JsonDocument.Parse(File.ReadAllText(@"S:\wallpaper-revision1250\export-readback1250.json"));
        var proof = audit.RootElement;
        if (proof.GetProperty("iso").GetString() != iso || !proof.GetProperty("readOnly").GetBoolean() ||
            !proof.GetProperty("configurationPreserved").GetBoolean() || !proof.GetProperty("postAppFilesAndOrderVerified").GetBoolean() ||
            !proof.GetProperty("bootImageMatches").GetBoolean() || proof.GetProperty("apps").GetInt32() != 11 ||
            proof.GetProperty("features").GetInt32() != 136 || proof.GetProperty("configuration").GetInt32() != 705 ||
            BootableDrive.Hash(iso) != proof.GetProperty("sourceSha256").GetString()) throw new IOException("Current audited installer required.");
        var engine = new BootableDrive(runner, Console.WriteLine);
        var disk = (await engine.List()).Single(d => d.SerialNumber == "TLOS-USB-1255");
        if (disk.Size != 16000000000 || disk.LogicalSectorSize != 512 || BootableDrive.Rejection(disk) is { })
            throw new IOException("Only the owned disposable 16GB virtual USB may be written.");
        var prepared = await engine.Prepare(iso, workspace, disk, CancellationToken.None);
        await engine.Write(prepared, disk, "ERASE " + disk.Number, CancellationToken.None);
        File.WriteAllText(report, JsonSerializer.Serialize(new { verified = true, physicalDevice = false, disk, source = prepared.Iso, sourceSha256 = prepared.IsoSha256, files = prepared.Files.Length, prepared.Bytes }));
        Console.WriteLine("Current 1250 candidate passed the complete owned 16GB USB write and readback; boot tests remain required.");
    }
}
