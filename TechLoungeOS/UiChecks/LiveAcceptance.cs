using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using TechLoungeOS;
using TechLoungeOS.Updates;

// Explicit VM-only acceptance entry point. Never selects a disk without its owned serial.
internal static class LiveAcceptance
{
    internal static bool OwnedBuilder(string? manufacturer,IEnumerable<string?> serials)=>
        string.Equals(manufacturer,"QEMU",StringComparison.OrdinalIgnoreCase) && serials.Count(s=>s!=null &&
            System.Text.RegularExpressions.Regex.IsMatch(s.Trim(),@"\ATLOS-SCRATCH114(?:\s+_00000001\.)?\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant))==1;
    internal static bool OwnedUsb(string? serial)=>serial is "TLOS-USB-114" or "TLOS-USB-347";
    private record Options(string Action,string Output,string? Iso=null,string? Workspace=null,string? Manifest=null,string? Package=null,string? Serial=null,string? ExpectedLauncherSha256=null,string? Destination=null);
    public static async Task Run(string path)
    {
        var o=JsonSerializer.Deserialize<Options>(File.ReadAllText(path))??throw new IOException("Missing live-test configuration.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.Output))!);
        var logGate=new object();
        void Log(string message){lock(logGate){Console.WriteLine(message);File.AppendAllText(o.Output+".log",message+Environment.NewLine);}}
        try
        {
            var vm=await new ProcessRunner(Log).PowerShell("$s=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop; [pscustomobject]@{Manufacturer=[string]$s.Manufacturer;Model=[string]$s.Model;Serials=@(Get-Disk -ErrorAction Stop|ForEach-Object {([string]$_.SerialNumber).Trim()})}|ConvertTo-Json -Compress");
            using(var identity=JsonDocument.Parse(vm.Trim()))
            {
                var system=identity.RootElement;
                if(!OwnedBuilder(system.GetProperty("Manufacturer").GetString(),system.GetProperty("Serials").EnumerateArray().Select(s=>s.GetString())))
                    throw new IOException("Live acceptance requires the owned QEMU builder and its uniquely identified scratch disk.");
            }
            if(o.Action=="storage")
            {
                using var identity=WindowsIdentity.GetCurrent();
                if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new IOException("Native storage check requires an elevated disposable VM.");
                var stage=UpdateStorage.Stage();File.WriteAllText(Path.Combine(stage,"probe.txt"),"native protected storage probe");
                using(var lease=UpdateStorage.ResourceLease("native-vm-probe",true)){}
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{stage,securedStorage=true,windows=Environment.OSVersion.Version.ToString()}));
            }
            else if(o.Action=="smoke")
            {
                using var app=Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,UpdateTrust.MainExe)){UseShellExecute=true})??throw new IOException("Builder did not start.");
                await Task.Delay(15000);app.Refresh();var className=new System.Text.StringBuilder(256);
                GetClassName(app.MainWindowHandle,className,256);
                if(app.HasExited||!className.ToString().StartsWith("HwndWrapper",StringComparison.Ordinal))throw new IOException("Builder did not open its native WPF window.");
                var title=app.MainWindowTitle;
                if(!app.CloseMainWindow()||!app.WaitForExit(30000))throw new IOException("Builder did not close normally.");
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{opened=true,closedNormally=true,title,version=FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory,UpdateTrust.MainExe)).FileVersion}));
            }
            else if(o.Action=="inventory")
            {
                var data=await new ProcessRunner(Log).PowerShell("[pscustomobject]@{disks=@(Get-Disk|Select-Object Number,FriendlyName,SerialNumber,BusType,Size,IsBoot,IsSystem,PartitionStyle);secureBoot=(Confirm-SecureBootUEFI);tpm=(Get-Tpm|Select-Object TpmPresent,TpmReady);offreg=(Test-Path C:\\Windows\\System32\\offreg.dll);builder=(Get-Item C:\\TLOS\\app\\TechLoungeOS.Builder.exe).VersionInfo.FileVersion;notes=[IO.File]::ReadAllText('C:\\TLOS\\app\\user-notes.txt');backups=@(Get-ChildItem C:\\TLOS\\app -Directory -Filter '.techloungeos-rollback-*'|Select-Object -ExpandProperty Name)}|ConvertTo-Json -Depth 4");
                File.WriteAllText(o.Output,data);
            }
            else if(o.Action=="update-audit")
            {
                var root=UpdateStorage.Root();
                var logs=Directory.GetFiles(root,"apply.log",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllText);
                var journals=Directory.GetDirectories(AppContext.BaseDirectory,".techloungeos-rollback-*").SelectMany(d=>Directory.GetFiles(d,"*.json")).ToDictionary(p=>p,File.ReadAllText);
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{version=FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory,UpdateTrust.MainExe)).FileVersion,logs,journals,notes=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"user-notes.txt"))}));
            }
            else if(o.Action=="build-status")
            {
                string Shared(string path)
                {
                    using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    if(stream.Length>10<<20)throw new IOException("Test status file exceeds its bound.");
                    using var reader=new StreamReader(stream);return reader.ReadToEnd();
                }
                var state=Shared(@"S:\ghost-build\state.json");
                var tail=Shared(@"S:\ghost-build\build.log").Split('\n').TakeLast(20).Select(s=>s.Length>2048?s[..2048]:s).ToArray();
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{state,tail,exportExists=File.Exists(@"S:\Ghost-animated.iso")}));
            }
            else if(o.Action=="pe-media")
            {
                // Independent PE/USB acceptance while the full Tweaks integration is blocked.
                // Fixed paths belong only to the two-serial-guarded disposable VM above.
                const string work=@"S:\pe-only-142",media=@"S:\pe-only-142\media",iso=@"S:\PE-only.iso";
                if(Directory.Exists(work)||File.Exists(iso))throw new IOException("PE acceptance needs fresh private destinations.");
                if(BootableDrive.Hash(@"S:\Source.iso")!="1784E5FD9CAE707647EFA97B09071C85AFF28791A9EB5A9F6E73FBCE2A962ADC")throw new IOException("Owned source copy changed.");
                Directory.CreateDirectory(work);var run=new ProcessRunner(Log,Path.Combine(work,"scratch"));
                await run.PowerShell("$d=Get-DiskImage -ImagePath S:\\Source.iso; $owned=!$d.Attached; try { if($owned){$d=Mount-DiskImage -ImagePath S:\\Source.iso -PassThru}; $v=$d|Get-Volume; if(!$v.DriveLetter){throw 'Source has no volume'}; & robocopy.exe ($v.DriveLetter+':\\') 'S:\\pe-only-142\\media' /E /COPY:DAT /DCOPY:DAT /R:0 /W:0 /XJ /NFL /NDL /NP; if($LASTEXITCODE -ge 8){throw 'Copy failed'}; Get-ChildItem 'S:\\pe-only-142\\media' -Recurse -File|ForEach-Object {$_.IsReadOnly=$false} } finally {if($owned){Dismount-DiskImage -ImagePath S:\\Source.iso|Out-Null}}");
                Dictionary<string,string> Snapshot()=>Directory.EnumerateFiles(media,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(media,p),BootableDrive.Hash,StringComparer.OrdinalIgnoreCase);
                var before=Snapshot();
                await WinPeBackground.Prepare(run,media,work,Log,CancellationToken.None);
                var after=Snapshot();
                if(before.Count!=after.Count||before.Any(p=>p.Key!=@"sources\boot.wim"&&(!after.TryGetValue(p.Key,out var h)||h!=p.Value)))throw new IOException("Original media outside boot.wim changed.");
                var originalHash=BootableDrive.Hash(@"S:\Source.iso");
                if(originalHash!="1784E5FD9CAE707647EFA97B09071C85AFF28791A9EB5A9F6E73FBCE2A962ADC")throw new IOException("Source copy changed during PE preparation.");
                if(WindowsIdentity.GetCurrent().ImpersonationLevel!=TokenImpersonationLevel.None)throw new IOException("Image-read impersonation was not restored.");
                await IsoExport.Create(run,media,iso,@"D:\tools\oscdimg.exe",Log,CancellationToken.None);
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{iso,filesPreservedOutsideBoot=before.Count-1,originalSha256=originalHash,windowsImageServiced=false,peOnly=true,impersonationRestored=true}));
            }
            else if(o.Action=="pe-finish")
            {
                // Resume only the fully committed, preservation-checked PE checkpoint after
                // the earlier harness log collision. Recheck source/media independently.
                const string work=@"S:\pe-only-142",media=@"S:\pe-only-142\media",iso=@"S:\PE-only.iso";
                using var audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(work,"pe-preservation.json")));
                Dictionary<string,string> Map(JsonElement value)=>JsonSerializer.Deserialize<Dictionary<string,string>>(value.GetRawText())!.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
                var before=Map(audit.RootElement.GetProperty("before"));var after=Map(audit.RootElement.GetProperty("after"));
                var added=new[]{@"Windows\System32\TechLoungeOS\PeBackground.exe",@"Windows\System32\TechLoungeOS\winpeshl.original.ini"};
                if(after.Count!=before.Count+2||!after.Keys.Except(before.Keys,StringComparer.OrdinalIgnoreCase).Order().SequenceEqual(added.Order())||before.Any(p=>p.Key!=@"Windows\System32\winpeshl.ini"&&(!after.TryGetValue(p.Key,out var h)||h!=p.Value)))throw new IOException("PE checkpoint preservation is incomplete.");
                if((await new ProcessRunner(Log).PowerShell("@(Get-WindowsImage -Mounted|Where-Object {$_.Path -like 'S:\\pe-only-142*'}).Count")).Trim()!="0")throw new IOException("PE checkpoint is still mounted.");
                var run=new ProcessRunner(Log,Path.Combine(work,"scratch"));
                var sourceJson=await run.PowerShell("$d=Get-DiskImage -ImagePath S:\\Source.iso; $owned=!$d.Attached;try{if($owned){$d=Mount-DiskImage -ImagePath S:\\Source.iso -PassThru};$v=$d|Get-Volume;if(!$v.DriveLetter){throw 'Source has no volume'};$root=$v.DriveLetter+':\\';$items=@{};Get-ChildItem -LiteralPath $root -Force -Recurse -File|ForEach-Object {$items[$_.FullName.Substring($root.Length)]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256 -ErrorAction Stop).Hash};ConvertTo-Json -InputObject $items -Compress}finally{if($owned){Dismount-DiskImage -ImagePath S:\\Source.iso|Out-Null}}");
                using var sourceDoc=JsonDocument.Parse(sourceJson.Trim());var source=Map(sourceDoc.RootElement);
                var current=Directory.EnumerateFiles(media,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(media,p),BootableDrive.Hash,StringComparer.OrdinalIgnoreCase);
                if(source.Count!=current.Count||source.Any(p=>p.Key!=@"sources\boot.wim"&&(!current.TryGetValue(p.Key,out var h)||h!=p.Value))||BootableDrive.Hash(Path.Combine(work,"source-boot.wim"))!=source[@"sources\boot.wim"])throw new IOException("Prepared media differs outside the retained boot checkpoint.");
                var originalHash=BootableDrive.Hash(@"S:\Source.iso");
                if(originalHash!="1784E5FD9CAE707647EFA97B09071C85AFF28791A9EB5A9F6E73FBCE2A962ADC")throw new IOException("Source copy changed.");
                await IsoExport.Create(run,media,iso,@"D:\tools\oscdimg.exe",Log,CancellationToken.None);
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{iso,filesPreservedOutsideBoot=source.Count-1,mountedPeFilesPreserved=before.Count-1,added,originalSha256=originalHash,isoSha256=BootableDrive.Hash(iso),windowsImageServiced=false,peOnly=true,resumedAfterHarnessLogFailure=true}));
            }
            else if(o.Action=="scratch")
            {
                var result=await new ProcessRunner(Log).PowerShell("$d=@(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -like 'TLOS-SCRATCH114*'});if($d.Count -ne 1 -or $d[0].IsBoot -or $d[0].IsSystem -or [string]$d[0].BusType -ne 'NVMe' -or $d[0].PartitionStyle -ne 'RAW' -or $d[0].Size -ne 171798691840 -or (Test-Path S:\\)){throw 'Owned blank virtual scratch disk safety check failed.'};$d[0]|Initialize-Disk -PartitionStyle GPT -PassThru|New-Partition -UseMaximumSize -DriveLetter S|Format-Volume -FileSystem NTFS -NewFileSystemLabel TLOS_SCRATCH -Confirm:$false|Out-Null;[pscustomobject]@{drive='S:';serial='TLOS-SCRATCH114';format=(Get-Volume -DriveLetter S).FileSystem}|ConvertTo-Json -Compress");
                File.WriteAllText(o.Output,result);
            }
            else if(o.Action=="export-audit")
            {
                File.WriteAllText(o.Output,await ExportReadback.Run(o.Iso!,o.Workspace!,o.ExpectedLauncherSha256??throw new IOException("Expected launcher hash is required for export readback."),Log));
            }
            else if(o.Action=="setup-compatibility-revision")
            {
                await SetupCompatibilityRevision.Run(o.Iso!,o.Workspace!,o.Destination!,o.Output,o.ExpectedLauncherSha256!,Log);
            }
            else if(o.Action=="regional-revision780")
            {
                await RegionalRevision.Run(o.Iso!,o.Workspace!,o.Destination!,o.Output,o.ExpectedLauncherSha256!,Log);
            }
            else if(o.Action=="tweaks-revision807")
            {
                await TweaksRevision.Run(o.Iso!,o.Workspace!,o.Destination!,o.Output,o.ExpectedLauncherSha256!,Log);
            }
            else if(o.Action=="usb")
            {
                if(!OwnedUsb(o.Serial))throw new IOException("Only the explicitly owned virtual USB 114 or 347 is allowed.");
                var engine=new BootableDrive(new ProcessRunner(Log),Log);var disks=await engine.List(CancellationToken.None);
                var disk=disks.Single(d=>d.SerialNumber==o.Serial);
                var prepared=await engine.Prepare(o.Iso!,o.Workspace!,disk,CancellationToken.None);
                await engine.Write(prepared,disk,"ERASE "+disk.Number,CancellationToken.None);
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{disk,source=prepared.Iso,sourceSha256=prepared.IsoSha256,prepared.Bytes,files=prepared.Files.Length,verified=true}));
            }
            else if(o.Action=="usb-retry-749")
            {
                const string workspace=@"S:\usb-full-749",iso=@"S:\Ghost-full-748.iso";
                const string digest="9A90EF5293C62C4D5D81C553EB0789CFC3CBD13B004310C1C65E0A4643F29420";
                if(o.Output!=@"C:\TLOS\usb-full-749.json" || o.Serial!="TLOS-USB-347")throw new IOException("Only the owned failed USB749 operation may resume.");
                var prepared=JsonSerializer.Deserialize<PreparedUsb>(File.ReadAllText(Path.Combine(workspace,"prepared.json")))??throw new IOException("Missing prepared USB checkpoint.");
                if(prepared.Workspace!=workspace || prepared.Iso!=iso || prepared.IsoSha256!=digest || BootableDrive.Hash(iso)!=digest)
                    throw new IOException("Prepared USB checkpoint does not match the verified748 ISO.");
                var engine=new BootableDrive(new ProcessRunner(Log),Log);
                var disk=(await engine.List()).Single(d=>d.SerialNumber=="TLOS-USB-347");
                await engine.Write(prepared,disk,"ERASE "+disk.Number,CancellationToken.None);
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{disk,source=prepared.Iso,sourceSha256=prepared.IsoSha256,prepared.Bytes,files=prepared.Files.Length,verified=true,resumedPreparedMedia=true}));
            }
            else if(o.Action=="usb-resume")
            {
                var prepared=JsonSerializer.Deserialize<PreparedUsb>(File.ReadAllText(@"S:\usb-pe-145\prepared.json"))??throw new IOException("Missing prepared USB checkpoint.");
                if(Path.GetFullPath(prepared.Workspace)!=@"S:\usb-pe-145"||prepared.Iso is null||Path.GetFullPath(prepared.Iso)!=@"S:\PE-only.iso"||prepared.IsoSha256!="7327F62BCF974A24656B857F38C5E83DCFACCFB55BA984B4D38BE80BDE13BF0F"||BootableDrive.Hash(prepared.Iso)!=prepared.IsoSha256)throw new IOException("Prepared USB checkpoint does not match the verified PE-only ISO.");
                var engine=new BootableDrive(new ProcessRunner(Log),Log);
                var disk=(await engine.List()).Single(d=>d.SerialNumber=="TLOS-USB-114");
                await engine.Write(prepared,disk,"ERASE "+disk.Number,CancellationToken.None);
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{disk,prepared.Bytes,files=prepared.Files.Length,verified=true,resumedPreparedMedia=true}));
            }
            else if(o.Action=="update")
            {
                var ready=AppUpdates.StageLocal(o.Manifest!,o.Package!,AppUpdates.CurrentVersion);
                AppUpdates.Launch(ready,false);
                File.WriteAllText(o.Output,JsonSerializer.Serialize(new{ready.Manifest.Version,ready.Folder,helperStarted=true}));
            }
            else throw new IOException("Unsupported live acceptance action.");
            await Report(o.Action,File.ReadAllText(o.Output));
        }
        catch(Exception ex){File.WriteAllText(o.Output+".error",ex.ToString());throw;}
    }
    private static async Task Report(string action,string result)
    {
        if(result.Length>100000)throw new IOException("Acceptance result exceeds its reporting bound.");
        using var http=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(10)};
        using var body=new System.Net.Http.StringContent(JsonSerializer.Serialize(new{action,result}),System.Text.Encoding.UTF8,"application/json");
        using var response=await http.PostAsync("http://10.0.2.2:8088/audit",body);response.EnsureSuccessStatusCode();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetClassNameW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(nint window,System.Text.StringBuilder name,int maximum);
}
