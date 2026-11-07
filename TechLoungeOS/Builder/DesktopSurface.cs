namespace TechLoungeOS;

// These are initial desktop choices, not app removals or permanent layout policies.
public static class DesktopSurface
{
    public const string Functions = """
        function Get-CpuZDesktopShortcuts([string]$folder, $links) {
          if(!(Test-Path -LiteralPath $folder -PathType Container)){return}
          foreach($file in @(Get-ChildItem -LiteralPath $folder -Filter '*.lnk' -File -Force)) {
            $knownName=$file.BaseName -match '^(?:CPUID[ _-]*)?CPU[ _-]*Z$'
            # Reparse points, offline files and recall placeholders must not be followed/read.
            if(([int]$file.Attributes -band 0x441400) -ne 0 -or $file.Length -gt 1MB){
              if($knownName){throw 'The CPU-Z shortcut is linked, offline or oversized; administrator review is needed.'}
              continue
            }
            try{$target=$links.CreateShortcut($file.FullName).TargetPath}catch{
              if($knownName){throw 'The CPU-Z shortcut cannot be inspected; administrator review is needed.'}
              continue
            }
            if([IO.Path]::GetFileName($target) -match '^cpuz(?:[_-](?:x32|x64|x86))?\.exe$'){$file}
          }
        }
        function Move-CpuZDesktopShortcuts([string]$folder,[string]$backup,$links) {
          foreach($file in @(Get-CpuZDesktopShortcuts $folder $links)) {
            if((Test-Path -LiteralPath $backup) -and ((Get-Item -LiteralPath $backup -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked shortcut backup folder rejected.'}
            New-Item -Path $backup -ItemType Directory -Force|Out-Null
            $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $saved=Join-Path $backup ($hash+'.lnk')
            if(Test-Path -LiteralPath $saved){
              if((Get-Item -LiteralPath $saved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked shortcut backup rejected.'}
            }else{Copy-Item -LiteralPath $file.FullName -Destination $saved -ErrorAction Stop}
            if((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ne $hash){throw 'Shortcut backup verification failed.'}
            @{path=$file.FullName;backup=$saved;sha256=$hash}|ConvertTo-Json -Compress|Add-Content -LiteralPath (Join-Path $backup 'removed.jsonl')
            if((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $hash){throw 'The desktop shortcut changed during backup; it was retained.'}
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
          }
        }
        function Unpin-DesktopMicrosoftStore($shell,[bool]$expectStore=$false) {
          $apps=$shell.Namespace('shell:AppsFolder')
          if(!$apps){throw 'Windows app shortcuts are not ready; Store unpin will retry.'}
          $store=$apps.ParseName('Microsoft.WindowsStore_8wekyb3d8bbwe!App')
          if($store){$store.InvokeVerb('taskbarunpin')}
          elseif($expectStore){throw 'Store registration is not ready; its taskbar cleanup will retry.'}
        }
        """;

    // SYSTEM handles the shared and template desktops before a standard user signs in.
    public static string MachineScript => Functions + "\n" + """
        $desktopLinks=New-Object -ComObject WScript.Shell
        $desktopBackup=Join-Path $env:ProgramData 'TechLoungeOS\desktop-shortcuts'
        Move-CpuZDesktopShortcuts ([Environment]::GetFolderPath('CommonDesktopDirectory')) (Join-Path $desktopBackup 'public') $desktopLinks
        $defaultProfile=[Environment]::ExpandEnvironmentVariables([string](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList' -Name Default).Default)
        if(!$defaultProfile){throw 'Windows default profile location is unavailable.'}
        Move-CpuZDesktopShortcuts (Join-Path $defaultProfile 'Desktop') (Join-Path $desktopBackup 'default') $desktopLinks
        """;

    public static string FirstLogin(bool storeExpected) => Functions + "\n" + """
        Invoke-Step 'desktop-shortcuts' {
          $desktopLinks=New-Object -ComObject WScript.Shell
          Move-CpuZDesktopShortcuts ([Environment]::GetFolderPath('Desktop')) (Join-Path $state 'desktop-shortcuts') $desktopLinks
          if(@(Get-CpuZDesktopShortcuts ([Environment]::GetFolderPath('CommonDesktopDirectory')) $desktopLinks).Count){
            throw 'A source setup hook recreated the shared CPU-Z shortcut. An administrator must rerun desktop-machine.ps1; no automatic elevation was attempted.'
          }
        }
        """+"\nInvoke-Step 'store-taskbar-unpin' { Unpin-DesktopMicrosoftStore (New-Object -ComObject Shell.Application) "+(storeExpected?"$true":"$false")+" }\n";
}
