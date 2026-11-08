using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TechLoungeOS;

// Explicitly selected restoration, separate from preserving unrelated source removals.
public static class StoreXbox
{
    public static readonly (string Product, string Identity)[] Products = [
        ("9WZDNCRFJBMP", "Microsoft.WindowsStore"),
        ("9NBLGGH4LS1F", "Microsoft.StorePurchaseApp"),
        ("9MV0B5HZVK9Z", "Microsoft.GamingApp"),
        ("9MWPM2CQNLHN", "Microsoft.GamingServices"),
        ("9WZDNCRD1HKW", "Microsoft.XboxIdentityProvider"),
        ("9NZKPSTSNW4P", "Microsoft.XboxGamingOverlay"),
        ("9MSMLRH6LZF3", "Microsoft.WindowsNotepad"),
        ("9MZ95KL8MR0L", "Microsoft.ScreenSketch")
    ];
    public record Package(string Identity, string Main, string[] Dependencies, Dictionary<string, string> Hashes);

    public static string Identity(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("AppxManifest.xml") ?? zip.GetEntry("AppxMetadata/AppxBundleManifest.xml")
            ?? throw new IOException("Package has no supported Appx manifest.");
        if (entry.Length > 2 << 20) throw new IOException("Package manifest is too large.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 << 20 });
        var xml = XDocument.Load(reader);
        return xml.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name")?.Value
            ?? throw new IOException("Package has no unique identity.");
    }

    public static bool Dependency(string name) => new[] { "Microsoft.VCLibs.", "Microsoft.UI.Xaml.", "Microsoft.NET.Native.", "Microsoft.WindowsAppRuntime.", "Microsoft.Services.Store.Engagement" }
        .Any(p => name.StartsWith(p, StringComparison.Ordinal));

    public static async Task Prepare(ICommandRunner run, string folder, string? supplied, CancellationToken ct, IReadOnlyCollection<string>? present = null, Version? imageVersion = null,
        Func<string,string,string,Version,CancellationToken,Task>? download = null)
    {
        Directory.CreateDirectory(folder);
        var packages = new List<Package>();
        foreach (var (product, identity) in Products)
        {
            if (present?.Contains(identity, StringComparer.Ordinal) == true)
            {
                packages.Add(new(identity, "", [], new()));
                continue;
            }
            var packageFolder = Path.Combine(folder, product);
            Directory.CreateDirectory(packageFolder);
            if (supplied != null) Provisioning.CopyTree(Path.Combine(supplied, product), packageFolder);
            else
            {
                try
                {
                    await run.Run("winget.exe", ["download", "--id", product, "--exact", "--source", "msstore", "--architecture", "x64", "--platform", "Windows.Desktop",
                        "--skip-license", "--authentication-mode", "silent", "--download-directory", packageFolder, "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"], ct);
                }
                catch (IOException)
                {
                    ct.ThrowIfCancellationRequested();
                    packageFolder=Path.Combine(packageFolder,"official");
                    Directory.CreateDirectory(packageFolder);
                    try { await (download ?? StorePackageDownload.Acquire)(product,identity,packageFolder,imageVersion ?? new Version(10,0,22000,0),ct); }
                    catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or JsonException or XmlException or InvalidOperationException)
                    { throw new IOException("Microsoft Store/Xbox package acquisition failed through WinGet and the Microsoft component catalogue. Select a trusted offline package folder or retry when Microsoft downloads are available. No sign-in or licensing bypass was attempted. "+ex.Message,ex); }
                }
            }
            var files = Directory.GetFiles(packageFolder, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".appx" or ".appxbundle" or ".msix" or ".msixbundle").ToArray();
            if (files.Length is < 1 or > 48 || files.Sum(f => new FileInfo(f).Length) > 2L << 30) throw new IOException("Unsupported Store/Xbox package set for " + identity);
            var main = files.Where(f => Identity(f) == identity).ToArray();
            if (main.Length != 1) throw new IOException("Expected one " + identity + " package under " + product);
            var dependencies = files.Except(main).ToArray();
            if (dependencies.Any(f => !Dependency(Identity(f)))) throw new IOException("An unexpected package was supplied as a Store/Xbox dependency.");
            foreach (var file in files)
            {
                Policy.RequireUnlinkedPath(file);
                await run.PowerShell($$"""
                    $signature = Get-AuthenticodeSignature -LiteralPath {{Policy.Ps(file)}}
                    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') { throw 'Store/Xbox package is not validly signed by Microsoft.' }
                    """, ct);
            }
            packages.Add(new(identity, Path.GetRelativePath(folder, main[0]), dependencies.Select(f => Path.GetRelativePath(folder, f)).ToArray(),
                files.ToDictionary(f => Path.GetRelativePath(folder, f), RuntimePack.Hash)));
        }
        File.WriteAllText(Path.Combine(folder, "packages.json"), JsonSerializer.Serialize(packages));
        File.WriteAllText(Path.Combine(folder, "install.ps1"), Script);
    }

    public const string FirstLogin = """
          Invoke-Step 'store-xbox' {
            $payload = Join-Path $env:ProgramData 'TechLoungeOS\MachineSetup\payloads\StoreXbox'
            if (!(Test-Path "$payload\packages-ready")) { throw 'Store/Xbox machine provisioning was skipped or failed. Use Retry setup or review ProgramData\TechLoungeOS\MachineSetup\payloads\StoreXbox\status.txt.' }
            $policy='HKCU:\SOFTWARE\Policies\Microsoft\WindowsStore'
            foreach ($name in @('RemoveWindowsStore','DisableStoreApps')) {
              if ((Get-ItemProperty $policy -ErrorAction SilentlyContinue).PSObject.Properties.Name -contains $name) { Remove-ItemProperty -Path $policy -Name $name -ErrorAction Stop }
            }
            foreach ($name in @('Microsoft.WindowsStore','Microsoft.StorePurchaseApp','Microsoft.GamingApp','Microsoft.GamingServices','Microsoft.XboxIdentityProvider','Microsoft.XboxGamingOverlay','Microsoft.WindowsNotepad','Microsoft.ScreenSketch')) {
              if (!(Get-AppxPackage -Name $name)) { Add-AppxPackage -RegisterByFamilyName -MainPackage ($name + '_8wekyb3d8bbwe') -ErrorAction Stop }
              for($wait=0;$wait -lt 15;$wait++) {
                if(Get-AppxPackage -Name $name | Where-Object Status -eq 'Ok'){break}
                Start-Sleep -Seconds 2
              }
              if (!(Get-AppxPackage -Name $name | Where-Object Status -eq 'Ok')) { throw ('Package registration is missing or unhealthy for ' + $name) }
            }
            foreach($name in @('GamingServices','GamingServicesNet')) {
              for($wait=0;$wait -lt 15;$wait++) {
                $service=Get-Service -Name $name -ErrorAction SilentlyContinue
                if($service){break}
                Start-Sleep -Seconds 2
              }
              if(!$service -or $service.StartType -eq 'Disabled'){throw ('Gaming service is missing or disabled after package registration: '+$name)}
            }
            if (!(Test-Path "$payload\complete")) { throw 'Packages are registered, but machine service setup needs administrator recovery. Review the Store/Xbox status.txt and retry install.ps1 as administrator.' }
          }
        """;

    public const string Deployment = """
          Add-Type -AssemblyName System.Runtime.WindowsRuntime
          $manager=New-Object 'Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime'
          $awaiter=[System.WindowsRuntimeSystemExtensions].GetMethods()|Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 2 -and $_.GetParameters().Count -eq 1}|Select-Object -First 1
          function Wait-StoreDeployment($operation) {
            $task=$awaiter.MakeGenericMethod([Windows.Management.Deployment.DeploymentResult,Windows.Management.Deployment,ContentType=WindowsRuntime],[Windows.Management.Deployment.DeploymentProgress,Windows.Management.Deployment,ContentType=WindowsRuntime]).Invoke($null,@($operation))
            $result=$task.GetAwaiter().GetResult()
            if($result.ErrorText){throw $result.ErrorText}
          }
        """;

    public const string Script = """
        $ErrorActionPreference='Stop'
        $payload=$PSScriptRoot
        if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Store/Xbox provisioning requires administrator permission.' }
        if (Test-Path "$payload\complete") { exit 0 }
        $lock=[IO.File]::Open("$payload\install.lock",'OpenOrCreate','ReadWrite','None')
        $transcribing=$false
        try {
          Start-Transcript -Path "$payload\install.log" -Append
          $transcribing=$true
        """ + "\n" + Deployment + "\n" + """
          # Missing Windows infrastructure requires compatible source media, not registry fabrication.
          foreach ($name in @('AppXSvc','ClipSVC','InstallService','LicenseManager','StateRepository','wuauserv','BITS')) {
            $service=Get-Service -Name $name -ErrorAction Stop
            if ($service.StartType -eq 'Disabled') {
              $start = if ($name -eq 'StateRepository') { 'Automatic' } else { 'Manual' }
              Set-Service -Name $name -StartupType $start
            }
          }
          foreach ($key in @('HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore')) {
            if (Test-Path $key) {
              foreach ($name in @('RemoveWindowsStore','DisableStoreApps')) {
                if ((Get-ItemProperty $key).PSObject.Properties.Name -contains $name) { Remove-ItemProperty -Path $key -Name $name -ErrorAction Stop }
              }
            }
          }
          foreach ($package in (Get-Content "$payload\packages.json" -Raw|ConvertFrom-Json)) {
            foreach ($hash in $package.Hashes.PSObject.Properties) {
              if ((Get-FileHash -LiteralPath (Join-Path $payload $hash.Name) -Algorithm SHA256).Hash -ne $hash.Value) { throw 'Store/Xbox payload hash mismatch.' }
            }
            $existing=@(Get-AppxProvisionedPackage -Online|Where-Object DisplayName -eq $package.Identity)
            if (!$existing.Count) {
              if (!$package.Main) { throw ('A source-provisioned package disappeared during setup and no replacement payload was needed at build time: '+$package.Identity) }
              $dependencies=New-Object 'System.Collections.Generic.List[Uri]'
              foreach($dependency in $package.Dependencies){$dependencies.Add([Uri](Join-Path $payload $dependency))}
              Wait-StoreDeployment ($manager.StagePackageAsync([Uri](Join-Path $payload $package.Main),$dependencies,[Windows.Management.Deployment.DeploymentOptions]::None))
              Wait-StoreDeployment ($manager.ProvisionPackageForAllUsersAsync($package.Identity+'_8wekyb3d8bbwe'))
            }
            if (!(Get-AppxProvisionedPackage -Online|Where-Object DisplayName -eq $package.Identity)) { throw ('Machine provisioning failed: ' + $package.Identity) }
          }
          Set-Content "$payload\packages-ready" 'Packages provisioned; user registration may now proceed.'
          foreach ($name in @('XblAuthManager','XblGameSave','XboxNetApiSvc','GamingServices','GamingServicesNet')) {
            # Gaming Services are package-owned and can materialise only after
            # the first user's registration. FirstLogin verifies them separately.
            $service=Get-Service -Name $name -ErrorAction SilentlyContinue
            if(!$service -and $name -in @('GamingServices','GamingServicesNet')){Write-Output ('Deferred package-owned service verification until user registration: '+$name);continue}
            if(!$service){throw ('Required Xbox infrastructure service is missing: '+$name)}
            if ($service.StartType -eq 'Disabled') {
              $start=if ($name -in @('GamingServices','GamingServicesNet')) { 'Automatic' } else { 'Manual' }
              Set-Service -Name $name -StartupType $start
            }
          }
          Set-Content "$payload\status.txt" 'Store and Xbox packages provisioned for new users. Existing services enabled. Package-owned Gaming Services and user registration are verified at first login.'
          Set-Content "$payload\complete" 'Machine provisioning completed'
        } catch { Set-Content "$payload\status.txt" $_.Exception.GetBaseException().Message; throw }
        finally { if ($transcribing) { Stop-Transcript }; $lock.Dispose() }
        """;
}
