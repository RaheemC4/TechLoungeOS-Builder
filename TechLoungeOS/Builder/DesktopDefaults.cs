using System.Text.Json;

namespace TechLoungeOS;

// Explicit user-requested desktop choices, separate from the ten-tweak profile.
public static class DesktopDefaults
{
    public static IReadOnlyList<RecommendedProfile.Setting> Settings {get;}=Array.AsReadOnly(new RecommendedProfile.Setting[]{
        new("file_extensions_visible","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","HideFileExt",0),
        new("hidden_files_visible","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","Hidden",1),
        new("taskbar_end_task","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings","TaskbarEndTask",1),
        new("taskbar_left","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","TaskbarAl",0),
        new("this_pc_hidden","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel","{20D04FE0-3AEA-1069-A2D8-08002B30309D}",1),
        new("this_pc_classic_hidden","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu","{20D04FE0-3AEA-1069-A2D8-08002B30309D}",1),
        new("search_hidden","User",@"Software\Microsoft\Windows\CurrentVersion\Search","SearchboxTaskbarMode",0),
        new("widgets_hidden","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","TaskbarDa",0),
        new("task_view_hidden","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","ShowTaskViewButton",0),
        new("chat_hidden","User",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","TaskbarMn",0),
        new("transparency_off","User",@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","EnableTransparency",0),
        new("lock_screen_picture","User",@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager","RotatingLockScreenEnabled",0),
        new("lock_screen_overlay_off","User",@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager","RotatingLockScreenOverlayEnabled",0),
        new("apps_dark","User",@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",0),
        new("windows_dark","User",@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0),
        new("internet_zone_launch","User",@"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\3","1806",0),
        new("new_app_association_alerts_off","Machine",@"SOFTWARE\Policies\Microsoft\Windows\Explorer","NoNewAppAlert",1),
        new("uac_enabled","Machine",@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System","EnableLUA",1),
        new("uac_never_notify","Machine",@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System","ConsentPromptBehaviorAdmin",0),
        new("uac_no_dimming","Machine",@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System","PromptOnSecureDesktop",0)
    });
    public static string Script(bool machine) => """
        $ErrorActionPreference='Stop'
        """+"\n$desktopSettings=ConvertFrom-Json "+Policy.Ps(JsonSerializer.Serialize(Settings.Where(s=>s.Scope==(machine?"Machine":"User"))))+"\n"+
        (machine?"$desktopState=Join-Path $env:ProgramData 'TechLoungeOS\\desktop-machine';$desktopHive='HKLM:\\'\n":"$desktopState=Join-Path $state 'desktop-user';$desktopHive='HKCU:\\'\n")+"""
        New-Item -Path $desktopState -ItemType Directory -Force|Out-Null
        $desktopLock=[IO.File]::Open("$desktopState\apply.lock",'OpenOrCreate','ReadWrite','None')
        try {
          foreach($setting in $desktopSettings) {
            $marker=Join-Path $desktopState ($setting.Tweak+'.done')
            if(Test-Path -LiteralPath $marker){continue}
            $path=$desktopHive+$setting.Path
            $previous=$null
            if(Test-Path -LiteralPath $path){
              $key=Get-Item -LiteralPath $path
              try{
                if($key.GetValueNames() -contains $setting.Name){
                  $previous=[pscustomobject]@{Kind=$key.GetValueKind($setting.Name).ToString();Value=$key.GetValue($setting.Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}
                }
              }finally{$key.Close()}
            }
            $backup=Join-Path $desktopState ($setting.Tweak+'.before.xml')
            if(!(Test-Path -LiteralPath $backup)){
              [pscustomobject]@{Path=$path;Name=$setting.Name;Previous=$previous}|Export-Clixml -LiteralPath ($backup+'.partial')
              Move-Item -LiteralPath ($backup+'.partial') -Destination $backup -ErrorAction Stop
            }
            if(!$previous -or $previous.Kind -ne 'DWord' -or $previous.Value -ne $setting.Value){
              if(!(Test-Path -LiteralPath $path)){New-Item -Path $path -Force|Out-Null}
              New-ItemProperty -LiteralPath $path -Name $setting.Name -Value $setting.Value -PropertyType DWord -Force|Out-Null
            }
            if((Get-ItemPropertyValue -LiteralPath $path -Name $setting.Name) -ne $setting.Value){throw ('Desktop setting readback failed: '+$setting.Tweak)}
            Set-Content -LiteralPath $marker -Value 'Verified'
          }
          Set-Content "$desktopState\complete" 'Verified requested desktop settings'
        }finally{$desktopLock.Dispose()}
        """;
    public static string FirstLogin => """

        Invoke-Step 'desktop-machine' {
          if(!(Test-Path (Join-Path $env:ProgramData 'TechLoungeOS\desktop-machine\complete'))){throw 'Desktop machine settings need administrator recovery. Review ProgramData\TechLoungeOS\desktop-machine.ps1. No automatic elevation was attempted.'}
        }
        Invoke-Step 'desktop-defaults' {
        """+"\n"+Script(false)+"\n}\n";
    public static void StageMachine(string mount)
    {
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS");
        Policy.RequireUnlinkedPath(folder);Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"desktop-machine.ps1"),Script(true)+"\n"+DesktopSurface.MachineScript);
        SetupHooks.Prefix(mount,"@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"%ProgramData%\\TechLoungeOS\\desktop-machine.ps1\"\r\n");
    }
}
