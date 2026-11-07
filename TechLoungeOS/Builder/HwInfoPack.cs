using System.Text.Json;

namespace TechLoungeOS;

// Its signed installer launches an elevated GUI even with /VERYSILENT.
// Install in the existing pre-desktop setup phase, without elevating user provisioning.
public static class HwInfoPack
{
    public record Installer(string Version,string Sha256);
    public static bool Is(string id)=>id.Equals("REALiX.HWiNFO",StringComparison.OrdinalIgnoreCase);

    public static async Task Prepare(ICommandRunner run,string folder,CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var download=Path.Combine(folder,"download");Directory.CreateDirectory(download);
        await run.Run("winget.exe",["download","--id","REALiX.HWiNFO","--exact","--source","winget","--architecture","x64",
            "--skip-dependencies","--skip-license","--download-directory",download,"--accept-source-agreements","--accept-package-agreements","--disable-interactivity"],ct);
        var files=Directory.GetFiles(download,"*.exe",SearchOption.AllDirectories);
        if(files.Length!=1)throw new IOException("Expected one checksum-verified HWiNFO installer.");
        var version=RuntimePack.DownloadVersion(Path.GetFileName(files[0]));
        var target=Path.Combine(folder,"HWiNFO.exe");File.Move(files[0],target);
        await run.PowerShell(Signature(Policy.Ps(target)),ct);
        File.WriteAllText(Path.Combine(folder,"installer.json"),JsonSerializer.Serialize(new Installer(version,RuntimePack.Hash(target))));
        File.WriteAllText(Path.Combine(folder,"install.ps1"),Script);
    }

    private static string Signature(string path)=>"$signature=Get-AuthenticodeSignature -LiteralPath "+path+"\n"+
        "if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ne 'REALiX, s.r.o.'){throw 'HWiNFO does not have a valid REALiX publisher signature.'}\n";

    public static string Script=>"""
        $ErrorActionPreference='Stop'
        $payload=$PSScriptRoot
        if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'HWiNFO setup requires administrator permission.'}
        $lock=[IO.File]::Open("$payload\install.lock",'OpenOrCreate','ReadWrite','None')
        $transcribing=$false
        try{
          Start-Transcript -Path "$payload\install.log" -Append;$transcribing=$true
          $exe=Join-Path $env:ProgramFiles 'HWiNFO64\HWiNFO64.exe'
          if(!(Test-Path -LiteralPath $exe)){
            $item=Get-Content "$payload\installer.json" -Raw|ConvertFrom-Json
            $installer=Join-Path $payload HWiNFO.exe
            if((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $item.Sha256){throw 'HWiNFO installer checksum mismatch.'}
        """+"\n"+Signature("$installer")+"""
            $prior=@(Get-Process HWiNFO64 -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Id)
            $session=(Get-Process -Id $PID).SessionId
            $process=Start-Process $installer -ArgumentList '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru
            $null=$process.Handle;$process.WaitForExit()
            if($process.ExitCode -notin @(0,3010)){throw ('HWiNFO installer failed: '+$process.ExitCode)}
            # Only this fresh install's exact executable in the setup session qualifies.
            for($attempt=0;$attempt -lt 60;$attempt++){
              foreach($child in @(Get-Process HWiNFO64 -ErrorAction SilentlyContinue|Where-Object {$_.Id -notin $prior -and $_.SessionId -eq $session -and $_.Path -ieq $exe})){
                Stop-Process -Id $child.Id -ErrorAction Stop
              }
              Start-Sleep -Milliseconds 250
            }
          }
          if(!(Test-Path -LiteralPath $exe)){throw 'HWiNFO setup did not produce the installed executable.'}
        """+"\n"+Signature("$exe")+"""
          Set-Content "$payload\status.txt" 'HWiNFO is ready. Existing installations are retained; first login creates its shortcut without launching it.'
          Set-Content "$payload\complete" 'HWiNFO verified'
        }catch{
          if(Test-Path "$payload\complete"){Remove-Item -LiteralPath "$payload\complete"}
          Set-Content "$payload\status.txt" $_.Exception.Message;throw
        }finally{if($transcribing){Stop-Transcript};$lock.Dispose()}
        """;

    public const string FirstLogin="""
          Invoke-Step 'REALiX.HWiNFO' {
            if(!(Test-Path "$env:ProgramData\TechLoungeOS\MachineSetup\payloads\HWiNFO\complete")){throw 'HWiNFO setup was skipped or failed. Use Retry setup or review ProgramData\TechLoungeOS\MachineSetup\payloads\HWiNFO\status.txt.'}
            $exe=Join-Path $env:ProgramFiles 'HWiNFO64\HWiNFO64.exe'
            if(!(Test-Path -LiteralPath $exe)){throw 'The installed HWiNFO executable is missing.'}
            $shell=New-Object -ComObject WScript.Shell
            $link=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'HWiNFO64.lnk'))
            $link.TargetPath=$exe;$link.WorkingDirectory=Split-Path $exe;$link.IconLocation=$exe;$link.Save()
          }
        """;
}
