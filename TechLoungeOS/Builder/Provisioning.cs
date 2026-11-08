using System.Text.Json;

namespace TechLoungeOS;

public static class Provisioning
{
    public static string Script(string[] apps, bool gameMode, bool suggestions, string? wallpaperName = null, bool runtimePack = false, bool storeXbox = false, bool office = false, bool recommended = false, bool nvidia = false, bool amdIntelGraphics = false, bool edge = false, bool desktopDefaults = false, string? wallpaperPreset = null, bool wallpaperLibrary = false, bool windowsRecovery = false, bool currentTweaks = false, bool legacyNetwork = false, bool bootOptimizer = false, bool standalone = false) => """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
        $state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS'
        New-Item $state -ItemType Directory -Force | Out-Null
        if (Test-Path "$state\complete") { exit 0 }
        try { $lock = [IO.File]::Open("$state\provision.lock", 'OpenOrCreate', 'ReadWrite', 'None') }
        catch { Write-Warning 'Provisioning is already running or its state is inaccessible.'; exit 1 }
        $script:failures = New-Object 'System.Collections.Generic.List[string]'
        $script:warnings = New-Object 'System.Collections.Generic.List[string]'
        $script:startedUtc=[DateTime]::UtcNow.ToString('o');$script:activeStep='';$script:stepStartedUtc=$script:startedUtc
        function Write-SetupProgress([string]$step,[string]$message,[string]$phase='installing') {
          if($script:activeStep -cne $step){$script:activeStep=$step;$script:stepStartedUtc=[DateTime]::UtcNow.ToString('o')}
          $path=Join-Path $state 'progress.json';$temporary=$path+'.tmp'
          $json=@{step=$step;message=$message;phase=$phase;updatedUtc=[DateTime]::UtcNow.ToString('o');startedUtc=$script:startedUtc;stepStartedUtc=$script:stepStartedUtc;completed=@(Get-ChildItem -LiteralPath $state -Filter '*.done' -File).Count}|ConvertTo-Json -Compress
          [IO.File]::WriteAllText($temporary,$json,[Text.UTF8Encoding]::new($false))
          if(Test-Path -LiteralPath $path){[IO.File]::Replace($temporary,$path,[NullString]::Value)}else{[IO.File]::Move($temporary,$path)}
        }
        function Invoke-Step([string]$name, [scriptblock]$action) {
          if (Test-Path "$state\$name.done") { return }
          Write-SetupProgress $name 'You can keep using Windows while setup finishes.'
          try {
            & $action
            Set-Content "$state\$name.done" 'Completed' -ErrorAction Stop
          } catch {
            $script:failures.Add("${name}: $($_.Exception.Message)")
            Write-Warning "${name}: $($_.Exception.Message). Retry at next sign-in."
          }
        }
        """ + "\n" + WinGetInstall + "\n" + """
        $transcribing = $false
        try {
        Start-Transcript -Path "$state\first-login.log" -Append
        $transcribing = $true
        """ + SourcePresentation.FirstLogin + (wallpaperLibrary ? WallpaperPresets.LibraryShortcut : "") + (wallpaperName != null ? Wallpaper.FirstLogin(wallpaperName) : WallpaperPresets.FirstLogin(wallpaperPreset)) + (desktopDefaults ? "\n"+DesktopDefaults.FirstLogin+"\n"+InputDefaults.Script+LockScreen.FirstLogin : "") + (windowsRecovery || runtimePack || storeXbox || office || edge || legacyNetwork ? "\n"+DesktopSetup.FirstLogin+"\n" : "") + (windowsRecovery ? WindowsRecovery.FirstLogin : "") + (edge ? "\n" + EdgePack.FirstLogin : "") + (runtimePack ? "\n" + RuntimePack.FirstLoginCheck + "\n" + DefenderFirstLogin : "") + (storeXbox ? "\n" + StoreXbox.FirstLogin : "") + (gameMode ? """

          Invoke-Step 'game-mode' {
            New-Item 'HKCU:\Software\Microsoft\GameBar' -Force | Out-Null
            New-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -Name AutoGameModeEnabled -Value 1 -PropertyType DWord -Force | Out-Null
          }
        """ : "") + (suggestions ? """

          Invoke-Step 'suggestions' {
            New-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -Force | Out-Null
            New-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -Name SystemPaneSuggestionsEnabled -Value 0 -PropertyType DWord -Force | Out-Null
          }
        """ : "") + (legacyNetwork ? LegacyNetwork.Verify : "") + (currentTweaks ? TweaksInstaller.FirstLogin : "") + (recommended ? RecommendedProfile.FirstLogin : "") + "\n" + string.Join("\n", RecommendedApps.NormalizeIds(apps).Select(a => AppRecipes.Custom(a) ? AppRecipes.Script(a) : $$"""
          Invoke-Step '{{a}}' {
            Install-WinGetApp {{Policy.Ps(a)}}
          }
        """)) + (office ? "\n" + DesktopSetup.OfficeFirstLogin + "\n" + OfficePack.FirstLogin : "") + (nvidia ? NvidiaSetup.FirstLogin : "") + (amdIntelGraphics ? GraphicsSetup.FirstLogin : "") + (desktopDefaults ? "\n"+DesktopSurface.FirstLogin(storeXbox) : "") + """

          $tweaks = Join-Path $env:ProgramData 'TechLoungeOS\TechLoungeTweaks\TechLoungeTweaks.exe'
          if (Test-Path $tweaks) { Invoke-Step 'tweaks-shortcut' {
            $shell = New-Object -ComObject WScript.Shell
            $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'TechLoungeTweaks.lnk'))
            $link.TargetPath = $tweaks
            $link.WorkingDirectory = Split-Path $tweaks
            $launcher = Join-Path $env:ProgramData 'TechLoungeOS\FirstLogin.exe'
            if (Test-Path $launcher) {
              $link.TargetPath = $launcher
              $link.Arguments = '--open-tweaks'
              $link.IconLocation = $tweaks
            }
            $link.Save()
          } }
        """ + (bootOptimizer ? "\n"+BootOptimizer.FirstLogin(apps) : "") + (standalone ? "\n"+Standalone.FirstLogin(apps) : "") + (desktopDefaults ? "\n"+PowerDefaults.FirstLogin : "") + "\n" + """
          # Source setup may restart the PC during initial sign-in. Invite Hello
          # only after provisioning succeeds, rather than losing an early window.
          $helloRequest=Join-Path $env:ProgramData 'TechLoungeOS\account-setup\hello-requested'
          if(!$script:failures.Count -and (Test-Path -LiteralPath $helloRequest) -and ([IO.File]::ReadAllText($helloRequest)).Trim() -eq $env:USERNAME) {
            Invoke-Step 'hello-options' { Start-Process -FilePath 'ms-settings:signinoptions' }
          }
          if ($script:failures.Count) {
            Set-Content "$state\status.txt" (@('Provisioning incomplete. Failed steps retry at your next sign-in.') + $script:failures.ToArray())
            Write-SetupProgress '' ($script:failures -join [Environment]::NewLine) 'failed'
            exit 1
          }
          Set-Content "$state\status.txt" (@('All selected provisioning steps completed.') + $script:warnings.ToArray())
          Set-Content "$state\complete" 'Provisioning completed'
          $restartMarker=Join-Path $env:ProgramData 'TechLoungeOS\MachineSetup\restart-required'
          $restartNeeded=Test-Path -LiteralPath $restartMarker
          if($restartNeeded){
            try{
              $boot=(Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop).LastBootUpTime
              if($boot -is [datetime]){
                $restartNeeded=(Get-Item -LiteralPath $restartMarker -ErrorAction Stop).LastWriteTimeUtc -ge $boot.ToUniversalTime()
              }
            }catch{Write-Verbose 'Boot time could not be verified; retain the restart notice.'}
          }
          if($restartNeeded){
            Write-SetupProgress '' 'Your apps are installed. Restart Windows normally when convenient to finish system changes. No restart was forced.' 'restart-needed'
          }else{Write-SetupProgress '' 'Your selected apps and settings are ready.' 'complete'}
        } catch { Write-SetupProgress '' $_.Exception.Message 'failed';Write-Error $_ -ErrorAction Continue; exit 1 }
        finally { if ($transcribing) { Stop-Transcript }; $lock.Dispose() }
        """;

    public const string DefenderFirstLogin = """
          Invoke-Step 'defender-definitions' {
            $service = Get-Service WinDefend -ErrorAction SilentlyContinue
            if (!$service -or $service.Status -ne 'Running') { Write-Output 'Source Defender service is absent or stopped; its configuration is retained.'; return }
            $status = Get-MpComputerStatus -ErrorAction Stop
            $preferences = Get-MpPreference -ErrorAction Stop
            if (!$status.AntivirusEnabled -or $preferences.DisableRealtimeMonitoring) { Write-Output 'Source Defender preference is retained.'; return }
            if ($status.AntivirusSignatureVersion -and $status.AntivirusSignatureVersion -ne '0.0.0.0' -and $status.AMEngineVersion -and $status.AMEngineVersion -ne '0.0.0.0') { return }
            try {
              Write-Output 'Existing Defender has no usable definitions. Requesting its normal Microsoft update.'
              Update-MpSignature -ErrorAction Stop
              $status = Get-MpComputerStatus -ErrorAction Stop
              if (!$status.RealTimeProtectionEnabled -or !$status.AntivirusSignatureVersion -or $status.AntivirusSignatureVersion -eq '0.0.0.0' -or !$status.AMEngineVersion -or $status.AMEngineVersion -eq '0.0.0.0') { throw 'Defender is not ready after its definition update.' }
            } catch { throw ('Defender definitions need attention. An administrator can run Update-MpSignature and review Windows Security. No security settings were changed: ' + $_.Exception.Message) }
          }
        """;

    public const string WinGetInstall = QuietApps.Functions + "\n" + """
        function Install-WinGetApp([string]$id, [ValidateSet('winget','msstore')][string]$source = 'winget') {
          $winget = Get-Command winget.exe -ErrorAction Stop
          $quiet = Get-QuietAppInstall $id
          # The Chrome EXE manifest has version-specific URLs and do-not-launch
          # switches; the rotating enterprise MSI URL can outrun its checksum.
          $installId=if($id -eq 'Google.Chrome'){'Google.Chrome.EXE'}else{$id}
          # Discord's catalogue omits silent arguments. Its recipe completes the
          # required installer database initialisation without launching the app.
          $installerArguments=if($id -eq 'Discord.Discord' -and $source -eq 'winget'){@('--override','--silent')}else{@()}
          for ($attempt=0; $attempt -lt 2; $attempt++) {
            & $winget.Source install --id $installId --exact --source $source --no-upgrade --silent --accept-source-agreements --accept-package-agreements --disable-interactivity @installerArguments
            $code = $LASTEXITCODE
            # A rotating vendor download can outpace the catalog. Refresh once,
            # but always require WinGet's checksum verification on the retry.
            if ($code -in @(-1978335217,-1978335215) -and $attempt -eq 0) {
              Write-Output 'The app catalog is missing data or its checksum is stale. Refreshing it and retrying once.'
              & $winget.Source source update --name $source --disable-interactivity
              if ($LASTEXITCODE -ne 0) { throw "App catalog refresh failed for $id (exit $LASTEXITCODE). Retry at next sign-in." }
              continue
            }
            if ($code -eq -1978335215) { throw "App installation failed: $id (exit $code). The download still does not match the catalog checksum. No unverified installer was run; retry after the publisher and catalog agree." }
            if ($code -ne 0 -and $code -ne -1978335135) { throw "App installation failed: $id (exit $code). Retry at next sign-in." }
            if ($quiet) { Complete-QuietAppInstall $quiet }
            return
          }
        }
        """;

    public static void Stage(string mount, BuildOptions o)
    {
        var folder = Path.Combine(mount, "ProgramData", "TechLoungeOS");
        Policy.RequireUnlinkedPath(folder);
        if (Directory.Exists(folder)) throw new IOException("Image already contains TechLoungeOS provisioning. Use a fresh source.");
        Directory.CreateDirectory(folder);
        using(var license=typeof(Provisioning).Assembly.GetManifestResourceStream("TechLoungeOS.PeFonts.OFL.txt")??throw new IOException("The setup font licence was not packaged."))
        using(var output=File.Create(Path.Combine(folder,"Manrope-LICENCE.txt")))license.CopyTo(output);
        using(var license=typeof(Provisioning).Assembly.GetManifestResourceStream("TechLoungeOS.SetupFonts.OFL.txt")??throw new IOException("The setup Sora font licence was not packaged."))
        using(var output=File.Create(Path.Combine(folder,"Sora-LICENCE.txt")))license.CopyTo(output);
        WallpaperPresets.Validate(o.WallpaperPreset);
        WallpaperPresets.Stage(folder);
        Wallpaper.Validate(o.WallpaperFile);
        var wallpaperName = o.WallpaperFile == null ? null : "wallpaper" + Path.GetExtension(o.WallpaperFile).ToLowerInvariant();
        if (wallpaperName != null) File.Copy(o.WallpaperFile!, Path.Combine(folder, wallpaperName), false);
        var script=Script(o.Apps, o.GameMode, o.DisableSuggestions, wallpaperName, o.IncludeRuntimePack, o.IncludeStoreXbox, o.IncludeOffice, o.TweaksFolder != null, NvidiaSetup.Enabled(o), o.IncludeAmdIntelGraphics, o.IncludeEdge, true, o.WallpaperFile == null ? o.WallpaperPreset : null, true, true,o.UseCurrentTweaksInstaller,true,true,false);
        File.WriteAllText(Path.Combine(folder, "first-login.ps1"),script);
        var progressSteps=System.Text.RegularExpressions.Regex.Matches(script,@"Invoke-Step '([A-Za-z0-9._-]+)'")
            .Select(match=>match.Groups[1].Value).Where(id=>id is not ("hello-options" or "tweaks-shortcut" or "source-presentation")).Distinct(StringComparer.Ordinal).ToArray();
        File.WriteAllText(Path.Combine(folder,"progress-plan.json"),JsonSerializer.Serialize(new{steps=progressSteps}));
        var startup = Path.Combine(mount, @"Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        Policy.RequireUnlinkedPath(startup);
        Directory.CreateDirectory(startup);
        var entry = Path.Combine(startup, "TechLoungeOS.exe");
        if (File.Exists(entry) || File.Exists(Path.Combine(startup,"TechLoungeOS.cmd"))) throw new IOException("Existing TechLoungeOS startup entry would be overwritten.");
        var bootstrap=Path.Combine(AppContext.BaseDirectory,"FirstLogin.exe");
        SourcePreservation.RequireImageFile(bootstrap,512,1<<20);
        File.Copy(bootstrap,entry,false);
        File.Copy(bootstrap,Path.Combine(folder,"FirstLogin.exe"),false);
        if(o.AnimatedWinPe)
        {
            var accountSetup=Path.Combine(AppContext.BaseDirectory,"AccountSetup.exe");
            SourcePreservation.RequireImageFile(accountSetup,512,1<<20);
            File.Copy(accountSetup,Path.Combine(folder,"AccountSetup.exe"),false);
        }
        if (o.TweaksFolder != null)
        {
            var privateRuntime = HasPrivateRuntime(o.TweaksFolder);
            if(o.UseCurrentTweaksInstaller)
            {
                CopyTree(Path.Combine(o.Workspace,"tweaks-installer"),Path.Combine(folder,"TweaksInstaller"));
                CopyTree(Path.Combine(o.TweaksFolder,"WebView2Runtime"),Path.Combine(folder,"WebView2Runtime"));
            }
            else
            {
                CopyTree(o.TweaksFolder, Path.Combine(folder, "TechLoungeTweaks"));
                if (privateRuntime)
                {
                    Directory.Move(Path.Combine(folder,"TechLoungeTweaks","WebView2Runtime"),Path.Combine(folder,"WebView2Runtime"));
                    File.WriteAllText(Path.Combine(folder, "launch-tweaks.ps1"), PrivateTweaksLauncher);
                }
            }
        }
        WriteHashes(folder);
    }

    public static void WriteHashes(string folder)
    {
        var hashes = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => Path.GetRelativePath(folder, f) != "payload-sha256.json").ToDictionary(
            f => Path.GetRelativePath(folder, f), HashFile);
        File.WriteAllText(Path.Combine(folder, "payload-sha256.json"), JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Read the supplied integration before downloading or mounting anything. Security software
    // may reject a bundled file even when it can be listed or copied from read-only media.
    public static void ValidatePayload(string folder, CancellationToken ct)
    {
        Policy.RequireUnlinkedPath(folder);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            Policy.RequireUnlinkedPath(file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files are not accepted: " + file);
            try { _ = HashFile(file); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 225 or 226)
            {
                throw new IOException("Windows security blocked a file in the TechLoungeTweaks bundle: " + file +
                    ". The build has stopped. Keep security protection enabled and use a compatible trusted bundle, or deselect Tweaks integration.", ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Cannot read the TechLoungeTweaks bundle file: " + file +
                    ". Check file access or choose another bundle before building.", ex);
            }
        }
        foreach (var directory in Directory.EnumerateDirectories(folder)) ValidatePayload(directory, ct);
    }

    public static bool HasPrivateRuntime(string tweaksFolder)
    {
        var runtime = Path.Combine(tweaksFolder, "WebView2Runtime");
        if (!Directory.Exists(runtime)) return false;
        Policy.RequireUnlinkedPath(runtime);
        if (new[] { "msedgewebview2.exe", "msedge.dll", "icudtl.dat", "resources.pak" }.Any(name => !File.Exists(Path.Combine(runtime, name))))
            throw new IOException("The private WebView2Runtime folder is incomplete. Use the complete trusted Microsoft x64 Fixed Version package.");
        return true;
    }

    public static bool HasImageRuntime(string mount) => new[] {
        @"Program Files (x86)\Microsoft\EdgeWebView\Application",
        @"Program Files\Microsoft\EdgeWebView\Application",
        @"Users\Default\AppData\Local\Microsoft\EdgeWebView\Application"
    }.Any(relative => {
        var path = Path.Combine(mount, relative);
        return Directory.Exists(path) && Directory.EnumerateDirectories(path)
            .Any(version => File.Exists(Path.Combine(version, "msedgewebview2.exe")));
    });

    public const string PrivateTweaksLauncher = """
        $ErrorActionPreference = 'Stop'
        Start-Process -FilePath (Join-Path $PSScriptRoot 'FirstLogin.exe') -ArgumentList '--open-tweaks' -WorkingDirectory $PSScriptRoot
        """;

    public static void CopyTree(string source, string target)
    {
        Policy.RequireUnlinkedPath(target);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked paths are not accepted: " + source);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files are not accepted: " + file);
            var dest = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, dest, false);
            File.SetAttributes(dest, FileAttributes.Normal);
        }
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
