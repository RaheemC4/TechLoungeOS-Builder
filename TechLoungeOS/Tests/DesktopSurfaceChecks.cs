using System.Text.Json;
using TechLoungeOS;

static class DesktopSurfaceChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"desktop-surface");Directory.CreateDirectory(folder);
        var result=await new ProcessRunner(_=>{}).PowerShell("$ErrorActionPreference='Stop'\n"+DesktopSurface.Functions+"\n"+$$"""
            $root={{Policy.Ps(folder)}}
            $desktop=Join-Path $root 'Desktop';$backup=Join-Path $root 'Backup'
            New-Item -Path $desktop -ItemType Directory|Out-Null
            $exe=Join-Path $root 'cpuz.exe';[IO.File]::WriteAllText($exe,'Inert fixture, never executed')
            $other=Join-Path $root 'notes.txt';[IO.File]::WriteAllText($other,'Unrelated file')
            $shell=New-Object -ComObject WScript.Shell
            $shortcut=Join-Path $desktop 'CPUID CPU-Z.lnk'
            $link=$shell.CreateShortcut($shortcut);$link.TargetPath=$exe;$link.Save()
            $keep=Join-Path $desktop 'CPU-Z notes.lnk'
            $link=$shell.CreateShortcut($keep);$link.TargetPath=$other;$link.Save()
            [IO.File]::WriteAllText((Join-Path $desktop 'unrelated-broken.lnk'),'Not a shortcut')
            $hash=(Get-FileHash -LiteralPath $shortcut).Hash
            Move-CpuZDesktopShortcuts $desktop $backup $shell
            Move-CpuZDesktopShortcuts $desktop $backup $shell
            $item=New-Object PSObject
            $item|Add-Member NoteProperty Calls 0
            $item|Add-Member ScriptMethod InvokeVerb {param($verb) if($verb -ne 'taskbarunpin'){throw 'Wrong Store action'};$this.Calls++}
            $apps=New-Object PSObject;$apps|Add-Member NoteProperty Item $item
            $apps|Add-Member ScriptMethod ParseName {param($name) if($name -ne 'Microsoft.WindowsStore_8wekyb3d8bbwe!App'){throw 'Wrong app'};$this.Item}
            $api=New-Object PSObject;$api|Add-Member NoteProperty Apps $apps
            $api|Add-Member ScriptMethod Namespace {param($name) if($name -ne 'shell:AppsFolder'){throw 'Wrong namespace'};$this.Apps}
            Unpin-DesktopMicrosoftStore $api
            Unpin-DesktopMicrosoftStore $api
            $apps.Item=$null;$storeDeferred=$false
            Unpin-DesktopMicrosoftStore $api $false
            try{Unpin-DesktopMicrosoftStore $api $true}catch{$storeDeferred=$true}
            $blocked=Join-Path $desktop 'another CPU-Z.lnk'
            $link=$shell.CreateShortcut($blocked);$link.TargetPath=$exe;$link.Description='Conflict fixture';$link.Save()
            $blockedHash=(Get-FileHash -LiteralPath $blocked).Hash
            [IO.File]::WriteAllText((Join-Path $backup ($blockedHash+'.lnk')),'Wrong existing backup')
            $refused=$false
            try{Move-CpuZDesktopShortcuts $desktop $backup $shell}catch{$refused=$true}
            [pscustomobject]@{
              removed=!(Test-Path -LiteralPath $shortcut)
              executableUnchanged=([IO.File]::ReadAllText($exe) -eq 'Inert fixture, never executed')
              unrelatedRetained=((Test-Path -LiteralPath $keep) -and (Test-Path -LiteralPath (Join-Path $desktop 'unrelated-broken.lnk')))
              backupMatches=((Get-FileHash -LiteralPath (Join-Path $backup ($hash+'.lnk'))).Hash -eq $hash)
              backups=@(Get-ChildItem $backup -Filter ($hash+'.lnk')).Count
              storeUnpinCalls=$item.Calls
              expectedStoreDeferred=$storeDeferred
              conflictingBackupRefused=($refused -and (Test-Path -LiteralPath $blocked))
            }|ConvertTo-Json -Compress
            """);
        using var data=JsonDocument.Parse(result);var r=data.RootElement;
        check(r.GetProperty("removed").GetBoolean() && r.GetProperty("executableUnchanged").GetBoolean(),"CPU-Z desktop cleanup removes its real shortcut while retaining its executable");
        check(r.GetProperty("unrelatedRetained").GetBoolean(),"CPU-Z cleanup retains unrelated and broken shortcuts");
        check(r.GetProperty("backupMatches").GetBoolean() && r.GetProperty("backups").GetInt32()==1,"repeated cleanup preserves one exact recoverable shortcut backup");
        check(r.GetProperty("storeUnpinCalls").GetInt32()==2,"Store cleanup targets only its language-neutral taskbar-unpin action without uninstalling it");
        check(r.GetProperty("expectedStoreDeferred").GetBoolean(),"pending Store registration defers cleanup instead of recording a false success");
        check(r.GetProperty("conflictingBackupRefused").GetBoolean(),"an invalid existing backup stops cleanup before deleting the source shortcut");
        check(DesktopDefaults.Settings.Count(s=>s.Name=="{20D04FE0-3AEA-1069-A2D8-08002B30309D}" && s.Value==1)==2,"This PC is hidden for modern and classic desktop namespace views");
        check(DesktopDefaults.Settings.Single(s=>s.Name=="NoNewAppAlert").Value==1,"new browser installs retain the selected default without Windows new-association notifications");
    }
}
