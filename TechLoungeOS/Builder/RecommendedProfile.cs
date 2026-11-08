using System.Text.Json;

namespace TechLoungeOS;

// Reviewed subset of TechLoungeTweaks' existing Recommended preset. This is a
// positive list, not a call to Apply All or a filter over future upstream presets.
public static class RecommendedProfile
{
    public record Setting(string Tweak, string Scope, string Path, string Name, int Value);
    public static IReadOnlyList<Setting> Settings { get; } = Array.AsReadOnly(new Setting[] {
        new("bing_search", "User", @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
        new("cdm_ads", "User", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0),
        new("cdm_ads", "User", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0),
        new("cdm_ads", "User", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
        new("cdm_ads", "User", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
        new("cdm_ads", "User", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0),
        new("telemetry", "Machine", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
        new("ceip", "Machine", @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 0),
        new("feedback", "User", @"SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
        new("activity_history", "Machine", @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
        new("ad_id", "User", @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
        new("typing_insights", "User", @"SOFTWARE\Microsoft\Input\TIPC", "Enabled", 0),
        new("speech_data", "User", @"SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0),
        new("ink_collection", "User", @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1)
    });

    public static string Script(bool machine) => """
        $ErrorActionPreference = 'Stop'
        """ + "\n$recommendedSettings = ConvertFrom-Json " + Policy.Ps(JsonSerializer.Serialize(Settings.Where(s => s.Scope == (machine ? "Machine" : "User")))) + "\n" +
        (machine ? "$recommendedRoot = Join-Path $env:ProgramData 'TechLoungeOS\\recommended-machine'\n$recommendedHive = 'HKLM:\\'\n" :
                   "$recommendedRoot = Join-Path $state 'recommended-user'\n$recommendedHive = 'HKCU:\\'\n") + """
        New-Item $recommendedRoot -ItemType Directory -Force | Out-Null
        if (!(Test-Path "$recommendedRoot\complete")) {
          $recommendedLock = [IO.File]::Open("$recommendedRoot\apply.lock", 'OpenOrCreate', 'ReadWrite', 'None')
          try {
            $recommendedErrors = New-Object 'System.Collections.Generic.List[string]'
            for ($recommendedIndex = 0; $recommendedIndex -lt $recommendedSettings.Count; $recommendedIndex++) {
              $setting = $recommendedSettings[$recommendedIndex]
              $marker = Join-Path $recommendedRoot ($recommendedIndex.ToString() + '.done')
              try {
              if (Test-Path $marker) {
                $savedKey = Get-Item -LiteralPath ($recommendedHive + $setting.Path) -ErrorAction SilentlyContinue
                if ($savedKey) {
                  try { $stillMatches = $savedKey.GetValueNames() -contains $setting.Name -and $savedKey.GetValueKind($setting.Name) -eq 'DWord' -and $savedKey.GetValue($setting.Name) -eq $setting.Value }
                  finally { $savedKey.Close() }
                  if ($stillMatches) { continue }
                }
              }
                $key = $recommendedHive + $setting.Path
                # A protected user-policy key may already be seeded in the offline
                # default profile. Verify its exact type/value without rewriting it.
                if ($setting.Path -like 'SOFTWARE\Policies\*' -and (Test-Path -LiteralPath $key)) {
                  $policyKey=Get-Item -LiteralPath $key
                  try {
                    $matchesPolicy=$policyKey.GetValueNames() -contains $setting.Name -and $policyKey.GetValueKind($setting.Name) -eq 'DWord' -and $policyKey.GetValue($setting.Name) -eq $setting.Value
                  } finally { $policyKey.Close() }
                  if($matchesPolicy){Set-Content -LiteralPath $marker -Value 'Verified preconfigured policy';continue}
                }
                $backup = Join-Path $recommendedRoot ($recommendedIndex.ToString() + '.before.xml')
                if (!(Test-Path $backup)) {
                  $previous = $null
                  if (Test-Path -LiteralPath $key) {
                    $registryKey = Get-Item -LiteralPath $key
                    try {
                      if ($registryKey.GetValueNames() -contains $setting.Name) {
                        $previous = [pscustomobject]@{Kind=$registryKey.GetValueKind($setting.Name).ToString();Value=$registryKey.GetValue($setting.Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}
                      }
                    } finally { $registryKey.Close() }
                  }
                  # Save the exact pre-change value and type before touching this entry.
                  [pscustomobject]@{Scope=$setting.Scope;Path=$setting.Path;Name=$setting.Name;Previous=$previous} | Export-Clixml -LiteralPath ($backup + '.partial') -ErrorAction Stop
                  Move-Item -LiteralPath ($backup + '.partial') -Destination $backup -ErrorAction Stop
                }
                if (!(Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
                New-ItemProperty -LiteralPath $key -Name $setting.Name -Value $setting.Value -PropertyType DWord -Force | Out-Null
                $verifiedKey = Get-Item -LiteralPath $key
                try {
                  if ($verifiedKey.GetValueKind($setting.Name) -ne 'DWord' -or $verifiedKey.GetValue($setting.Name) -ne $setting.Value) { throw 'The requested DWORD value did not read back.' }
                } finally { $verifiedKey.Close() }
                Set-Content -LiteralPath $marker -Value 'Completed' -ErrorAction Stop
              } catch { $recommendedErrors.Add($setting.Tweak + ': ' + $_.Exception.Message) }
            }
            if ($recommendedErrors.Count) {
              Set-Content "$recommendedRoot\status.txt" $recommendedErrors.ToArray()
              throw ('Recommended profile incomplete: ' + ($recommendedErrors -join '; '))
            }
            Set-Content "$recommendedRoot\status.txt" 'Recommended privacy settings completed and verified. Windows Update and Defender were left unchanged. Graphics drivers and GPU profiles are handled separately for the detected hardware; this message does not confirm their installation.'
            Set-Content "$recommendedRoot\complete" 'Completed'
          } finally { $recommendedLock.Dispose() }
        }
        """;

    public static string FirstLogin => """

          Invoke-Step 'recommended-machine' {
            $recommendedMachine = Join-Path $env:ProgramData 'TechLoungeOS\recommended-machine'
            if (!(Test-Path "$recommendedMachine\complete")) {
              throw 'Recommended machine settings were skipped or failed during setup. An administrator can run ProgramData\TechLoungeOS\recommended-machine.ps1 and review recommended-machine\status.txt. No automatic elevation is attempted.'
            }
          }
          Invoke-Step 'recommended-user' {
        """ + "\n" + Script(false) + "\n  }\n";

    public static void StageMachine(string mount)
    {
        var folder = Path.Combine(mount, @"ProgramData\TechLoungeOS");
        Policy.RequireUnlinkedPath(folder);
        File.WriteAllText(Path.Combine(folder, "recommended-machine.ps1"), Script(true));
        File.WriteAllText(Path.Combine(folder, "recommended-profile.json"), JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        SetupHooks.Prefix(mount, "@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"%ProgramData%\\TechLoungeOS\\recommended-machine.ps1\"\r\n");
    }
}
