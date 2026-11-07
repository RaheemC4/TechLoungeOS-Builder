using System.Text.Json;
using TechLoungeOS;

static class DiscordInstallChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var folder=Path.Combine(root,"discord-installer");Directory.CreateDirectory(folder);
        var script="$fixture="+Policy.Ps(folder)+";\n$ErrorActionPreference='Stop'\n"+AppRecipes.DiscordDatabase+"\n"+"""
            function Seed($name,$version='app-1.0.9267') {
              $fixtureRoot=Join-Path $fixture $name
              $app=Join-Path $fixtureRoot $version
              New-Item $app -ItemType Directory -Force|Out-Null
              $bytes=New-Object byte[] 128
              [Text.Encoding]::ASCII.GetBytes('SQLite format 3').CopyTo($bytes,0)
              $bytes[100]=42
              [IO.File]::WriteAllBytes((Join-Path $app 'installer.db'),$bytes)
              return $fixtureRoot
            }
            $fresh=Seed fresh
            $seed=Join-Path $fresh 'app-1.0.9267\installer.db';$db=Join-Path $fresh 'installer.db'
            $hash=(Get-FileHash $seed).Hash
            Initialize-DiscordDatabase $fresh
            $moved=!(Test-Path $seed) -and (Get-FileHash $db).Hash -eq $hash
            $stamp=(Get-Item $db).LastWriteTimeUtc
            Initialize-DiscordDatabase $fresh
            $retry=(Get-FileHash $db).Hash -eq $hash -and (Get-Item $db).LastWriteTimeUtc -eq $stamp
            $existing=Seed existing
            [IO.File]::WriteAllText((Join-Path $existing 'installer.db'),'existing user database, never replace')
            Initialize-DiscordDatabase $existing
            $kept=[IO.File]::ReadAllText((Join-Path $existing 'installer.db')) -ceq 'existing user database, never replace' -and (Test-Path (Join-Path $existing 'app-1.0.9267\installer.db'))
            $multiple=Seed multiple;[void](Seed multiple app-2.0.0)
            $ambiguous=$false
            try{Initialize-DiscordDatabase $multiple}catch{$ambiguous=$_.Exception.Message -like '*ambiguous*'}
            $bad=Seed bad;[IO.File]::WriteAllBytes((Join-Path $bad 'app-1.0.9267\installer.db'),(New-Object byte[] 128))
            $badFormat=$false
            try{Initialize-DiscordDatabase $bad}catch{$badFormat=$_.Exception.Message -like '*format*'}
            $small=Seed small;[IO.File]::WriteAllBytes((Join-Path $small 'app-1.0.9267\installer.db'),(New-Object byte[] 3))
            $badSize=$false
            try{Initialize-DiscordDatabase $small}catch{$badSize=$_.Exception.Message -like '*size*'}
            $missing=Join-Path $fixture missing;New-Item $missing -ItemType Directory|Out-Null
            $absent=$false
            try{Initialize-DiscordDatabase $missing}catch{$absent=$_.Exception.Message -like '*incomplete*'}
            $target=Seed linked-target
            $linked=Join-Path $fixture linked;New-Item $linked -ItemType Directory|Out-Null
            $junction=Join-Path $linked app-1.0.9267
            New-Item $junction -ItemType Junction -Target (Join-Path $target app-1.0.9267)|Out-Null
            $linkKept=$false
            try{Initialize-DiscordDatabase $linked}catch{$linkKept=$_.Exception.Message -like '*Linked*'}finally{[IO.Directory]::Delete($junction)}
            @{moved=$moved;retry=$retry;existing=$kept;ambiguous=($ambiguous -and !(Test-Path (Join-Path $multiple installer.db)));badFormat=($badFormat -and !(Test-Path (Join-Path $bad installer.db)));badSize=$badSize;missing=$absent;linked=($linkKept -and (Test-Path (Join-Path $target 'app-1.0.9267\installer.db')))}|ConvertTo-Json -Compress
            """;
        using(var doc=JsonDocument.Parse(await new ProcessRunner(_=>{}).PowerShell(script)))
            foreach(var gate in doc.RootElement.EnumerateObject())
                check(gate.Value.GetBoolean(),"Discord database initialisation: "+gate.Name+" (inert files only)");

        var commands=Provisioning.WinGetInstall+"\n"+"""
            $ErrorActionPreference='Stop';$script:calls=@()
            function Get-QuietAppInstall {param($id) return $null}
            function Get-Command {param($Name,$ErrorAction) if($Name -ne 'winget.exe'){throw 'Unexpected command lookup'};[pscustomobject]@{Source='Invoke-WinGetFixture'}}
            function Invoke-WinGetFixture {$script:calls+=,@($args);$script:LASTEXITCODE=0}
            Install-WinGetApp 'Discord.Discord'
            Install-WinGetApp 'Google.Chrome'
            Install-WinGetApp '9PLM9XGG6VKS' msstore
            ConvertTo-Json -InputObject $script:calls -Depth 4 -Compress
            """;
        using var calls=JsonDocument.Parse(await new ProcessRunner(_=>{}).PowerShell(commands));
        var rows=calls.RootElement.EnumerateArray().Select(r=>r.EnumerateArray().Select(v=>v.GetString()).ToArray()).ToArray();
        check(rows.Length==3 && rows[0].TakeLast(2).SequenceEqual(new[]{"--override","--silent"}) && rows.Skip(1).All(r=>!r.Contains("--override")),"only stable Discord receives the verified quiet installer override; other installers retain their catalogue arguments");
        check(rows[0].Contains("--no-upgrade") && rows[0].Contains("--accept-package-agreements") && rows[1].Contains("Google.Chrome.EXE") && rows[2].Contains("msstore"),"quiet Discord keeps WinGet install protection and the existing Chrome/Store routes");
    }
}
