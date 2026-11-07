using System.Text.Json;
using TechLoungeOS;

static class QuietAppChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var folder=Path.Combine(root,"quiet-apps");Directory.CreateDirectory(folder);
        var script="$fixture="+Policy.Ps(folder)+";"+QuietApps.Functions+"\n"+"""
            $env:LOCALAPPDATA=Join-Path $fixture local
            $env:ProgramFiles=Join-Path $fixture pf
            ${env:ProgramFiles(x86)}=Join-Path $fixture pfx86
            $state=Join-Path $fixture state;New-Item $state -ItemType Directory -Force|Out-Null
            $script:after=$false;$script:removed=@();$script:stopped=@();$script:saved=$null;$script:saves=0;$script:showNewStartup=$true;$script:existingShortcut=$false
            function Test-Path {param($LiteralPath)
              if($script:conflict -and $LiteralPath -eq (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Steam.lnk')){return $true}
              if($LiteralPath -like 'HK*'){return $LiteralPath.StartsWith('HKCU:')}
              if($LiteralPath -in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('CommonDesktopDirectory'))){return $true}
              Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
            }
            function Get-ItemProperty {param($LiteralPath)
              $o=[pscustomobject]@{ExistingSteam='"'+$env:LOCALAPPDATA+'\Steam\steam.exe" -silent'}
              if($script:after){if($script:showNewStartup){$o|Add-Member NoteProperty NewSteam ('"'+$env:LOCALAPPDATA+'\Steam\steam.exe" -silent')};$o|Add-Member NoteProperty Other 'C:\Unrelated\app.exe'}
              return $o
            }
            function Get-Process {param($Id)
              if($Id){return [pscustomobject]@{SessionId=7}}
              $items=@(@(100,7,($env:LOCALAPPDATA+'\Steam\steam.exe')))
              if($script:after){$items+=,@(101,7,($env:LOCALAPPDATA+'\Steam\steam.exe'));$items+=,@(102,7,'C:\Unrelated\app.exe');$items+=,@(103,8,($env:LOCALAPPDATA+'\Steam\steam.exe'))}
              foreach($i in $items){$o=[pscustomobject]@{Id=$i[0];SessionId=$i[1];Path=$i[2];MainWindowHandle=1};$o|Add-Member ScriptMethod CloseMainWindow {$false};$o|Add-Member ScriptMethod WaitForExit {param($Milliseconds) $false};$o}
            }
            function Stop-Process {param($Id,$ErrorAction) $script:stopped+= $Id}
            function Remove-ItemProperty {param($LiteralPath,$Name,$ErrorAction) $script:removed+=$Name;if($Name -eq 'NewSteam'){$script:showNewStartup=$false}}
            function Get-ChildItem {param($LiteralPath,$Filter,[switch]$File,[switch]$Force,$ErrorAction) if(!$Force){throw 'Public desktop requires hidden-folder support'};if($script:existingShortcut){[pscustomobject]@{FullName=(Join-Path $fixture 'ExistingSteam.lnk')}}}
            function New-Object {param($ComObject)
              if($ComObject -ne 'WScript.Shell'){throw 'Unexpected COM request'}
              $shell=[pscustomobject]@{}
              $shell|Add-Member ScriptMethod CreateShortcut {param($Path)
                $link=[pscustomobject]@{TargetPath='';WorkingDirectory='';IconLocation=''}
                if($Path -eq (Join-Path $fixture 'ExistingSteam.lnk')){$link.TargetPath=$env:LOCALAPPDATA+'\Steam\steam.exe'}
                $link|Add-Member ScriptMethod Save {$script:saves++;$script:saved=@{target=$this.TargetPath;folder=$this.WorkingDirectory;icon=$this.IconLocation}}
                return $link
              };return $shell
            }
            $app=Get-QuietAppInstall 'Valve.Steam'
            if(!$app){throw 'Expected new-install snapshot'}
            New-Item (Split-Path $app.Paths[0]) -ItemType Directory -Force|Out-Null
            Set-Content -LiteralPath $app.Paths[0] 'inert fixture, never executed'
            $script:after=$true
            Complete-QuietAppInstall $app
            $existing=Get-QuietAppInstall 'Valve.Steam'
            $beforeExisting=@($script:removed.Count,$script:stopped.Count,$script:saves)
            Complete-QuietAppInstall $existing
            $existingSafe=($script:removed.Count -eq $beforeExisting[0] -and $script:stopped.Count -eq $beforeExisting[1] -and $script:saves -eq ($beforeExisting[2]+1))
            $unknown=Get-QuietAppInstall 'Vendor.Unknown'
            Remove-Item -LiteralPath (Join-Path $state 'Valve.Steam.quiet-complete')
            $retry=Get-QuietAppInstall 'Valve.Steam'
            $script:existingShortcut=$true
            Complete-QuietAppInstall $retry
            $script:existingShortcut=$false;$script:conflict=$true;$conflictKept=$false
            try{Complete-QuietAppInstall $existing}catch{$conflictKept=$_.Exception.Message -like '*kept unchanged*'}
            $result=@{removed=$script:removed;stopped=$script:stopped;shortcut=$script:saved;saves=$script:saves;conflictKept=$conflictKept;existingSafe=$existingSafe;existingRetained=$existing.Existing;unknownSkipped=($null -eq $unknown);retryRetainsOriginal=($retry.Run.ContainsKey('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run|ExistingSteam') -and !$retry.Run.ContainsKey('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run|NewSteam'));retryRetainsRunning=(101 -in $retry.Pids);backup=(Get-Content (Join-Path $state 'Valve.Steam.startup-backup.jsonl') -Raw|ConvertFrom-Json)}
            $result|ConvertTo-Json -Depth 5
            """;
        var output=await new ProcessRunner(_=>{}).PowerShell(script);
        using var result=JsonDocument.Parse(output);var r=result.RootElement;
        check(r.GetProperty("removed").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(new[]{"NewSteam"}) && r.GetProperty("backup").GetProperty("name").GetString()=="NewSteam","quiet install backs up/removes only its new startup value, retaining existing and unrelated startup choices");
        check(r.GetProperty("stopped").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{101}),"quiet install closes only a newly created matching app process in this session, retaining prior apps and other sessions");
        check(r.GetProperty("shortcut").GetProperty("target").GetString()==Path.Combine(folder,@"local\Steam\steam.exe") && r.GetProperty("shortcut").GetProperty("icon").GetString()==Path.Combine(folder,@"local\Steam\steam.exe"),"quiet install creates a normal executable desktop shortcut with the installed app icon without opening the app");
        check(r.GetProperty("existingRetained").GetBoolean() && r.GetProperty("existingSafe").GetBoolean() && r.GetProperty("unknownSkipped").GetBoolean(),"an existing selected app gets its missing desktop shortcut without closing processes or changing startup; unknown installers remain untouched");
        check(r.GetProperty("retryRetainsOriginal").GetBoolean()&&r.GetProperty("retryRetainsRunning").GetBoolean(),"interrupted cleanup retries its original startup snapshot while protecting apps already running at the retry");
        check(r.GetProperty("saves").GetInt32()==2,"an existing matching installer desktop shortcut is reused instead of creating another icon");
        check(r.GetProperty("conflictKept").GetBoolean(),"a same-named shortcut to another program is preserved and reported instead of overwritten");
    }
}
