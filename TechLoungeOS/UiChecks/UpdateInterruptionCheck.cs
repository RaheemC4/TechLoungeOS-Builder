using System.Diagnostics;
using System.Text.Json;
using TechLoungeOS;
using TechLoungeOS.Updates;

// Explicit VM-only fault injection. Never interrupts servicing or the real installation.
internal static class UpdateInterruptionCheck
{
    private const string Install=@"C:\TLOS\update-recovery1066";
    private const string State=@"C:\TLOS\update-recovery1066-state.json";
    private const string Output=@"C:\TLOS\update-recovery1066-result.json";
    private record Receipt(string Stage,Dictionary<string,string> Before);

    private static void Owned()
    {
        var identity=new ProcessRunner(_=>{}).PowerShell("$s=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop;[pscustomobject]@{Manufacturer=$s.Manufacturer;Serials=@(Get-Disk -ErrorAction Stop|ForEach-Object {([string]$_.SerialNumber).Trim()})}|ConvertTo-Json -Compress").GetAwaiter().GetResult();
        using var json=JsonDocument.Parse(identity);
        if(!LiveAcceptance.OwnedBuilder(json.RootElement.GetProperty("Manufacturer").GetString(),json.RootElement.GetProperty("Serials").EnumerateArray().Select(v=>v.GetString())))
            throw new IOException("The owned builder VM is required for updater fault injection.");
        UpdatePackage.Unlinked(Install);UpdatePackage.Unlinked(State);UpdatePackage.Unlinked(Output);
    }

    internal static void Interrupt()
    {
        Owned();
        var receipt=JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(State))??throw new IOException("Missing native recovery state.");
        UpdatePackage.Unlinked(receipt.Stage);
        var current=Version.Parse(FileVersionInfo.GetVersionInfo(Path.Combine(Install,UpdateTrust.MainExe)).FileVersion!);
        var manifest=UpdatePackage.VerifyEnvelope(File.ReadAllBytes(Path.Combine(receipt.Stage,"builder-update.json")),UpdateTrust.PublicKeySpki,current,Environment.OSVersion.Version.Build,DateTimeOffset.UtcNow);
        using var lease=UpdateCoordinator.Enter(Install);
        UpdateCoordinator.EnsureAppClosed(Install);
        UpdateCommit.Apply(Install,Path.Combine(receipt.Stage,"verified"),manifest,_=>Environment.Exit(23));
        throw new IOException("Fault injection did not interrupt a committed move.");
    }

    internal static void Run()
    {
        Owned();
        if(Directory.Exists(Install)||File.Exists(State)||File.Exists(Output))throw new IOException("Existing recovery-test state needs inspection.");
        const string source=@"C:\TLOS\worker1057";
        UpdateCommit.ValidateInstall(source);
        void Copy(string from,string to)
        {
            UpdatePackage.Unlinked(from);UpdatePackage.Unlinked(to);Directory.CreateDirectory(to);
            foreach(var path in Directory.GetFiles(from)){UpdatePackage.Unlinked(path);File.Copy(path,Path.Combine(to,Path.GetFileName(path)),false);}
            foreach(var path in Directory.GetDirectories(from))Copy(path,Path.Combine(to,Path.GetFileName(path)));
        }
        Copy(source,Install);
        var note=Path.Combine(Install,"native-user-notes.txt");
        File.WriteAllText(note,"Unrelated user content must survive an interrupted update.");File.SetAttributes(note,FileAttributes.ReadOnly);
        var before=Directory.GetFiles(Install,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(Install,p),UpdatePackage.Hash);
        var current=Version.Parse(FileVersionInfo.GetVersionInfo(Path.Combine(Install,UpdateTrust.MainExe)).FileVersion!);
        var ready=AppUpdates.StageLocal(@"E:\update1066\builder-update.json",@"E:\update1066\TechLoungeOS-Builder-win-x64.zip",current);
        File.WriteAllBytes(State,JsonSerializer.SerializeToUtf8Bytes(new Receipt(ready.Folder,before)));
        var self=Environment.ProcessPath??throw new IOException("Published test executable missing.");
        if(!Path.GetFileName(self).Equals("UiChecks.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Use the published UiChecks executable in the VM.");
        var start=new ProcessStartInfo(self){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--update-interrupt-child");
        using(var child=Process.Start(start)??throw new IOException("Native interruption worker did not start."))
        {
            if(!child.WaitForExit(120000))throw new IOException("Interruption worker is still running; retain the VM and inspect PID"+child.Id);
            if(child.ExitCode!=23)throw new IOException("Interruption worker failed before the intended commit boundary: "+child.ExitCode);
        }
        var journalPath=Path.Combine(Install,".techloungeos-update-journal.json");
        var journal=JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(journalPath))??throw new IOException("No durable interruption journal.");
        if(journal.Complete||journal.Moves.Length!=1)throw new IOException("Expected one durable partial move.");
        var move=journal.Moves[0];
        if(UpdatePackage.Hash(Path.Combine(Install,move.Relative))!=move.NewSha256)throw new IOException("The interruption happened before replacement.");
        var helper=Path.Combine(ready.Folder,"verified","updater","TechLoungeOS.Updater.exe");
        var helperFile=ready.Manifest.Files.Single(f=>f.Path=="updater/TechLoungeOS.Updater.exe");
        if(UpdatePackage.Hash(helper)!=helperFile.Sha256)throw new IOException("Signed recovery helper changed.");
        var recover=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true};recover.ArgumentList.Add("--recover");recover.ArgumentList.Add(Install);
        using(var process=Process.Start(recover)??throw new IOException("Real recovery helper did not start."))
            if(!process.WaitForExit(60000)||process.ExitCode!=0)throw new IOException("Real recovery helper did not complete; retain all evidence.");
        if(File.Exists(journalPath)||!File.Exists(Path.Combine(journal.Backup,"rolled-back.json"))||before.Any(p=>!File.Exists(Path.Combine(Install,p.Key))||UpdatePackage.Hash(Path.Combine(Install,p.Key))!=p.Value))
            throw new IOException("Recovery failed to restore the original installed files.");
        if(!move.Existed&&File.Exists(Path.Combine(Install,move.Relative)))throw new IOException("Recovery retained a newly introduced file.");
        if((File.GetAttributes(note)&FileAttributes.ReadOnly)==0)throw new IOException("Unrelated file attributes changed.");
        using(var app=Process.Start(new ProcessStartInfo(Path.Combine(Install,UpdateTrust.MainExe)){UseShellExecute=true})??throw new IOException("Restored app did not start."))
        {
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(!app.HasExited&&app.MainWindowHandle==IntPtr.Zero&&DateTime.UtcNow<deadline){Thread.Sleep(100);app.Refresh();}
            if(app.HasExited||app.MainWindowHandle==IntPtr.Zero||!app.MainWindowTitle.Contains("TechLoungeOS"))throw new IOException("Restored Builder did not open its window.");
            if(!app.CloseMainWindow()||!app.WaitForExit(30000))throw new IOException("Restored Builder did not close normally.");
        }
        var result=new{action="update-interruption1066",utc=DateTimeOffset.UtcNow,realCommitInterrupted=true,committedMoves=1,realRecoveryHelper=true,originalFilesRestored=before.Count,unrelatedReadOnlyFilePreserved=true,restoredAppOpenedAndClosed=true,hostedDiscoveryTested=false,install=Install,stage=ready.Folder};
        var bytes=JsonSerializer.SerializeToUtf8Bytes(result,new JsonSerializerOptions{WriteIndented=true});File.WriteAllBytes(Output,bytes);
        using var http=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(15)};
        using var body=new System.Net.Http.ByteArrayContent(bytes);body.Headers.ContentType=new("application/json");
        using var response=http.PostAsync("http://10.0.2.2:8088/audit",body).GetAwaiter().GetResult();response.EnsureSuccessStatusCode();
    }
}
