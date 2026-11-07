using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS;

// Exact owned-VM continuation. Reuses an accepted serviced WIM, never a stock replacement.
internal static class NativeFinalIntegration
{
    private const string Work=@"R:\ghost-final1758";
    private const string Cache=@"R:\stock-desktop1510";
    private const string Wim=@"R:\feature1586\selected.wim";
    private const string Mount=@"S:\ghost-final1758-mount";
    private const string Accepted=@"S:\feature-repair1609\migration1651\accept1726\result.json";
    private static readonly Version Target=new(10,0,26300,9725);
    private static readonly string[] Packs=["office-pack","runtime-pack","edge-pack","hwinfo-pack","nvidia-setup","recommended-apps","tweaks-installer"];
    private static readonly string[] Apps=RecommendedApps.ForBuild([RecommendedApps.Discord,"Brave.Brave","Google.Chrome","Valve.Steam","Guru3D.Afterburner","Stremio.Stremio","Proton.ProtonVPN",RecommendedApps.ChatGPT,"REALiX.HWiNFO"]);

    private static async Task Guard(ICommandRunner run,bool resume=false)
    {
        Preflight.ValidatePackage();
        await run.PowerShell("""
            if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563'){throw 'Exact owned test VM required'}
            $boot=@(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot})
            $r=Get-Partition -DriveLetter R|Get-Disk;$s=Get-Partition -DriveLetter S|Get-Disk
            if($boot.Count -ne 1 -or ([string]$r.SerialNumber).Trim() -ne 'TLOS-DESKTOP1502' -or $r.IsBoot -or $r.IsSystem -or
              ([string]$s.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $s.IsBoot -or $s.IsSystem){throw 'Exact owned work disks required'}
            if(Get-Process dism,DismHost,MigrationTools1726 -ErrorAction SilentlyContinue){throw 'Finish current servicing first'}
            if(@(Get-WindowsImage -Mounted).Count){throw 'Existing mounts left untouched'}
            'Owned continuation guards passed; no original ISO or physical disk is writable'
            """);
        using var accepted=JsonDocument.Parse(File.ReadAllText(Accepted));var a=accepted.RootElement;
        if(!a.GetProperty("validated").GetBoolean() || !a.GetProperty("committed").GetBoolean() || !a.GetProperty("metadataVerified").GetBoolean() || a.GetProperty("mounted").GetBoolean() || a.GetProperty("failure").ValueKind!=JsonValueKind.Null)
            throw new IOException("A committed and independently accepted native migration is required.");
        Policy.RequireUnlinkedPath(Wim);Policy.RequireUnlinkedPath(Cache);
        if(resume)
        {
            using var pause=JsonDocument.Parse(File.ReadAllText(@"C:\TLOS-final1759\pause1788-result.json"));var p=pause.RootElement;
            if(!p.GetProperty("workerStopped").GetBoolean() || p.GetProperty("remainingMounts").GetInt32()!=0 ||
                p.GetProperty("mountsDiscarded").GetInt32()!=1 || !p.GetProperty("completedWinPe").GetBoolean())
                throw new IOException("A normally closed, uncommitted integration with completed WinPE is required.");
            Policy.RequireUnlinkedPath(Work);Policy.RequireUnlinkedPath(Mount);
            if(!Directory.Exists(Work)||!Directory.Exists(Mount)||Directory.EnumerateFileSystemEntries(Mount).Any()||
                File.Exists(Path.Combine(Work,"native-result.json")) || File.Exists(Path.Combine(Work,"packed-install.wim")) || File.Exists(Path.Combine(Work,"packed-install.esd")) ||
                File.Exists(Path.Combine(Work,@"media\sources\install.wim")) || File.Exists(@"R:\Ghost-26H2-final1758.iso"))
                throw new IOException("The paused integration is no longer at its expected recovery boundary.");
            using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(Work,"state.json")));
            if(state.RootElement.GetProperty("phase").GetString()!="Servicing")throw new IOException("Unexpected paused build stage.");
            using var pe=JsonDocument.Parse(File.ReadAllText(Path.Combine(Work,"pe-preservation.json")));
            var before=pe.RootElement.GetProperty("before");var after=pe.RootElement.GetProperty("after");
            foreach(var entry in before.EnumerateObject())
                if(!entry.Name.Equals(@"Windows\System32\winpeshl.ini",StringComparison.OrdinalIgnoreCase) &&
                    (!after.TryGetProperty(entry.Name,out var current)||entry.Value.GetString()!=current.GetString()))
                    throw new IOException("Recorded inherited WinPE preservation differs: "+entry.Name);
            using var resource=typeof(WinPeBackground).Assembly.GetManifestResourceStream("TechLoungeOS.PeBackground.exe")!;
            if(after.GetProperty(@"Windows\System32\TechLoungeOS\PeBackground.exe").GetString()!=Convert.ToHexString(SHA256.HashData(resource)))
                throw new IOException("The preserved WinPE executable differs from the current payload.");
            if(!Guid.TryParse(File.ReadAllText(Path.Combine(Work,@"media\TechLoungeOS.media")),out _) ||
                WinPeImage.ReadBootIndex(Path.Combine(Work,@"media\sources\boot.wim"))!=2)
                throw new IOException("Committed WinPE marker or boot selection differs.");
            var source=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Work,"source-media.json")))!;
            foreach(var item in source.Where(i=>!i.Key.Equals(@"sources\boot.wim",StringComparison.OrdinalIgnoreCase)))
                VerifyHash(Path.Combine(Work,"media",item.Key),item.Value);
        }
        else if(Directory.Exists(Work)||File.Exists(Work)||Directory.Exists(Mount)||File.Exists(Mount))throw new IOException("Fresh private continuation paths required.");
    }

    private static void VerifyHash(string path,string expected)
    {
        Policy.RequireUnlinkedPath(path);
        if(!BootableDrive.Hash(path).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Cached installer checksum differs: "+path);
    }
    private static void ScriptMatches(string pack,string name,string expected)
    {
        var text=File.ReadAllText(Path.Combine(Cache,pack,name)).Replace("\r\n","\n").TrimStart('\uFEFF');
        if(text!=expected.Replace("\r\n","\n").TrimStart('\uFEFF'))throw new IOException("Cached setup code is stale: "+pack+"/"+name);
    }
    internal static async Task CheckCache()
    {
        var run=new ProcessRunner(Console.WriteLine);await Guard(run);await VerifyCache(run);
        Console.WriteLine("Owned cached installers verified; no image mounted, copied or serviced.");
    }
    private static async Task VerifyCache(ICommandRunner run)
    {
        ScriptMatches("runtime-pack","install.ps1",RuntimePack.Script);
        ScriptMatches("office-pack","TechLoungeOS-Office.ps1",OfficePack.Script);
        ScriptMatches("edge-pack","install.ps1",EdgePack.Script);
        ScriptMatches("hwinfo-pack","install.ps1",HwInfoPack.Script);
        var runtimes=JsonSerializer.Deserialize<RuntimeInstaller[]>(File.ReadAllText(Path.Combine(Cache,@"runtime-pack\installers.json")))??throw new IOException("Runtime manifest missing.");
        if(!runtimes.Select(i=>(i.Id,i.Architecture)).SequenceEqual(RuntimePack.Packages(true).Append(("Microsoft.DirectX.June2010","x86+x64"))))throw new IOException("Required runtime identities differ.");
        foreach(var item in runtimes)
        {
            if(Path.GetFileName(item.File)!=item.File || !item.File.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || item.Arguments!=(item.Id=="Microsoft.DirectX.June2010"?"":RuntimePack.Arguments(item.Id)))throw new IOException("Unsafe cached runtime instructions.");
            var path=Path.Combine(Cache,"runtime-pack",item.File);VerifyHash(path,item.Sha256);await RuntimePack.VerifyMicrosoft(run,path);
        }
        var office=Path.Combine(Cache,"office-pack");var version=OfficePack.ValidateFiles(office);
        var files=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(office,"TechLoungeOS-Office.sha256.json")))??throw new IOException("Office checksum manifest missing.");
        foreach(var item in files){if(Path.IsPathRooted(item.Key)||item.Key.Split('\\','/').Any(s=>s is ".." or "."))throw new IOException("Unsafe cached Office path.");VerifyHash(Path.Combine(office,item.Key),item.Value);}
        await RuntimePack.VerifyMicrosoft(run,Path.Combine(office,"setup.exe"));
        using var hw=JsonDocument.Parse(File.ReadAllText(Path.Combine(Cache,@"hwinfo-pack\installer.json")));VerifyHash(Path.Combine(Cache,@"hwinfo-pack\HWiNFO.exe"),hw.RootElement.GetProperty("Sha256").GetString()!);
        Console.WriteLine("Cached Microsoft365 version "+version+" and "+runtimes.Length+" runtime installers verified; no latest-version claim is inferred from a cache.");
    }

    internal static async Task Run(bool resume=false)
    {
        var run=new ProcessRunner(Console.WriteLine);await Guard(run,resume);await VerifyCache(run);
        if(new DriveInfo("R").AvailableFreeSpace<35L<<30 || new DriveInfo("S").AvailableFreeSpace<20L<<30)throw new IOException("Reserved integration/export space required.");
        if(!resume && (new DriveInfo("E").DriveType!=DriveType.CDRom || !File.Exists(@"E:\sources\install.wim")))throw new IOException("Attach the original Ghost ISO read-only as E before integration.");
        var source=resume?new[]{"10.0.26200.9457","WINDOWS 11 IoT ENT - COMPACT + DEF"}:(await run.PowerShell(@"$image=Get-WindowsImage -ImagePath 'E:\sources\install.wim' -Index 2; [string]$image.Version; [string]$image.ImageName")).Trim().Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var saved=(await run.PowerShell("$image=Get-WindowsImage -ImagePath "+Policy.Ps(Wim)+" -Index 1; [string]$image.Version; [string]$image.ImageName")).Trim().Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(source.Length!=2 || saved.Length!=2 || source[0]!="10.0.26200.9457" || saved[0]!=Target.ToString() || source[1]!=saved[1])throw new IOException("Original and accepted image identities differ.");
        Directory.CreateDirectory(Work);Directory.CreateDirectory(Mount);
        using var exclusive=new FileStream(Path.Combine(Work,"build.lock"),resume?FileMode.Open:FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        using var fileLog=new StreamWriter(Path.Combine(Work,"build.log"),resume){AutoFlush=true};var gate=new object();
        void Write(string text){lock(gate){fileLog.WriteLine(DateTimeOffset.UtcNow.ToString("o")+" "+text);Console.WriteLine(text);}}
        var options=new BuildOptions(@"D:\Downloads\WIN11.IoT-ENT.25H2.U11.X64.(WPE).ISO",2,Work,@"R:\Ghost-26H2-final1758.iso",@"C:\TLOS-Native1329\oscdimg.exe",[],Apps,true,true,@"C:\TLOS-AppUpdate1285\TechLoungeOS-Builder\integration")
        {AnimatedWinPe=true,SetupRegion="en-GB",SetupKeyboard="00000809",UseCurrentTweaksInstaller=true,IncludeNet8=true,StoreXboxPackages=Path.Combine(Cache,"store-xbox")};
        var edition=new Edition(1,saved[1],"9",Target.ToString(),"IoTEnterprise");
        string phase="Preparing verified continuation";bool mounted=false,committed=false,complete=false;string? failure=null;
        void State(string next){phase=next;File.WriteAllText(Path.Combine(Work,"state.json"),JsonSerializer.Serialize(new{phase,mount=Mount,wim=Wim,options,edition}));Write(next);}
        var media=Path.Combine(Work,"media");
        run=new ProcessRunner(Write,Path.Combine(Work,"scratch"));
        try
        {
            State(phase);File.WriteAllText(Path.Combine(Work,"cached-inputs.json"),JsonSerializer.Serialize(new{cache=Cache,apps=Apps,latestDownloadsRechecked=false,acceptedMigration=Accepted}));
            if(!resume)foreach(var pack in Packs)CopyCached(pack);
            Provisioning.ValidatePayload(Path.Combine(options.TweaksFolder!,"WebView2Runtime"),CancellationToken.None);NvidiaSetup.Validate(options.TweaksFolder!);
            if(!resume)
            {
            State("Copying original Ghost media except its superseded install.wim");
            Directory.CreateDirectory(media);
            var copied=new Dictionary<string,string>();
            foreach(var file in Directory.EnumerateFiles(@"E:\","*",SearchOption.AllDirectories))
            {
                var relative=Path.GetRelativePath(@"E:\",file);if(relative.Equals(@"sources\install.wim",StringComparison.OrdinalIgnoreCase))continue;
                var destination=Path.Combine(media,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination);File.SetAttributes(destination,File.GetAttributes(destination)&~FileAttributes.ReadOnly);
                var hash=BootableDrive.Hash(file);if(BootableDrive.Hash(destination)!=hash)throw new IOException("Original media copy differs: "+relative);copied.Add(relative,hash);
            }
            File.WriteAllText(Path.Combine(Work,"source-media.json"),JsonSerializer.Serialize(copied));
            }
            State("Mounting the accepted 26H2 image");mounted=true;await run.Dism("/Mount-Image","/ImageFile:"+Wim,"/Index:1","/MountDir:"+Mount,"/CheckIntegrity");
            if(WindowsImageUpdates.ReadVersion(Mount)!=Target || WindowsImageUpdates.ReadEdition(Mount)!="IoTEnterprise")throw new IOException("Accepted image actual version/edition differs.");
            State("Servicing");
            await BuildEngine.ServiceAndStage(run,options,edition,media,Mount,Target,null,null,Write,State,CancellationToken.None,prepareWinPe:!resume);
            State("Committing final integrated image");await run.Dism("/Unmount-Image","/MountDir:"+Mount,"/Commit","/CheckIntegrity");mounted=false;committed=true;
            State("Compressing final installation image without removing components");
            var packed=await WimPacking.Compact(run,Wim,Work,Target,Write,CancellationToken.None);
            var compact=Path.Combine(media,"sources",Path.GetExtension(packed).Equals(".esd",StringComparison.OrdinalIgnoreCase)?"install.esd":"install.wim");File.Move(packed,compact);
            var reported=(await run.PowerShell("(Get-WindowsImage -ImagePath "+Policy.Ps(compact)+" -Index 1).Version.ToString()")).Trim();if(reported!=Target.ToString())throw new IOException("Final image metadata differs.");
            State("Preparing Windows Setup compatibility defaults");
            await SetupCompatibility.Prepare(run,media,Work,Write,CancellationToken.None);BootableDrive.Check16GbTarget(media,Write);
            State("Creating ISO");await IsoExport.Create(run,media,options.Output,options.Oscdimg,Write,CancellationToken.None,State);State("Complete");
            var audit=await ExportReadback.Run(options.Output,Work,BootableDrive.Hash(Path.Combine(AppContext.BaseDirectory,"FirstLogin.exe")),Write,BootableDrive.Hash(Path.Combine(AppContext.BaseDirectory,"Personalise.exe")));
            File.WriteAllText(Path.Combine(Work,"export-readback.json"),audit);complete=true;
        }
        catch(Exception error){failure=error.ToString();Write(failure);throw;}
        finally
        {
            if(mounted)try{await run.Dism("/Unmount-Image","/MountDir:"+Mount,"/Discard");mounted=false;}catch(Exception cleanup){failure+="\nMount recovery required: "+cleanup;}
            File.WriteAllText(Path.Combine(Work,"native-result.json"),JsonSerializer.Serialize(new{phase,committed,complete,mounted,failure,originalReadOnly=true,windowsUpdateRepeated=false,installationValidated=false,utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    private static void CopyCached(string pack)
    {
        var source=Path.Combine(Cache,pack);var destination=Path.Combine(Work,pack);Policy.RequireUnlinkedPath(source);Directory.CreateDirectory(destination);
        foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
        {
            var relative=Path.GetRelativePath(source,file);
            if(pack=="office-pack" && !relative.StartsWith("Office"+Path.DirectorySeparatorChar,StringComparison.Ordinal) && !new[]{"setup.exe","Configuration.xml","TechLoungeOS-Office.ps1","TechLoungeOS-Office.template.xml","TechLoungeOS-Office.sha256.json"}.Contains(relative,StringComparer.Ordinal))continue;
            var target=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);Policy.RequireUnlinkedPath(file);
            if(Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".msi" or ".dat" or ".cab")
            {if(!CreateHardLinkW(target,file,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
            else File.Copy(file,target);
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateHardLinkW(string link,string existing,nint security);
}
