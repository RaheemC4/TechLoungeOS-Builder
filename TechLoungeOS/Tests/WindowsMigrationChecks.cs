using TechLoungeOS;

internal static class WindowsMigrationChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        var rule=WindowsUpgradeRules.Get("win11-26h2-x64");
        var plan=new WindowsImageUpdates.Plan(2,"10.0.26200.9457","10.0.26300.9725",[
            new("windows11.0-kb5043080-x64_953449672073f8fb99badb4cc6d5d7849b9c83e8.msu",533761740,"819632610A101AF11C72907FB13FF05B5FB4222C0E263EE9A03BCC1D1D5F26A4"),
            new("windows11.0-kb5134133-x64_0e59d2f988a5422dfb489e97faed3b3835fc7568.msu",4879117191,"CDD9D082A87C131F2ECFF346717E163C23D3DADC323FA8FAED00826D8E135872"),
            new(rule.File,rule.Bytes,rule.Sha256)],new(rule.Id,"10.0.26200.9725"));
        WindowsImageUpdates.ValidatePlan(plan);
        var route=WindowsUpdateMigrations.Find(plan)??throw new Exception("Reviewed native migration route missing.");
        check(route.Transitions.Length==19,"the exact observed Microsoft package sequence resolves its bounded reviewed service transitions after camera correction");
        var standard=WindowsUpdateMigrations.Find(plan with{SourceVersion="10.0.26200.8037"})??throw new Exception("Measured standard migration route missing");
        check(standard.Transitions.Length==65 && standard.RequiredServices?.Length==2 && standard.Metadata?.Length==3 &&
            standard.Tasks?.BeforeRaw=="21B57F0C717A5B8018CD344455F7E6BC4DD4168A5CC4B33643412DC2A8D2CF3F" &&
            standard.Frameworks?.Single(f=>f.RetiresSource).Source=="Microsoft.WindowsAppRuntime.vNext.CBS_8wekyb3d8bbwe",
            "the measured8037 route is separately bounded to its original services, task cache and reviewed framework replacement");
        Reject(()=>WindowsUpdateMigrations.RequireSource(standard,"unused",new Dictionary<string,string>(),new([],[])),
            "the stock migration rejects missing source security/AI services before reading frameworks or servicing the image");
        foreach(var other in new[]{plan with{SourceVersion="10.0.26200.9444"},plan with{TargetVersion="10.0.26300.9800"},plan with{Feature=null},
            plan with{Packages=[plan.Packages[1],plan.Packages[0],plan.Packages[2]]},plan with{Packages=[plan.Packages[0] with{Sha256=new string('0',64)},plan.Packages[1],plan.Packages[2]]}})
            check(WindowsUpdateMigrations.Find(other) is null,"different source, target, route, package identity or order cannot inherit a servicing exception");
        // Literal hashes from independent stock8037/native26H2 evidence, not calculated by the verifier.
        const string original="B618D5A7DDAF1F78C4F4AC646A1265B9A35FED8F775D0233D87790A6C3D566D6";
        const string updated="0908A46B1225CF9066B25C92D2734FA38265A13EF18514356777E8430E450CA7";
        var before=new Dictionary<string,string>{{@"ControlSet001\McmSvc",original},{"policy:custom","user value"}};
        var after=new Dictionary<string,string>{{@"ControlSet001\McmSvc",updated},{"policy:custom","user value"}};
        WindowsUpdateMigrations.VerifyConfiguration(route,before,after);
        check(true,"a known stock service definition can receive its reviewed Windows security metadata without changing a custom policy");
        const string predecessor=@"ControlSet001\IsoEnvBroker",replacement=@"ControlSet001\IsoSessionCore";
        // Independent hashes recorded by the native before/after hive comparison.
        const string predecessorHash="D4ACCE91EC594B96343E27AE93865F6EA087A376A5B9F6ED6FFF4D05A22EA92E";
        const string replacementHash="350E5BA77949AC03FC9128D6D29F869EADBDCECFD200272492ACC06378905DD6";
        var oldService=new Dictionary<string,string>{{predecessor,predecessorHash}};
        var newService=new Dictionary<string,string>{{replacement,replacementHash}};
        WindowsUpdateMigrations.VerifyConfiguration(route,oldService,newService);
        check(true,"a verified replacement service requires its matching original predecessor and retirement");
        Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,new Dictionary<string,string>(),newService),
            "a replacement cannot resurrect a service already stripped from the custom source");
        Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,new Dictionary<string,string>{{predecessor,"custom disabled configuration"}},newService),
            "a replacement cannot discard custom disabled predecessor settings");
        Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,oldService,new Dictionary<string,string>(newService){{predecessor,predecessorHash}}),
            "a verified replacement cannot leave both the old and new services installed");
        foreach(var changed in new[]{
            new Dictionary<string,string>(after){["policy:custom"]="overwritten"},
            new Dictionary<string,string>(after){[@"ControlSet001\McmSvc"]=new string('0',64)},
            new Dictionary<string,string>(after){[@"ControlSet001\UnexpectedService"]="new"}})
            Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,before,changed),"unknown service changes and overwritten policy settings still reject export");
        Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,new Dictionary<string,string>(before){[@"ControlSet001\McmSvc"]=new string('1',64)},after),
            "a custom source startup/security branch cannot be replaced under a stock-default migration rule");
        Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(null,before,after),"unreviewed updates retain the original exact service gate");
        var kept=new ImageServicing.Feature("Kept","Disabled with Payload Removed");
        var enabled=new ImageServicing.Feature(WindowsUpdateMigrations.CameraFeature,"Enabled");
        var removed=enabled with{State="Disabled with Payload Removed"};
        check(WindowsUpdateMigrations.ExistingFeatures(route,[kept],[kept,removed]).SequenceEqual([kept]),
            "only the known newly introduced, payload-removed camera entry is excluded from the old feature catalogue comparison");
        Reject(()=>WindowsUpdateMigrations.ExistingFeatures(route,[kept],[kept,enabled]),"an enabled new camera feature cannot be accepted");
        check(WindowsUpdateMigrations.ExistingFeatures(route,[kept],[kept,enabled with{State="Disabled"}]).SequenceEqual(new[]{kept}),
            "Windows client's fully disabled camera feature can retain servicing files for recovery");
        Reject(()=>WindowsUpdateMigrations.ExistingFeatures(route,[kept],[kept,enabled with{State="Disable Pending"}]),"a pending camera disable cannot be accepted");
        Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,new Dictionary<string,string>(),new Dictionary<string,string>{{@"ControlSet001\Ccx01000","3F5765E14A6E2ABD3FB41B67BE2AA15AF0F096C219DA682C29B2CA6E9CB6EA58"}}),
            "a restored camera driver registration cannot use the reviewed service exceptions");
        check(WindowsUpdateMigrations.ExistingFeatures(route,[kept,enabled],[kept,enabled]).Length==2,
            "a camera feature that existed in the source remains subject to ordinary source-state preservation");
        var commands=new CameraCommands();
        check(await WindowsUpdateMigrations.DisableNewCamera(commands,@"C:\inert mount",route,[kept],[kept,enabled],CancellationToken.None) &&
            commands.Arguments!.SequenceEqual(new[]{@"/Image:C:\inert mount","/Disable-Feature","/FeatureName:"+WindowsUpdateMigrations.CameraFeature,"/Remove","/NoRestart"}),
            "the camera correction uses standard DISM removal for that feature only and never forces a restart");
        commands.Arguments=null;
        check(!await WindowsUpdateMigrations.DisableNewCamera(commands,"unused",route,[kept,enabled],[kept,enabled],CancellationToken.None) && commands.Arguments is null,
            "the correction never removes an original source camera feature");
        check(!await WindowsUpdateMigrations.DisableNewCamera(commands,"unused",route,[kept],[kept,removed],CancellationToken.None) && commands.Arguments is null,
            "a completed camera removal is idempotent");
        check(!await WindowsUpdateMigrations.DisableNewCamera(commands,"unused",route,[kept],[kept,enabled with{State="Disabled"}],CancellationToken.None) && commands.Arguments is null,
            "Windows client's completed camera disable is idempotent");
        check(!await WindowsUpdateMigrations.DisableNewCamera(commands,"unused",null,[kept],[kept,enabled],CancellationToken.None) && commands.Arguments is null,
            "unknown update routes cannot invoke camera correction");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try{await WindowsUpdateMigrations.DisableNewCamera(commands,"unused",route,[kept],[kept,enabled],cancelled.Token);throw new Exception("Cancellation ignored");}
        catch(OperationCanceledException){check(commands.Arguments is null,"cancellation before the correction invokes no servicing command");}
        var fixture=Path.Combine(Path.GetTempPath(),"TLOS-migration-preferences-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
        try
        {
            var system=Path.Combine(fixture,"SYSTEM");var software=Path.Combine(fixture,"SOFTWARE");
            SettingsChecks.WriteValues(system,[(@"ControlSet001\Control\PriorityControl","Win32PrioritySeparation",4,BitConverter.GetBytes(38u))]);
            SettingsChecks.WriteValues(software,[(@"Microsoft\Windows\CurrentVersion\Internet Settings\Zones\3","1806",4,BitConverter.GetBytes(0u))]);
            var preferences=SourcePreservation.CaptureUpdatePreferences(system,software);
            WindowsUpdateMigrations.VerifyConfiguration(route,preferences,SourcePreservation.CaptureUpdatePreferences(system,software));
            check(true,"real offline global performance and Internet-zone preferences survive a read-only migration comparison");
            File.Delete(system);SettingsChecks.WriteValues(system,[(@"ControlSet001\Control\PriorityControl","Win32PrioritySeparation",4,BitConverter.GetBytes(2u))]);
            Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,preferences,SourcePreservation.CaptureUpdatePreferences(system,software)),
                "a changed global performance tweak outside service/policy branches still blocks a reviewed Windows migration");
            File.Delete(system);SettingsChecks.WriteValues(system,[(@"ControlSet001\Control\PriorityControl","Win32PrioritySeparation",4,BitConverter.GetBytes(38u))]);
            File.Delete(software);SettingsChecks.WriteValues(software,[(@"Microsoft\Windows\CurrentVersion\Internet Settings\Zones\3","1806",4,BitConverter.GetBytes(3u))]);
            Reject(()=>WindowsUpdateMigrations.VerifyConfiguration(route,preferences,SourcePreservation.CaptureUpdatePreferences(system,software)),
                "resetting an existing Internet-zone preference cannot be hidden by a service migration exception");
        }
        finally{Directory.Delete(fixture,true);}
        void Reject(Action action,string name){try{action();throw new Exception("Unsafe migration accepted: "+name);}catch(IOException){check(true,name);}}
    }
    private sealed class CameraCommands:ICommandRunner
    {
        internal string[]? Arguments;
        public Task<string> Dism(params string[] args){Arguments=args;return Task.FromResult("Inert command boundary");}
        public Task<string> PowerShell(string script,CancellationToken ct=default)=>throw new Exception("No PowerShell may run in this fixture");
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)=>throw new Exception("No executable may run in this fixture");
    }
}
