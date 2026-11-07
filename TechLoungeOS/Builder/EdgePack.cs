using System.Net.Http;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TechLoungeOS;

// Offline enterprise MSI; default associations apply at a new user's first sign-in.
public static class EdgePack
{
    public record Installer(string Version, string Sha256);
    public static async Task Prepare(ICommandRunner run, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
        using var metadata = JsonDocument.Parse(await client.GetStringAsync("https://edgeupdates.microsoft.com/api/products?view=enterprise", ct));
        var releases = metadata.RootElement.EnumerateArray().Single(p => p.GetProperty("Product").GetString() == "Stable").GetProperty("Releases").EnumerateArray()
            .Where(r => r.GetProperty("Platform").GetString() == "Windows" && r.GetProperty("Architecture").GetString() == "x64")
            .OrderByDescending(r => Version.Parse(r.GetProperty("ProductVersion").GetString()!)).ToArray();
        var release = releases.First();
        var artifact = release.GetProperty("Artifacts").EnumerateArray().Single(a => a.GetProperty("ArtifactName").GetString() == "msi");
        var uri = new Uri(artifact.GetProperty("Location").GetString()!);
        var hash = artifact.GetProperty("Hash").GetString()!;
        if (uri.Scheme != "https" || uri.Host != "msedge.sf.dl.delivery.mp.microsoft.com" || !uri.AbsolutePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            || artifact.GetProperty("HashAlgorithm").GetString() != "SHA256" || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new IOException("Edge metadata does not describe the expected signed Microsoft enterprise MSI.");
        var size = artifact.GetProperty("SizeInBytes").GetInt64();
        if (size is < 1 or > 400L * 1024 * 1024) throw new IOException("Edge installer exceeds its size budget.");
        var path = Path.Combine(folder, "MicrosoftEdge.msi");
        using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long written = 0; int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                written += count;
                if (written > size) throw new IOException("Edge download exceeds its declared size.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (written != size) throw new IOException("Incomplete Edge download.");
        }
        if (!RuntimePack.Hash(path).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Edge MSI SHA-256 mismatch.");
        await RuntimePack.VerifyMicrosoft(run, path);
        File.WriteAllText(Path.Combine(folder, "installer.json"), JsonSerializer.Serialize(new Installer(release.GetProperty("ProductVersion").GetString()!, hash)));
        File.WriteAllText(Path.Combine(folder, "install.ps1"), Script);
    }

    public static XDocument Associations(string? existing)
    {
        var document = new XDocument(new XElement("DefaultAssociations"));
        if (existing != null)
        {
            if (existing.Length > 2 << 20) throw new IOException("Source default associations are too large.");
            using var reader = XmlReader.Create(new StringReader(existing), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
            if (document.Root?.Name != "DefaultAssociations") throw new IOException("Unsupported source default associations.");
        }
        foreach (var identifier in new[] { "http", "https", ".htm", ".html" })
        {
            document.Root!.Elements("Association").Where(a => string.Equals((string?)a.Attribute("Identifier"), identifier, StringComparison.OrdinalIgnoreCase)).Remove();
            document.Root.Add(new XElement("Association", new XAttribute("Identifier", identifier), new XAttribute("ProgId", "MSEdgeHTM"), new XAttribute("ApplicationName", "Microsoft Edge")));
        }
        return document;
    }

    public static async Task<DesktopSetup.Step> Stage(ICommandRunner run, string mount, string prepared, CancellationToken ct)
    {
        var original = Path.Combine(mount, @"Windows\System32\OEMDefaultAssociations.xml");
        Policy.RequireUnlinkedPath(original);
        if (File.Exists(original)) SourcePreservation.RequireImageFile(original, 0, 2 << 20);
        var existing = File.Exists(original) ? File.ReadAllText(original) : null;
        var backup = Path.Combine(mount, @"ProgramData\TechLoungeOS\source-hooks");
        Directory.CreateDirectory(backup);
        if (existing != null) File.Copy(original, Path.Combine(backup, "Edge-DefaultAssociations.xml"), false);
        var merged = Path.Combine(prepared, "associations.xml");
        Associations(existing).Save(merged);
        await run.Dism("/Image:" + mount, "/Import-DefaultAppAssociations:" + merged);
        ct.ThrowIfCancellationRequested();
        return RuntimePack.AddSetupCommand(mount, "TechLoungeOS-Edge", prepared);
    }

    public const string FirstLogin = """
          Invoke-Step 'edge-browser' {
            if (!(Test-Path "$env:ProgramData\TechLoungeOS\MachineSetup\payloads\Edge\complete")) { throw 'Offline Edge setup did not complete. Use Retry setup or review ProgramData\TechLoungeOS\MachineSetup\payloads\Edge\status.txt.' }
          }
        """;

    // Configure before first sign-in; retain explicit source/organisation policy.
    public const string FirstRunPolicy = """
          $policy='HKLM:\SOFTWARE\Policies\Microsoft\Edge'
          $settings=Get-ItemProperty -LiteralPath $policy -ErrorAction SilentlyContinue
          if (!$settings -or !$settings.PSObject.Properties['HideFirstRunExperience']) {
            if (!(Test-Path -LiteralPath $policy)) { New-Item -Path $policy -Force | Out-Null }
            New-ItemProperty -LiteralPath $policy -Name HideFirstRunExperience -Value 1 -PropertyType DWord -Force | Out-Null
            if ((Get-ItemPropertyValue -LiteralPath $policy -Name HideFirstRunExperience -ErrorAction Stop) -ne 1) { throw 'Edge first-run policy did not apply.' }
          } elseif ($settings.HideFirstRunExperience -ne 1) {
            Write-Warning 'The source Edge first-run policy is retained; its welcome screen may still appear.'
          }
        """;

    public const string Script = """
        $ErrorActionPreference='Stop'
        $payload=$PSScriptRoot
        if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Edge setup requires administrator permission.' }
        $lock=[IO.File]::Open("$payload\install.lock",'OpenOrCreate','ReadWrite','None')
        $transcribing=$false
        try {
          Start-Transcript -Path "$payload\install.log" -Append; $transcribing=$true
        """ + "\n" + FirstRunPolicy + "\n" + """
          $item=Get-Content "$payload\installer.json" -Raw|ConvertFrom-Json
          $exe=Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'
          if (!(Test-Path $exe)) { $exe=Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe' }
          $current=if(Test-Path $exe){[version](Get-Item $exe).VersionInfo.ProductVersion}else{$null}
          if (!$current -or $current -lt [version]$item.Version) {
            $msi=Join-Path $payload 'MicrosoftEdge.msi'
            if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne $item.Sha256) { throw 'Offline Edge MSI hash mismatch.' }
            $signature=Get-AuthenticodeSignature $msi
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') { throw 'Edge MSI signature is not validly signed by Microsoft.' }
            # -Wait also waits for the persistent updater descendants. Follow the MSI client only.
            $process=Start-Process "$env:SystemRoot\System32\msiexec.exe" -ArgumentList @('/i',('"'+$msi+'"'),'/qn','/norestart','/L*v',('"'+$payload+'\msi.log"')) -PassThru
            $process.WaitForExit()
            if ($process.ExitCode -notin @(0,3010)) { throw ('Offline Edge installation failed: '+$process.ExitCode) }
            if ($process.ExitCode -eq 3010) { Set-Content "$payload\restart-required" 'Windows-controlled restart required' }
          }
          $paths=@((Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),(Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'))
          $installed=@($paths|Where-Object {Test-Path $_}|Where-Object {[version](Get-Item $_).VersionInfo.ProductVersion -ge [version]$item.Version})
          if (!$installed.Count) { throw 'Edge setup returned without the required installed browser version.' }
          $signature=Get-AuthenticodeSignature $installed[0]
          if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') { throw 'Installed Edge is not validly signed by Microsoft.' }
          Set-Content "$payload\status.txt" 'Offline Edge installed. Browser defaults apply to new users; existing user choices are retained.'
          Set-Content "$payload\complete" 'Edge verified'
        } catch { if(Test-Path "$payload\complete"){Remove-Item -LiteralPath "$payload\complete"}; Set-Content "$payload\status.txt" $_.Exception.Message; throw }
        finally { if($transcribing){Stop-Transcript};$lock.Dispose() }
        """;
}
