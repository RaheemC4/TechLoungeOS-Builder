using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Net.Http;

namespace TechLoungeOS;

public static class OfficePack
{
    public const string Product = "O365ProPlusRetail";
    public static XDocument Configuration(string? source = null, string? version = null)
    {
        var add = new XElement("Add", new XAttribute("OfficeClientEdition", "64"), new XAttribute("Channel", "Current"), new XAttribute("AllowCdnFallback", "FALSE"),
            new XElement("Product", new XAttribute("ID", Product), new XElement("Language", new XAttribute("ID", "en-us")),
                new[] { "OneDrive", "Teams", "OutlookForWindows", "Publisher" }.Select(id => new XElement("ExcludeApp", new XAttribute("ID", id)))));
        if (source != null) add.Add(new XAttribute("SourcePath", source));
        if (version != null) add.Add(new XAttribute("Version", version));
        return new XDocument(new XElement("Configuration", add,
            new XElement("Display", new XAttribute("Level", "None"), new XAttribute("AcceptEULA", "TRUE")),
            new XElement("Property", new XAttribute("Name", "FORCEAPPSHUTDOWN"), new XAttribute("Value", "FALSE")),
            new XElement("Property", new XAttribute("Name", "SETUP_REBOOT"), new XAttribute("Value", "Never")),
            new XElement("Updates", new XAttribute("Enabled", "TRUE"))));
    }

    public static async Task Prepare(ICommandRunner run, string folder, string? supplied, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var setup = Path.Combine(folder, "setup.exe");
        if (supplied != null)
        {
            Policy.RequireUnlinkedPath(supplied);
            Provisioning.CopyTree(Path.Combine(supplied, "Office"), Path.Combine(folder, "Office"));
        }
        // Refresh the signed tool even when reusing cached Office data.
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
            var page = await Download(client, new("https://www.microsoft.com/en-us/download/details.aspx?id=49117"), 4 << 20, ct);
            var url = ToolUrl(System.Text.Encoding.UTF8.GetString(page));
            var archive = Path.Combine(folder, "officedeploymenttool.exe");
            await File.WriteAllBytesAsync(archive, await Download(client, new(url), 16 << 20, ct), ct);
            await RuntimePack.VerifyMicrosoft(run, archive);
            var extracted = Path.Combine(folder, "tool-extract"); Directory.CreateDirectory(extracted);
            await run.Run(archive, ["/quiet", "/extract:" + extracted], ct);
            File.Copy(Path.Combine(extracted, "setup.exe"), setup);
            File.WriteAllText(Path.Combine(folder, "tool-source.json"), JsonSerializer.Serialize(new { url, sha256 = RuntimePack.Hash(archive) }));
        }
        await RuntimePack.VerifyMicrosoft(run, setup);
        var version = await RefreshCurrent(run,setup,folder,ct);
        Configuration(version: version).Save(Path.Combine(folder, "Configuration.xml"));
        Configuration(version: version).Save(Path.Combine(folder, "TechLoungeOS-Office.template.xml"));
        File.WriteAllText(Path.Combine(folder, "TechLoungeOS-Office.ps1"), Script);
        var filesToHash = Directory.EnumerateFiles(Path.Combine(folder, "Office"), "*", SearchOption.AllDirectories)
            .Concat(new[] { setup, Path.Combine(folder, "TechLoungeOS-Office.template.xml") });
        File.WriteAllText(Path.Combine(folder, "TechLoungeOS-Office.sha256.json"), JsonSerializer.Serialize(filesToHash.ToDictionary(f => Path.GetRelativePath(folder, f), RuntimePack.Hash)));
    }

    public static string ToolUrl(string html)
    {
        var urls = Regex.Matches(html, @"https://download\.microsoft\.com/download/[A-Za-z0-9/-]+/officedeploymenttool_[0-9-]+\.exe", RegexOptions.IgnoreCase)
            .Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (urls.Length != 1) throw new IOException("Microsoft's ODT page did not identify one expected tool download. No installer was run.");
        return urls[0];
    }

    public static async Task<string> RefreshCurrent(ICommandRunner run,string setup,string folder,CancellationToken ct)
    {
        var before=Versions(folder);
        if(before.Length>0)ValidateFiles(folder);
        var config=Path.Combine(folder,"Download.xml");Configuration(folder).Save(config);
        // An unpinned download lets the signed ODT resolve Current on every build,
        // reusing matching cached data without installing anything on the builder.
        await run.Run(setup,["/download",config],ct);
        var added=Versions(folder).Except(before,StringComparer.OrdinalIgnoreCase).ToArray();
        if(added.Length>1)throw new IOException("Office download produced more than one new version.");
        var version=ValidateFiles(folder,added.SingleOrDefault());
        foreach(var obsolete in before.Where(v=>!v.Equals(version,StringComparison.OrdinalIgnoreCase)))
        {
            var data=Path.GetFullPath(Path.Combine(folder,"Office","Data"));
            var target=Path.GetFullPath(Path.Combine(data,obsolete));
            if(!string.Equals(Path.GetDirectoryName(target),data,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected Office cache retirement path.");
            Policy.RequireUnlinkedPath(target);
            foreach(var item in Directory.EnumerateFileSystemEntries(target,"*",SearchOption.AllDirectories))Policy.RequireUnlinkedPath(item);
            // Only the obsolete private workspace copy is removed, after validating
            // its replacement. The supplied source cache is never changed.
            Directory.Delete(target,true);
        }
        ValidateFiles(folder);
        File.WriteAllText(Path.Combine(folder,"office-source.json"),JsonSerializer.Serialize(new{product=Product,channel="Current",version,resolvedUtc=DateTimeOffset.UtcNow,refresh="Microsoft ODT /download"}));
        return version;
    }

    private static string[] Versions(string folder)
    {
        var data=Path.Combine(folder,"Office","Data");
        return Directory.Exists(data)?Directory.GetDirectories(data).Select(Path.GetFileName).OfType<string>().Where(v=>Regex.IsMatch(v,@"\A16\.0\.\d+\.\d+\z")).ToArray():[];
    }

    private static async Task<byte[]> Download(HttpClient client, Uri uri, int maximum, CancellationToken ct)
    {
        if (uri.Scheme != "https" || uri.UserInfo != "" || uri.Port != 443 || uri.Host is not ("www.microsoft.com" or "download.microsoft.com")) throw new IOException("Unexpected Office download host.");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("Office tool download exceeds its size limit.");
        using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        { if (output.Length + count > maximum) throw new IOException("Office tool download exceeds its size limit."); await output.WriteAsync(buffer.AsMemory(0, count), ct); }
        return output.ToArray();
    }

    public static string ValidateFiles(string folder,string? expected=null)
    {
        var data = Path.Combine(folder, "Office", "Data");
        if (!Directory.Exists(data) || !File.Exists(Path.Combine(data, "v64.cab"))) throw new IOException("Office download has no complete x64 metadata.");
        var versions = Versions(folder).Where(v=>expected==null||v.Equals(expected,StringComparison.OrdinalIgnoreCase)).Select(v=>Path.Combine(data,v)).ToArray();
        if (versions.Length != 1) throw new IOException("Office payload must contain one pinned version.");
        foreach (var name in new[] { "stream.x64.x-none.dat", "stream.x64.en-us.dat", "i640.cab", "s640.cab" })
            if (!File.Exists(Path.Combine(versions[0], name)) || new FileInfo(Path.Combine(versions[0], name)).Length == 0) throw new IOException("Office payload is incomplete: " + name);
        return Path.GetFileName(versions[0]);
    }

    public static DesktopSetup.Step Stage(string media, string mount, string prepared)
    {
        var target = SetupHooks.OemScripts(media);
        foreach (var baseFolder in new[] { target, Path.Combine(mount, @"Windows\Setup\Scripts") })
        {
            Policy.RequireUnlinkedPath(baseFolder);
            foreach (var name in new[] { "Office", "setup.exe", "Configuration.xml", "TechLoungeOS-Office.ps1", "TechLoungeOS-Office.template.xml", "TechLoungeOS-Office.sha256.json", "TechLoungeOS-Office-State" })
                if (File.Exists(Path.Combine(baseFolder, name)) || Directory.Exists(Path.Combine(baseFolder, name))) throw new IOException("Source already contains an Office setup payload which would be overwritten: " + name);
        }
        var body = SetupHooks.Read(SetupHooks.ImageHook(mount));
        if (System.Text.Encoding.ASCII.GetString(body).Contains("TechLoungeOS_Office_End", StringComparison.OrdinalIgnoreCase)) throw new IOException("Source has a conflicting Office setup label.");
        Directory.CreateDirectory(target);
        Provisioning.CopyTree(Path.Combine(prepared, "Office"), Path.Combine(target, "Office"));
        foreach (var name in new[] { "setup.exe", "Configuration.xml", "TechLoungeOS-Office.ps1", "TechLoungeOS-Office.template.xml", "TechLoungeOS-Office.sha256.json" })
        {
            var destination=Path.Combine(target,name);
            File.Copy(Path.Combine(prepared,name),destination);
            File.SetAttributes(destination,File.GetAttributes(destination)&~FileAttributes.ReadOnly);
        }
        var batch = Path.Combine(target, "TechLoungeOS-Office.cmd");
        if (File.Exists(batch)) throw new IOException("Source already contains an Office setup command.");
        File.WriteAllText(batch, Batch, System.Text.Encoding.ASCII);
        // Snapshot only our payload, never the inherited SetupComplete body.
        var names = Directory.GetFiles(Path.Combine(target, "Office"), "*", SearchOption.AllDirectories)
            .Concat(new[] { "setup.exe", "Configuration.xml", "TechLoungeOS-Office.ps1", "TechLoungeOS-Office.template.xml", "TechLoungeOS-Office.sha256.json", "TechLoungeOS-Office.cmd" }.Select(n => Path.Combine(target, n)));
        var step = new DesktopSetup.Step("Office", names.Select(file => new DesktopSetup.Input("Windows",
            @"Setup\Scripts\" + Path.GetRelativePath(target, file), Path.GetRelativePath(target, file), new FileInfo(file).Length, RuntimePack.Hash(file))).ToArray());
        DesktopSetup.Validate(step);
        return step;
    }

    // Keep the configure command beside its offline payload, but run it after the desktop.
    public const string Batch = "@echo off\r\nfltmc >nul || exit /b\r\nsetlocal\r\ncd /d \"%~dp0\"\r\n\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%~dp0TechLoungeOS-Office.ps1\" -Phase Before\r\nif errorlevel 2 exit /b 0\r\nif errorlevel 1 exit /b 1\r\nsetup.exe /configure Configuration.xml\r\n\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%~dp0TechLoungeOS-Office.ps1\" -Phase After -InstallerExit %errorlevel%\r\nexit /b %errorlevel%\r\n";
    public const string FirstLogin = """
          Invoke-Step 'office' {
            $status = Join-Path $env:ProgramData 'TechLoungeOS\MachineSetup\payloads\Office\TechLoungeOS-Office-State'
            if (!(Test-Path "$status\complete")) { throw 'Office setup was skipped or failed. Use Retry setup or review ProgramData\TechLoungeOS\MachineSetup\payloads\Office\TechLoungeOS-Office-State\status.txt.' }
          }
        """;

    public const string Script = """
        param([ValidateSet('Before','After','Recover')][string]$Phase='Recover',[int]$InstallerExit=0)
        $ErrorActionPreference='Stop'
        $payload=$PSScriptRoot
        $state=Join-Path $payload 'TechLoungeOS-Office-State'
        if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Office setup requires administrator permission.' }
        New-Item $state -ItemType Directory -Force|Out-Null
        $beforePhase=$Phase -eq 'Before'
        $transcribing=$false
        try {
          Start-Transcript -Path "$state\install.log" -Append
          $transcribing=$true
          if ($Phase -in @('Before','Recover')) {
            if (Test-Path "$state\complete") { if ($beforePhase) { exit 2 }; exit 0 }
            $existing=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction SilentlyContinue
            if ($existing.ProductReleaseIds) {
              if (($existing.ProductReleaseIds -split ',') -notcontains 'O365ProPlusRetail' -or $existing.Platform -ne 'x64') { throw 'An existing Office edition/architecture needs review. It was not removed or replaced.' }
              $Phase='After'; $InstallerExit=0
            } else {
              if (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object DisplayName -Match '^Microsoft (Office|365)') { throw 'Existing Office installation requires review; automatic removal is not allowed.' }
              $hashes=Get-Content "$payload\TechLoungeOS-Office.sha256.json" -Raw|ConvertFrom-Json
              foreach ($entry in $hashes.PSObject.Properties) {
                if ((Get-FileHash -LiteralPath (Join-Path $payload $entry.Name) -Algorithm SHA256).Hash -ne $entry.Value) { throw 'Offline Office payload failed SHA-256 verification.' }
              }
              [xml]$config=Get-Content "$payload\TechLoungeOS-Office.template.xml" -Raw
              $config.Configuration.Add.SetAttribute('SourcePath',$payload)
              # ISO/OEM copying can retain the media's read-only bit. Only our
              # generated configuration is writable; payload hashes were checked.
              $configPath=Join-Path $payload Configuration.xml
              if(Test-Path -LiteralPath $configPath){[IO.File]::SetAttributes($configPath,([IO.File]::GetAttributes($configPath) -band (-bnot [IO.FileAttributes]::ReadOnly)))}
              $config.Save("$payload\Configuration.xml")
              Set-Content "$state\status.txt" 'Offline Office payload verified. Waiting for setup.exe /configure Configuration.xml.'
              if ($Phase -eq 'Before') { exit 0 }
              $process=Start-Process -FilePath "$payload\setup.exe" -ArgumentList '/configure Configuration.xml' -WorkingDirectory $payload -Wait -PassThru
              $InstallerExit=$process.ExitCode; $Phase='After'
            }
          }
          if ($InstallerExit -notin @(0,3010)) { throw "Office configure failed: $InstallerExit" }
          $installed=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction Stop
          if (($installed.ProductReleaseIds -split ',') -notcontains 'O365ProPlusRetail' -or $installed.Platform -ne 'x64') { throw 'Expected x64 Microsoft 365 product was not registered.' }
          foreach ($name in @('WINWORD.EXE','EXCEL.EXE','POWERPNT.EXE')) {
            if (!(Test-Path (Join-Path $installed.InstallationPath ('root\Office16\'+$name)))) { throw ('Installed Office file missing: '+$name) }
          }
          if ($InstallerExit -eq 3010) { Set-Content "$state\restart-required" 'Normal Windows restart required; none was forced.' }
          Set-Content "$state\status.txt" 'Microsoft 365 apps installed or an existing matching installation retained. Sign in with your licensed account to activate.'
          Set-Content "$state\complete" 'Office installation verified; activation not performed.'
          if ($beforePhase) { exit 2 }
        } catch { Set-Content "$state\status.txt" $_.Exception.Message; Write-Error $_ -ErrorAction Continue; exit 1 }
        finally { if ($transcribing) { Stop-Transcript } }
        """;
}
