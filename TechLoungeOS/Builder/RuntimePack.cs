using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

public record RuntimeInstaller(string Id, string Architecture, string File, string Version, string Sha256, string Arguments);

// Download only on the builder. Install only inside the installed Windows image.
public static class RuntimePack
{
    // Microsoft ends .NET 8 servicing on 10 November 2026; newer builds retain .NET 10.
    public static bool Net8Supported(DateOnly date) => date <= new DateOnly(2026, 11, 10);
    public const string RelativeFolder = @"Windows\Setup\TechLoungeOS-Runtimes";
    public static IEnumerable<(string Id, string Architecture)> Packages(bool includeNet8)
    {
        foreach (var year in new[] { "2005", "2008", "2010", "2012", "2013", "2015+" })
            foreach (var arch in new[] { "x86", "x64" }) yield return ($"Microsoft.VCRedist.{year}.{arch}", arch);
        foreach (var major in includeNet8 ? new[] { "10", "8" } : new[] { "10" })
            foreach (var arch in new[] { "x86", "x64" }) yield return ($"Microsoft.DotNet.DesktopRuntime.{major}", arch);
    }

    public static async Task Prepare(ICommandRunner run, string folder, bool includeNet8, CancellationToken ct)
    {
        if (includeNet8 && !Net8Supported(DateOnly.FromDateTime(DateTime.UtcNow)))
            throw new IOException(".NET 8 is out of support. Deselect the optional .NET 8 compatibility runtime.");
        Directory.CreateDirectory(folder);
        var installers = new List<RuntimeInstaller>();
        foreach (var (id, arch) in Packages(includeNet8))
        {
            ct.ThrowIfCancellationRequested();
            var download = Path.Combine(folder, id.Replace('+', '_') + "-" + arch);
            Directory.CreateDirectory(download);
            await run.Run("winget.exe", ["download", "--id", id, "--exact", "--source", "winget", "--architecture", arch,
                "--skip-dependencies", "--skip-license", "--download-directory", download,
                "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"], ct);
            var files = Directory.GetFiles(download, "*.exe", SearchOption.AllDirectories);
            if (files.Length != 1) throw new IOException("Expected one offline Microsoft installer for " + id);
            var version = DownloadVersion(Path.GetFileName(files[0]));
            var name = installers.Count + ".exe";
            var dest = Path.Combine(folder, name);
            File.Move(files[0], dest);
            await VerifyMicrosoft(run, dest);
            installers.Add(new(id, arch, name, version, Hash(dest), Arguments(id)));
        }
        var dx = Path.Combine(folder, "directx.exe");
        await run.PowerShell($$"""
            Invoke-WebRequest -UseBasicParsing -Uri 'https://download.microsoft.com/download/8/4/a/84a35bf1-dafe-4ae8-82af-ad2ae20b6b14/directx_Jun2010_redist.exe' -OutFile {{Policy.Ps(dx)}}
            """, ct);
        await VerifyMicrosoft(run, dx);
        installers.Add(new("Microsoft.DirectX.June2010", "x86+x64", "directx.exe", "9.29.1974.1", Hash(dx), ""));
        File.WriteAllText(Path.Combine(folder, "installers.json"), JsonSerializer.Serialize(installers, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(folder, "install.ps1"), Script);
    }

    public static string DownloadVersion(string name)
    {
        var match = Regex.Match(name, @"_(\d+(?:\.\d+){1,3})_(?:Machine|User|Unknown)_", RegexOptions.IgnoreCase);
        if (!match.Success) throw new IOException("WinGet returned an unfamiliar download filename: " + name);
        return match.Groups[1].Value;
    }

    public static string Arguments(string id) => id.Contains(".2005.", StringComparison.Ordinal)
        ? "/Q /C:\"msiexec /i \"\"vcredist.msi\"\" /quiet /norestart\""
        : id.Contains(".2008.", StringComparison.Ordinal) ? "/qn /norestart"
        : "/quiet /norestart";

    public static Task<string> VerifyMicrosoft(ICommandRunner run, string path) => run.PowerShell($$"""
        $signature = Get-AuthenticodeSignature -LiteralPath {{Policy.Ps(path)}}
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
          throw 'The runtime installer does not have a valid Microsoft Authenticode signature.'
        }
        """);

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    public static DesktopSetup.Step Stage(string mount, string prepared)
    {
        var target = Path.Combine(mount, RelativeFolder);
        Policy.RequireUnlinkedPath(target);
        if (Directory.Exists(target)) throw new IOException("The source already contains a TechLoungeOS runtime pack.");
        return AddSetupCommand(mount, "TechLoungeOS-Runtimes", prepared);
    }

    public static DesktopSetup.Step AddSetupCommand(string mount, string name, string prepared)
    {
        if (name is not ("TechLoungeOS-Runtimes" or "TechLoungeOS-StoreXbox" or "TechLoungeOS-Edge" or "TechLoungeOS-HWiNFO")) throw new ArgumentException("Unknown setup payload.");
        var target = Path.Combine(mount, "Windows", "Setup", name);
        Policy.RequireUnlinkedPath(target);
        if (Directory.Exists(target)) throw new IOException("The source already contains this setup payload: " + name);
        var hook = Path.Combine(mount, @"Windows\Setup\Scripts\SetupComplete.cmd");
        var original = SetupHooks.Read(hook);
        Provisioning.CopyTree(prepared, target);
        File.WriteAllBytes(Path.Combine(target, "source-SetupComplete.cmd"), original);
        return DesktopSetup.Snapshot(name["TechLoungeOS-".Length..], "Windows", @"Setup\" + name, target);
    }

    public const string FirstLoginCheck = """
          Invoke-Step 'runtime-pack' {
            $runtime = Join-Path $env:ProgramData 'TechLoungeOS\MachineSetup\payloads\Runtimes'
            if (!(Test-Path (Join-Path $runtime 'complete'))) {
              throw 'Runtime setup was skipped or failed. Use Retry setup or review ProgramData\TechLoungeOS\MachineSetup\payloads\Runtimes\status.txt and install.log. Existing successful app steps are kept.'
            }
          }
        """;

    public const string Script = """
        $ErrorActionPreference = 'Stop'
        $payload = $PSScriptRoot
        if (!( [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Runtime installation needs administrator permission.' }
        if (Test-Path "$payload\complete") { exit 0 }
        $lock = [IO.File]::Open("$payload\install.lock", 'OpenOrCreate', 'ReadWrite', 'None')
        $failed = New-Object 'System.Collections.Generic.List[string]'
        $transcribing = $false
        try {
          Start-Transcript -Path "$payload\install.log" -Append
          $transcribing = $true
          $entries = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue)
          foreach ($item in (Get-Content "$payload\installers.json" -Raw | ConvertFrom-Json)) {
            $marker = Join-Path $payload ($item.File + '.done')
            if (Test-Path $marker) { continue }
            try {
              $file = Join-Path $payload $item.File
              if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $item.Sha256) { throw 'Payload SHA-256 mismatch.' }
              $arch = [regex]::Escape($item.Architecture)
              $pattern = $null
              $dotnetRuntime = $false
              if ($item.Id -match '^Microsoft\.VCRedist\.(.+)\.(x86|x64)$') {
                $year = $Matches[1]
                $yearPattern = if ($year -eq '2015+') { '(?:2015|2017|2019|2022|2026|v14)' } else { [regex]::Escape($year) }
                $pattern = '^Microsoft Visual C\+\+ ' + $yearPattern + '.*(?:\(' + $arch + '\)| ' + $arch + ')'
                # The oldest x86 products omit the architecture in their display name.
                if ($year -in @('2005','2008') -and $item.Architecture -eq 'x86') { $pattern = '^Microsoft Visual C\+\+ ' + $yearPattern + '.*Redistributable(?!.*(?:x64|64-bit))' }
              } elseif ($item.Id -match '^Microsoft\.DotNet\.DesktopRuntime\.(\d+)$') {
                $dotnetRuntime = $true
                $pattern = '^Microsoft Windows Desktop Runtime(?: -)? (?<runtime>' + $Matches[1] + '\.\d+\.\d+(?:\.\d+)?) \(' + $arch + '\)'
              }
              $existing = @($entries | Where-Object {
                if (!$pattern -or $_.Publisher -notlike 'Microsoft*') { return $false }
                $match = [regex]::Match([string]$_.DisplayName, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
                if (!$match.Success) { return $false }
                # .NET component ARP versions can be 80.x rather than the runtime's 10.x.
                $versionText = if ($dotnetRuntime) { $match.Groups['runtime'].Value } else { [string]$_.DisplayVersion }
                $v = $null
                [version]::TryParse($versionText, [ref]$v) -and $v -ge [version]$item.Version
              })
              if ($existing.Count) { Write-Output ('Keeping existing/newer ' + $item.Id + ' ' + $item.Architecture); Set-Content $marker 'Existing or newer runtime retained'; continue }
              if ($item.Id -eq 'Microsoft.DirectX.June2010') {
                $extract = Join-Path $payload 'DirectX'
                New-Item $extract -ItemType Directory -Force | Out-Null
                $process = Start-Process -FilePath $file -ArgumentList ('/Q /T:"' + $extract + '"') -Wait -PassThru
                if ($process.ExitCode -ne 0) { throw "DirectX extraction failed: $($process.ExitCode)" }
                $setup = Join-Path $extract 'DXSETUP.exe'
                $signature = Get-AuthenticodeSignature -LiteralPath $setup
                if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') { throw 'DirectX setup signature check failed.' }
                $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru
              } else {
                $process = Start-Process -FilePath $file -ArgumentList $item.Arguments -Wait -PassThru
              }
              $accepted = if ($item.Id -eq 'Microsoft.DirectX.June2010') { @(0,3010) } else { @(0,3010,1638,-2147023258) }
              if ($process.ExitCode -notin $accepted) { throw "Installer failed: $($process.ExitCode)" }
              if ($process.ExitCode -eq 3010) { Set-Content "$payload\restart-required" 'A normal Windows restart is required. No restart was forced.' }
              Set-Content $marker ('Installer result: ' + $process.ExitCode)
            } catch { $failed.Add($item.Id + ' ' + $item.Architecture + ': ' + $_.Exception.Message); Write-Warning $failed[$failed.Count-1] }
          }
          if ($failed.Count) { Set-Content "$payload\status.txt" $failed.ToArray(); exit 1 }
          Set-Content "$payload\status.txt" 'All runtime installers completed or retained an existing/newer runtime.'
          Set-Content "$payload\complete" 'Runtime setup completed'
        } catch { Set-Content "$payload\status.txt" $_.Exception.Message; throw }
        finally { if ($transcribing) { Stop-Transcript }; $lock.Dispose() }
        """;
}
