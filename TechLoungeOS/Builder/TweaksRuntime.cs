namespace TechLoungeOS;

// Windows Setup prepares one shared app-specific runtime path. The unchanged
// Tweaks application owns its per-user data; no SYSTEM writes enter user hives.
public static class TweaksRuntime
{
    public const string Worker = """
        function Initialize-TweaksRuntimePolicies {
          $common=Join-Path $env:ProgramData TechLoungeOS
          if(!(Test-Path -LiteralPath (Join-Path $common 'TweaksInstaller\installer.json'))){return}
          $runtime=Join-Path $common WebView2Runtime
          if(!(Test-Path -LiteralPath (Join-Path $runtime msedgewebview2.exe))){throw 'The protected private WebView2 runtime is missing'}
          Assert-DesktopSetupUnlinked $runtime
          Set-TweaksRuntimePolicy ([Microsoft.Win32.Registry]::LocalMachine) $runtime
        }
        function Set-TweaksRuntimePolicy($Machine,[string]$Runtime) {
          $path='Software\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder'
          $key=$Machine.OpenSubKey($path)
          try{
            if($key){
              $old=$key.GetValue('TechLoungeTweaks.exe')
              if($null -ne $old){
                if($old -isnot [string] -or $old -ine $Runtime){throw 'An existing app-specific WebView2 policy differs. It was retained.'}
                return
              }
            }
          }finally{if($key){$key.Dispose()}}
          $key=$Machine.CreateSubKey($path)
          try{
            $key.SetValue('TechLoungeTweaks.exe',$Runtime,[Microsoft.Win32.RegistryValueKind]::String)
            if($key.GetValue('TechLoungeTweaks.exe') -ine $Runtime){throw 'Runtime policy readback failed'}
          }finally{if($key){$key.Dispose()}}
        }
        """;
}