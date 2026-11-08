using System.Text.Json;
using TechLoungeOS;

// Native destructive acceptance: only the serial-numbered disposable disk in our owned VM.
internal static class NativeCurrentUsb
{
    internal static async Task Run(string proofPath)
    {
        var runner = new ProcessRunner(Console.WriteLine);
        await Guard(runner);
        using var document = JsonDocument.Parse(File.ReadAllText(proofPath));
        var proof = document.RootElement;
        foreach (var field in new[] { "readOnly", "configurationPreserved", "postAppFilesAndOrderVerified", "bootImageMatches" })
            if (!proof.GetProperty(field).GetBoolean()) throw new IOException("Completed ISO readback required: " + field);
        const string iso = @"S:\current2985.iso", work = @"S:\usb2985", report = @"S:\usb2985-result.json";
        if (Directory.Exists(work) || File.Exists(work) || File.Exists(report)) throw new IOException("Review the previous USB transaction before retrying.");
        var expectedHash = proof.GetProperty("sourceSha256").GetString();
        if(!File.Exists(iso))
        {
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};
            using var response=await client.GetAsync("http://10.0.2.100:4182/source.iso",HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var destination=new FileStream(iso,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            await response.Content.CopyToAsync(destination);destination.Flush(true);
        }
        if (BootableDrive.Hash(iso) != expectedHash) throw new IOException("Transferred ISO differs from accepted export.");
        var engine = new BootableDrive(runner, Console.WriteLine);
        var usb = (await engine.List()).Single(d => d.SerialNumber == "TLOS-USB-2985");
        if (usb.Size != 16_000_000_000L || usb.LogicalSectorSize != 512 || BootableDrive.Rejection(usb) != null)
            throw new IOException("Only the owned disposable 16 GB virtual USB is permitted.");
        var prepared = await engine.Prepare(iso, work, usb, CancellationToken.None);
        if (prepared.IsoSha256 != expectedHash) throw new IOException("ISO changed after acceptance; no disk was erased.");
        const string label = "TLOS_SETUP";
        await engine.Write(prepared, usb, "ERASE " + usb.Number, CancellationToken.None, label);
        var actual = (await runner.PowerShell("[string](((Get-Partition -DiskNumber " + usb.Number + ")|Get-Volume|Where-Object FileSystem -eq '" +
            (BootableDrive.SeparateData(prepared.Files) ? "NTFS" : "FAT32") + "').FileSystemLabel)")).Trim();
        if (actual != label) throw new IOException("USB label readback differs.");
        var result=JsonSerializer.Serialize(new { verified = true, physicalDevice = false, usb,
            sourceSha256 = prepared.IsoSha256, files = prepared.Files.Length, prepared.Bytes, volumeLabel = actual,
            bootValidated = false, utc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(report,result);
        using var upload=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
        using var accepted=await upload.PostAsync("http://10.0.2.100:4182/result",new StringContent(result));accepted.EnsureSuccessStatusCode();
        Console.WriteLine("Owned 16 GB USB write, full file readback and label checks passed; boot acceptance follows.");
    }
    private static async Task Guard(ProcessRunner runner)
    {
        await runner.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563' -or
              @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Exact owned test VM required'}
            $work=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$work.SerialNumber).Trim() -ne 'TLOS-REV1250' -or $work.IsBoot -or $work.IsSystem){throw 'Exact owned test workspace required'}
            if(@(Get-WindowsImage -Mounted).Count){throw 'Existing image mounts retained'}
            """);
    }
}
