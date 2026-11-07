using System.Text.Json;
using TechLoungeOS;

static class UsbChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        check(BootableDrive.BootsectIdentityMatches("bootsect.exe.mui","bootsect.exe") && BootableDrive.BootsectIdentityMatches("BOOTSECT.EXE",null),
            "genuine bootsect MUI-backed and neutral version identities are accepted");
        check(!BootableDrive.BootsectIdentityMatches("bootsect.exe.mui","other.exe") && !BootableDrive.BootsectIdentityMatches("notepad.exe","bootsect.exe") && !BootableDrive.BootsectIdentityMatches(null,null),
            "missing or unrelated signed executable metadata cannot impersonate bootsect");
        var bootHeaderFixture=Path.Combine(root,"bootsect-header.exe");
        void Header(ushort machine,ushort flags,uint offset=64)
        {
            var bytes=new byte[128];BitConverter.GetBytes((ushort)0x5A4D).CopyTo(bytes,0);BitConverter.GetBytes(offset).CopyTo(bytes,0x3c);
            if(offset==64){BitConverter.GetBytes(0x4550u).CopyTo(bytes,64);BitConverter.GetBytes(machine).CopyTo(bytes,68);BitConverter.GetBytes(flags).CopyTo(bytes,86);}
            File.WriteAllBytes(bootHeaderFixture,bytes);
        }
        Header(0x8664,2);BootableDrive.RequireBootsectExecutable(bootHeaderFixture);
        check(true,"boot utility validation accepts a runnable x64 PE header without executing it");
        foreach(var bad in new[]{(machine:(ushort)0x8664,flags:(ushort)0x2002,offset:64u),(machine:(ushort)0xaa64,flags:(ushort)2,offset:64u),(machine:(ushort)0x8664,flags:(ushort)0,offset:64u),(machine:(ushort)0x8664,flags:(ushort)2,offset:uint.MaxValue)})
        {
            Header(bad.machine,bad.flags,bad.offset);
            try{BootableDrive.RequireBootsectExecutable(bootHeaderFixture);throw new Exception("Expected boot utility rejection");}
            catch(IOException){check(true,"resource DLL,unsupported architecture,non-executable or invalid header is rejected before erase");}
        }
        var good=new UsbDisk(99,"usb-unique","SERIAL-99",@"\\?\usbstor#fixture","Fixture USB",64L<<30,7,512,false,false,false,false,false,["R"]);
        check(BootableDrive.Rejection(good)==null,"USB eligibility accepts an identified non-system USB disk");
        check(BootableDrive.Rejection(good with{Size=8_000_000_000L})==null&&BootableDrive.PartitionBytes(good with{Size=8_000_000_000L},6_000_000_000L)>6_500_000_000L,"a retail 8 GB drive is eligible when the actual media and headroom fit");
        check(BootableDrive.Rejection(good with{Size=7_999_999_999L})!=null,"USB minimum capacity uses the advertised decimal GB unit");
        foreach(var bad in new[]{good with{IsBoot=true},good with{IsSystem=true},good with{Protected=true},good with{BusType=17},good with{SerialNumber=""},good with{UniqueId=""},good with{LogicalSectorSize=4096},good with{IsReadOnly=true},good with{IsOffline=true}})
            check(BootableDrive.Rejection(bad)!=null,"USB rejects unsafe disk state: "+JsonSerializer.Serialize(bad));
        try{BootableDrive.SameDevice(good,good with{SerialNumber="replacement"});throw new Exception("Expected identity rejection");}catch(IOException){check(true,"reused disk number cannot substitute another USB drive");}
        check(BootableDrive.PartitionBytes(good,12L<<30)==31L<<30,"large USB uses a bounded FAT32 boot partition");
        try{BootableDrive.PartitionBytes(good,31L<<30);throw new Exception("Expected capacity rejection");}catch(IOException){check(true,"USB capacity includes filesystem and working headroom before erase");}
        MediaFile[] large=[new("bootmgr",4096,""),new(@"boot\bcd",4096,""),new(@"boot\bootsect.exe",65536,""),new(@"efi\boot\bootx64.efi",4096,""),new(@"sources\boot.wim",700L<<20,""),new(@"sources\install.esd",7_000_000_000L,""),new(@"sources\$OEM$\Office.dat",3_000_000_000L,"")];
        check(BootableDrive.SeparateData(large)&&BootableDrive.BootPartitionBytes(large)==771L<<20&&BootableDrive.RequiredMediaBytes(large)==12_112_958_464L,
            "large ESD plans a771MiB FAT32 boot volume and budgets the separate NTFS data, duplicated boot files and headroom");
        check(BootableDrive.PlannedBootPartition(good with{Size=16_000_000_000L},large)==771L<<20&&BootableDrive.BootFiles(large).Length==5,
            "large installation and Office files stay on NTFS; only the existing boot chain is duplicated onto FAT32");
        var ordinary=large.Select(f=>f.RelativePath==@"sources\install.esd"?f with{RelativePath=@"sources\install.wim"}:f).ToArray();
        check(BootableDrive.RequiredIsoUsbBytes(ordinary)==11_304_506_368L&&BootableDrive.RequiredIsoUsbBytes(large)==12_112_958_464L,
            "ISO budgeting retains ordinary WIM splitting and charges dual partitions only for files that require NTFS");
        try{BootableDrive.PlannedBootPartition(good with{Size=8_000_000_000L},large);throw new Exception("Oversized dual layout accepted");}catch(IOException){check(true,"combined boot/data capacity is rejected before erase");}
        try{BootableDrive.PlannedBootPartition(good with{Size=3L<<40},large);throw new Exception("Oversized MBR disk accepted");}catch(IOException){check(true,"dual-partition drives beyond the MBR address limit are rejected before erase");}
        try{BootableDrive.BootPartitionBytes(large.Where(f=>f.RelativePath!="bootmgr").ToArray());throw new Exception("Missing boot file accepted");}catch(IOException){check(true,"a large data image cannot bypass required boot files");}
        try{BootableDrive.BootPartitionBytes(large.Select(f=>f.RelativePath==@"sources\boot.wim"?f with{Length=5L<<30}:f).ToArray());throw new Exception("Oversized boot image accepted");}catch(IOException){check(true,"a boot image exceeding FAT32 is rejected without changing its contents");}
        var work=Path.Combine(root,"usb-prepared");var media=Path.Combine(work,"media");Directory.CreateDirectory(media);
        File.WriteAllText(Path.Combine(media,"setup.exe"),"fixture only; never executed");
        var files=BootableDrive.Scan(media);var prepared=new PreparedUsb(work,Path.Combine(root,"source.iso"),"unused",files,files.Sum(f=>f.Length));
        var runner=new ReadOnlyUsbRunner(good);var engine=new BootableDrive(runner,_=>{});
        try{await engine.Write(prepared,good,"ERASE 0",CancellationToken.None);throw new Exception("Expected confirmation rejection");}catch(IOException ex){check(ex.Message.Contains("confirmation")&&runner.Calls==0,"wrong erase confirmation executes no commands");}
        var byteRunner=new ReadOnlyUsbRunner(good);
        try{await new BootableDrive(byteRunner,_=>{}).Write(prepared with{Bytes=0},good,"ERASE 99",CancellationToken.None);throw new Exception("Expected byte-count rejection");}
        catch(IOException error){check(error.Message.Contains("byte count differs")&&byteRunner.Calls==1,"a forged prepared byte count cannot bypass capacity checking or start formatting");}
        File.AppendAllText(Path.Combine(media,"setup.exe")," tampered");
        try{await engine.Write(prepared,good,"ERASE 99",CancellationToken.None);throw new Exception("Expected media rejection");}catch(IOException ex){check(ex.Message.Contains("Prepared media changed")&&runner.Calls==1,"changed staged bytes block USB formatting");}
        var ps=new ProcessRunner(_=>{});
        var nativeDisks=await new BootableDrive(ps,_=>{}).List();
        check(nativeDisks.Any(d=>d.IsBoot&&d.Protected),"read-only native Windows drive inventory resolves CIM bus values and protects the boot disk");
        var script=BootableDrive.FormatScript(good,20L<<30);
        var path=Path.Combine(root,"usb-format.ps1");File.WriteAllText(path,script);
        await ps.PowerShell("$t=$null;$e=$null;[void][Management.Automation.Language.Parser]::ParseFile("+Policy.Ps(path)+",[ref]$t,[ref]$e);if($e.Count){throw ($e|Out-String)}");
        check(true,"USB format script parses in Windows PowerShell 5.1");
        await ps.PowerShell($$"""
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Policy.Ps(path)}},[ref]$tokens,[ref]$errors)
            $storage=@('Get-Disk','Get-Partition','Clear-Disk','Initialize-Disk','New-Partition','Set-Partition','Format-Volume')
            foreach($command in $ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst]},$true)){
              $name=$command.GetCommandName()
              if($name -notin $storage){continue}
              $native=Get-Command $name -CommandType Function,Cmdlet -ErrorAction Stop
              foreach($argument in $command.CommandElements){
                if($argument -is [Management.Automation.Language.CommandParameterAst] -and !$native.Parameters.ContainsKey($argument.ParameterName)){
                  throw "$name does not support parameter $($argument.ParameterName)"
                }
              }
            }
            """);
        check(true,"USB formatting parameters exist on the real Windows storage commands before storage is mocked");
        var header="""
            function Get-Disk { [pscustomobject]@{Number=99;BusType=7;IsBoot=$false;IsSystem=$false;IsOffline=$false;IsReadOnly=$false;UniqueId='usb-unique';SerialNumber='WRONG';Path='fixture';Size=68719476736;LogicalSectorSize=512} }
            function Clear-Disk { throw 'A destructive command must not run' }
            """;
        try{await ps.PowerShell(header+script);throw new Exception("Expected guarded script rejection");}catch(IOException ex){check(ex.Message.Contains("identity or safety state changed")&&!ex.Message.Contains("A destructive command must not run"),"native USB erase script rechecks identity before any destructive command");}
        foreach(var bus in new[]{"7","'USB'"})
        {
            var positive=header.Replace("BusType=7","BusType="+bus).Replace("SerialNumber='WRONG'","SerialNumber="+Policy.Ps(good.SerialNumber)).Replace("Path='fixture'","Path="+Policy.Ps(good.Path));
            positive+="\nfunction Get-Partition { @() }\nfunction Get-CimInstance { @() }\nfunction Clear-Disk { throw 'VALIDATED_BEFORE_ERASE' }\n";
            try{await ps.PowerShell(positive+script);throw new Exception("Expected fixture stop before erase");}
            catch(IOException ex){check(ex.Message.Contains("VALIDATED_BEFORE_ERASE"),"USB identity validation accepts the native text bus type and numeric fixture type before the mocked erase boundary");}
        }
        var simulation="""
            $script:style='__STYLE__';$script:initialized=0;$script:created=0;$script:formatted=0;$script:reads=0;$script:swap=$false
            function Get-Disk {
              [CmdletBinding()]param([int]$Number)
              $script:reads++;$serial=if($script:swap -and $script:reads -ge 2){'replacement'}else{'SERIAL-99'}
              [pscustomobject]@{Number=99;BusType='USB';IsBoot=$false;IsSystem=$false;IsOffline=$false;IsReadOnly=$false;UniqueId='usb-unique';SerialNumber=$serial;Path=__PATH__;Size=68719476736;LogicalSectorSize=512;PartitionStyle=$script:style}
            }
            function Get-Partition { [CmdletBinding()]param([int]$DiskNumber) @() }
            function Get-CimInstance { [CmdletBinding()]param([string]$ClassName) @() }
            function Clear-Disk {
              [CmdletBinding(SupportsShouldProcess=$true)]param([Parameter(ValueFromPipeline=$true)]$InputObject,[switch]$RemoveData,[switch]$RemoveOEM)
              process { }
            }
            function Initialize-Disk {
              [CmdletBinding()]param([Parameter(ValueFromPipeline=$true)]$InputObject,[string]$PartitionStyle)
              process {if($script:style -ne 'RAW'){throw 'Fixture disk is already initialized'};$script:initialized++;$script:style=$PartitionStyle}
            }
            function New-Partition {
              [CmdletBinding()]param([int]$DiskNumber,[long]$Size,[switch]$AssignDriveLetter)
              $script:created++;[pscustomobject]@{DiskNumber=$DiskNumber;Size=$Size}
            }
            function Set-Partition {
              [CmdletBinding()]param([Parameter(ValueFromPipeline=$true)]$InputObject,[bool]$IsActive)
              process {if(!$IsActive){throw 'Legacy boot partition must be active'}}
            }
            function Invoke-FixtureDiskpart {
              $text=$input|Out-String
              if($text.Trim() -ne "select disk 99`nconvert mbr`nexit" -or $script:style -ne 'GPT'){throw 'Unexpected MBR conversion'}
              $script:style='MBR';$global:LASTEXITCODE=0
            }
            function Format-Volume {
              [CmdletBinding(SupportsShouldProcess=$true)]param([Parameter(ValueFromPipeline=$true)]$InputObject,[string]$FileSystem,[string]$NewFileSystemLabel,[switch]$Force)
              process {if($FileSystem -ne 'FAT32'){throw 'Expected FAT32'};$script:formatted++;[pscustomobject]@{UniqueId='\\?\Volume{12345678-1234-1234-1234-123456789abc}\';DriveLetter='R';FileSystem=$FileSystem}}
            }
            """;
        simulation=simulation.Replace("__PATH__",Policy.Ps(good.Path));
        foreach(var style in new[]{"RAW","MBR","GPT"})
        {
            var fixtureScript=script.Replace("& \"$env:SystemRoot\\System32\\diskpart.exe\"","& Invoke-FixtureDiskpart");
            var result=await ps.PowerShell(simulation.Replace("__STYLE__",style)+"\n$volumeJson=& {\n"+fixtureScript+"\n};[pscustomobject]@{volume=($volumeJson|ConvertFrom-Json);initialized=$script:initialized;created=$script:created;formatted=$script:formatted;style=$script:style}|ConvertTo-Json -Depth 3");
            using var parsed=JsonDocument.Parse(result.Trim());var state=parsed.RootElement;
            check(state.GetProperty("style").GetString()=="MBR"&&state.GetProperty("initialized").GetInt32()==(style=="RAW"?1:0)&&state.GetProperty("created").GetInt32()==1&&state.GetProperty("formatted").GetInt32()==1&&state.GetProperty("volume").GetProperty("Root").GetString()==@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\",
                "USB formatting produces active MBR/FAT32 from "+style+" (storage commands mocked)");
        }
        var dual=BootableDrive.FormatScript(good,771L<<20,"TLOS26H2",true);
        var dualFixture=simulation.Replace("__STYLE__","MBR")+"""

            function New-Partition {
              [CmdletBinding()]param([int]$DiskNumber,[long]$Size,[switch]$UseMaximumSize,[switch]$AssignDriveLetter)
              $script:created++
              if(($script:created -eq 1 -and ($UseMaximumSize -or $Size -ne 808452096)) -or ($script:created -eq 2 -and !$UseMaximumSize)){throw 'Wrong boot/data partition sizes'}
              [pscustomobject]@{DiskNumber=$DiskNumber;Number=$script:created}
            }
            function Format-Volume {
              [CmdletBinding(SupportsShouldProcess=$true)]param([Parameter(ValueFromPipeline=$true)]$InputObject,[string]$FileSystem,[string]$NewFileSystemLabel,[switch]$Force)
              process {
                $script:formatted++
                if($InputObject.Number -eq 1){if($FileSystem -ne 'FAT32' -or $NewFileSystemLabel -ne 'TLOS_BOOT'){throw 'Wrong boot format/label'};[pscustomobject]@{UniqueId='\\?\Volume{12345678-1234-1234-1234-123456789abc}\';DriveLetter='R';FileSystem='FAT32'}}
                elseif($InputObject.Number -eq 2){if($FileSystem -ne 'NTFS' -or $NewFileSystemLabel -ne 'TLOS26H2'){throw 'Wrong data format/label'};[pscustomobject]@{UniqueId='\\?\Volume{22345678-1234-1234-1234-123456789abc}\';DriveLetter='T';FileSystem='NTFS'}}
                else{throw 'Unexpected additional partition'}
              }
            }
            """;
        var dualResult=await ps.PowerShell(dualFixture+"\n$volumeJson=& {\n"+dual+"\n};if($script:created -ne 2 -or $script:formatted -ne 2){throw 'Incomplete dual layout'};$volumeJson");
        var dualVolume=JsonSerializer.Deserialize<BootableDrive.WrittenVolume>(dualResult.Trim())!;
        check(dualVolume.DriveLetter=="R"&&dualVolume.DataDriveLetter=="T"&&dualVolume.DataRoot!=dualVolume.Root,
            "dual layout formats exactly FAT32 boot and NTFS data partitions with distinct verified volume identities (storage mocked)");
        var dataGuard=BootableDrive.DataVolumeScript(good,dualVolume);
        var dataFixture=simulation.Replace("__STYLE__","MBR")+"""

            function Get-Partition { [CmdletBinding()]param([string]$DriveLetter) if($DriveLetter -ne 'T'){throw 'Wrong data letter'};[pscustomobject]@{DiskNumber=99;IsActive=$false;IsBoot=$false;IsSystem=$false} }
            function Get-Volume { [CmdletBinding()]param([string]$DriveLetter) [pscustomobject]@{UniqueId='\\?\Volume{22345678-1234-1234-1234-123456789abc}\';FileSystem='NTFS'} }
            """;
        check((await ps.PowerShell(dataFixture+dataGuard+"\n'DATA_VERIFIED'")).Contains("DATA_VERIFIED"),"the data destination must resolve to the same USB and its distinct NTFS volume");
        foreach(var bad in new[]{dataFixture.Replace("DiskNumber=99;IsActive","DiskNumber=0;IsActive"),dataFixture.Replace("FileSystem='NTFS'","FileSystem='FAT32'"),dataFixture.Replace("SERIAL-99","replacement")})
            check((await ps.PowerShell(bad+"\ntry{\n"+dataGuard+"\nthrow 'Expected refusal'}catch{if($_.Exception.Message -eq 'Expected refusal'){throw};'DATA_BLOCKED'}")).Contains("DATA_BLOCKED"),"substituted data disk,filesystem or USB identity blocks copying (storage mocked)");
        var bootVolume=new BootableDrive.WrittenVolume(@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\","R");
        var bootScript=BootableDrive.BootCodeScript(good,bootVolume,"fixture-bootsect").Replace("& 'fixture-bootsect'","& Invoke-FixtureBootsect");
        var bootFixture=simulation.Replace("__STYLE__","MBR")+"""

            function Get-Partition { [CmdletBinding()]param([string]$DriveLetter) [pscustomobject]@{DiskNumber=99;IsActive=$true;IsBoot=$false;IsSystem=$false} }
            function Get-Volume { [CmdletBinding()]param([string]$DriveLetter) [pscustomobject]@{UniqueId='\\?\Volume{12345678-1234-1234-1234-123456789abc}\';FileSystem='FAT32'} }
            function Invoke-FixtureBootsect {
              if(($args -join '|') -cne '/nt60|R:|/mbr'){throw 'Wrong boot-code destination'}
              $script:bootWritten=$true;$global:LASTEXITCODE=0
            }
            """;
        check((await ps.PowerShell(bootFixture+bootScript+"\nif(!$script:bootWritten){throw 'Boot code missing'};'BOOT_WRITTEN'")).Contains("BOOT_WRITTEN"),"boot code targets only the verified USB partition with NT60 and MBR (native executable mocked)");
        foreach(var invalid in new[]{bootFixture.Replace("DiskNumber=99;IsActive","DiskNumber=0;IsActive"),bootFixture.Replace("IsActive=$true","IsActive=$false"),bootFixture.Replace("SERIAL-99","replacement"),bootFixture.Replace("FileSystem='FAT32'","FileSystem='NTFS'")})
        {
            check((await ps.PowerShell(invalid+"\ntry{\n"+bootScript+"\nthrow 'Expected refusal'}catch{if($script:bootWritten -or $_.Exception.Message -eq 'Expected refusal'){throw};'BOOT_BLOCKED'}")).Contains("BOOT_BLOCKED"),"changed disk/partition/volume identity blocks boot-code writes (native executable mocked)");
        }
        var changed=simulation.Replace("__STYLE__","MBR")+"\n$script:swap=$true;try{\n"+script+"\n;throw 'Expected swap rejection'}catch{if($_.Exception.Message -notlike '*identity or safety state changed*' -or $script:created -ne 0){throw};'SWAP_BLOCKED_BEFORE_PARTITION'}";
        check((await ps.PowerShell(changed)).Contains("SWAP_BLOCKED_BEFORE_PARTITION"),"device substitution after clear is rejected before any new partition is created (storage commands mocked)");
        // Replace only the external executable boundary; no host storage mutation.
        var removable=simulation.Replace("__STYLE__","MBR")+"""

            $script:layoutWritten=$false
            function Get-Partition {
              [CmdletBinding()]param()
              if(!$script:layoutWritten){[pscustomobject]@{DiskNumber=99;Offset=0;Size=68719476736;MbrType=4}}
            }
            function Invoke-FixtureDiskpart {
              $text=$input|Out-String
              if($text.Trim() -ne "select disk 99`nclean`nconvert mbr`nexit"){throw 'Wrong disk/table commands'}
              $script:layoutWritten=$true;$global:LASTEXITCODE=0
            }
            """;
        var removableScript=script.Replace("& \"$env:SystemRoot\\System32\\diskpart.exe\"","& Invoke-FixtureDiskpart");
        var fallback=await ps.PowerShell(removable+"\n"+removableScript+"\nif(!$script:layoutWritten -or $script:created -ne 1 -or $script:formatted -ne 1){throw 'Incomplete fallback'};'REMOVABLE_FORMATTED'");
        check(fallback.Contains("REMOVABLE_FORMATTED"),"whole-disk removable placeholder creates an MBR table then formats the bounded partition (storage commands mocked)");
        foreach(var failure in new[]{"Offset=4096","Size=1234","MbrType=7"})
        {
            var partition="DiskNumber=99;Offset=0;Size=68719476736;MbrType=4";
            var property=failure.Split('=')[0];
            var original=property+"="+(property=="Offset"?"0":property=="Size"?"68719476736":"4");
            var invalid=removable.Replace(partition,partition.Replace(original,failure));
            var stopped=invalid+"\ntry{\n"+removableScript+"\nthrow 'Expected leftover partition rejection'}catch{if($_.Exception.Message -notlike '*did not clear completely*' -or $script:created -ne 0 -or $script:layoutWritten){throw};'LEFTOVER_REJECTED'}";
            check((await ps.PowerShell(stopped)).Contains("LEFTOVER_REJECTED"),"unrecognised remaining partition blocks fallback: "+failure);
        }
        var ineffective=removable+"\nfunction Invoke-FixtureDiskpart { $null=$input|Out-String;$global:LASTEXITCODE=0 }\ntry{\n"+removableScript+"\nthrow 'Expected postcondition rejection'}catch{if($_.Exception.Message -notlike '*did not clear completely*' -or $script:created -ne 0){throw};'NO_PARTITION_CREATED'}";
        check((await ps.PowerShell(ineffective)).Contains("NO_PARTITION_CREATED"),"DiskPart exit zero cannot bypass verification of an empty partition table (storage commands mocked)");
    }
    private sealed class ReadOnlyUsbRunner(UsbDisk disk):ICommandRunner
    {
        public int Calls;
        public Task<string> PowerShell(string script,CancellationToken ct=default){Calls++;if(!script.Contains("$items=@(Get-Disk"))throw new Exception("Unexpected destructive request");return Task.FromResult(JsonSerializer.Serialize(new[]{disk}));}
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)=>throw new Exception("No native commands allowed");
        public Task<string> Dism(params string[] args)=>throw new Exception("No servicing allowed");
    }
}
