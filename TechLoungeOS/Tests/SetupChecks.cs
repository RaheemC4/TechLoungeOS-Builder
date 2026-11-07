using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TechLoungeOS;

static class SetupChecks
{
    public static async Task Run(string root, BuildOptions options, Action<bool, string> check)
    {
        var defaults = new BuildOptions("", 1, "", "", "", [], [], false, false, null);
        check(defaults.IncludeRuntimePack && defaults.IncludeStoreXbox && !defaults.IncludeNet8, "runtimes and machine Store/Xbox are default-on with .NET 8 optional");
        check(RuntimePack.Packages(false).Count() == 14 && RuntimePack.Packages(false).Distinct().Count() == 14, "runtime set contains both architectures for six VC families and .NET 10");
        check(!Policy.ValidApps([RecommendedApps.Discord, "discord.discord"]) && Policy.ValidApps([RecommendedApps.Discord, "Valve.Steam"]), "enhanced and plain Discord cannot both enter the build");
        check(RuntimePack.DownloadVersion("Microsoft .NET_10.0.13_Machine_X64_burn.exe") == "10.0.13", "actual WinGet filename format yields a concrete runtime version");
        try { RuntimePack.DownloadVersion("unknown.exe"); throw new Exception("Expected format rejection"); } catch (IOException) { check(true, "unrecognised download format stops preparation"); }

        var fixture = Path.Combine(root, "setup O'Brien 测试"); Directory.CreateDirectory(fixture);
        var prepared = Path.Combine(fixture, "prepared"); Directory.CreateDirectory(prepared);
        File.WriteAllText(Path.Combine(prepared, "install.ps1"), "# fixture; never installed");
        var mount = Path.Combine(fixture, "image");
        var hook = Path.Combine(mount, @"Windows\Setup\Scripts\SetupComplete.cmd"); Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        var original = Encoding.ASCII.GetBytes("@echo off\r\ncall \"%~dp0Vendor.cmd\"\r\nexit /b 0\r\n"); File.WriteAllBytes(hook, original);
        RuntimePack.Stage(mount, prepared);
        check(File.ReadAllBytes(hook).AsSpan().EndsWith(original) && File.ReadAllBytes(Path.Combine(mount, RuntimePack.RelativeFolder, "source-SetupComplete.cmd")).SequenceEqual(original), "setup hook retains original batch bytes at original path and keeps a backup");
        var hookAfterRuntime = File.ReadAllBytes(hook);
        RuntimePack.AddSetupCommand(mount, "TechLoungeOS-StoreXbox", prepared);
        check(File.ReadAllBytes(hook).AsSpan().EndsWith(hookAfterRuntime), "Store/Xbox setup chains the runtime hook and inherited vendor commands");
        var encodedMount = Path.Combine(fixture, "encoded"); var encodedHook = Path.Combine(encodedMount, @"Windows\Setup\Scripts\SetupComplete.cmd"); Directory.CreateDirectory(Path.GetDirectoryName(encodedHook)!);
        var encoded = Encoding.Unicode.GetBytes("echo source"); File.WriteAllBytes(encodedHook, encoded);
        try { RuntimePack.Stage(encodedMount, prepared); throw new Exception("Expected encoding rejection"); }
        catch (IOException) { check(File.ReadAllBytes(encodedHook).SequenceEqual(encoded), "unsupported inherited batch encoding is rejected without rewriting it"); }

        var run = new ProcessRunner(_ => { });
        var signatures = await run.PowerShell("(Get-Command Get-AuthenticodeSignature).Module.Path");
        check(signatures.Contains(@"WindowsPowerShell\v1.0\Modules\Microsoft.PowerShell.Security", StringComparison.OrdinalIgnoreCase), "Windows PowerShell loads its own signature module even when launched from PowerShell 7");
        foreach (var (name, script) in new[] { ("runtimes", RuntimePack.Script), ("store", StoreXbox.Script), ("discord", Provisioning.Script([RecommendedApps.Discord], false, false)), ("direct", Provisioning.Script(["TechLounge.HatchZip", "TechLounge.Shiftd"], false, false)), ("combined", Provisioning.Script([], false, false, runtimePack:true, storeXbox:true)) })
        {
            var path = Path.Combine(fixture, name + ".ps1"); File.WriteAllText(path, script);
            var parsed = await run.PowerShell("$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseFile(" + Policy.Ps(path) + ",[ref]$tokens,[ref]$errors); if($errors.Count){throw ($errors|Out-String)}; 'parsed'");
            check(parsed.Contains("parsed"), name + " generated script parses in Windows PowerShell 5.1");
        }

        // Substitute only the elevation predicate and installer process at this isolated fixture.
        // No installer, service change, DISM operation or real app is executed.
        var payload = Path.Combine(fixture, "runtime-execution"); Directory.CreateDirectory(payload);
        var items = new[] {
            new RuntimeInstaller("Microsoft.VCRedist.2008.x86", "x86", "vc2008-x86.exe", "9.0.30729.6161", "", RuntimePack.Arguments("Microsoft.VCRedist.2008.x86")),
            new RuntimeInstaller("Microsoft.VCRedist.2008.x64", "x64", "vc2008-x64.exe", "9.0.30729.6161", "", RuntimePack.Arguments("Microsoft.VCRedist.2008.x64")),
            new RuntimeInstaller("Microsoft.VCRedist.2015+.x64", "x64", "newer.exe", "14.1.0", "", "/quiet /norestart"),
            new RuntimeInstaller("Microsoft.VCRedist.2013.x86", "x86", "first.exe", "12.0.1", "", "/quiet /norestart"),
            new RuntimeInstaller("Microsoft.DotNet.DesktopRuntime.10", "x64", "retry.exe", "10.0.13", "", "/quiet /norestart") };
        foreach (var item in items) File.WriteAllText(Path.Combine(payload, item.File), "fixture; never executed");
        items = items.Select(i => i with { Sha256 = RuntimePack.Hash(Path.Combine(payload, i.File)) }).ToArray();
        File.WriteAllText(Path.Combine(payload, "installers.json"), JsonSerializer.Serialize(items));
        var runtimeScript = RuntimePack.Script.Replace("if (!( [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Runtime installation needs administrator permission.' }", "# fixture-only elevation substitution");
        var pathScript = Path.Combine(payload, "install.ps1"); File.WriteAllText(pathScript, runtimeScript);
        var header = $$"""
            function Get-ItemProperty { [pscustomobject]@{DisplayName='Microsoft Visual C++ 2022 X64 Additional Runtime - 14.2.0';Publisher='Microsoft Corporation';DisplayVersion='14.2.0'} }
            function Start-Process { param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru)
              if ($FilePath -like '*vc2008-*' -and $ArgumentList -ne '/qn /norestart') { throw 'VC 2008 requires its documented /qn switch; /quiet fails with 4096.' }
              Add-Content {{Policy.Ps(Path.Combine(payload, "calls.txt"))}} (Split-Path $FilePath -Leaf)
              $code=if($FilePath -like '*retry.exe' -and !(Test-Path {{Policy.Ps(Path.Combine(payload,"retry-ok"))}})){1603}else{3010}
              [pscustomobject]@{ExitCode=$code}
            }
            & {{Policy.Ps(pathScript)}}
            """;
        try { await run.PowerShell(header); throw new Exception("Expected synthetic installer failure"); } catch (IOException) { }
        check(File.Exists(Path.Combine(payload, "newer.exe.done")) && !File.ReadAllLines(Path.Combine(payload, "calls.txt")).Contains("newer.exe"), "existing newer VC runtime is retained without running its bundled installer");
        check(File.Exists(Path.Combine(payload, "first.exe.done")) && File.Exists(Path.Combine(payload, "restart-required")) && !File.Exists(Path.Combine(payload,"complete")), "runtime partial failure retains success and records deferred restart");
        File.WriteAllText(Path.Combine(payload, "retry-ok"), "fixture"); await run.PowerShell(header);
        var calls = File.ReadAllLines(Path.Combine(payload,"calls.txt"));
        check(calls.Count(c => c == "vc2008-x86.exe") == 1 && calls.Count(c => c == "vc2008-x64.exe") == 1 && File.Exists(Path.Combine(payload,"vc2008-x86.exe.done")) && File.Exists(Path.Combine(payload,"vc2008-x64.exe.done")), "generated runtime installer uses the documented VC 2008 switches for both architectures and retains successes on retry");
        check(File.Exists(Path.Combine(payload,"complete")) && calls.Count(c => c == "first.exe") == 1 && calls.Count(c => c == "retry.exe") == 2, "runtime recovery retries only its failed installer and completes");
        await run.PowerShell(header);
        check(File.ReadAllLines(Path.Combine(payload,"calls.txt")).Length == calls.Length, "completed runtime recovery does not reinstall anything");

        foreach (var (runtimeVersion, shouldInstall) in new[] { ("10.0.12", true), ("10.0.14", false) })
        {
            var netPayload = Path.Combine(fixture, "net-" + runtimeVersion); Directory.CreateDirectory(netPayload);
            File.WriteAllText(Path.Combine(netPayload, "desktop.exe"), "fixture; never executed");
            var netItem = new RuntimeInstaller("Microsoft.DotNet.DesktopRuntime.10", "x64", "desktop.exe", "10.0.13", RuntimePack.Hash(Path.Combine(netPayload, "desktop.exe")), "/quiet /norestart");
            File.WriteAllText(Path.Combine(netPayload, "installers.json"), JsonSerializer.Serialize(new[] { netItem }));
            var netScript = Path.Combine(netPayload, "install.ps1"); File.WriteAllText(netScript, runtimeScript);
            var netLog = Path.Combine(netPayload, "calls.txt");
            await run.PowerShell($$"""
                function Get-ItemProperty { [pscustomobject]@{DisplayName='Microsoft Windows Desktop Runtime - {{runtimeVersion}} (x64)';Publisher='Microsoft Corporation';DisplayVersion='80.52.61384'} }
                function Start-Process { param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru)
                  Add-Content {{Policy.Ps(netLog)}} (Split-Path $FilePath -Leaf)
                  [pscustomobject]@{ExitCode=0}
                }
                & {{Policy.Ps(netScript)}}
                """);
            check(File.Exists(Path.Combine(netPayload, "complete")) && File.Exists(netLog) == shouldInstall,
                "actual .NET " + runtimeVersion + " determines retention instead of internal ARP version 80.x");
        }

        var appx = Path.Combine(fixture, "package.msix");
        using (var zip = ZipFile.Open(appx, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open()); writer.Write("<Package><Identity Name='Microsoft.WindowsStore'/></Package>"); }
        check(StoreXbox.Identity(appx) == "Microsoft.WindowsStore" && StoreXbox.Dependency("Microsoft.VCLibs.140.00") && !StoreXbox.Dependency("Other.Package"), "offline package identities are read from manifests and dependencies are restricted");
        var bad = Path.Combine(fixture, "bad.msix");
        using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open()); writer.Write("<!DOCTYPE Package [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><Package><Identity Name='&e;'/></Package>"); }
        try { StoreXbox.Identity(bad); throw new Exception("Expected DTD rejection"); } catch (System.Xml.XmlException) { check(true, "offline package manifest cannot resolve external entities"); }
        await DiscordFixture(fixture, run, check);
        var receipt = Path.Combine(fixture, "native-discord-checks.txt");
        await run.Run(Path.Combine(AppContext.BaseDirectory, "DiscordLauncherChecks.exe"), [receipt]);
        check(File.ReadAllText(receipt).StartsWith("PASS: ", StringComparison.Ordinal), "unchanged native Discord helper passes repair/update/cache/shortcut fixture assertions");
        Console.WriteLine(File.ReadAllText(receipt).Trim());
    }

    private static async Task DiscordFixture(string root, ProcessRunner run, Action<bool, string> check)
    {
        var local = Path.Combine(root, "discord-user"); var roaming = Path.Combine(root, "roaming"); var data = Path.Combine(root, "data");
        var source = Path.Combine(data, @"TechLoungeOS\Recommended"); Directory.CreateDirectory(source);
        var resources = Path.Combine(local, @"Discord\app-1.0.1\resources"); Directory.CreateDirectory(resources);
        File.WriteAllText(Path.Combine(resources, "app.asar"), "stock fixture"); File.WriteAllText(Path.Combine(resources, "..", "Discord.exe"), "never executed");
        File.WriteAllText(Path.Combine(local,@"Discord\installer.db"),"existing profile database; preserve unchanged");
        foreach (var name in new[] { "openasar.asar", "openasar.sha256", "DiscordLauncher.exe" }) File.WriteAllText(Path.Combine(source, name), "fixture; never executed");
        File.WriteAllText(Path.Combine(source, "sha256.json"), JsonSerializer.Serialize(Directory.GetFiles(source).ToDictionary(p => Path.GetFileName(p), RuntimePack.Hash)));
        var settings = Path.Combine(roaming, @"discord\settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, """{"otherSetting":42,"otherText":"café 中文","openasar":{"customSetting":"keep"}}""");
        var fake = Path.Combine(root, "discord-winget.cmd"); File.WriteAllText(fake,"@echo off\r\nexit /b 0\r\n");
        var log = Path.Combine(local, @"TechLoungeTweaks\Discord\launcher.log");
        var header = $$"""
            $env:LOCALAPPDATA={{Policy.Ps(local)}}; $env:APPDATA={{Policy.Ps(roaming)}}; $env:ProgramData={{Policy.Ps(data)}}
            function Get-Command { [pscustomobject]@{Source={{Policy.Ps(fake)}}} }
            function Get-Process { }
            function Start-Process { param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru)
              if ($FilePath -notlike '*DiscordLauncher.exe' -or $ArgumentList -notin @('--repair-only','--integrate')) { throw 'Unexpected fixture process' }
              Add-Content {{Policy.Ps(log)}} 'openasar-restored: app-1.0.1'
              [pscustomobject]@{ExitCode=0}
            }
            """;
        await run.PowerShell(header + Provisioning.Script([RecommendedApps.Discord], false, false));
        using var result = JsonDocument.Parse(File.ReadAllText(settings));
        check(!File.ReadAllBytes(settings).Take(3).SequenceEqual(new byte[]{0xef,0xbb,0xbf}),"Discord settings use UTF-8 without a BOM so OpenAsar's JavaScript JSON reader can load them");
        check(result.RootElement.GetProperty("otherText").GetString()=="café 中文","Discord provisioning preserves non-ASCII existing settings when reading UTF-8 JSON in Windows PowerShell 5.1");
        check(result.RootElement.GetProperty("otherSetting").GetInt32() == 42 && result.RootElement.GetProperty("openasar").GetProperty("customSetting").GetString() == "keep" && result.RootElement.GetProperty("openasar").GetProperty("cmdPreset").GetString() == "perf", "Discord provisioning preserves existing settings and merges TechLounge OpenAsar defaults");
        check(File.Exists(settings + ".tlos-backup") && File.Exists(Path.Combine(local, @"TechLoungeOS\TechLounge.DiscordOpenAsar.done")), "Discord recipe backs up settings and completes only after repair and shortcut integration");
        var fresh = Path.Combine(root,"discord-fresh"); var freshLog=Path.Combine(fresh,@"TechLoungeTweaks\Discord\launcher.log");
        var delayed = $$"""
            $env:LOCALAPPDATA={{Policy.Ps(fresh)}}; $env:APPDATA={{Policy.Ps(Path.Combine(root,"fresh-roaming"))}}
            $script:stopped=$false; $script:afterStop=0
            function Get-Command { [pscustomobject]@{Source='Invoke-FixtureWinget'} }
            function Invoke-FixtureWinget {
              $dir=Join-Path $env:LOCALAPPDATA 'Discord\app-1.0.1\resources'
              New-Item $dir -ItemType Directory -Force|Out-Null
              Set-Content (Join-Path $dir 'app.asar') 'stock fixture'
              Set-Content (Join-Path $dir '..\Discord.exe') 'never executed'
              [IO.File]::WriteAllBytes((Join-Path $dir '..\installer.db'),[Text.Encoding]::ASCII.GetBytes('SQLite format 3'+[char]0))
              $global:LASTEXITCODE=0
            }
            function Get-Process {
              if($script:stopped){$script:afterStop++}
              if(!$script:stopped -or $script:afterStop -lt 3){[pscustomobject]@{Id=123;Path=(Join-Path $env:LOCALAPPDATA 'Discord\app-1.0.1\Discord.exe')} }
            }
            function Stop-Process { param($Id,$ErrorAction) if($Id -ne 123){throw 'Unexpected process'}; $script:stopped=$true }
            function Start-Sleep { }
            function Start-Process { param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru)
              Add-Content {{Policy.Ps(freshLog)}} 'openasar-restored: app-1.0.1'; [pscustomobject]@{ExitCode=0}
            }
            """;
        await run.PowerShell(header + delayed + Provisioning.Script([RecommendedApps.Discord],false,false));
        var freshState=Path.Combine(fresh,"TechLoungeOS");
        check(File.Exists(Path.Combine(freshState,"TechLounge.DiscordOpenAsar.done")),"fresh Discord provisioning waits for delayed process exit before applying OpenAsar");
        File.Delete(Path.Combine(freshState,"complete")); File.Delete(Path.Combine(freshState,"TechLounge.DiscordOpenAsar.done"));
        var existingSession="""
            function Get-Process { [pscustomobject]@{Id=123;Path=(Join-Path $env:LOCALAPPDATA 'Discord\app-1.0.1\Discord.exe')} }
            function Stop-Process { throw 'Existing session must never be closed' }
            """;
        try { await run.PowerShell(header + delayed + existingSession + Provisioning.Script([RecommendedApps.Discord],false,false)); throw new Exception("Expected existing-session retry"); }
        catch(IOException ex) { check(!File.Exists(Path.Combine(freshState,"TechLounge.DiscordOpenAsar.done")) && File.ReadAllText(Path.Combine(freshState,"status.txt")).Contains("Discord is running."),"existing Discord session remains open and provisioning reports a retry instead of terminating it: "+ex.Message+" / "+File.ReadAllText(Path.Combine(freshState,"status.txt"))); }
    }
}
