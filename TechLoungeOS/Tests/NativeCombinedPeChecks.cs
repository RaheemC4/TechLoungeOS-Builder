using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using TechLoungeOS;

// A tiny, non-bootable fixture validates real DISM/WIMGAPI orchestration.
// No original ISO, live Windows configuration, VM or physical USB is used.
internal static class NativeCombinedPeChecks
{
    internal static async Task Run(string root)
    {
        root=Path.GetFullPath(root);Policy.RequireUnlinkedPath(root);
        if(Directory.Exists(root)||File.Exists(root))throw new IOException("A new private native-check folder is required.");
        using var identity=WindowsIdentity.GetCurrent();
        if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new IOException("Native DISM check requires administrator access.");
        Directory.CreateDirectory(root);
        try
        {
            using var log=new StreamWriter(Path.Combine(root,"native.log")){AutoFlush=true};var lines=new List<string>();
            var runner=new ProcessRunner(s=>{lock(lines){lines.Add(s);log.WriteLine(s);}},Path.Combine(root,"scratch"));
            var exe=Path.Combine(AppContext.BaseDirectory,"ThirdParty","Wimlib","wimlib-imagex.exe");
            if(BootableDrive.Hash(exe)!="34C0C4165591AD1F592837ED99D08273C58D6ED3FE0ED6360CF34E7B0739B353"||BootableDrive.Hash(Path.Combine(Path.GetDirectoryName(exe)!,"libwim-15.dll"))!="BA853EE1E3FC5F5798581F02E8E066BA07A0A2375F0BF444FE981431FD508495")throw new IOException("Pinned fixture archiver required.");
            if((await runner.PowerShell("@(Get-WindowsImage -Mounted).Count")).Trim()!="0")throw new IOException("Existing image mounts retained; this fixture will not run alongside servicing.");
            var input=Path.Combine(root,"input");var system=Path.Combine(input,@"Windows\System32");Directory.CreateDirectory(system);
            foreach(var file in new[]{"cmd.exe","kernel32.dll","user32.dll","gdi32.dll","msvcrt.dll","advapi32.dll","shell32.dll","ole32.dll","msimg32.dll"})File.WriteAllText(Path.Combine(system,file),"inert prerequisite; never executed");
            File.WriteAllText(Path.Combine(system,"winpeshl.ini"),"[LaunchApps]\r\n%SYSTEMDRIVE%\\Windows\\System32\\LaunchBar_x64.exe\r\n");
            File.WriteAllText(Path.Combine(system,"LaunchBar_x64.exe"),"preserved inert source tool");
            SettingsChecks.WriteHive(Path.Combine(system,@"config\SYSTEM"),@"ControlSet001\Services\PreservedFixtureService",77);
            var originalServices=SourcePreservation.CaptureServices(input);var originalFiles=WinPeBackground.Snapshot(input,CancellationToken.None);
            var media=Path.Combine(root,"media");Directory.CreateDirectory(Path.Combine(media,"sources"));var wim=Path.Combine(media,@"sources\boot.wim");
            string[] properties=["--image-property=WINDOWS/ARCH=9","--image-property=WINDOWS/INSTALLATIONTYPE=WindowsPE","--image-property=WINDOWS/VERSION/MAJOR=10","--image-property=WINDOWS/VERSION/MINOR=0","--image-property=WINDOWS/VERSION/BUILD=26100","--image-property=WINDOWS/VERSION/SPBUILD=1","--image-property=WINDOWS/VERSION/SPLEVEL=0"];
            await runner.Run(exe,["capture",input,wim,"Inert PE 1","--compress=fast","--check",..properties]);
            await runner.Run(exe,["append",input,wim,"Inert PE 2","--boot","--check",..properties]);
            if(WinPeImage.ReadBootIndex(wim)!=2)throw new IOException("The native fixture has no marked second boot image.");
            var originalWimHash=BootableDrive.Hash(wim);var watch=Stopwatch.StartNew();
            await WinPeBackground.Prepare(runner,media,root,s=>log.WriteLine(s),CancellationToken.None);watch.Stop();
            if(WinPeImage.ReadBootIndex(wim)!=2||BootableDrive.Hash(Path.Combine(root,"source-boot.wim"))!=originalWimHash)throw new IOException("Combined preparation lost the boot index or original backup.");
            foreach(var index in new[]{1,2})
            {
                var extract=Path.Combine(root,"readback-"+index);await runner.Run(exe,["apply",wim,index.ToString(),extract,"--check"]);
                SourcePreservation.VerifyServices(originalServices,SourcePreservation.CaptureServices(extract));
                var values=SetupCompatibility.ReadValues(Path.Combine(extract,@"Windows\System32\config\SYSTEM"));
                if(SetupCompatibility.Names.Any(n=>!values.TryGetValue(n,out var value)||value!=1))throw new IOException("A required compatibility value was lost.");
                foreach(var file in originalFiles.Where(p=>p.Key!=@"Windows\System32\config\SYSTEM"&&(index!=2||p.Key!=@"Windows\System32\winpeshl.ini")))
                    if(BootableDrive.Hash(Path.Combine(extract,file.Key))!=file.Value)throw new IOException("An original fixture file changed: "+file.Key);
                var background=Path.Combine(extract,@"Windows\System32\TechLoungeOS\PeBackground.exe");
                if((index==2)!=File.Exists(background))throw new IOException("The interface was applied to the wrong boot image.");
            }
            var mounts=lines.Count(s=>s.StartsWith("> dism.exe")&&s.Contains("/Mount-Image"));var commits=lines.Count(s=>s.StartsWith("> dism.exe")&&s.Contains("/Commit"));
            if(mounts!=2||commits!=2)throw new IOException("Repeated native WinPE processing returned.");
            File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,verified=true,mounts,commits,seconds=watch.Elapsed.TotalSeconds,originalToolAndServicesPreserved=true,compatibilityOnBothImages=true,interfaceOnMarkedImageOnly=true,bootIndex=2,fixtureOnly=true,bootTested=false,originalIsoUsed=false,physicalDriveWritten=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(root,"failure.txt"),error.ToString());throw;}
    }
}
