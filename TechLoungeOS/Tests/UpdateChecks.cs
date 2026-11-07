using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TechLoungeOS;
using TechLoungeOS.Updates;

static class UpdateChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TechLoungeOS.Builder.Updates");
        var cacheExisted=Directory.Exists(cache);
        var coordinationPath=Path.Combine(root,"startup-without-update-storage");
        using(var lease=UpdateCoordinator.Enter(coordinationPath))
        {
            Exception? failure=null;
            var contender=new Thread(()=>{try{using var blocked=UpdateCoordinator.Enter(coordinationPath);}catch(Exception ex){failure=ex;}});
            contender.Start();if(!contender.Join(10000))throw new Exception("Update coordination test timed out.");
            check(failure is IOException && failure.Message.Contains("currently being applied"),"a competing startup cannot enter while the updater owns coordination");
            check(Directory.Exists(cache)==cacheExisted,"startup coordination does not create or require the protected update cache");
        }
        using(var reacquired=UpdateCoordinator.Enter(coordinationPath))check(true,"startup coordination is released after its owner exits");
        var acl=(System.Security.AccessControl.DirectorySecurity)typeof(UpdateStorage).GetMethod("Acl",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,null)!;
        var descriptor=new System.Security.AccessControl.RawSecurityDescriptor(acl.GetSecurityDescriptorBinaryForm(),0);
        check(descriptor.Owner==null,"update storage leaves owner selection to Windows instead of assigning an unavailable owner SID");
        var ownerRights=acl.GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().Single(a=>a.IdentityReference.Value=="S-1-3-4");
        check(ownerRights.FileSystemRights==(System.Security.AccessControl.FileSystemRights.ReadPermissions|System.Security.AccessControl.FileSystemRights.Synchronize) && ownerRights.InheritanceFlags==(System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit),"default ownership retains inherited owner-rights protection on staged files");
        var fixture=Path.Combine(root,"updates");Directory.CreateDirectory(fixture);var published=Path.Combine(fixture,"new");Directory.CreateDirectory(Path.Combine(published,"updater"));
        var stages=Path.Combine(fixture,"stages");Directory.CreateDirectory(stages);
        var abandoned=Path.Combine(stages,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(abandoned,"verified"));
        File.WriteAllText(Path.Combine(abandoned,"package.zip"),"partial download");File.WriteAllText(Path.Combine(abandoned,"verified","app.dll"),"partial extraction");
        var retained=Path.Combine(stages,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(retained);File.WriteAllText(Path.Combine(retained,"keep.txt"),"another update remains");
        UpdateStorage.DiscardStage(abandoned,stages);
        check(!Directory.Exists(abandoned)&&File.ReadAllText(Path.Combine(retained,"keep.txt"))=="another update remains","failed-update cleanup removes its partial download and extraction while retaining other stages");
        foreach(var refused in new[]{stages,Path.Combine(fixture,Guid.NewGuid().ToString("N")),Path.Combine(stages,"rollback-evidence")})
        {
            Directory.CreateDirectory(refused);var note=Path.Combine(refused,"keep.txt");File.WriteAllText(note,"retain evidence");
            try{UpdateStorage.DiscardStage(refused,stages);throw new Exception("Expected stage-boundary refusal");}
            catch(IOException){check(File.ReadAllText(note)=="retain evidence","failed-update cleanup refuses the root, external folders and non-stage evidence");}
        }
        var old=Path.Combine(fixture,"old.exe");var exe=Path.Combine(published,UpdateTrust.MainExe);var run=new ProcessRunner(_=>{});
        async Task BuildFixture(string dest,string version)
        {
            var code=dest+".cs";File.WriteAllText(code,"using System.Reflection;[assembly:AssemblyFileVersion(\""+version+"\")]class Fixture {static void Main(string[] args){if(args.Length!=0){System.IO.File.WriteAllText(args[0],\"ready\");System.Threading.Thread.Sleep(-1);}}}");
            await run.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),["/nologo","/target:winexe","/platform:x64","/out:"+dest,code]);File.Delete(code);
        }
        await BuildFixture(old,"0.1.0.0");await BuildFixture(exe,"0.2.0.0");
        var ready=Path.Combine(fixture,"holder-ready.txt");
        var start=new System.Diagnostics.ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(ready);
        using(var holder=System.Diagnostics.Process.Start(start)??throw new IOException("Fixture process did not start."))
        {
            try
            {
                // Process.Start returns before the image loader exposes MainModule.
                // Wait for fixture entry, not an arbitrary timing delay.
                var deadline=DateTime.UtcNow.AddSeconds(10);
                while(!File.Exists(ready)&&!holder.HasExited&&DateTime.UtcNow<deadline)await Task.Delay(20);
                holder.Refresh();
                if(!File.Exists(ready)||holder.HasExited||!string.Equals(holder.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase))throw new Exception("Fixture process did not reach its ready state.");
                try{UpdateCoordinator.EnsureAppClosed(published,[System.Diagnostics.Process.GetProcessById(holder.Id)]);throw new Exception("Live copy was not rejected.");}
                catch(IOException error){check(error.Message.Contains(holder.Id.ToString()),"a live matching installation blocks replacement and identifies the other process");}
                UpdateCoordinator.EnsureAppClosed(published,[System.Diagnostics.Process.GetProcessById(holder.Id)],holder.Id);
                check(true,"update preflight can exclude its own still-running caller");
                UpdateCoordinator.EnsureAppClosed(fixture,[System.Diagnostics.Process.GetProcessById(holder.Id)]);
                check(true,"a copy from another installation does not block this installation");
                holder.Kill();if(!holder.WaitForExit(5000))throw new Exception("Fixture process did not exit.");
                UpdateCoordinator.EnsureAppClosed(published,[holder]);
                check(true,"an exited process retained in a snapshot does not block replacement");
            }
            finally{try{if(!holder.HasExited){holder.Kill();holder.WaitForExit(5000);}}catch(InvalidOperationException){ /* Snapshot guard disposed the exited fixture. */ }}
        }
        File.Copy(exe,Path.Combine(published,@"updater\TechLoungeOS.Updater.exe"));
        File.WriteAllText(Path.Combine(published,UpdatePackage.Marker),JsonSerializer.Serialize(new{Product=UpdateTrust.Product}));
        var files=Directory.GetFiles(published,"*",SearchOption.AllDirectories).Select(p=>new UpdateFile(Path.GetRelativePath(published,p).Replace('\\','/'),new FileInfo(p).Length,UpdatePackage.Hash(p))).ToArray();
        string Zip(string name,string? extra=null)
        {
            var path=Path.Combine(fixture,name);using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
            foreach(var f in files)zip.CreateEntryFromFile(Path.Combine(published,f.Path),UpdateTrust.ArchiveRoot+f.Path);
            if(extra!=null){using var writer=new StreamWriter(zip.CreateEntry(extra).Open());writer.Write("unsafe fixture");}return path;
        }
        var package=Zip("valid.zip");
        var m=new UpdateManifest(UpdateTrust.Product,"0.2.0.0",UpdateTrust.Channel,"win-x64",22000,DateTimeOffset.UtcNow,"https://github.com/"+UpdateTrust.Repository+"/releases/download/v0.2.0.0/package.zip",new FileInfo(package).Length,UpdatePackage.Hash(package),files);
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var publicKey=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        byte[] Sign(UpdateManifest manifest){var payload=JsonSerializer.SerializeToUtf8Bytes(manifest);return JsonSerializer.SerializeToUtf8Bytes(new UpdateEnvelope(Convert.ToBase64String(payload),Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))));}
        var envelope=Sign(m);var verified=UpdatePackage.VerifyEnvelope(envelope,publicKey,new(0,1,0,0),26200,DateTimeOffset.UtcNow);
        check(verified.Version=="0.2.0.0","signed newer update is accepted against its pinned identity");
        using var other=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try{UpdatePackage.VerifyEnvelope(envelope,Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()),new(0,1,0,0),26200,DateTimeOffset.UtcNow);throw new Exception("Expected signature rejection");}catch(InvalidDataException){check(true,"another signing key cannot authorise an update");}
        foreach(var invalid in new[]{m with{Product="Other.App"},m with{Architecture="win-arm64"},m with{Version="0.1.0.0"},m with{Channel="preview"},m with{PackageUrl="https://example.com/package.zip"},m with{MinimumWindowsBuild=99999},m with{Files=[..files,files[0]]}})
            try{UpdatePackage.VerifyEnvelope(Sign(invalid),publicKey,new(0,1,0,0),26200,DateTimeOffset.UtcNow);throw new Exception("Expected metadata rejection");}catch(InvalidDataException){check(true,"signed update contract rejects mismatched, stale or unsafe metadata");}
        var changed=JsonSerializer.Deserialize<UpdateEnvelope>(envelope)!;var altered=JsonSerializer.SerializeToUtf8Bytes(changed with{Payload=Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))});
        try{UpdatePackage.VerifyEnvelope(altered,publicKey,new(0,1,0,0),26200,DateTimeOffset.UtcNow);throw new Exception("Expected tamper rejection");}catch(InvalidDataException){check(true,"changing signed metadata invalidates its signature");}
        foreach(var bad in new[]{"../outside","C:/outside","folder\\outside","file:stream","CON.txt","folder/name.","folder//name"})
            try{UpdatePackage.Relative(bad);throw new Exception("Expected path rejection");}catch(InvalidDataException){check(true,"unsafe update path is rejected: "+bad);}
        var extracted=Path.Combine(fixture,"verified");UpdatePackage.Extract(package,extracted,m);
        check(files.All(f=>UpdatePackage.Hash(Path.Combine(extracted,f.Path))==f.Sha256),"update extraction verifies actual package bytes and executable version");
        var unsafeZip=Zip("traversal.zip",UpdateTrust.ArchiveRoot+"../outside.txt");var unsafeM=m with{PackageBytes=new FileInfo(unsafeZip).Length,PackageSha256=UpdatePackage.Hash(unsafeZip)};
        var rejected=Path.Combine(fixture,"reject");
        try{UpdatePackage.Extract(unsafeZip,rejected,unsafeM);throw new Exception("Expected archive rejection");}catch(InvalidDataException){check(!Directory.Exists(rejected)&&!File.Exists(Path.Combine(fixture,"outside.txt")),"archive traversal is rejected before extraction writes");}
        string Install(string name)
        {
            var dir=Path.Combine(fixture,name);Directory.CreateDirectory(dir);File.Copy(old,Path.Combine(dir,UpdateTrust.MainExe));File.WriteAllText(Path.Combine(dir,UpdatePackage.Marker),JsonSerializer.Serialize(new{Product=UpdateTrust.Product}));File.WriteAllText(Path.Combine(dir,"user-notes.txt"),"keep user data");return dir;
        }
        var installed=Install("installed");UpdateCommit.Apply(installed,extracted,m);
        check(UpdatePackage.Hash(Path.Combine(installed,UpdateTrust.MainExe))==UpdatePackage.Hash(exe)&&File.ReadAllText(Path.Combine(installed,"user-notes.txt"))=="keep user data","in-place update replaces owned files and retains unrelated user files");
        var watchedInstall=Install("journal-reader");FileStream? journalReader=null;
        try
        {
            var completed=UpdateCommit.Apply(watchedInstall,extracted,m,n=>{
                if(n==1)journalReader=new FileStream(Path.Combine(watchedInstall,".techloungeos-update-journal.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            });
            using var completedJournal=JsonDocument.Parse(File.ReadAllText(Path.Combine(completed,"completed.json")));
            check(completedJournal.RootElement.GetProperty("Complete").GetBoolean()&&UpdatePackage.Hash(Path.Combine(watchedInstall,UpdateTrust.MainExe))==UpdatePackage.Hash(exe),"an external journal reader does not interrupt an atomic in-place update");
        }
        finally{journalReader?.Dispose();}
        var readOnlyInstall=Install("read-only-install");var readOnlyExe=Path.Combine(readOnlyInstall,UpdateTrust.MainExe);var readOnlyMarker=Path.Combine(readOnlyInstall,UpdatePackage.Marker);
        File.SetAttributes(readOnlyExe,FileAttributes.ReadOnly);File.SetAttributes(readOnlyMarker,FileAttributes.ReadOnly);
        var markerTime=File.GetLastWriteTimeUtc(readOnlyMarker);
        UpdateCommit.Apply(readOnlyInstall,extracted,m);
        check(UpdatePackage.Hash(readOnlyExe)==UpdatePackage.Hash(exe)&&(File.GetAttributes(readOnlyExe)&FileAttributes.ReadOnly)!=0,"authorised update replaces read-only owned files and retains their read-only preference");
        check(File.GetLastWriteTimeUtc(readOnlyMarker)==markerTime&&(File.GetAttributes(readOnlyMarker)&FileAttributes.ReadOnly)!=0,"byte-identical install metadata is left untouched during updates");
        File.SetAttributes(readOnlyExe,FileAttributes.Normal);File.SetAttributes(readOnlyMarker,FileAttributes.Normal);
        var readOnlyRollback=Install("read-only-rollback");var rollbackExe=Path.Combine(readOnlyRollback,UpdateTrust.MainExe);File.SetAttributes(rollbackExe,FileAttributes.ReadOnly);
        try{UpdateCommit.Apply(readOnlyRollback,extracted,m,n=>{if(n==2)throw new IOException("Read-only rollback injection");});throw new Exception("Expected rollback");}
        catch(IOException ex)when(ex.Message=="Read-only rollback injection"){check(UpdatePackage.Hash(rollbackExe)==UpdatePackage.Hash(old)&&(File.GetAttributes(rollbackExe)&FileAttributes.ReadOnly)!=0,"rollback restores original executable bytes and its read-only attribute");}
        File.SetAttributes(rollbackExe,FileAttributes.Normal);
        var rollback=Install("rollback");var oldHash=UpdatePackage.Hash(Path.Combine(rollback,UpdateTrust.MainExe));
        try{UpdateCommit.Apply(rollback,extracted,m,n=>{if(n==2)throw new IOException("Injected commit failure");});throw new Exception("Expected rollback");}catch(IOException ex) when(ex.Message=="Injected commit failure")
        {check(UpdatePackage.Hash(Path.Combine(rollback,UpdateTrust.MainExe))==oldHash&&!File.Exists(Path.Combine(rollback,@"updater\TechLoungeOS.Updater.exe"))&&File.ReadAllText(Path.Combine(rollback,"user-notes.txt"))=="keep user data","failed update rolls back previous bytes without deleting unrelated data");}
        var recovery=Install("power-cut");var backup=Path.Combine(recovery,".techloungeos-rollback-fixture");Directory.CreateDirectory(backup);
        File.Move(Path.Combine(recovery,UpdateTrust.MainExe),Path.Combine(backup,UpdateTrust.MainExe));File.Copy(exe,Path.Combine(recovery,UpdateTrust.MainExe));
        File.WriteAllText(Path.Combine(recovery,".techloungeos-update-journal.json"),JsonSerializer.Serialize(new UpdateJournal(recovery,backup,[new(UpdateTrust.MainExe,true,UpdatePackage.Hash(old),UpdatePackage.Hash(exe))],false)));
        UpdateCommit.Recover(recovery);
        check(UpdatePackage.Hash(Path.Combine(recovery,UpdateTrust.MainExe))==UpdatePackage.Hash(old),"durable journal restores the previous app after an interrupted file replacement");
        var changedRecovery=Install("changed-recovery");var changedBackup=Path.Combine(changedRecovery,".techloungeos-rollback-fixture");Directory.CreateDirectory(changedBackup);
        File.Copy(old,Path.Combine(changedBackup,UpdateTrust.MainExe));File.WriteAllText(Path.Combine(changedRecovery,UpdateTrust.MainExe),"user changed this owned file");
        File.WriteAllText(Path.Combine(changedRecovery,".techloungeos-update-journal.json"),JsonSerializer.Serialize(new UpdateJournal(changedRecovery,changedBackup,[new(UpdateTrust.MainExe,true,UpdatePackage.Hash(old),UpdatePackage.Hash(exe))],false)));
        try{UpdateCommit.Recover(changedRecovery);throw new Exception("Expected changed-file rejection");}
        catch(IOException ex){check(ex.Message.Contains("target changed")&&File.ReadAllText(Path.Combine(changedRecovery,UpdateTrust.MainExe))=="user changed this owned file"&&File.Exists(Path.Combine(changedBackup,UpdateTrust.MainExe)),"recovery retains backups and refuses to overwrite an unexpected later modification");}
        using var http=new HttpClient(new NoReleaseHandler());
        try{await new AppUpdates(http).Check(new(0,1,0,0),CancellationToken.None);throw new Exception("Expected unpublished channel status");}catch(AppUpdateFeedUnavailableException){check(true,"unpublished repository is unavailable, never falsely reported up-to-date");}
    }
    private sealed class NoReleaseHandler:HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));}
}
