using TechLoungeOS;

static class StandaloneChecks
{
    public static Task Run(string root,Action<bool,string> check)
    {
        var mount=Path.Combine(root,"excluded-standalone");
        try{Standalone.Stage(mount,["Vendor.Example"],true);throw new Exception("Activation staging was accepted");}
        catch(NotSupportedException){check(!Directory.Exists(mount),"excluded activation action fails before writing any installation payload");}
        try{_ = Provisioning.Script([],false,false,standalone:true);throw new Exception("Activation queue was generated");}
        catch(NotSupportedException){check(true,"legacy automatic-activation requests fail explicitly instead of generating a first-login action");}
        try{_ = FixedSetupTask.Queue("TechLoungeOS-Standalone");throw new Exception("Activation task was accepted");}
        catch(ArgumentException){check(true,"fixed task registration no longer permits the supplied activation action");}
        var script=Provisioning.Script(["Vendor.Example"],false,false,office:true,bootOptimizer:true,desktopDefaults:true);
        check(script.Contains("Invoke-Step 'office'") && script.Contains("Invoke-Step 'boot-optimizer'") && script.Contains("Invoke-Step 'power-defaults'") &&
            !script.Contains("Invoke-Step 'standalone'") && !script.Contains("TechLoungeOS-Standalone"),
            "normal Office deployment, post-app Boot Optimizer and final power defaults remain available without an activation action");
        check(!typeof(Standalone).Assembly.GetManifestResourceNames().Any(name=>name.StartsWith("TechLoungeOS.Standalone",StringComparison.Ordinal)),
            "compiled deployment assembly embeds neither the supplied activation CMD nor its worker");
        return Task.CompletedTask;
    }
}