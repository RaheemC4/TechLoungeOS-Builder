using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TechLoungeOS;

internal static class BuiltUsbMediaChecks
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var disk=new UsbDisk(9999,"INERT","INERT","INERT","Inert USB",16_000_000_000,7,512,false,false,false,false,false,[]);
        string Create(string name)
        {
            var work=Path.Combine(root,name);var media=Path.Combine(work,"media");Directory.CreateDirectory(media);
            foreach(var relative in new[]{@"efi\boot\bootx64.efi",@"sources\boot.wim","setup.exe","bootmgr",@"boot\bcd",@"sources\install.wim"})
            {var file=Path.Combine(media,relative);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,"inert setup fixture");}
            var bootsect=Path.Combine(media,@"boot\bootsect.exe");var bytes=new byte[128];
            BitConverter.GetBytes((ushort)0x5a4d).CopyTo(bytes,0);BitConverter.GetBytes(64u).CopyTo(bytes,0x3c);
            BitConverter.GetBytes(0x4550u).CopyTo(bytes,64);BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes,68);BitConverter.GetBytes((ushort)2).CopyTo(bytes,86);File.WriteAllBytes(bootsect,bytes);
            File.WriteAllText(Path.Combine(work,"state.json"),JsonSerializer.Serialize(new{phase="USB media ready",options=new{Workspace=work,ExportIso=false}}));
            File.WriteAllBytes(Path.Combine(work,"build.lock"),[]);return work;
        }
        var workspace=Create("direct-usb");var runner=new InertRunner(disk);var engine=new BootableDrive(runner,_=>{});
        var result=await engine.PrepareBuiltMedia(workspace,disk,CancellationToken.None);
        check(result.Iso is null&&result.IsoSha256 is null&&result.Workspace==workspace&&result.Files.Length==7&&result.Bytes==242&&
            runner.Scripts.All(s=>!s.Contains("Mount-DiskImage")&&!s.Contains("robocopy"))&&Directory.GetFiles(workspace,"*.iso").Length==0,
            "Direct USB preparation hashes the seven expected files in place without an ISO, copy or disk write (native tools mocked)");
        var calls=runner.Scripts.Count;
        var loaded=BootableDrive.LoadCheckpoint(Path.Combine(workspace,"prepared.json"));
        check(loaded.Files.SequenceEqual(result.Files)&&loaded.Bytes==242,"Prepared setup can be reopened without an ISO or Windows rebuild");
        var originalCheckpoint=File.ReadAllText(Path.Combine(workspace,"prepared.json"));
        foreach(var invalid in new[]{result with{Files=[new("..\\outside",1,new string('A',64))],Bytes=1},result with{Bytes=1},result with{Files=[result.Files[0],result.Files[0]],Bytes=result.Files[0].Length*2}})
        {
            File.WriteAllText(Path.Combine(workspace,"prepared.json"),JsonSerializer.Serialize(invalid));
            try{BootableDrive.LoadCheckpoint(Path.Combine(workspace,"prepared.json"));throw new Exception("Invalid checkpoint accepted");}
            catch(IOException){check(true,"Unsafe path, forged size or duplicate prepared file is rejected before any drive operation");}
        }
        File.WriteAllText(Path.Combine(workspace,"prepared.json"),originalCheckpoint);
        var large=disk with{Size=64_000_000_000};
        check(BootableDrive.SeparateData(large,result.Files)&&!BootableDrive.SeparateData(disk,result.Files)&&BootableDrive.PlannedBootPartition(large,result.Files)==268435456,
            "A 64 GB drive with only small files gets a 256 MiB boot partition and maximum-size NTFS data; a 16 GB drive retains FAT32");
        var layout=BootableDrive.FormatScript(large,268435456,"WINDOWS 11",true);
        check(layout.Contains("-UseMaximumSize")&&layout.Contains("-FileSystem NTFS")&&layout.Contains("-NewFileSystemLabel 'WINDOWS 11'"),"The visible setup volume uses the remaining USB capacity and requested label");
        var hidden=BootableDrive.HideBootScript(large,new(@"\\?\Volume{11111111-1111-1111-1111-111111111111}\","E",@"\\?\Volume{22222222-2222-2222-2222-222222222222}\","F"));
        check(hidden.Contains("Remove-PartitionAccessPath")&&hidden.Contains("-MbrType 239")&&hidden.Contains("!$check.IsActive")&&hidden.Contains("USB boot volume changed before hiding")&&hidden.Contains("'INERT'"),
            "Boot hiding retains the active native boot partition and checks exact disk and volume identities (generated-script fixture)");
        var resumed=await engine.PrepareBuiltMedia(workspace,disk,CancellationToken.None);
        check(resumed.Files.SequenceEqual(result.Files)&&runner.Scripts.Count==calls,"Completed direct preparation reuses its checkpoint without repeating native queries");
        File.WriteAllText(Path.Combine(workspace,@"media\setup.exe"),"changed after preparation");
        try{await engine.Write(result,disk,"ERASE 9999",CancellationToken.None);throw new Exception("Changed media accepted");}
        catch(IOException error){check(error.Message.Contains("Prepared media changed")&&runner.Scripts.All(s=>!s.Contains("Clear-Disk")),"Changed direct-build media is rejected before erase");}

        var active=Create("direct-active");
        using(var lease=new FileStream(Path.Combine(active,"build.lock"),FileMode.Open,FileAccess.Read,FileShare.None))
        {
            try{await engine.PrepareBuiltMedia(active,disk,CancellationToken.None);throw new Exception("Active build accepted");}
            catch(IOException){check(!File.Exists(Path.Combine(active,"prepared.json")),"An active build cannot be reused or mutated by USB preparation");}
        }
        var wrong=Create("direct-iso-build");File.WriteAllText(Path.Combine(wrong,"state.json"),JsonSerializer.Serialize(new{phase="Complete",options=new{Workspace=wrong,ExportIso=true}}));
        var savedIso=Path.Combine(wrong,"accepted.iso");File.WriteAllText(savedIso,"accepted ISO fixture");
        var isoHash=BootableDrive.Hash(savedIso);var isoPrepared=await engine.PrepareBuiltMedia(wrong,disk,CancellationToken.None,savedIso,isoHash);
        check(isoPrepared.Files.Length==7&&File.ReadAllText(savedIso)=="accepted ISO fixture"&&runner.Scripts.All(s=>!s.Contains("Mount-DiskImage")&&!s.Contains("robocopy")),
            "ISO plus USB reuses the completed build's private media without extracting or modifying the saved ISO");
        File.WriteAllText(savedIso,"user changed saved ISO");var preparedBefore=File.ReadAllText(Path.Combine(wrong,"prepared.json"));
        try{await engine.PrepareBuiltMedia(wrong,disk,CancellationToken.None,savedIso,isoHash);throw new Exception("Changed saved ISO reused");}
        catch(IOException error){check(error.Message.Contains("changed since")&&File.ReadAllText(Path.Combine(wrong,"prepared.json"))==preparedBefore,"A changed saved ISO cannot silently reuse stale completed build files");}
        var unknown=Create("direct-unknown-split");var unknownPart=Path.Combine(unknown,@"media\sources\install.swm");File.WriteAllText(unknownPart,"unrecognised part");
        try{await engine.PrepareBuiltMedia(unknown,disk,CancellationToken.None);throw new Exception("Unknown split accepted");}
        catch(IOException){check(File.ReadAllText(unknownPart)=="unrecognised part"&&!File.Exists(Path.Combine(unknown,"prepared.json")),"Unrecognised split parts are retained and rejected without a trusted recovery receipt");}

        foreach(var changed in new[]{false,true})
        {
            var recovery=Create("direct-recovery-"+changed);var image=Path.Combine(recovery,@"media\sources\install.wim");
            File.WriteAllText(image,"owned recovery image");var expected=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("owned recovery image")));
            File.WriteAllText(Path.Combine(recovery,"usb-split-source.json"),JsonSerializer.Serialize(new{Bytes=20,Sha256=expected}));
            var backup=Path.Combine(recovery,"usb-source.wim");File.Move(image,backup);var part=Path.Combine(recovery,@"media\sources\install.swm");File.WriteAllText(part,"incomplete owned split");
            if(changed)File.WriteAllText(backup,"unexpected changed backup");
            try
            {
                var recovered=await engine.PrepareBuiltMedia(recovery,disk,CancellationToken.None);
                check(!changed&&File.ReadAllText(image)=="owned recovery image"&&!File.Exists(part)&&!File.Exists(backup)&&recovered.Files.Length==7,
                    "Interrupted split preparation restores only its checksum-matched source and removes owned partial parts");
            }
            catch(IOException) when(changed)
            {check(File.ReadAllText(backup)=="unexpected changed backup"&&File.ReadAllText(part)=="incomplete owned split"&&!File.Exists(image),"A changed recovery source is retained without moving or deleting files");}
        }
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();var stopped=Create("direct-cancel");
        try{await engine.PrepareBuiltMedia(stopped,disk,cancelled.Token);throw new Exception("Cancelled preparation ran");}
        catch(OperationCanceledException){check(!File.Exists(Path.Combine(stopped,"usb-preparation.lock")),"Cancellation before direct preparation changes no working files or disks");}
    }
    private sealed class InertRunner(UsbDisk disk):ICommandRunner
    {
        internal List<string> Scripts {get;}=[];
        public Task<string> PowerShell(string script,CancellationToken ct=default)
        {
            Scripts.Add(script);
            if(script.Contains("Get-AuthenticodeSignature"))return Task.FromResult("");
            if(script.Contains(".VersionInfo"))return Task.FromResult("{\"OriginalFilename\":\"bootsect.exe\",\"InternalName\":\"bootsect.exe\"}");
            if(script.Contains("$items=@(Get-WindowsImage"))return Task.FromResult("[1]");
            if(script.Contains("$items=@(Get-Disk"))return Task.FromResult(JsonSerializer.Serialize(new[]{disk}));
            throw new Exception("Unexpected native operation in inert USB preparation");
        }
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)=>throw new Exception("Inert fixture cannot execute native tools");
        public Task<string> Dism(params string[] args)=>throw new Exception("Inert fixture cannot service or split a real image");
    }
}
