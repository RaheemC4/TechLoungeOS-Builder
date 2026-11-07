using System.IO.Compression;
using System.Text.Json;
using TechLoungeOS;

static class TweaksInstallerChecks
{
    public static void Run(string root, Action<bool,string> check)
    {
        var omitted=new BuildOptions("not-read.iso",1,Path.Combine(root,"tweaks-required"),"not-created.iso","not-run.exe",[],[],true,true,null){AnimatedWinPe=true,UseCurrentTweaksInstaller=true};
        try{new BuildEngine(_=>throw new Exception("No command may run")).Build(omitted,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Expected mandatory Tweaks rejection");}
        catch(InvalidOperationException ex) when(ex.Message.StartsWith("TechLoungeTweaks integration is required.",StringComparison.Ordinal))
        {check(!Directory.Exists(omitted.Workspace),"production builds cannot silently omit Tweaks; missing support is rejected before commands or workspace creation");}
        var registry=@"Software\TechLoungeOS-RuntimeFixture-"+Guid.NewGuid().ToString("N");
        try
        {
            using var machine=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registry+@"\machine");
            using var user=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registry+@"\user");
            const string policy=@"Software\Policies\Microsoft\Edge\WebView2\";
            TechLoungeOS.AccountSetup.RuntimePolicy.Prepare(machine,user,@"C:\Runtime",@"C:\User\Data");
            TechLoungeOS.AccountSetup.RuntimePolicy.Prepare(machine,user,@"C:\Runtime",@"C:\User\Data");
            using(var browser=user.OpenSubKey(policy+"BrowserExecutableFolder"))
                check((string?)browser!.GetValue("TechLoungeTweaks.exe")==@"C:\Runtime" && browser.GetValueNames().SequenceEqual(new[]{"TechLoungeTweaks.exe"}),"elevated account preparation writes only the executable-specific runtime policy and can repeat safely");
            using(var conflicting=machine.CreateSubKey(policy+"UserDataFolder"))conflicting.SetValue("TechLoungeTweaks.exe",@"C:\Managed\Data");
            user.DeleteSubKeyTree(policy+"BrowserExecutableFolder");
            try{TechLoungeOS.AccountSetup.RuntimePolicy.Prepare(machine,user,@"C:\Runtime",@"C:\User\Data");throw new Exception("Expected policy conflict");}
            catch(IOException){using var unchanged=user.OpenSubKey(policy+"BrowserExecutableFolder");check(unchanged==null,"runtime policy conflicts are checked before any missing key is created");}
        }
        finally{Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registry,false);}
        const string version="2026.10.23.174632";
        var exe=new byte[2048];exe[0]=77;exe[1]=90;
        var innerHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(exe));
        var revision=new string('a',64);
        var zip=Path.Combine(root,"tweaks-installer-check.zip");
        void WriteZip(bool unexpected=false,string? innerVersion=null)
        {
            if(File.Exists(zip))File.Delete(zip);
            using var archive=ZipFile.Open(zip,ZipArchiveMode.Create);
            using(var output=archive.CreateEntry("TechLoungeTweaks-Setup.exe").Open())output.Write(exe);
            using(var text=new StreamWriter(archive.CreateEntry("installer.json").Open()))text.Write(JsonSerializer.Serialize(new{schema=2,app_revision=revision,version=innerVersion??version,sha256=innerHash}));
            using(var text=new StreamWriter(archive.CreateEntry(unexpected?"../INSTALL.txt":"INSTALL.txt").Open()))text.Write("Instructions");
        }
        TweaksInstaller.Release Release(string? url=null)
        {
            var hash=RuntimePack.Hash(zip).ToLowerInvariant();var tag=$"app-{version}-{hash[..12]}";
            return TweaksInstaller.ParseRelease(JsonSerializer.Serialize(new{schema=2,version,distribution="installer",download_url=url??$"https://github.com/RaheemC4/tech-tips/releases/download/{tag}/TechLoungeTweaks.zip",files=new Dictionary<string,string>{{"TechLoungeTweaks/TechLoungeTweaks.zip",hash}},app_revision=revision,release_tag=tag}));
        }
        WriteZip();var release=Release();var folder=Path.Combine(root,"tweaks-verified");
        var package=TweaksInstaller.Extract(zip,folder,release);
        check(package.Version==version && File.ReadAllBytes(Path.Combine(folder,"TechLoungeTweaks-Setup.exe")).SequenceEqual(exe),"verified upstream installer bytes and version are retained without execution or repackaging");
        check(Directory.EnumerateFiles(folder).Count()==2,"only the installer and its verified manifest are staged");
        void Reject(Action operation,string label){try{operation();throw new Exception("Expected rejection: "+label);}catch(IOException){check(true,label);}}
        var published=TweaksInstaller.ParseRelease(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","tweaks-release-2026-10-23.json")));
        check(published.Version==version && published.Sha256=="7f42269fa18526ee14ff4f804bc3836dc57fd2e1575d10bdb2a7e80e6101a982" && published.Revision=="5dd289fe8c825fe17d57facf527d80abebcec0ec22ca3310ef28980d992d5b19","published schema2 Tweaks release parses its real snake-case fields and full archive hash from files");
        foreach(var invalid in new[]{"{}","null","{\"schema\":3}","{\"schema\":\"2\"}","{\"schema\":2,\"version\":null}","{bad json"})
            Reject(()=>TweaksInstaller.ParseRelease(invalid),"missing, changed or malformed release metadata produces an actionable error before downloading");
        Reject(()=>TweaksInstaller.Prepare(folder,CancellationToken.None).GetAwaiter().GetResult(),"reused preparation folders are rejected before downloads and existing verified payloads remain intact");
        check(RuntimePack.Hash(Path.Combine(folder,"TechLoungeTweaks-Setup.exe"))==innerHash,"rejected preparation retry retains the previously verified installer bytes");
        Reject(()=>Release("https://github.com/other/project/releases/download/x/TechLoungeTweaks.zip"),"unrelated installer repositories are rejected");
        Reject(()=>TweaksInstaller.Extract(zip,Path.Combine(root,"bad-digest"),release with{Sha256=new string('0',64)}),"corrupt outer archive is rejected before extraction");
        WriteZip(true);Reject(()=>TweaksInstaller.Extract(zip,Path.Combine(root,"bad-path"),Release()),"unexpected or traversing ZIP entries are rejected");
        WriteZip(innerVersion:"2025.01.01.010101");Reject(()=>TweaksInstaller.Extract(zip,Path.Combine(root,"bad-version"),Release()),"inner installer version must match the current release");
        var generated=Provisioning.Script([],false,false,currentTweaks:true);
        var script=Path.Combine(root,"tweaks-current-first-login.ps1");File.WriteAllText(script,generated);
        var run=new ProcessRunner(_=>{});
        run.PowerShell("$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile("+Policy.Ps(script)+",[ref]$tokens,[ref]$errors);if($errors){throw ($errors|Out-String)}").GetAwaiter().GetResult();
        check(true,"complete first-login script with current Tweaks installation parses in Windows PowerShell 5.1");
        var fixture=Path.Combine(root,"tweaks-installed-user");Directory.CreateDirectory(fixture);
        var header=$$$"""
            $ErrorActionPreference='Stop'
            $env:ProgramData={{{Policy.Ps(fixture)}}};$env:LOCALAPPDATA={{{Policy.Ps(fixture)}}}
            $script:root=Join-Path $env:LOCALAPPDATA managed
            $payload=Join-Path $env:ProgramData 'TechLoungeOS\TweaksInstaller'
            [void][IO.Directory]::CreateDirectory($payload)
            [IO.File]::WriteAllText((Join-Path $payload TechLoungeTweaks-Setup.exe),'fixture, never execute')
            @{Version='2026.10.23.174632';Sha256=(Get-FileHash (Join-Path $payload TechLoungeTweaks-Setup.exe)).Hash}|ConvertTo-Json|Set-Content (Join-Path $payload installer.json)
            [void][IO.Directory]::CreateDirectory((Join-Path $env:ProgramData 'TechLoungeOS\WebView2Runtime'))
            [IO.File]::WriteAllText((Join-Path $env:ProgramData 'TechLoungeOS\WebView2Runtime\msedgewebview2.exe'),'fixture')
            [void][IO.Directory]::CreateDirectory((Join-Path $script:root 'versions\2026.10.24.010101'))
            foreach($name in @('TechLounge.exe','versions\2026.10.24.010101\TechLoungeTweaks.exe')){[IO.File]::WriteAllText((Join-Path $script:root $name),'fixture')}
            [IO.File]::WriteAllText((Join-Path $script:root current.txt),'2026.10.24.010101')
            $script:writes=@();$script:conflict=$false
            function Invoke-Step {param($name,$action) & $action}
            function Get-ItemProperty {param($LiteralPath,$Path,$Name,$ErrorAction)
              if($Path -eq 'HKCU:\Software\TechLoungeTweaks' -or $LiteralPath -eq 'HKCU:\Software\TechLoungeTweaks'){return @{InstallRoot=$script:root;Startup='1'}}
              if($script:conflict){return @{'TechLoungeTweaks.exe'='existing managed policy'}}
              if($script:shared -and $LiteralPath -eq 'HKLM:\Software\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder'){return @{'TechLoungeTweaks.exe'=(Join-Path $env:ProgramData 'TechLoungeOS\WebView2Runtime')}}
              if($script:prepared -and $LiteralPath -like 'HKCU:\Software\Policies\Microsoft\Edge\WebView2\*') {
                $value=if($LiteralPath.EndsWith('BrowserExecutableFolder')){Join-Path $env:ProgramData 'TechLoungeOS\WebView2Runtime'}else{Join-Path $env:LOCALAPPDATA 'TechLoungeOS\WebView2'}
                return @{'TechLoungeTweaks.exe'=$value}
              }
            }
            function New-Item {param($Path,[switch]$Force) if($Path -notlike 'HKCU:\Software\Policies\Microsoft\Edge\WebView2\*'){throw 'Unexpected registry key'}}
            function New-ItemProperty {param($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force) $script:writes+=@{Path=$LiteralPath;Name=$Name;Value=$Value}}
            function Start-Process {throw 'Already-current installation must not execute an installer'}
            function New-Object {param($ComObject)
              if($ComObject -ne 'WScript.Shell'){throw 'Unexpected COM request'}
              $shell=[pscustomobject]@{}
              $shell|Add-Member ScriptMethod CreateShortcut {param($path)
                $link=[pscustomobject]@{TargetPath='';WorkingDirectory='';IconLocation=''}
                $link|Add-Member ScriptMethod Save {[IO.File]::WriteAllText((Join-Path $script:root 'saved-link.txt'),$this.TargetPath)}
                return $link
              };return $shell
            }
            """;
        var worker=TweaksInstaller.FirstLogin;
        var sharedRuntime="""
            $script:shared=$true
            function New-Item {throw 'Ordinary provisioning cannot write policy keys'}
            """;
        run.PowerShell(header+"\n"+sharedRuntime+"\n"+worker+"\nif($script:writes.Count -ne 0){throw 'User policy must not be written'};if([IO.File]::ReadAllText((Join-Path $script:root saved-link.txt)) -ne (Join-Path $script:root TechLounge.exe)){throw 'Managed updater launcher not used'}").GetAwaiter().GetResult();
        check(true,"a newer managed installation and updater launcher are retained using only the shared app runtime, without forcing a user-data override or writing user policies");
        run.PowerShell(header+"\n$script:prepared=$true\nfunction New-Item {throw 'A normal user must not recreate an already matching protected policy key'}\n"+worker+"\nif($script:writes.Count){throw 'Matching protected policies were rewritten'}").GetAwaiter().GetResult();
        check(true,"normal first-login installation reads matching protected policy values without attempting registry writes");
        using(var health=JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture,@"TechLoungeOS\tweaks-compatibility.json"))))
            check(health.RootElement.GetProperty("corePresent").GetBoolean()&&!health.RootElement.GetProperty("antivirusExceptionsAdded").GetBoolean()&&health.RootElement.GetProperty("missingAdvancedTools").GetArrayLength()==4,"missing advanced tools are reported separately without claiming complete functionality or adding antivirus exceptions");
        try{run.PowerShell(header+"\n$script:conflict=$true\n"+worker).GetAwaiter().GetResult();throw new Exception("Expected policy conflict rejection");}
        catch(IOException){check(true,"an existing different WebView2 policy stops setup before changing policy or running the installer");}
        try{run.PowerShell(header+"\n"+worker).GetAwaiter().GetResult();throw new Exception("Expected missing-runtime rejection");}
        catch(IOException error)when(error.Message.Contains("not registered")){check(true,"a missing shared or user runtime policy fails with a recovery instruction and no automatic elevation");}
        var isolated=@"Software\TechLoungeOS-RuntimeWorker-"+Guid.NewGuid().ToString("N");
        try{
            var runtimeFixture=TweaksRuntime.Worker+"\n$machine=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("+Policy.Ps(isolated+@"\machine")+");\n"+"""
                Set-TweaksRuntimePolicy $machine 'C:\Protected\WebView2'
                $path='Software\Policies\Microsoft\Edge\WebView2\'
                $key=$machine.OpenSubKey($path+'BrowserExecutableFolder',$true)
                if($key.GetValue('TechLoungeTweaks.exe') -cne 'C:\Protected\WebView2' -or @($key.GetValueNames()).Count -ne 1){throw 'Runtime scope differs'}
                $key.SetValue('TechLoungeTweaks.exe','C:\Protected\WebView2',[Microsoft.Win32.RegistryValueKind]::ExpandString);$key.Dispose()
                Set-TweaksRuntimePolicy $machine 'C:\Protected\WebView2'
                $key=$machine.OpenSubKey($path+'BrowserExecutableFolder');if($key.GetValueKind('TechLoungeTweaks.exe') -ne [Microsoft.Win32.RegistryValueKind]::ExpandString){throw 'Matched policy type changed'};$key.Dispose()
                if($machine.OpenSubKey($path+'UserDataFolder')){throw 'Machine policy must not share user data'}
                $blocked=$false;try{Set-TweaksRuntimePolicy $machine 'C:\Different'}catch{$blocked=$true}
                $key=$machine.OpenSubKey($path+'BrowserExecutableFolder');if(!$blocked -or $key.GetValue('TechLoungeTweaks.exe') -cne 'C:\Protected\WebView2'){throw 'Conflicting runtime policy changed'};$key.Dispose();$machine.Dispose()
                """;
            run.PowerShell(runtimeFixture).GetAwaiter().GetResult();
            check(true,"machine runtime preparation writes only the Tweaks executable path, retains matching policy types, rejects conflicts and never sets machine user-data policy");
        }finally{Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(isolated,false);}
    }
}
