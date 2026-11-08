using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public record UsbDisk(int Number, string UniqueId, string SerialNumber, string Path, string FriendlyName,
    long Size, int BusType, int LogicalSectorSize, bool IsBoot, bool IsSystem, bool IsOffline, bool IsReadOnly,
    bool Protected, string[] Volumes)
{
    public string Label => $"{FriendlyName} · {Size / 1_000_000_000d:F1} GB"+(Volumes.Length==0?"":" · "+string.Join(", ",Volumes.Select(v=>v.TrimEnd(':')+":")));
}
public record MediaFile(string RelativePath, long Length, string Sha256);
public record PreparedUsb(string Workspace, string? Iso, string? IsoSha256, MediaFile[] Files, long Bytes);

// Microsoft's FAT32 boot + optional NTFS data layout, retaining the source boot chain.
public sealed partial class BootableDrive(ICommandRunner runner, Action<string> log)
{
    public const long Fat32Limit = uint.MaxValue;
    public const long MaximumBootPartition = 31L * 1024 * 1024 * 1024;
    // Nominal 16 GB sticks vary. Reserve a conservative capacity and still
    // validate the actual selected drive, split-WIM bytes and headroom later.
    public const long Target16GbBytes = 15_500_000_000L;
    public static long RequiredUsbBytes(long mediaBytes)
    {
        if (mediaBytes < 0) throw new ArgumentOutOfRangeException(nameof(mediaBytes));
        return checked(mediaBytes + Math.Max(512L << 20, mediaBytes / 20) + (32L << 20));
    }
    public static void Check16GbTarget(string media, Action<string> log)
    {
        var files=Directory.EnumerateFiles(media,"*",SearchOption.AllDirectories)
            .Select(f=>new MediaFile(Path.GetRelativePath(media,f),new FileInfo(f).Length,"")).ToArray();
        var bytes=files.Sum(f=>f.Length);var required=RequiredIsoUsbBytes(files);
        log($"16 GB USB target: {bytes:N0} media bytes; {required:N0} bytes including headroom. The actual drive is checked again before writing.");
        if (required > Target16GbBytes) throw new IOException("The selected media exceeds the conservative 16 GB USB target. No components were silently removed. This build needs smaller installation payloads before it can fit.");
    }
    public static bool SeparateData(IReadOnlyList<MediaFile> files)=>files.Any(f=>f.Length>Fat32Limit);
    public static bool SeparateData(UsbDisk disk,IReadOnlyList<MediaFile> files)=>NeedsFullDataVolume(disk)||SeparateData(files);
    internal static bool NeedsFullDataVolume(UsbDisk disk)=>disk.Size>MaximumBootPartition+(32L<<20);
    public static MediaFile[] BootFiles(IReadOnlyList<MediaFile> files)=>files.Where(f=>
        f.RelativePath.StartsWith(@"boot\",StringComparison.OrdinalIgnoreCase) ||
        f.RelativePath.StartsWith(@"efi\",StringComparison.OrdinalIgnoreCase) ||
        new[]{"bootmgr","bootmgr.efi",@"sources\boot.wim"}.Contains(f.RelativePath,StringComparer.OrdinalIgnoreCase)).ToArray();
    public static long BootPartitionBytes(IReadOnlyList<MediaFile> files,bool force=false)
    {
        if(!force&&!SeparateData(files))return 0;
        var boot=BootFiles(files);
        foreach(var required in new[]{"bootmgr",@"boot\bcd",@"boot\bootsect.exe",@"efi\boot\bootx64.efi",@"sources\boot.wim"})
            if(!boot.Any(f=>f.RelativePath.Equals(required,StringComparison.OrdinalIgnoreCase)))throw new IOException("Required USB boot file missing: "+required);
        if(boot.Any(f=>f.Length>Fat32Limit))throw new IOException("A required boot file exceeds FAT32's4GB limit; this source cannot use the supported boot layout.");
        var bytes=boot.Sum(f=>f.Length);
        return checked(Math.Max(256L<<20,bytes+Math.Max(64L<<20,bytes/10))+(1L<<20)-1)/(1L<<20)*(1L<<20);
    }
    public static long RequiredMediaBytes(IReadOnlyList<MediaFile> files)=>checked(RequiredUsbBytes(files.Sum(f=>f.Length))+BootPartitionBytes(files));
    // Large ordinary WIMs are split during preparation; other large files need NTFS.
    public static long RequiredIsoUsbBytes(IReadOnlyList<MediaFile> files)=>checked(RequiredUsbBytes(files.Sum(f=>f.Length))+
        (files.Any(f=>f.Length>Fat32Limit&&!f.RelativePath.Equals(@"sources\install.wim",StringComparison.OrdinalIgnoreCase))?BootPartitionBytes(files):0));
    internal static long ProjectedUsbBytes(string media,string replacedImage,string acceptedImage,long? targetCapacity=null)
    {
        replacedImage=Path.GetFullPath(replacedImage);
        var files=Directory.EnumerateFiles(media,"*",SearchOption.AllDirectories).Where(f=>!Path.GetFullPath(f).Equals(replacedImage,StringComparison.OrdinalIgnoreCase))
            .Select(f=>new MediaFile(Path.GetRelativePath(media,f),new FileInfo(f).Length,""))
            .Append(new MediaFile(Path.Combine("sources","install"+Path.GetExtension(acceptedImage)),new FileInfo(acceptedImage).Length,"")).ToArray();
        return targetCapacity>MaximumBootPartition+(32L<<20)
            ?checked(RequiredUsbBytes(files.Sum(f=>f.Length))+BootPartitionBytes(files,true))
            :RequiredIsoUsbBytes(files);
    }
    public static long PlannedBootPartition(UsbDisk disk,IReadOnlyList<MediaFile> files)
    {
        if(Rejection(disk) is { } reason)throw new IOException(reason);
        var boot=BootPartitionBytes(files,SeparateData(disk,files));
        if(boot==0)return PartitionBytes(disk,files.Sum(f=>f.Length));
        if(disk.Size>(2L<<40))throw new IOException("This BIOS-compatible boot/data layout supports USB drives up to2TiB. No drive was erased.");
        if(boot>MaximumBootPartition || checked(RequiredUsbBytes(files.Sum(f=>f.Length))+boot)>disk.Size)throw new IOException("The boot and data partitions do not fit this USB, including free-space headroom. No drive was erased.");
        return boot;
    }
    public static string? Rejection(UsbDisk d)
    {
        if (d.Number < 0 || string.IsNullOrWhiteSpace(d.UniqueId) || string.IsNullOrWhiteSpace(d.SerialNumber) || string.IsNullOrWhiteSpace(d.Path)) return "This drive has no reliable device identity.";
        if (d.BusType != 7) return "Only USB-connected drives are supported.";
        if (d.IsBoot || d.IsSystem || d.Protected) return "This drive contains Windows, a page file, the app or its working files.";
        if (d.IsOffline || d.IsReadOnly) return "This drive is offline or read-only.";
        if (d.LogicalSectorSize != 512) return "This release requires 512-byte logical sectors.";
        if (d.Size < 8_000_000_000L) return "Use a USB drive of at least 8 GB.";
        return null;
    }
    public static void SameDevice(UsbDisk expected, UsbDisk actual)
    {
        if (Rejection(actual) is { } reason) throw new IOException(reason);
        if (expected.Number != actual.Number || expected.UniqueId != actual.UniqueId || expected.SerialNumber != actual.SerialNumber || expected.Path != actual.Path || expected.Size != actual.Size)
            throw new IOException("The selected drive changed. Refresh the drive list and review it again.");
    }
    public static long PartitionBytes(UsbDisk disk, long mediaBytes)
    {
        if (Rejection(disk) is { } reason) throw new IOException(reason);
        var size = Math.Min(MaximumBootPartition, disk.Size - (32L << 20));
        if (mediaBytes < 0 || RequiredUsbBytes(mediaBytes) - (32L << 20) > size)
            throw new IOException("The prepared Windows media does not fit the supported FAT32 boot partition, including free-space headroom.");
        return size;
    }
    public async Task<UsbDisk[]> List(CancellationToken ct = default)
    {
        var protectedRoots = new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppContext.BaseDirectory, Path.GetTempPath() }.Select(Path.GetPathRoot).Where(p => p?.Length >= 2).Select(p => p![..1]).Distinct();
        var json = await runner.PowerShell("$protected=@(" + string.Join(',', protectedRoots.Select(Policy.Ps)) + ");\n" + InventoryScript, ct);
        return JsonSerializer.Deserialize<UsbDisk[]>(json.Trim()) ?? throw new IOException("Windows returned no drive inventory.");
    }
    public static readonly string InventoryScript = """
        $pageDrives=@(Get-CimInstance Win32_PageFileUsage -ErrorAction Stop | ForEach-Object { $_.Name.Substring(0,1) })
        $allParts=@(Get-Partition -ErrorAction Stop)
        $items=@(Get-Disk -ErrorAction Stop | ForEach-Object {
          $d=$_; $parts=@($allParts | Where-Object {$_.DiskNumber -eq $d.Number})
          $letters=@($parts | Where-Object DriveLetter | ForEach-Object { [string]$_.DriveLetter })
          [pscustomobject]@{
            Number=[int]$d.Number; UniqueId=[string]$d.UniqueId; SerialNumber=([string]$d.SerialNumber).Trim(); Path=[string]$d.Path
            FriendlyName=[string]$d.FriendlyName; Size=[long]$d.Size; BusType=[int]$d.CimInstanceProperties['BusType'].Value; LogicalSectorSize=[int]$d.LogicalSectorSize
            IsBoot=[bool]$d.IsBoot; IsSystem=[bool]$d.IsSystem; IsOffline=[bool]$d.IsOffline; IsReadOnly=[bool]$d.IsReadOnly
            Protected=[bool](@($parts | Where-Object {$_.IsBoot -or $_.IsSystem}).Count -or @($letters | Where-Object {$_ -in $protected -or $_ -in $pageDrives}).Count)
            Volumes=@($letters)
          }
        })
        ConvertTo-Json -InputObject $items -Depth 4 -Compress
        """;

    public async Task<PreparedUsb> Prepare(string iso, string workspace, UsbDisk disk, CancellationToken ct)
    {
        if (Rejection(disk) is { } reason) throw new IOException(reason);
        iso = Path.GetFullPath(iso); workspace = Path.GetFullPath(workspace);
        foreach (var p in new[] { iso, workspace })
        {
            Policy.RequireUnlinkedPath(p);
            if (disk.Volumes.Contains(Path.GetPathRoot(p)![..1], StringComparer.OrdinalIgnoreCase)) throw new IOException("The ISO and working folder must be on a different drive from the USB target.");
        }
        if (!File.Exists(iso) || Directory.Exists(workspace) || File.Exists(workspace)) throw new IOException("Choose an existing ISO and a new working folder.");
        var storage=new DriveInfo(Path.GetPathRoot(workspace)!);
        if(storage.DriveType==DriveType.Network)throw new IOException("Choose a local NTFS working folder, separate from the USB target.");
        Policy.ValidateStorage(storage.DriveFormat,storage.AvailableFreeSpace,checked(new FileInfo(iso).Length * 4 + (4L << 30)),true);
        Directory.CreateDirectory(workspace);
        using var locked = new FileStream(Path.Combine(workspace,"operation.lock"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
        var media=Path.Combine(workspace,"media"); Directory.CreateDirectory(media);
        var sourceHash=Hash(iso,ct); log("Preparing a private media copy before any drive is erased.");
        await runner.PowerShell($$"""
            $iso={{Policy.Ps(iso)}}; $disk=Get-DiskImage -ImagePath $iso -ErrorAction Stop; $owned=!$disk.Attached
            try {
              if($owned){$disk=Mount-DiskImage -ImagePath $iso -PassThru -ErrorAction Stop}
              $v=$disk|Get-Volume -ErrorAction Stop
              if(!$v.DriveLetter){throw 'ISO has no accessible volume.'}
              & "$env:SystemRoot\System32\robocopy.exe" "$($v.DriveLetter):\" {{Policy.Ps(media)}} /E /COPY:DAT /DCOPY:DAT /XJ /R:1 /W:1 /NP /NFL /NDL
              if($LASTEXITCODE -ge 8){throw "ISO copy failed: $LASTEXITCODE"}
            } finally {if($owned){Dismount-DiskImage -ImagePath $iso -ErrorAction Stop|Out-Null} }
            """,ct);
        return await PrepareFiles(workspace,disk,ct,iso,sourceHash);
    }

    private async Task<PreparedUsb> PrepareFiles(string workspace,UsbDisk disk,CancellationToken ct,string? iso=null,string? sourceHash=null)
    {
        var media=Path.Combine(workspace,"media");
        foreach(var required in new[]{@"efi\boot\bootx64.efi",@"sources\boot.wim","setup.exe","bootmgr",@"boot\bcd",@"boot\bootsect.exe"})
            if(!File.Exists(Path.Combine(media,required))) throw new IOException("Windows BIOS/UEFI setup file missing: "+required);
        await ValidateBootsect(media);
        var wim=Path.Combine(media,@"sources\install.wim"); var esd=Path.Combine(media,@"sources\install.esd");
        if(File.Exists(wim) && File.Exists(esd)) throw new IOException("Media contains ambiguous WIM and ESD installation images.");
        if(!File.Exists(wim) && !File.Exists(esd)) throw new IOException("Use an ISO containing install.wim or install.esd. Pre-split custom media is not supported in this release.");
        var image=File.Exists(wim)?wim:esd;
        var indices=await runner.PowerShell($$"""
            $items=@(Get-WindowsImage -ImagePath {{Policy.Ps(image)}} -ErrorAction Stop | ForEach-Object {
              $detail=Get-WindowsImage -ImagePath {{Policy.Ps(image)}} -Index $_.ImageIndex -ErrorAction Stop
              if([int]$detail.Architecture -ne 9 -or ([version]$detail.Version).Build -lt 22000){throw 'Only Windows 11 x64 installation images are supported.'}
              [int]$_.ImageIndex
            })
            ConvertTo-Json -InputObject $items -Compress
            """,ct);
        var editions=JsonSerializer.Deserialize<int[]>(indices.Trim()) ?? [];
        if(editions.Length==0) throw new IOException("The installation image contains no editions.");
        if(image==wim && new FileInfo(image).Length>Fat32Limit && !NeedsFullDataVolume(disk))
        {
            log("Splitting the installation image for FAT32. Source ISO is unchanged.");
            if(iso is null)RecordSplitSource(workspace,wim);
            await runner.Dism("/Split-Image","/ImageFile:"+wim,"/SWMFile:"+Path.Combine(media,@"sources\install.swm"),"/FileSize:3800","/CheckIntegrity");
            var parts=Directory.GetFiles(Path.Combine(media,"sources"),"install*.swm");
            if(parts.Length==0) throw new IOException("DISM produced no split installation files.");
            File.SetAttributes(wim,FileAttributes.Normal);
            if(iso is null)File.Move(wim,Path.Combine(workspace,"usb-source.wim"),false);
            else File.Delete(wim);
        }
        ct.ThrowIfCancellationRequested();
        log("Checking all prepared setup files before USB erase confirmation.");
        var files=Scan(media,ct); var bytes=files.Sum(f=>f.Length);
        PlannedBootPartition(disk,files);
        if(iso is not null && Hash(iso,ct)!=sourceHash) throw new IOException("The input ISO changed during preparation. No drive was erased.");
        var prepared=new PreparedUsb(workspace,iso,sourceHash,files,bytes);
        File.WriteAllText(Path.Combine(workspace,"prepared.json"),JsonSerializer.Serialize(prepared,new JsonSerializerOptions{WriteIndented=true}));
        return prepared;
    }
    public static MediaFile[] Scan(string media,CancellationToken ct=default)
    {
        var files=new List<MediaFile>();
        void Visit(string dir)
        {
            Policy.RequireUnlinkedPath(dir);
            foreach(var file in Directory.EnumerateFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked media files are not supported.");
                var size=new FileInfo(file).Length;
                files.Add(new(Path.GetRelativePath(media,file),size,Hash(file,ct)));
            }
            foreach(var child in Directory.EnumerateDirectories(dir)) Visit(child);
        }
        Visit(media); if(files.Count==0) throw new IOException("Prepared media is empty."); return files.ToArray();
    }
    public static string Hash(string file)=>Hash(file,CancellationToken.None);
    internal static string Hash(string file,CancellationToken ct)=>UsbFileTransfer.Hash(file,ct,static _=>{});

    public static string VolumeLabel(string label)
    {
        label=label.Trim().ToUpperInvariant();
        if(!Regex.IsMatch(label,@"\A[A-Z0-9 _-]{1,11}\z"))throw new IOException("Use a USB label of 1–11 letters, numbers, spaces, hyphens or underscores.");
        return label;
    }
    public async Task Write(PreparedUsb prepared,UsbDisk selected,string confirmation,CancellationToken ct,string volumeLabel="TLOS_SETUP",IProgress<UsbTransferProgress>? progress=null)
    {
        volumeLabel=VolumeLabel(volumeLabel);
        if(confirmation!=$"ERASE {selected.Number}") throw new IOException("Drive erase confirmation does not match the selected disk.");
        var current=(await List(ct)).SingleOrDefault(d=>d.Number==selected.Number) ?? throw new IOException("The selected USB drive disconnected.");
        SameDevice(selected,current);
        if(prepared.Bytes!=prepared.Files.Sum(f=>f.Length))throw new IOException("Prepared media byte count differs. Nothing was erased.");
        var size=PlannedBootPartition(current,prepared.Files);var separate=SeparateData(current,prepared.Files);
        foreach(var p in new[]{prepared.Workspace,prepared.Iso,AppContext.BaseDirectory}.Where(p=>p is not null))
            if(current.Volumes.Contains(Path.GetPathRoot(Path.GetFullPath(p!))![..1],StringComparer.OrdinalIgnoreCase)) throw new IOException("The target contains an input, working folder or application.");
        var media=Path.Combine(prepared.Workspace,"media");
        var fresh=Scan(media,ct);
        if(!fresh.OrderBy(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase).SequenceEqual(prepared.Files.OrderBy(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase))) throw new IOException("Prepared media changed. Nothing was erased.");
        using var bootsectLease=new FileStream(Path.Combine(media,@"boot\bootsect.exe"),FileMode.Open,FileAccess.Read,FileShare.Read);
        await ValidateBootsect(media);
        var logPath=Path.Combine(prepared.Workspace,"usb-write.log");
        File.AppendAllText(logPath,DateTimeOffset.Now+" Erase explicitly confirmed for "+current.Label+Environment.NewLine);
        using var lease=Updates.UpdateStorage.ResourceLease("usb:"+current.UniqueId+"|"+current.SerialNumber,true);
        try
        {
            log("Erasing the confirmed USB drive and creating BIOS/UEFI media.");
            var volumeJson=await runner.PowerShell(FormatScript(current,size,volumeLabel,separate),ct);
            var volume=JsonSerializer.Deserialize<WrittenVolume>(volumeJson.Trim()) ?? throw new IOException("No formatted volume returned.");
            if(!Regex.IsMatch(volume.Root,@"\A\\\\\?\\Volume\{[0-9a-fA-F-]{36}\}\\\z")) throw new IOException("Windows returned an invalid volume GUID path.");
            if(separate && (volume.DataRoot==null || !Regex.IsMatch(volume.DataRoot,@"\A\\\\\?\\Volume\{[0-9a-fA-F-]{36}\}\\\z") || volume.DataRoot.Equals(volume.Root,StringComparison.OrdinalIgnoreCase)))throw new IOException("Windows returned no distinct data volume.");
            SameDevice(current,(await List(ct)).Single(d=>d.Number==current.Number));
            // Write boot code before copying files, so Explorer/open file handles do not
            // require a forced dismount. Never target SYS, ALL or an inferred drive.
            await runner.PowerShell(BootCodeScript(current,volume,Path.Combine(media,@"boot\bootsect.exe")),ct);
            if(separate)await runner.PowerShell(DataVolumeScript(current,volume),ct);
            var copies=prepared.Files.Select(f=>(File:f,Root:separate?volume.DataRoot!:volume.Root))
                .Concat(separate?BootFiles(prepared.Files).Select(f=>(File:f,Root:volume.Root)):[]).ToArray();
            var total=copies.Sum(c=>c.File.Length);long done=0;var reported=System.Diagnostics.Stopwatch.StartNew();
            void Report(bool verifying,long bytes,bool force=false)
            {
                if(progress is null||(!force&&reported.ElapsedMilliseconds<500))return;
                reported.Restart();progress.Report(new(verifying,bytes,total));
            }
            Report(false,0,true);
            foreach(var copy in copies)
            {
                ct.ThrowIfCancellationRequested();
                var dest=Path.Combine(copy.Root,copy.File.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var before=done;
                UsbFileTransfer.Copy(Path.Combine(media,copy.File.RelativePath),dest,copy.File.Length,ct,bytes=>Report(false,before+bytes));
                File.SetAttributes(dest,FileAttributes.Normal);done+=copy.File.Length;
            }
            Report(false,total,true);
            log("Verifying every file written to the USB drive.");
            done=0;Report(true,0,true);
            foreach(var copy in copies)
            {
                ct.ThrowIfCancellationRequested();
                var dest=Path.Combine(copy.Root,copy.File.RelativePath);
                var before=done;
                if(new FileInfo(dest).Length!=copy.File.Length || UsbFileTransfer.Hash(dest,ct,bytes=>Report(true,before+bytes))!=copy.File.Sha256) throw new IOException("USB verification failed: "+copy.File.RelativePath);
                done+=copy.File.Length;
            }
            Report(true,total,true);
            SameDevice(current,(await List(ct)).Single(d=>d.Number==current.Number));
            if(separate)await runner.PowerShell(HideBootScript(current,volume)+"\n"+DataVolumeScript(current,volume),ct);
            File.WriteAllText(Path.Combine(prepared.Workspace,"usb-complete.json"),JsonSerializer.Serialize(new{current,volume,files=prepared.Files.Length,bootCopies=copies.Length-prepared.Files.Length,hiddenBoot=separate,verified=true}));
            File.AppendAllText(logPath,"All copied files verified. Use Windows Safely Remove Hardware before unplugging."+Environment.NewLine);
        }
        catch(Exception ex){File.AppendAllText(logPath,"Incomplete USB media: "+ex.Message+Environment.NewLine);throw;}
    }
    private async Task ValidateBootsect(string media)
    {
        var path=Path.Combine(media,@"boot\bootsect.exe");
        if(!File.Exists(path)) throw new IOException("This ISO lacks Microsoft's bootsect.exe required for legacy BIOS USB boot. No drive was erased.");
        RequireBootsectExecutable(path);
        await RuntimePack.VerifyMicrosoft(runner,path);
        var metadata=await runner.PowerShell($$"""
            (Get-Item -LiteralPath {{Policy.Ps(path)}} -ErrorAction Stop).VersionInfo |
              Select-Object OriginalFilename,InternalName | ConvertTo-Json -Compress
            """);
        var identity=JsonSerializer.Deserialize<BootsectIdentity>(metadata.Trim());
        if(identity==null || !BootsectIdentityMatches(identity.OriginalFilename,identity.InternalName))
            throw new IOException("The signed media utility is not Microsoft bootsect.exe. No drive was erased.");
    }
    private record BootsectIdentity(string? OriginalFilename,string? InternalName);
    public static bool BootsectIdentityMatches(string? original,string? internalName) =>
        string.Equals(original,"bootsect.exe",StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(original,"bootsect.exe.mui",StringComparison.OrdinalIgnoreCase) && string.Equals(internalName,"bootsect.exe",StringComparison.OrdinalIgnoreCase));
    public static void RequireBootsectExecutable(string path)
    {
        using var file=File.OpenRead(path);using var reader=new BinaryReader(file);
        void Reject()=>throw new IOException("The media boot utility is not a runnable x86/x64 Windows executable. No drive was erased.");
        if(file.Length<64 || reader.ReadUInt16()!=0x5A4D)Reject();
        file.Position=0x3c;var offset=reader.ReadUInt32();
        if(offset<64 || offset>file.Length-24)Reject();
        file.Position=offset;if(reader.ReadUInt32()!=0x4550)Reject();
        var machine=reader.ReadUInt16();file.Position=offset+22;var flags=reader.ReadUInt16();
        if(machine is not 0x14c and not 0x8664 || (flags&2)==0 || (flags&0x2000)!=0)Reject();
        if(machine==0x14c && !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),"kernel32.dll")))
            throw new IOException("This Microsoft boot utility requires Windows x86 application support on the servicing host. No drive was erased.");
    }
    public static string BootCodeScript(UsbDisk disk,WrittenVolume volume,string bootsect) => $$"""
        {{IdentityGuard(disk)}}
        $letter={{Policy.Ps(volume.DriveLetter)}}
        if($letter -cnotmatch '^[A-Z]$'){throw 'Invalid USB drive letter.'}
        $p=Get-Partition -DriveLetter $letter -ErrorAction Stop
        if(@($p).Count -ne 1 -or $p.DiskNumber -ne {{disk.Number}} -or !$p.IsActive -or $p.IsBoot -or $p.IsSystem){throw 'USB boot partition identity changed.'}
        $d=Get-Disk -Number $p.DiskNumber -ErrorAction Stop
        Assert-TechLoungeUsb $d
        if([string]$d.PartitionStyle -ne 'MBR'){throw 'Legacy USB requires MBR.'}
        $v=Get-Volume -DriveLetter $letter -ErrorAction Stop
        if([string]$v.UniqueId -cne {{Policy.Ps(volume.Root)}} -or [string]$v.FileSystem -ne 'FAT32'){throw 'USB boot volume identity changed.'}
        $detail=& {{Policy.Ps(bootsect)}} /nt60 "$($letter):" /mbr | Out-String
        if($LASTEXITCODE -ne 0){throw "USB boot-code installation failed. $detail"}
        """;
    public record WrittenVolume(string Root,string DriveLetter,string? DataRoot=null,string? DataDriveLetter=null);
    public static string DataVolumeScript(UsbDisk disk,WrittenVolume volume)=>$$"""
        {{IdentityGuard(disk)}}
        $letter={{Policy.Ps(volume.DataDriveLetter??"")}}
        if($letter -cnotmatch '^[A-Z]$' -or $letter -ceq {{Policy.Ps(volume.DriveLetter)}}){throw 'Invalid separate USB data letter.'}
        $p=Get-Partition -DriveLetter $letter -ErrorAction Stop
        if(@($p).Count -ne 1 -or $p.DiskNumber -ne {{disk.Number}} -or $p.IsBoot -or $p.IsSystem -or $p.IsActive){throw 'USB data partition identity changed.'}
        Assert-TechLoungeUsb (Get-Disk -Number $p.DiskNumber -ErrorAction Stop)
        $v=Get-Volume -DriveLetter $letter -ErrorAction Stop
        if([string]$v.UniqueId -cne {{Policy.Ps(volume.DataRoot??"")}} -or [string]$v.FileSystem -ne 'NTFS'){throw 'USB data volume identity changed.'}
        """;
    public static string HideBootScript(UsbDisk disk,WrittenVolume volume)=>$$"""
        {{IdentityGuard(disk)}}
        Assert-TechLoungeUsb (Get-Disk -Number {{disk.Number}} -ErrorAction Stop)
        $bootLetter={{Policy.Ps(volume.DriveLetter)}}
        $p=Get-Partition -DriveLetter $bootLetter -ErrorAction Stop
        if(@($p).Count -ne 1 -or $p.DiskNumber -ne {{disk.Number}} -or !$p.IsActive -or $p.IsBoot -or $p.IsSystem){throw 'USB boot partition changed before hiding.'}
        $v=Get-Volume -DriveLetter $bootLetter -ErrorAction Stop
        if([string]$v.UniqueId -cne {{Policy.Ps(volume.Root)}} -or [string]$v.FileSystem -ne 'FAT32'){throw 'USB boot volume changed before hiding.'}
        # Mark the existing FAT32 boot partition as an MBR EFI System Partition.
        # Its native boot code stays active for BIOS; Windows leaves it unlettered.
        $p|Remove-PartitionAccessPath -AccessPath "$($bootLetter):\" -ErrorAction Stop
        Set-Partition -DiskNumber {{disk.Number}} -PartitionNumber $p.PartitionNumber -MbrType 239 -ErrorAction Stop
        $check=Get-Partition -DiskNumber {{disk.Number}} -PartitionNumber $p.PartitionNumber -ErrorAction Stop
        if([int]$check.MbrType -ne 239 -or !$check.IsActive -or [string]$check.DriveLetter -match '^[A-Za-z]$'){throw 'The small USB boot partition was not hidden as expected.'}
        """;
    private static string IdentityGuard(UsbDisk disk) => $$"""
        function Assert-TechLoungeUsb($disk) {
          if([string]$disk.BusType -notin @('USB','7') -or $disk.IsBoot -or $disk.IsSystem -or $disk.IsOffline -or $disk.IsReadOnly -or
             [string]$disk.UniqueId -cne {{Policy.Ps(disk.UniqueId)}} -or ([string]$disk.SerialNumber).Trim() -cne {{Policy.Ps(disk.SerialNumber)}} -or
             [string]$disk.Path -cne {{Policy.Ps(disk.Path)}} -or [long]$disk.Size -ne {{disk.Size}} -or [int]$disk.LogicalSectorSize -ne 512){throw 'USB identity or safety state changed before erase.'}
        }
        """;
    public static string FormatScript(UsbDisk disk,long partitionSize,string volumeLabel="TLOS_SETUP",bool separateData=false) => $$"""
        {{IdentityGuard(disk)}}
        $d=Get-Disk -Number {{disk.Number}} -ErrorAction Stop
        Assert-TechLoungeUsb $d
        $parts=@(Get-Partition -ErrorAction Stop | Where-Object {$_.DiskNumber -eq $d.Number})
        if(@($parts|Where-Object {$_.IsBoot -or $_.IsSystem}).Count){throw 'A protected partition is on this disk.'}
        $page=@(Get-CimInstance Win32_PageFileUsage -ErrorAction Stop | ForEach-Object {$_.Name.Substring(0,1)})
        if(@($parts|Where-Object {$_.DriveLetter -and [string]$_.DriveLetter -in $page}).Count){throw 'A page file is on this disk.'}
        $d | Clear-Disk -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop
        $d=Get-Disk -Number {{disk.Number}} -ErrorAction Stop
        Assert-TechLoungeUsb $d
        $remaining=@(Get-Partition -ErrorAction Stop | Where-Object {$_.DiskNumber -eq $d.Number})
        # Some removable-media providers expose a synthetic whole-disk FAT16
        # partition after clearing. DiskPart can create its actual MBR table.
        if([string]$d.PartitionStyle -eq 'MBR' -and $remaining.Count -eq 1 -and
           [long]$remaining[0].Offset -eq 0 -and [long]$remaining[0].Size -eq [long]$d.Size -and [int]$remaining[0].MbrType -eq 4) {
          Assert-TechLoungeUsb (Get-Disk -Number {{disk.Number}} -ErrorAction Stop)
          $layout="select disk {{disk.Number}}`nclean`nconvert mbr`nexit`n"
          $detail=$layout | & "$env:SystemRoot\System32\diskpart.exe" | Out-String
          if($LASTEXITCODE -ne 0){throw "USB partition-table creation failed. $detail"}
          $d=Get-Disk -Number {{disk.Number}} -ErrorAction Stop
          Assert-TechLoungeUsb $d
          if([string]$d.PartitionStyle -ne 'MBR'){throw "USB partition table was not created. $detail"}
          $remaining=@(Get-Partition -ErrorAction Stop | Where-Object {$_.DiskNumber -eq $d.Number})
        }
        if($remaining.Count){throw 'The confirmed USB did not clear completely.'}
        # Some removable drives remain initialized as MBR/GPT after Clear-Disk.
        # Initialize-Disk accepts RAW disks only. Legacy BIOS needs MBR.
        if([string]$d.PartitionStyle -eq 'RAW') {
          $d | Initialize-Disk -PartitionStyle MBR -ErrorAction Stop | Out-Null
          $d=Get-Disk -Number {{disk.Number}} -ErrorAction Stop
          Assert-TechLoungeUsb $d
        }
        if([string]$d.PartitionStyle -eq 'GPT') {
          Assert-TechLoungeUsb (Get-Disk -Number {{disk.Number}} -ErrorAction Stop)
          $layout="select disk {{disk.Number}}`nconvert mbr`nexit`n"
          $detail=$layout | & "$env:SystemRoot\System32\diskpart.exe" | Out-String
          if($LASTEXITCODE -ne 0){throw "USB MBR conversion failed. $detail"}
          $d=Get-Disk -Number {{disk.Number}} -ErrorAction Stop
          Assert-TechLoungeUsb $d
          if(@(Get-Partition -ErrorAction Stop|Where-Object {$_.DiskNumber -eq $d.Number}).Count){throw 'USB partition state changed during MBR conversion.'}
        }
        if([string]$d.PartitionStyle -ne 'MBR'){throw 'The confirmed USB has no verified MBR partition table.'}
        $p=New-Partition -DiskNumber {{disk.Number}} -Size {{partitionSize}} -AssignDriveLetter -ErrorAction Stop
        $p | Set-Partition -IsActive $true -ErrorAction Stop
        Assert-TechLoungeUsb (Get-Disk -Number {{disk.Number}} -ErrorAction Stop)
        $v=$p|Format-Volume -FileSystem FAT32 -NewFileSystemLabel {{Policy.Ps(separateData?"TLOS_BOOT":VolumeLabel(volumeLabel))}} -Force -Confirm:$false -ErrorAction Stop
        if(!$v.UniqueId -or !$v.DriveLetter -or [string]$v.FileSystem -ne 'FAT32'){throw 'USB volume did not format as expected.'}
        {{(separateData?$$"""
        Assert-TechLoungeUsb (Get-Disk -Number {{disk.Number}} -ErrorAction Stop)
        $data=New-Partition -DiskNumber {{disk.Number}} -UseMaximumSize -AssignDriveLetter -ErrorAction Stop
        if([long]$data.Size -lt {{disk.Size-partitionSize-(4L<<20)}}){throw 'The USB data partition did not use its available capacity.'}
        Assert-TechLoungeUsb (Get-Disk -Number {{disk.Number}} -ErrorAction Stop)
        $dataVolume=$data|Format-Volume -FileSystem NTFS -NewFileSystemLabel {{Policy.Ps(VolumeLabel(volumeLabel))}} -Force -Confirm:$false -ErrorAction Stop
        Assert-TechLoungeUsb (Get-Disk -Number {{disk.Number}} -ErrorAction Stop)
        if(!$dataVolume.UniqueId -or !$dataVolume.DriveLetter -or [string]$dataVolume.FileSystem -ne 'NTFS'){throw 'USB data volume did not format as expected.'}
        [pscustomobject]@{Root=[string]$v.UniqueId;DriveLetter=[string]$v.DriveLetter;DataRoot=[string]$dataVolume.UniqueId;DataDriveLetter=[string]$dataVolume.DriveLetter}|ConvertTo-Json -Compress
        """:"[pscustomobject]@{Root=[string]$v.UniqueId;DriveLetter=[string]$v.DriveLetter}|ConvertTo-Json -Compress")}}
        """;
}
