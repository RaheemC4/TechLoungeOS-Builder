# This step runs as the signed-in user. Preserve upstream installation/update code;
# never grant antivirus exclusions or launch its manual presets automatically.
Invoke-Step 'tweaks-install' {
  $payload = Join-Path $env:ProgramData 'TechLoungeOS\TweaksInstaller'
  $manifest = Get-Content -LiteralPath (Join-Path $payload installer.json) -Raw | ConvertFrom-Json
  if ($manifest.Version -notmatch '^\d{4}\.\d{2}\.\d{2}\.\d{6}$' -or $manifest.Sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid Tweaks installer manifest.' }
  $setup = Join-Path $payload TechLoungeTweaks-Setup.exe
  if ((Get-FileHash -LiteralPath $setup).Hash -ne $manifest.Sha256) { throw 'Tweaks installer integrity check failed. Review Windows Security if files were removed.' }
  $settings = Get-ItemProperty 'HKCU:\Software\TechLoungeTweaks' -ErrorAction SilentlyContinue
  $root = if ($settings.InstallRoot) { [string]$settings.InstallRoot } else { Join-Path $env:LOCALAPPDATA 'Programs\TechLoungeTweaks' }
  if (![IO.Path]::IsPathRooted($root) -or $root.StartsWith('\\') -or $root.IndexOf(':',2) -ge 0) { throw 'Unsupported existing Tweaks installation location.' }
  $current = Join-Path $root current.txt
  $version = if (Test-Path -LiteralPath $current) { ([IO.File]::ReadAllText($current)).Trim() } else { '' }
  if ($version -and $version -notmatch '^\d{4}\.\d{2}\.\d{2}\.\d{6}$') { throw 'Existing Tweaks version marker needs review; it was left unchanged.' }
  $runtime = Join-Path $env:ProgramData 'TechLoungeOS\WebView2Runtime'
  if (!(Test-Path -LiteralPath (Join-Path $runtime msedgewebview2.exe))) { throw 'The private Microsoft WebView2 runtime is missing.' }
  $policies = @{ BrowserExecutableFolder = $runtime; UserDataFolder = (Join-Path $env:LOCALAPPDATA 'TechLoungeOS\WebView2') }
  if(Test-Path -LiteralPath (Join-Path $env:ProgramData 'TechLoungeOS\AccountSetup.exe')) {
    $accountState=Join-Path $env:ProgramData 'TechLoungeOS\account-setup'
    for($attempt=0;$attempt -lt 30 -and !(Test-Path -LiteralPath (Join-Path $accountState tweaks-runtime-ready));$attempt++) {
      if(Test-Path -LiteralPath (Join-Path $accountState tweaks-runtime-failed)){throw 'The account setup could not prepare the app-specific runtime policy. Review account-setup\tweaks-runtime-failed.'}
      Start-Sleep -Seconds 1
    }
  }
  # Scope policies to this executable only, including launches from the unchanged
  # upstream updater/tray. An existing different policy requires human review.
  foreach ($name in $policies.Keys) {
    foreach ($hive in @('HKLM:','HKCU:')) {
      $key = "$hive\Software\Policies\Microsoft\Edge\WebView2\$name"
      $old = Get-ItemProperty -LiteralPath $key -Name TechLoungeTweaks.exe -ErrorAction SilentlyContinue
      if ($old -and $old.'TechLoungeTweaks.exe' -ne $policies[$name]) { throw 'An existing app-specific WebView2 policy differs. It was left unchanged.' }
    }
  }
  # Browser data remains the app's normal per-user location when no existing
  # user override is present. Setup owns only the shared executable-specific path.
  $runtimeReady=$false
  foreach($hive in @('HKLM:','HKCU:')){
    $present=Get-ItemProperty -LiteralPath ($hive+'\Software\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder') -Name TechLoungeTweaks.exe -ErrorAction SilentlyContinue
    if($present -and $present.'TechLoungeTweaks.exe' -ieq $runtime){$runtimeReady=$true}
  }
  if(!$runtimeReady){throw 'The app-specific private runtime is not registered. An administrator must retry the fixed machine setup task. No user policy or permissions were changed.'}
  if (!$version -or [Version]$version -lt [Version]$manifest.Version) {
    # Fresh installs have no tray task or unsolicited window. Existing preferences
    # stay intact. /UPDATE suppresses the installer's immediate tray launch.
    if (!$settings -or $null -eq $settings.Startup) {
      New-Item 'HKCU:\Software\TechLoungeTweaks' -Force | Out-Null
      New-ItemProperty 'HKCU:\Software\TechLoungeTweaks' -Name Startup -Value '0' -PropertyType String -Force | Out-Null
    }
    $process = Start-Process -FilePath $setup -ArgumentList '/S /UPDATE' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Tweaks installer failed (exit $($process.ExitCode))." }
    $settings = Get-ItemProperty 'HKCU:\Software\TechLoungeTweaks' -ErrorAction Stop
    $root = [string]$settings.InstallRoot
    $version = ([IO.File]::ReadAllText((Join-Path $root current.txt))).Trim()
    if ($version -notmatch '^\d{4}\.\d{2}\.\d{2}\.\d{6}$' -or [Version]$version -lt [Version]$manifest.Version) { throw 'Tweaks did not confirm the requested version.' }
  }
  $launcher = Join-Path $root TechLounge.exe
  $core = Join-Path $root "versions\$version\TechLoungeTweaks.exe"
  if (!(Test-Path -LiteralPath $launcher) -or !(Test-Path -LiteralPath $core)) { throw 'Tweaks application files are missing. Review Windows Security; no exclusions were added.' }
  $shell = New-Object -ComObject WScript.Shell
  $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'TechLoungeTweaks.lnk'))
  $link.TargetPath = $launcher
  $link.WorkingDirectory = $root
  $link.IconLocation = $core
  $link.Save()
  $versionRoot = Split-Path $core
  $advanced = @('resources\defender-remover\payload\Remove_Defender\DisableDefenderPolicies.reg',
    '_internal\vendor\mas\HWID_Activation.cmd', 'resources\defender-remover\Defender.Remover.13.exe',
    '_internal\vendor\mas\Change_Windows_Edition.cmd')
  $missing = @($advanced | Where-Object { !(Test-Path -LiteralPath (Join-Path $versionRoot $_)) })
  $health = Join-Path $env:LOCALAPPDATA TechLoungeOS
  [void][IO.Directory]::CreateDirectory($health)
  @{version=$version;corePresent=$true;upstreamInstallerUnmodified=$true;antivirusExceptionsAdded=$false;missingAdvancedTools=$missing} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $health tweaks-compatibility.json)
  if ($missing.Count) {
    $warning = 'Tweaks opens, but some advanced activation/Defender-removal tools are absent from their known package paths. Review tweaks-compatibility.json and Windows Security. Full functionality is not confirmed.'
    Write-Warning $warning
    if ($null -ne $script:warnings) { $script:warnings.Add($warning) }
  }
  Write-Output "TechLoungeTweaks $version installed with its original updater. Antivirus compatibility remains subject to local protection."
}
