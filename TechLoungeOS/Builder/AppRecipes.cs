using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TechLoungeOS;

public static class AppRecipes
{
    public static bool NeedsPayload(string id) => RecommendedApps.IsDiscord(id)||id is "TechLounge.HatchZip" or "TechLounge.Shiftd";
    public static bool Custom(string id) => NeedsPayload(id)||RecommendedApps.IsChatGPT(id)||HwInfoPack.Is(id);
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };

    private static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.UserInfo == "" && uri.Port == 443 &&
        uri.Host is "api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    private static async Task<HttpResponseMessage> Get(Uri uri, CancellationToken ct)
    {
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (!Allowed(uri)) throw new IOException("App download left the permitted GitHub hosts.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("TechLoungeOS-Builder/1.0");
            var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            { uri = new Uri(uri, next); response.Dispose(); continue; }
            response.EnsureSuccessStatusCode(); return response;
        }
        throw new IOException("Too many app download redirects.");
    }

    private static async Task<byte[]> Read(Uri uri, int maximum, CancellationToken ct)
    {
        using var response = await Get(uri, ct);
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("App download exceeds its size limit.");
        using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await input.ReadAsync(buffer, ct)) != 0)
        { if (output.Length + n > maximum) throw new IOException("App download exceeds its size limit."); await output.WriteAsync(buffer.AsMemory(0, n), ct); }
        return output.ToArray();
    }

    public static async Task Prepare(string[] apps, string folder, CancellationToken ct)
    {
        apps=RecommendedApps.NormalizeIds(apps);
        if (!apps.Any(NeedsPayload)) return;
        Directory.CreateDirectory(folder);
        foreach (var id in apps.Where(NeedsPayload))
        {
            if (id == "TechLounge.Shiftd")
            {
                var bytes = await Read(new("https://github.com/RaheemC4/tech-tips/releases/download/shiftd-1.0.0-20261011-r2/Shiftd-1.0.0-Setup.exe"), 400 << 20, ct);
                VerifiedWrite(folder, "Shiftd.exe", bytes, "e6b8aae534698f1b687184a4790c8f18956e69d27b334b4b33bc55a8fa31a1fe");
                continue;
            }
            var discord = id == RecommendedApps.Discord;
            var api = discord ? "https://api.github.com/repos/GooseMod/OpenAsar/releases/tags/nightly" : "https://api.github.com/repos/xevrion/hatchzip/releases/latest";
            using var release = JsonDocument.Parse(await Read(new(api), 2 << 20, ct));
            if (discord && release.RootElement.GetProperty("tag_name").GetString() != "nightly") throw new IOException("Unexpected OpenAsar release.");
            var wanted = discord ? "app.asar" : "HatchZip-x64-setup.exe";
            var asset = release.RootElement.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == wanted);
            var digest = asset.GetProperty("digest").GetString() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(digest, @"\Asha256:[0-9a-fA-F]{64}\z")) throw new IOException("The publisher release has no SHA-256 digest.");
            var size = asset.GetProperty("size").GetInt32();
            if (size < 1024 || size > (discord ? 5 << 20 : 128 << 20)) throw new IOException("Implausible app asset size.");
            var url = asset.GetProperty("browser_download_url").GetString()!;
            var prefix = discord ? "https://github.com/GooseMod/OpenAsar/releases/download/nightly/" : "https://github.com/xevrion/hatchzip/releases/download/";
            if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith("/" + wanted, StringComparison.Ordinal)) throw new IOException("Unexpected app publisher URL.");
            var data = await Read(new(url), size, ct);
            if (data.Length != size) throw new IOException("Incomplete app asset.");
            VerifiedWrite(folder, discord ? "openasar.asar" : "HatchZip.exe", data, digest[7..]);
            if (discord)
            {
                File.WriteAllText(Path.Combine(folder, "openasar.sha256"), digest[7..] + "\n");
                File.Copy(Path.Combine(AppContext.BaseDirectory, "DiscordLauncher.exe"), Path.Combine(folder, "DiscordLauncher.exe"));
            }
        }
        var hashes = Directory.GetFiles(folder).ToDictionary(p => Path.GetFileName(p), RuntimePack.Hash);
        File.WriteAllText(Path.Combine(folder, "sha256.json"), JsonSerializer.Serialize(hashes));
    }

    private static void VerifiedWrite(string folder, string name, byte[] bytes, string expected)
    {
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("App asset SHA-256 mismatch: " + name);
        File.WriteAllBytes(Path.Combine(folder, name), bytes);
    }

    public static string Script(string id)
    {
        id=RecommendedApps.NormalizeId(id);
        if(id==RecommendedApps.Discord)return DiscordScript;
        if(id==RecommendedApps.ChatGPT)return ChatGPTScript;
        if(HwInfoPack.Is(id))return HwInfoPack.FirstLogin;
        if(!NeedsPayload(id))throw new ArgumentException("Unknown app recipe.",nameof(id));
        return $$"""
          Invoke-Step '{{id}}' {
            $existing = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object DisplayName -Match '^{{(id == "TechLounge.HatchZip" ? "HatchZip" : "Shiftd")}}(?:\s|$)')
            if ($existing.Count) { Write-Output 'Keeping the existing app installation.' }
            else {
            $file = Join-Path $env:ProgramData 'TechLoungeOS\Recommended\{{(id == "TechLounge.HatchZip" ? "HatchZip" : "Shiftd")}}.exe'
            $expected = (Get-Content (Join-Path (Split-Path $file) 'sha256.json') -Raw | ConvertFrom-Json).(Split-Path $file -Leaf)
            if (!$expected -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $expected) { throw 'Bundled installer failed SHA-256 verification.' }
            $process = Start-Process -FilePath $file -ArgumentList '{{(id == "TechLounge.HatchZip" ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" : "/S")}}' -Wait -PassThru
            if ($process.ExitCode -notin @(0,3010)) { throw "Installer failed: $($process.ExitCode)" }
            }
        {{(id == RecommendedApps.HatchZip ? HatchZipShortcut : "")}}
          }
        """;
    }

    public const string HatchZipShortcut = """
            $hatchExe=Join-Path $env:LOCALAPPDATA 'Programs\HatchZip\hatchzip-gui.exe'
            if(!(Test-Path -LiteralPath $hatchExe -PathType Leaf)){throw 'HatchZip is installed but its desktop application could not be located. Retry setup after checking its installation.'}
            $hatchShell=New-Object -ComObject WScript.Shell
            try{
              $hatchPath=Join-Path ([Environment]::GetFolderPath('Desktop')) 'HatchZip.lnk'
              $hatchLink=$hatchShell.CreateShortcut($hatchPath)
              try{
                if((Test-Path -LiteralPath $hatchPath) -and !$hatchLink.TargetPath.Equals($hatchExe,[StringComparison]::OrdinalIgnoreCase)){throw 'An existing HatchZip desktop shortcut points elsewhere. It was kept unchanged.'}
                $hatchLink.TargetPath=$hatchExe;$hatchLink.WorkingDirectory=Split-Path $hatchExe;$hatchLink.IconLocation=$hatchExe
                $hatchLink.Save()
              }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($hatchLink)}
            }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($hatchShell)}
        """;

    public static string ChatGPTScript => $$"""
          Invoke-Step '{{RecommendedApps.ChatGPT}}' {
            $package=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Select-Object -First 1
            if(!$package){
              # An existing staged Store app still needs registration for this user.
              try{Add-AppxPackage -RegisterByFamilyName -MainPackage 'OpenAI.Codex_2p2nqsd0c76g0' -ErrorAction Stop}catch{Write-Verbose 'ChatGPT is not yet staged for this profile; requesting its normal Store installation.'}
              $package=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Select-Object -First 1
            }
            if(!$package){
              Install-WinGetApp '{{RecommendedApps.ChatGPTStore}}' 'msstore'
              $package=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Select-Object -First 1
              if(!$package){
                Add-AppxPackage -RegisterByFamilyName -MainPackage 'OpenAI.Codex_2p2nqsd0c76g0' -ErrorAction Stop
                $package=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Select-Object -First 1
              }
            }
            if(!$package) { throw 'The installed ChatGPT package was not found for this user.' }
            if($package.PackageFamilyName -cne 'OpenAI.Codex_2p2nqsd0c76g0'){throw 'The installed ChatGPT package identity differs from its official Store family.'}
            $manifest=Get-AppxPackageManifest -Package $package.PackageFullName -ErrorAction Stop
            $entry=@($manifest.Package.Applications.Application | Where-Object { $_.VisualElements -and $_.Executable } | Select-Object -First 1)
            if(!$entry.Count) { throw 'ChatGPT has no desktop application entry.' }
            $shell=New-Object -ComObject WScript.Shell
            $link=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'ChatGPT.lnk'))
            $link.TargetPath=Join-Path $env:WINDIR 'explorer.exe'
            $link.Arguments='shell:AppsFolder\'+$package.PackageFamilyName+'!'+$entry[0].Id
            $link.IconLocation=Join-Path $package.InstallLocation $entry[0].Executable
            $link.Save()
          }
        """;

    public const string DiscordDatabase = """
        function Initialize-DiscordDatabase([string]$root) {
          $database=Join-Path $root 'installer.db'
          # Existing profiles own their database. Never replace or reset it.
          if(Test-Path -LiteralPath $database){return}
          $seeds=@(Get-ChildItem -LiteralPath $root -Directory -Filter 'app-*' | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'installer.db') })
          if($seeds.Count -ne 1){throw 'Discord installer initialisation is incomplete or ambiguous. Retry setup after fully quitting Discord.'}
          $seed=Join-Path $seeds[0].FullName 'installer.db'
          $part=$seed
          while($part){
            if((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked Discord installer paths are not supported.'}
            $part=Split-Path $part
          }
          $file=Get-Item -LiteralPath $seed
          if($file.Length -lt 16 -or $file.Length -gt 16777216){throw 'Unexpected Discord installer database size.'}
          $stream=[IO.File]::OpenRead($seed)
          try{
            $header=New-Object byte[] 16
            if($stream.Read($header,0,16) -ne 16 -or [Text.Encoding]::ASCII.GetString($header,0,15) -cne 'SQLite format 3' -or $header[15] -ne 0){throw 'Unrecognised Discord installer database format.'}
          }finally{$stream.Dispose()}
          $hash=(Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash
          # Silent Squirrel setup omits the vendor first-run database relocation.
          # Move its seed unchanged before any app launch; File.Move cannot overwrite.
          [IO.File]::Move($seed,$database)
          if((Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash -ne $hash){throw 'Discord installer database verification failed.'}
        }
        """;

    public const string DiscordScript = DiscordDatabase + "\n" + """
          Invoke-Step 'TechLounge.DiscordOpenAsar' {
            $root = Join-Path $env:LOCALAPPDATA 'Discord'
            $existingDiscord = Test-Path $root
            Install-WinGetApp 'Discord.Discord'
            $payload = Join-Path $env:LOCALAPPDATA 'TechLoungeTweaks\Discord'
            foreach ($path in @($root, $payload)) {
              $part = $path
              while ($part) {
                if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked Discord paths are not supported.' }
                $part = Split-Path $part
              }
            }
            # Only close processes created by a fresh installation in this profile.
            if (!$existingDiscord) {
              for ($closing=0; $closing -lt 20; $closing++) {
                $running=@(Get-Process Discord -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) })
                foreach ($process in $running) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
                # Stop-Process returns before process exit; installer children can also appear late.
                Start-Sleep -Milliseconds 500
                if (!@(Get-Process Discord -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) }).Count) { break }
              }
            }
            if (@(Get-Process Discord -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'Discord is running. Fully quit it; OpenAsar setup will retry next sign-in.' }
            $ready = $false
            for ($attempt=0; $attempt -lt 20; $attempt++) {
              if (@(Get-ChildItem -LiteralPath $root -Directory -Filter 'app-*' -ErrorAction SilentlyContinue | Where-Object { (Test-Path (Join-Path $_.FullName 'Discord.exe')) -and (Test-Path (Join-Path $_.FullName 'resources\app.asar')) }).Count) { $ready=$true; break }
              Start-Sleep -Milliseconds 500
            }
            if (!$ready) { throw 'Discord has no supported complete app-* directory. Open Discord once, fully quit it, then retry.' }
            Initialize-DiscordDatabase $root
            New-Item $payload -ItemType Directory -Force | Out-Null
            $source = Join-Path $env:ProgramData 'TechLoungeOS\Recommended'
            $hashes = Get-Content "$source\sha256.json" -Raw | ConvertFrom-Json
            foreach ($name in @('openasar.asar','openasar.sha256','DiscordLauncher.exe')) {
              if ((Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash -ne $hashes.$name) { throw 'Discord payload failed SHA-256 verification.' }
              Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $payload $name) -Force
            }
            $launcher = Join-Path $payload 'DiscordLauncher.exe'
            $process = Start-Process -FilePath $launcher -ArgumentList '--repair-only' -Wait -PassThru
            if ($process.ExitCode -ne 0) { throw 'OpenAsar repair failed. See LocalAppData\TechLoungeTweaks\Discord\launcher.log.' }
            if ((Get-Content (Join-Path $payload 'launcher.log') -Tail 1) -notmatch 'openasar-(present|restored):') { throw 'OpenAsar was not applied. Fully quit Discord and retry.' }
            $folder = Join-Path $env:APPDATA 'discord'
            New-Item $folder -ItemType Directory -Force | Out-Null
            $settings = Join-Path $folder 'settings.json'
            $config = if (Test-Path $settings) { [IO.File]::ReadAllText($settings) | ConvertFrom-Json } else { New-Object PSObject }
            if (!$config -or $config -is [array] -or $config -is [string] -or $config -is [ValueType]) { throw 'Discord settings must contain a JSON object.' }
            $oa = $config.openasar
            if (!$oa -or $oa -is [array] -or $oa -is [string] -or $oa -is [ValueType]) { $oa = New-Object PSObject }
            @{setup=$true; cmdPreset='perf'; noTrack=$true; noTyping=$true; themeSync=$true; quickstart=$true; multiInstance=$false}.GetEnumerator() | ForEach-Object { $oa | Add-Member -MemberType NoteProperty -Name $_.Key -Value $_.Value -Force }
            $config | Add-Member -MemberType NoteProperty -Name openasar -Value $oa -Force
            $temporary = $settings + '.tlos-' + [guid]::NewGuid().ToString('N')
            try {
              # Node's JSON.parse does not accept Windows PowerShell's UTF-8 BOM.
              [IO.File]::WriteAllText($temporary,($config | ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
              if (Test-Path $settings) { Copy-Item -LiteralPath $settings -Destination ($settings + '.tlos-backup') -Force }
              Move-Item -LiteralPath $temporary -Destination $settings -Force
            } finally { if (Test-Path $temporary) { Remove-Item -LiteralPath $temporary } }
            $process = Start-Process -FilePath $launcher -ArgumentList '--integrate' -Wait -PassThru
            if ($process.ExitCode -ne 0) { throw 'Discord shortcut integration failed. See the launcher log.' }
          }
        """;
}

// Deliberately scoped to known installers: never guess which existing process or
// startup entry belongs to an arbitrary catalogue result.
public static class QuietApps
{
    public const string Functions = """
        function Get-QuietAppInstall([string]$id) {
          $spec = switch ($id) {
            'Brave.Brave' { @('Brave', 'BraveSoftware\Brave-Browser\Application\brave.exe', 'BraveSoftware\Brave-Browser') }
            'Google.Chrome' { @('Google Chrome', 'Google\Chrome\Application\chrome.exe', 'Google\Chrome') }
            'Valve.Steam' { @('Steam', 'Steam\steam.exe', 'Steam') }
            'Guru3D.Afterburner' { @('MSI Afterburner', 'MSI Afterburner\MSIAfterburner.exe', 'MSI Afterburner') }
            'Stremio.Stremio' { @('Stremio', 'Programs\LNV\Stremio-4\stremio.exe', 'Programs\LNV\Stremio-4') }
            'Proton.ProtonVPN' { @('Proton VPN', 'Proton\VPN\ProtonVPN.Launcher.exe', 'Proton\VPN') }
            'Discord.Discord' { @('Discord', 'Discord\Update.exe', 'Discord') }
          }
          if (!$spec) { return $null }
          $bases=@($env:LOCALAPPDATA,$env:ProgramFiles,${env:ProgramFiles(x86)}) | Where-Object { $_ }
          $paths=@($bases | ForEach-Object { Join-Path $_ $spec[1] })
          $snapshot=Join-Path $state ($id+'.quiet-before.xml')
          $finished=Join-Path $state ($id+'.quiet-complete')
          $pending=(Test-Path -LiteralPath $snapshot) -and !(Test-Path -LiteralPath $finished)
          if (!$pending -and @($paths | Where-Object { Test-Path -LiteralPath $_ }).Count) {
            return [pscustomobject]@{Name=$spec[0];Id=$id;Paths=$paths;Existing=$true}
          }
          $run=if($pending){Import-Clixml -LiteralPath $snapshot}else{@{}}
          if($run -isnot [hashtable]){throw 'Invalid quiet-install startup snapshot'}
          if(!$pending){
          foreach($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')) {
            if(Test-Path -LiteralPath $key) {
              $values=Get-ItemProperty -LiteralPath $key
              foreach($p in $values.PSObject.Properties | Where-Object Name -NotMatch '^PS') { $run[$key+'|'+$p.Name]=[string]$p.Value }
            }
          }
          $run|Export-Clixml -LiteralPath $snapshot
          }
          [pscustomobject]@{Name=$spec[0];Id=$id;Paths=$paths;Roots=@($bases | ForEach-Object { Join-Path $_ $spec[2] });Run=$run;Pids=@(Get-Process | Select-Object -ExpandProperty Id)}
        }
        function Complete-QuietAppInstall($app) {
          $exe=@($app.Paths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
          if(!$exe.Count) { Write-Warning ('Desktop shortcut needs review for '+$app.Name); return }
          if($app.Existing){Complete-AppDesktopShortcut $app $exe[0];return}
          # Only this fresh install's processes in the signed-in session qualify.
          foreach($p in @(Get-Process | Where-Object { $_.Id -notin $app.Pids -and $_.SessionId -eq (Get-Process -Id $PID).SessionId })) {
            $path=$null;try{$path=$p.Path}catch{}
            if($path -and @($app.Roots | Where-Object { $path.StartsWith($_+'\',[StringComparison]::OrdinalIgnoreCase) }).Count) {
              if($p.MainWindowHandle -ne 0) { [void]$p.CloseMainWindow() }
              if(!$p.WaitForExit(5000)) { Stop-Process -Id $p.Id -ErrorAction Stop }
            }
          }
          foreach($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')) {
            if(!(Test-Path -LiteralPath $key)) { continue }
            foreach($p in (Get-ItemProperty -LiteralPath $key).PSObject.Properties | Where-Object Name -NotMatch '^PS') {
              $command=[Environment]::ExpandEnvironmentVariables([string]$p.Value).TrimStart()
              $ours=@($app.Roots | Where-Object { $command.StartsWith('"'+$_+'\',[StringComparison]::OrdinalIgnoreCase) -or $command.StartsWith($_+'\',[StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
              if($ours -and !$app.Run.ContainsKey($key+'|'+$p.Name)) {
                # Retain the exact new entry for diagnosis/undo, before deleting it.
                $backup=Join-Path $state ($app.Id+'.startup-backup.jsonl')
                @{key=$key;name=$p.Name;value=[string]$p.Value}|ConvertTo-Json -Compress|Add-Content -LiteralPath $backup
                Remove-ItemProperty -LiteralPath $key -Name $p.Name -ErrorAction Stop
              }
            }
          }
          Complete-AppDesktopShortcut $app $exe[0]
          Set-Content -LiteralPath (Join-Path $state ($app.Id+'.quiet-complete')) 'Verified quiet install cleanup'
        }
        function Complete-AppDesktopShortcut($app,[string]$exe) {
          # Existing apps get only a missing shortcut, never startup/process changes.
          # Discord's recipe supplies the OpenAsar repair shortcut afterwards.
          if($app.Id -ne 'Discord.Discord') {
            $shell=New-Object -ComObject WScript.Shell
            $hasShortcut=$false
            foreach($desktop in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('CommonDesktopDirectory'))) {
              if(!(Test-Path -LiteralPath $desktop)){continue}
              foreach($file in @(Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File -Force -ErrorAction SilentlyContinue)) {
                try {
                  $existing=$shell.CreateShortcut($file.FullName)
                  if([Environment]::ExpandEnvironmentVariables($existing.TargetPath).Equals($exe,[StringComparison]::OrdinalIgnoreCase)){$hasShortcut=$true;break}
                } catch { } # A broken unrelated shortcut must not block this app.
              }
              if($hasShortcut){break}
            }
            if(!$hasShortcut) {
              $destination=Join-Path ([Environment]::GetFolderPath('Desktop')) ($app.Name+'.lnk')
              if(Test-Path -LiteralPath $destination){throw ('An existing '+$app.Name+' shortcut points elsewhere. It was kept unchanged.')}
              $link=$shell.CreateShortcut($destination)
              $link.TargetPath=$exe;$link.WorkingDirectory=Split-Path $exe;$link.IconLocation=$exe;$link.Save()
            }
          }
        }
        """;
}
