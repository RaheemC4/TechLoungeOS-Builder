using TechLoungeOS;

internal static class WindowsMetadataMigrationChecks
{
    internal static void Run(string folder,Action<bool,string> check)
    {
        string H(char value)=>new(value,64);
        const string profile="default-user:NTUSER",service=@"ControlSet001\WinDefend";
        var route=new WindowsUpdateMigrations.Route("fixture","10.0.26200.8037","10.0.26300.9725",[],[],
            [new(service,H('1'))],[new(profile,H('2'),H('3'))],new(H('4'),H('5'),H('6'),H('7'),H('E')));
        var before=new Dictionary<string,string>{[service]=H('1'),[profile]=H('8'),[WindowsUpdateMetadata.ValueKey(profile)]=H('2'),
            [WindowsUpdateMetadata.RemainderKey(profile)]=H('9'),[TaskCacheIdentity.RawKey]=H('4'),[TaskCacheIdentity.SemanticKey]=H('A'),
            [TaskCacheIdentity.RegistrationKey]=H('B'),[WindowsUpdateMetadata.TaskFilesKey]=H('5'),["policy:custom"]="retained"};
        var after=new Dictionary<string,string>(before){[profile]=H('C'),[WindowsUpdateMetadata.ValueKey(profile)]=H('3'),
            [TaskCacheIdentity.RawKey]=H('D'),[TaskCacheIdentity.SemanticKey]=H('E'),[TaskCacheIdentity.RegistrationKey]=H('7'),[WindowsUpdateMetadata.TaskFilesKey]=H('6')};
        WindowsUpdateMigrations.RequireSource(route,"unused",before,new([],[]));
        WindowsUpdateMigrations.VerifyConfiguration(route,before,after);
        check(true,"reviewed task and profile metadata changes require matching source, task files, permissions and retained settings");
        foreach(var change in new[]{
            (WindowsUpdateMetadata.RemainderKey(profile),H('0')),(WindowsUpdateMetadata.ValueKey(profile),H('0')),
            (WindowsUpdateMetadata.TaskFilesKey,H('0')),(TaskCacheIdentity.RegistrationKey,H('0')),(TaskCacheIdentity.SemanticKey,H('0')),
            (TaskCacheIdentity.SemanticKey,"unreviewed-task-identities"),(TaskCacheIdentity.RawKey,"not a fingerprint"),("policy:custom","overwritten")})
            Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,before,new Dictionary<string,string>(after){[change.Item1]=change.Item2}),"an unrelated setting, task definition, permission or unreviewed identity cannot inherit a metadata exception");
        Reject(()=>WindowsUpdateMigrations.RequireSource(route,"unused",new Dictionary<string,string>(before){[TaskCacheIdentity.RawKey]=H('0')},new([],[])),
            "source task-registry customisations stop this specific migration before servicing");
        Reject(()=>WindowsUpdateMigrations.RequireSource(route,"unused",new Dictionary<string,string>(before){[WindowsUpdateMetadata.TaskFilesKey]=H('0')},new([],[])),
            "source task XML or access customisations stop this specific migration before servicing");
        Reject(()=>WindowsUpdateMigrations.RequireSource(route,"unused",new Dictionary<string,string>(before){[service]=H('0')},new([],[])),
            "a changed or disabled source security service cannot acquire a stock-only migration");

        var mount=Path.Combine(folder,"framework-migration");var apps=Path.Combine(mount,@"Windows\SystemApps");
        const string oldName="Microsoft.Old.CBS_8wekyb3d8bbwe",newName="Microsoft.New.CBS_8wekyb3d8bbwe";
        var original=Path.Combine(apps,oldName);var added=Path.Combine(apps,newName);Directory.CreateDirectory(original);Directory.CreateDirectory(added);
        var oldManifest=Path.Combine(original,"AppxManifest.xml");var newManifest=Path.Combine(added,"AppxManifest.xml");
        File.WriteAllText(oldManifest,"inert source manifest fixture");File.WriteAllText(newManifest,"inert replacement manifest fixture");
        var row=new WindowsUpdateMigrations.FrameworkTransition(newName,oldName,BootableDrive.Hash(oldManifest),BootableDrive.Hash(newManifest));
        var frameworkRoute=route with{Metadata=null,Tasks=null,Frameworks=[row]};var payloads=new WindowsUpdatePreservation.Snapshot([],[oldName]);
        WindowsUpdateMigrations.RequireSource(frameworkRoute,mount,before,payloads);
        WindowsUpdatePreservation.Verify(mount,payloads,frameworkRoute);
        check(true,"a reviewed internal framework requires its original source framework and exact new manifest");
        Reject(()=>WindowsUpdateMigrations.RequireSource(frameworkRoute,mount,before,new([],[])),"a stripped source framework cannot be restored under the reviewed route");
        File.WriteAllText(newManifest,"changed fixture");
        Reject(()=>WindowsUpdatePreservation.Verify(mount,payloads,frameworkRoute),"a changed target framework manifest is rejected");
        File.WriteAllText(newManifest,"inert replacement manifest fixture");
        Reject(()=>WindowsUpdatePreservation.Verify(mount,new([], [oldName,"UserChosenSystemApp"]),frameworkRoute),"a disappearing unrelated source system app is rejected");
        var retiredRoute=frameworkRoute with{Frameworks=[row with{RetiresSource=true}]};
        Reject(()=>WindowsUpdatePreservation.Verify(mount,payloads,retiredRoute),"a declared framework replacement cannot silently leave an extra predecessor installed");
        File.Delete(oldManifest);Directory.Delete(original);WindowsUpdatePreservation.Verify(mount,payloads,retiredRoute);
        check(true,"only the explicitly reviewed predecessor may retire when its exact replacement is present");
        var differences=WindowsUpdatePreservation.Differences([],[],[],[],new Dictionary<string,string>(),new Dictionary<string,string>(),payloads,new([], [newName]));
        check(differences.Any(d=>d.Kind=="system-app" && d.Name==oldName && d.Before=="present" && d.After=="absent"),"diagnostics retain disappearing system apps as well as additions");
        void Reject(Action action,string name){try{action();throw new Exception("Unsafe metadata migration accepted");}catch(IOException){check(true,name);}}
    }
}
