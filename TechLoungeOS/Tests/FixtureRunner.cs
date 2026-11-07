using TechLoungeOS;

// A filesystem fixture, not evidence of DISM or Windows installation compatibility.
sealed class FixtureRunner(string workspace, bool failRemoval = false, bool failExport = false, Action<string[]>? afterDism = null, bool ignoreRemoval = false, bool failInventory = false, bool failPostFeatureInventory = false, bool inheritedSetup = false, Queue<string>? healthResults = null, bool missingFramework = false, bool failVerification = false) : ICommandRunner
{
    public List<string[]> Commands { get; } = [];
    public List<string> Scripts { get; } = [];
    private bool packageRemoved;
    private bool featureDisabled;
    public Task<string> PowerShell(string script, CancellationToken ct = default)
    {
        Scripts.Add(script);
        if (script.StartsWith("# TechLoungeOS complete ISO file verification", StringComparison.Ordinal))
        {
            if (failVerification) throw new IOException("Injected export verification failure");
            return Task.FromResult("Fixture export verification only; no ISO mounted.");
        }
        if(script.StartsWith("# TechLoungeOS mandatory SMB1 source check"))return Task.FromResult("Disabled");
        if(script.StartsWith("[string](Get-WindowsImage -ImagePath ")&&script.Contains("packed-install.esd"))return Task.FromResult("10.0.26200.1");
        if (script.Contains("TlosWimMetadata")) return Task.FromResult("<WIM><IMAGE INDEX='2'><NAME>Windows 11 Pro</NAME><WINDOWS><ARCH>9</ARCH><EDITIONID>Professional</EDITIONID><VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26200</BUILD><SPBUILD>1</SPBUILD></VERSION></WINDOWS></IMAGE></WIM>");
        if (script.Contains("Get-WindowsImage -ImagePath")) return Task.FromResult("""[{"ImageIndex":2,"ImageName":"Windows 11 Pro","Architecture":"9","Version":"10.0.26200.1"}]""");
        if (script.Contains("robocopy.exe"))
        {
            foreach (var name in new[] { @"boot\etfsboot.com", @"efi\microsoft\boot\efisys.bin", @"sources\boot.wim", @"sources\install.esd", "setup.exe" })
            {
                var dest = Path.Combine(workspace, "media", name);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.WriteAllText(dest, "fixture");
            }
            if (inheritedSetup)
            {
                File.WriteAllText(Path.Combine(workspace, "media", "autounattend.xml"), "<unattend />");
                File.WriteAllText(Path.Combine(workspace, "media", "sources", "PID.txt"), "fixture configuration, no real key");
            }
            return Task.FromResult("");
        }
        throw new Exception("Unexpected script: " + script);
    }
    public Task<string> Dism(params string[] args)
    {
        if(args.Contains("/Mount-Image") && args.Any(a=>a.EndsWith("setup-compatibility-boot.wim",StringComparison.Ordinal)))
        {
            var mount=args.Single(a=>a.StartsWith("/MountDir:",StringComparison.Ordinal))[10..];
            SettingsChecks.WriteHive(Path.Combine(mount,@"Windows\System32\config\SYSTEM"),@"ControlSet001\Services\ExistingGhostService",42);
            File.WriteAllText(Path.Combine(mount,"original-tool.txt"),"inert original tool fixture");
            Commands.Add(args);afterDism?.Invoke(args);return Task.FromResult("");
        }
        if(args.Contains("/Mount-Image"))
        {
            foreach(var relative in new[]{@"Windows\System32\reagentc.exe",@"Windows\System32\Recovery\Winre.wim",@"Windows\System32\powrprof.dll"}){
                var recovery=Path.Combine(workspace,"mount",relative);Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);File.WriteAllText(recovery,"inert recovery prerequisite fixture");
            }
            var profile=Path.Combine(workspace,@"mount\Users\Default\NTUSER.DAT");
            if(!File.Exists(profile))SettingsChecks.WriteHive(profile,@"Software\FixtureVendor",42);
        }
        if(args.Contains("/Mount-Image")&&!missingFramework){
            var framework=Path.Combine(workspace,@"mount\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll");Directory.CreateDirectory(Path.GetDirectoryName(framework)!);File.WriteAllText(framework,"inert Windows Framework prerequisite fixture");
            foreach(var dependency in new[]{"System.Drawing.dll","System.Windows.Forms.dll","System.Web.Extensions.dll"})File.WriteAllText(Path.Combine(Path.GetDirectoryName(framework)!,dependency),"inert desktop prerequisite fixture");
        }
        Commands.Add(args);
        if (failInventory && args.Contains("/Get-ProvisionedAppxPackages")) throw new IOException("Native inventory initialization failed (87)");
        if (failPostFeatureInventory && featureDisabled && args.Contains("/Get-Features")) throw new IOException("Post-servicing feature inventory failed (87)");
        if (args.Contains("/Get-ProvisionedAppxPackages")) return Task.FromResult((packageRemoved ? "" : "DisplayName : Microsoft.BingNews\r\nPackageName : Microsoft.BingNews_1.0_x64__abc\r\n") + "The operation completed successfully.");
        if (args.Contains("/Get-Features")) return Task.FromResult("Feature Name : WorkFolders-Client\r\nState : " + (featureDisabled ? "Disabled" : "Enabled") + "\r\nThe operation completed successfully.");
        if (args.Contains("/Export-Image")) File.WriteAllText(Path.Combine(workspace, "selected.wim"), "fixture wim");
        if (failRemoval && args.Contains("/Remove-ProvisionedAppxPackage")) throw new IOException("Injected removal failure");
        if (args.Contains("/Remove-ProvisionedAppxPackage") && !ignoreRemoval) packageRemoved = true;
        if (args.Contains("/Disable-Feature")) featureDisabled = true;
        afterDism?.Invoke(args);
        return Task.FromResult(args.Contains("/ScanHealth") ? healthResults?.Dequeue() ?? "No component store corruption detected." : "");
    }
    public Task<string> Run(string exe, IEnumerable<string> args, CancellationToken ct = default)
    {
        var values=args.ToArray();Commands.Add(values);
        if(Path.GetFileName(exe)=="wimlib-imagex.exe")
        {
            var packed=Path.Combine(workspace,"packed-install.esd");
            if(!values.SequenceEqual(new[]{"export",Path.Combine(workspace,"selected.wim"),"1",packed,"--solid","--threads=2","--check"}))throw new Exception("Unexpected compressor fixture request");
            File.WriteAllText(packed,"inert packed image fixture");return Task.FromResult("");
        }
        if(Path.GetFileName(exe) is not ("oscdimg.exe" or "inert-oscdimg-fixture") || !values[^1].EndsWith(".partial.iso",StringComparison.Ordinal))throw new Exception("Unexpected executable fixture request");
        File.WriteAllText(values[^1], "fixture ISO");
        if (failExport) throw new IOException("Injected ISO failure");
        return Task.FromResult("");
    }
}
