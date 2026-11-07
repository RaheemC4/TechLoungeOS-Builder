using System.Text.Json;
using System.Xml.Linq;
using TechLoungeOS;

static class OfficeChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var omitted = new BuildOptions("not-read.iso", 1, Path.Combine(root, "office-required"), "not-created.iso", "not-run.exe", [], [], false, false, null) { IncludeOffice = false };
        try { await new BuildEngine(_ => throw new Exception("No command may run")).Build(omitted, CancellationToken.None); throw new Exception("Expected mandatory Office rejection"); }
        catch (InvalidOperationException ex) when (ex.Message == "Office is required in every app-created ISO.")
        { check(!Directory.Exists(omitted.Workspace), "production builds cannot omit Office, even outside the UI; rejection creates no workspace or commands"); }
        var originalCache=Path.Combine(root,"office-original-cache");SeedCache(originalCache,"16.0.99999.99");
        var refreshing=Path.Combine(root,"office-refresh");Directory.CreateDirectory(refreshing);
        Provisioning.CopyTree(Path.Combine(originalCache,"Office"),Path.Combine(refreshing,"Office"));
        var download=new CurrentDownload("16.0.1.3");
        var latest=await OfficePack.RefreshCurrent(download,"unused-signed-odt.exe",refreshing,CancellationToken.None);
        check(latest=="16.0.1.3" && OfficePack.ValidateFiles(refreshing)==latest && !Directory.Exists(Path.Combine(refreshing,"Office","Data","16.0.99999.99")),"ODT's newly downloaded Current version replaces only the copied cache, even when its version number is lower than an unrelated cached channel");
        check(Directory.Exists(Path.Combine(originalCache,"Office","Data","16.0.99999.99")) && File.ReadAllBytes(Path.Combine(originalCache,"Office","Data","16.0.99999.99","s640.cab")).SequenceEqual(new byte[]{1}),"refresh leaves the original supplied Office cache unchanged");
        check(await OfficePack.RefreshCurrent(download,"unused-signed-odt.exe",refreshing,CancellationToken.None)==latest && download.Calls==2,"an already current Office cache still invokes unpinned Current download on each build");
        var failedDownload=new CurrentDownload("16.0.1.4",false);
        try{await OfficePack.RefreshCurrent(failedDownload,"unused-signed-odt.exe",refreshing,CancellationToken.None);throw new Exception("Expected incomplete refresh rejection");}
        catch(IOException){check(Directory.Exists(Path.Combine(refreshing,"Office","Data",latest)),"an incomplete newer Office download stops before the prior copied version is retired");}
        var xml = OfficePack.Configuration(@"D:\O'Brien & cache", "16.0.1.2"); var add = xml.Root!.Element("Add")!;
        check(add.Attribute("OfficeClientEdition")!.Value == "64" && add.Element("Product")!.Attribute("ID")!.Value == "O365ProPlusRetail" && add.Element("Product")!.Element("Language")!.Attribute("ID")!.Value == "en-us", "Office configuration matches the requested x64 en-US subscription product");
        check(xml.Root.Element("Remove") == null && !xml.ToString().Contains("PIDKEY") && xml.Root.Element("Updates")!.Attribute("Enabled")!.Value == "TRUE", "Office configuration keeps updates enabled without removal or product keys");
        check(XDocument.Parse(xml.ToString()).Root!.Element("Add")!.Attribute("SourcePath")!.Value == @"D:\O'Brien & cache", "Office source path is safely XML-escaped");
        check(OfficePack.ToolUrl("<a href='https://download.microsoft.com/download/abc-123/officedeploymenttool_20430-20146.exe'>Download</a>") == "https://download.microsoft.com/download/abc-123/officedeploymenttool_20430-20146.exe", "Office tool resolves from the expected official download URL shape");
        try { OfficePack.ToolUrl("https://example.com/officedeploymenttool_1-2.exe"); throw new Exception("Expected URL rejection"); } catch (IOException) { check(true, "an unfamiliar Office tool source is rejected"); }
        var fixture = Path.Combine(root,"office O'Brien 测试"); Directory.CreateDirectory(fixture);
        var prepared = Path.Combine(fixture,"prepared"); var data = Path.Combine(prepared,@"Office\Data"); var version = Path.Combine(data,"16.0.1.2"); Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(data,"v64.cab"),"fixture");
        foreach (var name in new[] { "stream.x64.x-none.dat", "stream.x64.en-us.dat", "i640.cab", "s640.cab" }) File.WriteAllText(Path.Combine(version,name),"fixture, never installed");
        check(OfficePack.ValidateFiles(prepared) == "16.0.1.2", "Office payload validation pins one complete metadata/version layout");
        File.WriteAllText(Path.Combine(prepared,"setup.exe"),"fixture, never executed");
        OfficePack.Configuration(version:"16.0.1.2").Save(Path.Combine(prepared,"Configuration.xml"));
        OfficePack.Configuration(version:"16.0.1.2").Save(Path.Combine(prepared,"TechLoungeOS-Office.template.xml"));
        var script = OfficePack.Script.Replace("if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Office setup requires administrator permission.' }", "# fixture-only elevation substitution");
        File.WriteAllText(Path.Combine(prepared,"TechLoungeOS-Office.ps1"),script);
        var files = Directory.GetFiles(Path.Combine(prepared,"Office"),"*",SearchOption.AllDirectories).Concat(new[] {Path.Combine(prepared,"setup.exe"),Path.Combine(prepared,"TechLoungeOS-Office.template.xml")});
        File.WriteAllText(Path.Combine(prepared,"TechLoungeOS-Office.sha256.json"),JsonSerializer.Serialize(files.ToDictionary(f => Path.GetRelativePath(prepared,f), RuntimePack.Hash)));
        var media = Path.Combine(fixture,"media"); var mount = Path.Combine(fixture,"mount");
        Directory.CreateDirectory(SetupHooks.OemScripts(media)); Directory.CreateDirectory(Path.GetDirectoryName(SetupHooks.ImageHook(mount))!);
        File.WriteAllText(SetupHooks.ImageHook(mount),"@echo image-original\r\n"); var original="@echo oem-original\r\n";
        File.WriteAllText(Path.Combine(SetupHooks.OemScripts(media),"SetupComplete.cmd"),original);
        check(SetupHooks.Prepare(media,mount), "existing OEM hook precedence is recognised");
        File.SetAttributes(Path.Combine(prepared,"Configuration.xml"),FileAttributes.ReadOnly);
        var officeStep=OfficePack.Stage(media,mount,prepared);
        check((File.GetAttributes(Path.Combine(SetupHooks.OemScripts(media),"Configuration.xml"))&FileAttributes.ReadOnly)==0 &&
            (File.GetAttributes(Path.Combine(prepared,"Configuration.xml"))&FileAttributes.ReadOnly)!=0,"Office staging makes its generated destination writable while retaining the supplied media attributes");
        var runtime=Path.Combine(fixture,"runtime");Directory.CreateDirectory(runtime);File.WriteAllText(Path.Combine(runtime,"install.ps1"),"# fixture");RuntimePack.Stage(mount,runtime);
        SetupHooks.FinalizeOem(media,mount);
        var target=SetupHooks.OemScripts(media);var batch=File.ReadAllText(Path.Combine(target,"SetupComplete.cmd"));
        check(Directory.Exists(Path.Combine(target,"Office")) && File.Exists(Path.Combine(target,"setup.exe")) && File.Exists(Path.Combine(target,"Configuration.xml")), "Office folder, setup.exe and Configuration.xml sit beside SetupComplete in sources/$OEM$/$$/Setup/Scripts");
        var deferred=File.ReadAllText(Path.Combine(target,"TechLoungeOS-Office.cmd"));
        check(deferred.Contains("fltmc >nul || exit /b\r\n") && deferred.Contains("cd /d \"%~dp0\"\r\n") && deferred.Contains("setup.exe /configure Configuration.xml\r\n") && batch.EndsWith(original), "deferred Office command retains the configure steps while SetupComplete keeps only the inherited body");
        check(batch == original && batch == File.ReadAllText(SetupHooks.ImageHook(mount)) && officeStep.Id=="Office" && officeStep.Files.Any(f=>f.Destination=="TechLoungeOS-Office.cmd") && !officeStep.Files.Any(f=>f.Destination=="SetupComplete.cmd"), "Office is pinned for deferred execution without consuming or running the inherited source hook");
        check(File.ReadAllText(Path.Combine(mount,@"ProgramData\TechLoungeOS\source-hooks\Image-SetupComplete.cmd")) == "@echo image-original\r\n", "superseded image setup body is retained without running extra source commands");
        var run = new ProcessRunner(_=>{}); var scriptPath=Path.Combine(prepared,"TechLoungeOS-Office.ps1");
        var parsed=await run.PowerShell("$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseFile("+Policy.Ps(scriptPath)+",[ref]$tokens,[ref]$errors);if($errors.Count){throw ($errors|Out-String)};'parsed'");
        check(parsed.Contains("parsed"), "Office setup script parses in Windows PowerShell 5.1");
        var installed=Path.Combine(fixture,"installed");Directory.CreateDirectory(Path.Combine(installed,@"root\Office16"));
        foreach(var name in new[]{"WINWORD.EXE","EXCEL.EXE","POWERPNT.EXE"})File.WriteAllText(Path.Combine(installed,@"root\Office16",name),"fixture");
        var header=$$"""
            $script:installed=$false
            function Get-ItemProperty { param($Path,$ErrorAction)
              if($Path -like '*ClickToRun*' -and $script:installed){[pscustomobject]@{ProductReleaseIds='O365ProPlusRetail';Platform='x64';InstallationPath={{Policy.Ps(installed)}} } }
            }
            function Start-Process { param($FilePath,$ArgumentList,$WorkingDirectory,[switch]$Wait,[switch]$PassThru)
              if($FilePath -ne {{Policy.Ps(Path.Combine(prepared,"setup.exe"))}} -or $ArgumentList -ne '/configure Configuration.xml' -or $WorkingDirectory -ne {{Policy.Ps(prepared)}}){throw 'Unexpected fixture process'}
              Add-Content {{Policy.Ps(Path.Combine(fixture,"configure-calls.txt"))}} 'configure'
              $script:installed=$true;[pscustomobject]@{ExitCode=0}
            }
            & {{Policy.Ps(scriptPath)}} -Phase Recover
            """;
        await run.PowerShell(header);
        check((File.GetAttributes(Path.Combine(prepared,"Configuration.xml"))&FileAttributes.ReadOnly)==0,"Office recovery handles a read-only copied configuration before invoking the installer");
        var generated=XDocument.Load(Path.Combine(prepared,"Configuration.xml"));
        check(generated.Root!.Element("Add")!.Attribute("SourcePath")!.Value == prepared && generated.Root.Element("Add")!.Attribute("Version")!.Value == "16.0.1.2", "Office configure uses the local payload and pinned build without a host-specific source path");
        check(File.Exists(Path.Combine(prepared,@"TechLoungeOS-Office-State\complete")), "Office completes only after the configured product, architecture and desktop executables are verified");
        await run.PowerShell(header);
        check(File.ReadAllLines(Path.Combine(fixture,"configure-calls.txt")).Length == 1,"completed Office recovery does not reinstall");
        File.Delete(Path.Combine(prepared,@"TechLoungeOS-Office-State\complete")); File.Delete(Path.Combine(version,"s640.cab"));
        try{await run.PowerShell(header);throw new Exception("Expected corrupt payload rejection");}catch(IOException){check(File.ReadAllLines(Path.Combine(fixture,"configure-calls.txt")).Length==1 && !File.Exists(Path.Combine(prepared,@"TechLoungeOS-Office-State\complete")),"missing Office payload stops before any configure invocation");}
    }
    private static void SeedCache(string folder,string version)
    {
        var data=Path.Combine(folder,"Office","Data");var target=Path.Combine(data,version);Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(data,"v64.cab"),[1]);
        foreach(var name in new[]{"stream.x64.x-none.dat","stream.x64.en-us.dat","i640.cab","s640.cab"})File.WriteAllBytes(Path.Combine(target,name),[1]);
    }
    private sealed class CurrentDownload(string version,bool complete=true):ICommandRunner
    {
        public int Calls {get;private set;}
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)
        {
            ct.ThrowIfCancellationRequested();var arguments=args.ToArray();
            if(exe!="unused-signed-odt.exe" || arguments.Length!=2 || arguments[0]!="/download")throw new Exception("Only download mode is allowed in this fixture");
            var add=XDocument.Load(arguments[1]).Root!.Element("Add")!;
            if(add.Attribute("Version")!=null || add.Attribute("Channel")!.Value!="Current")throw new Exception("Cache refresh pinned an old Office version or changed channel");
            var folder=Path.GetDirectoryName(arguments[1])!;SeedCache(folder,version);
            if(!complete)File.Delete(Path.Combine(folder,"Office","Data",version,"s640.cab"));
            Calls++;return Task.FromResult("");
        }
        public Task<string> PowerShell(string script,CancellationToken ct=default)=>throw new Exception("No PowerShell or installer execution allowed in the cache fixture");
        public Task<string> Dism(params string[] args)=>throw new Exception("No servicing allowed in the cache fixture");
    }
}
