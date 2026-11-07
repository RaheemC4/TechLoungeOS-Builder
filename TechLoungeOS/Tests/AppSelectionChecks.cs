using TechLoungeOS;

static class AppSelectionChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var standard=RecommendedApps.ForBuild([]);
        check(standard.SequenceEqual(new[]{RecommendedApps.HatchZip,RecommendedApps.Shiftd}),"a real build with no optional apps still includes HatchZip and Shiftd");
        var required=RecommendedApps.ForBuild(["VideoLAN.VLC","techlounge.hatchzip","TECHLOUNGE.SHIFTD",RecommendedApps.Shiftd]);
        check(required.SequenceEqual(new[]{"VideoLAN.VLC",RecommendedApps.HatchZip,RecommendedApps.Shiftd}) && required.Count(RecommendedApps.IncludedInIso)==2,"mandatory apps are canonicalized and deduplicated while retaining optional selections");
        check(Provisioning.Script(standard,false,false).Contains("Invoke-Step 'TechLounge.HatchZip'") && AppRecipes.NeedsPayload(standard[0]),"mandatory HatchZip uses the existing verified payload and provisioning recipe");
        var firstLogin=Provisioning.Script(standard,false,false);
        check(firstLogin.Contains("Invoke-Step 'TechLounge.Shiftd'") && firstLogin.Contains("Shiftd.exe") && firstLogin.Contains("-ArgumentList '/S'") && AppRecipes.NeedsPayload(RecommendedApps.Shiftd),"mandatory Shiftd uses its bundled payload and silent first-login installer even with no optional apps");
        check(BootOptimizer.FirstLogin(standard).Contains("TechLounge.Shiftd") && RecommendedApps.IncludedInIso("techlounge.shiftd"),"Boot Optimizer waits for Shiftd and it cannot be treated as an optional selection");
        var shortcutFixture=Path.Combine(root,"hatch-shortcut");Directory.CreateDirectory(shortcutFixture);
        var shortcutScript=AppRecipes.HatchZipShortcut.Replace("[Environment]::GetFolderPath('Desktop')","(Join-Path $fixture desktop)");
        var shortcutResult=await new ProcessRunner(_=>{}).PowerShell("$fixture="+Policy.Ps(shortcutFixture)+"\n"+"""
            $env:LOCALAPPDATA=Join-Path $fixture local
            $desktop=Join-Path $fixture desktop;New-Item $desktop -ItemType Directory|Out-Null
            $exe=Join-Path $env:LOCALAPPDATA 'Programs\HatchZip\hatchzip-gui.exe'
            New-Item (Split-Path $exe) -ItemType Directory -Force|Out-Null;Set-Content $exe 'inert fixture, never executed'
            """+"\n"+shortcutScript+"\n"+shortcutScript+"\n"+"""
            $shell=New-Object -ComObject WScript.Shell
            $path=Join-Path $desktop HatchZip.lnk;$link=$shell.CreateShortcut($path)
            $target=$link.TargetPath;$icon=$link.IconLocation;$folder=$link.WorkingDirectory
            $link.TargetPath=Join-Path $fixture unrelated.exe;$link.Save()
            $before=(Get-FileHash $path).Hash
            $conflict=$false;try{
            """+"\n"+shortcutScript+"\n"+"""
            }catch{$conflict=$_.Exception.Message -like '*kept unchanged*'}
            $retained=(Get-FileHash $path).Hash -ceq $before
            Remove-Item -LiteralPath $path;Remove-Item -LiteralPath $exe
            $missing=$false;try{
            """+"\n"+shortcutScript+"\n"+"""
            }catch{$missing=$_.Exception.Message -like '*could not be located*'}
            @{target=$target;icon=$icon;folder=$folder;conflict=$conflict;retained=$retained;missing=$missing;missingLink=!(Test-Path $path)}|ConvertTo-Json -Compress
            """);
        using(var shortcut=System.Text.Json.JsonDocument.Parse(shortcutResult))
        {
            var s=shortcut.RootElement;var exe=Path.Combine(shortcutFixture,@"local\Programs\HatchZip\hatchzip-gui.exe");
            check(s.GetProperty("target").GetString()==exe&&s.GetProperty("icon").GetString()==exe+",0"&&s.GetProperty("folder").GetString()==Path.GetDirectoryName(exe),"HatchZip creates and reuses a native desktop shortcut with its actual installed icon without launching the app");
            check(s.GetProperty("conflict").GetBoolean()&&s.GetProperty("retained").GetBoolean()&&s.GetProperty("missing").GetBoolean()&&s.GetProperty("missingLink").GetBoolean(),"HatchZip keeps conflicting user shortcuts and reports a missing executable instead of making a broken shortcut");
        }
        var baseline=new BuildOptions("unused.iso",1,"unused-work","unused-output.iso","unused-tool",[],[],true,true,"unused-support"){AnimatedWinPe=true,UseCurrentTweaksInstaller=true};
        foreach(var invalid in new[]{baseline with{IncludeOffice=false},baseline with{IncludeEdge=false},baseline with{IncludeRuntimePack=false},baseline with{IncludeStoreXbox=false},baseline with{AnimatedWinPe=false},baseline with{GameMode=false},baseline with{DisableSuggestions=false},baseline with{UseCurrentTweaksInstaller=false},baseline with{IncludeNvidiaSetup=false},baseline with{IncludeAmdIntelGraphics=false}})
        {
            try{await new BuildEngine(_=>throw new Exception("A disabled mandatory feature reached processing")).Build(invalid,CancellationToken.None);throw new Exception("Disabled mandatory feature accepted");}
            catch(InvalidOperationException error){check(error.Message.Contains("required"),"real builds reject disabling a mandatory feature before any processing");}
        }
        var expected=new[]{RecommendedApps.Discord,"Brave.Brave","Google.Chrome","Valve.Steam","Guru3D.Afterburner","Stremio.Stremio","Proton.ProtonVPN",RecommendedApps.ChatGPT,"REALiX.HWiNFO"};
        var preset=RecommendedApps.ApplyPreset([]);
        check(preset.Select(a=>a.Id).SequenceEqual(expected),"recommended preset selects the nine requested applications including HWiNFO64");
        var merged=RecommendedApps.ApplyPreset([new("VLC","VideoLAN.VLC","1"),new("Old Discord label","discord.discord","1")]);
        check(merged.Length==10&&merged.Any(a=>a.Id=="VideoLAN.VLC")&&merged.Count(a=>RecommendedApps.IsDiscord(a.Id))==1&&merged.Single(a=>RecommendedApps.IsDiscord(a.Id)).Name=="Discord","preset retains other choices and canonicalizes Discord without a duplicate or custom display label");
        check(RecommendedApps.ApplyPreset(merged).Select(a=>a.Id).SequenceEqual(merged.Select(a=>a.Id)),"repeated preset selection is idempotent");
        foreach(var alternate in new[]{"Discord.Discord.PTB","Discord.Discord.Canary","Discord.Discord.Development","Discord.Discord.arm64","discord.discord.ptb","BetterDiscord.BetterDiscord","Vencord.Vesktop"})
        {
            check(!Policy.ValidApps([alternate])&&RecommendedApps.Selectable([new("Discord alternative",alternate,"1")]).Length==0,"alternate Discord cannot enter search selection or build policy: "+alternate);
            try{Provisioning.Script([alternate],false,false);throw new Exception("Expected Discord rejection");}
            catch(InvalidOperationException){check(true,"direct provisioning also rejects the alternate Discord edition");}
        }
        foreach(var stable in new[]{"Discord.Discord","discord.discord",RecommendedApps.Discord,RecommendedApps.Discord.ToLowerInvariant()})
        {
            var script=Provisioning.Script([stable],false,false);
            check(AppRecipes.NeedsPayload(stable)&&script.Contains("Invoke-Step 'TechLounge.DiscordOpenAsar'")&&script.Contains("openasar.asar")&&script.Contains("DiscordLauncher.exe"),"every stable Discord selection uses OpenAsar and the repair launcher: "+stable);
        }
        check(Policy.ValidApps(expected)&&!Policy.ValidApps([RecommendedApps.Discord,"Discord.Discord"]),"recommended identifiers pass policy and plain/custom Discord duplicates remain rejected");
        var folder=Path.Combine(root,"chatgpt-no-offline-payload");
        await AppRecipes.Prepare([RecommendedApps.ChatGPT],folder,CancellationToken.None);
        check(!Directory.Exists(folder)&&AppRecipes.Custom(RecommendedApps.ChatGPT)&&!AppRecipes.NeedsPayload(RecommendedApps.ChatGPT),"ChatGPT queues its official Store install without downloading an unrelated offline installer");
        var generated=Provisioning.Script(expected,false,false);
        check(generated.Contains("Install-WinGetApp '9PLM9XGG6VKS' 'msstore'")&&!generated.Contains("Install-WinGetApp 'OpenAI.ChatGPT'"),"ChatGPT uses the OpenAI-documented official Store product rather than a similarly named community package");
        var chatHeader="""
            $ErrorActionPreference='Stop';$script:registered=$false;$script:staged=$true;$script:installs=0;$script:registrations=0;$script:link=$null
            function Invoke-Step {param($Name,$Action) & $Action}
            function Get-AppxPackage {param($Name,$ErrorAction) if($Name -ne 'OpenAI.Codex'){throw 'Unexpected app'};if($script:registered){[pscustomobject]@{PackageFullName='official-fixture';PackageFamilyName='OpenAI.Codex_2p2nqsd0c76g0';InstallLocation='C:\Fixture'}}}
            function Add-AppxPackage {param([switch]$RegisterByFamilyName,$MainPackage,$ErrorAction) if(!$RegisterByFamilyName -or $MainPackage -cne 'OpenAI.Codex_2p2nqsd0c76g0'){throw 'Unexpected registration target'};if(!$script:staged){throw 'Package not staged'};$script:registered=$true;$script:registrations++}
            function Install-WinGetApp {param($Id,$Source) if($Id -ne '9PLM9XGG6VKS' -or $Source -ne 'msstore'){throw 'Unexpected installer'};$script:installs++;$script:staged=$true}
            function Get-AppxPackageManifest {param($Package,$ErrorAction) [pscustomobject]@{Package=@{Applications=@{Application=@([pscustomobject]@{VisualElements=@{};Executable='ChatGPT.exe';Id='App'})}}}}
            function New-Object {param($ComObject)
              if($ComObject -ne 'WScript.Shell'){throw 'Unexpected COM object'};$shell=[pscustomobject]@{}
              $shell|Add-Member ScriptMethod CreateShortcut {param($Path) $link=[pscustomobject]@{TargetPath='';Arguments='';IconLocation=''};$link|Add-Member ScriptMethod Save {$script:link=$this.Arguments};$link};$shell
            }
            """;
        var chatResult=await new ProcessRunner(_=>{}).PowerShell(chatHeader+"\n"+AppRecipes.ChatGPTScript+"\n$first=@($script:installs,$script:registrations,$script:link);$script:registered=$false;$script:staged=$false;\n"+AppRecipes.ChatGPTScript+"\n@{first=$first;installs=$script:installs;registrations=$script:registrations;link=$script:link}|ConvertTo-Json -Compress");
        using(var chat=System.Text.Json.JsonDocument.Parse(chatResult)){
            var c=chat.RootElement;
            check(c.GetProperty("first")[0].GetInt32()==0&&c.GetProperty("first")[1].GetInt32()==1&&c.GetProperty("first")[2].GetString()==@"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App","an additional user registers the already staged official ChatGPT package and gets a shortcut without reinstalling or requesting elevation");
            check(c.GetProperty("installs").GetInt32()==1&&c.GetProperty("registrations").GetInt32()==2,"a fresh profile without staged ChatGPT requests its normal official Store install and verifies user registration afterwards");
        }
        var path=Path.Combine(root,"recommended-preset.ps1");File.WriteAllText(path,generated);
        await new ProcessRunner(_=>{}).PowerShell("$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile("+Policy.Ps(path)+",[ref]$tokens,[ref]$errors);if($errors){throw ($errors|Out-String)}");
        check(true,"the complete recommended-app first-login script parses in Windows PowerShell 5.1");
    }
}
