<#
.SYNOPSIS
    Automated boot speed optimisation for Windows 10/11.
    Detects CPU and GPU vendor and applies the right tweaks itself.

.DESCRIPTION
    Everything is applied automatically. The user is not asked to change
    BIOS settings, because the risky ones (Fast Boot, CSM, Legacy USB)
    can lock people out of their own firmware.

    Vendor handling is automatic:
      NVIDIA GPU -> delays the NVIDIA App/GeForce Experience containers,
                    disables telemetry tasks. Display driver, NVIDIA
                    Control Panel and G-Sync are NOT touched.
      AMD GPU    -> delays Crash Defender and logging, disables the
                    Adrenalin tray auto-launch. FreeSync, Anti-Lag and
                    the display driver are NOT touched.
      Intel CPU  -> delays Management Engine and DAL helper services.
      AMD CPU    -> no safe Windows-side boot items exist; nothing done.

    NEVER modifies VBS, HVCI, Secure Boot, TPM or hypervisorlaunchtype,
    so kernel anti-cheat (Vanguard, BattlEye, EAC, Ricochet) keeps working.

    A rollback file is written to the script's folder. Run with -Rollback
    to undo every service and startup change.

.PARAMETER Rollback
    Undo changes using the rollback file from a previous run.

.PARAMETER RemoveVirtualisation
    Also remove Hyper-V / Containers. Auto-blocked if WSL, Docker or
    VMs are actually in use.

.PARAMETER ForceFastStartup
    Override the dual-boot safety block.

.PARAMETER SkipStartupApps
    Leave third-party startup apps alone.

.PARAMETER WhatIf
    Preview without changing anything.

.EXAMPLE
    .\Optimize-Boot.ps1
    .\Optimize-Boot.ps1 -WhatIf
    .\Optimize-Boot.ps1 -RemoveVirtualisation
    .\Optimize-Boot.ps1 -Rollback
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$Rollback,
    [switch]$RemoveVirtualisation,
    [switch]$ForceFastStartup,
    [switch]$SkipStartupApps,
    [string]$RollbackFilePath  # Passed by the all-in-one .bat so the
                               # rollback JSON lands next to the .bat,
                               # not in the temp folder the script runs from.
)

#region --- Setup -------------------------------------------------------------

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

# Load modules up front with WhatIf suppressed. Passing -WhatIf:$false
# is not enough: the alias creation happens inside each module and reads
# $WhatIfPreference from the calling scope, so the variable itself has to
# be overridden for the duration of the imports. Without this, -WhatIf
# floods the output with "Set Alias" previews unrelated to this script.
$savedWhatIf     = $WhatIfPreference
$WhatIfPreference = $false
foreach ($m in 'CimCmdlets','Storage','ScheduledTasks','TrustedPlatformModule','DISM','Microsoft.PowerShell.Management','Microsoft.PowerShell.Security') {
    Import-Module $m -ErrorAction SilentlyContinue -WarningAction SilentlyContinue | Out-Null
}
# Touch each area once so any lazy auto-load also happens here, quietly.
Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue | Out-Null
Get-Volume        -ErrorAction SilentlyContinue | Out-Null
Get-Partition     -ErrorAction SilentlyContinue | Out-Null
Get-ScheduledTask -ErrorAction SilentlyContinue | Out-Null
$WhatIfPreference = $savedWhatIf

$script:Changes  = [System.Collections.Generic.List[string]]::new()
$script:Warnings = [System.Collections.Generic.List[string]]::new()
$script:Failed   = [System.Collections.Generic.List[string]]::new()

# Rollback record. Every reversible change appends its previous state.
$script:Undo = [System.Collections.Generic.List[object]]::new()

$scriptDir    = if ($PSScriptRoot) { $PSScriptRoot } else { $PWD.Path }
$rollbackFile = if ($RollbackFilePath) { $RollbackFilePath } else { Join-Path $scriptDir 'Optimize-Boot-rollback.json' }

function Write-Step { param([string]$T) Write-Host "`n=== $T ===" -ForegroundColor Cyan }
function Write-Ok   { param([string]$T) Write-Host "  [OK]   $T" -ForegroundColor Green }
function Write-Skip { param([string]$T) Write-Host "  [SKIP] $T" -ForegroundColor DarkGray }
function Write-Fail { param([string]$T) Write-Host "  [FAIL] $T" -ForegroundColor Red }
function Write-Info { param([string]$T) Write-Host "  [INFO] $T" -ForegroundColor Yellow }
function Write-Warn { param([string]$T) Write-Host "  [WARN] $T" -ForegroundColor Magenta }

function Add-Undo {
    param([string]$Kind, [hashtable]$Data)
    $rec = @{ Kind = $Kind }
    foreach ($k in $Data.Keys) { $rec[$k] = $Data[$k] }
    $script:Undo.Add([pscustomobject]$rec)
}

function Set-RegValue {
    param(
        [string]$Path, [string]$Name, $Value,
        [string]$Type = 'DWord', [string]$Description,
        [switch]$NoUndo
    )
    try {
        $existed = Test-Path $Path
        if (-not $existed) {
            if ($PSCmdlet.ShouldProcess($Path, 'Create registry key')) {
                New-Item -Path $Path -Force -ErrorAction Stop | Out-Null
            } else { return }
        }
        $current = (Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue).$Name
        if ($null -ne $current -and "$current" -eq "$Value") {
            Write-Skip "$Description (already set)"
            return
        }
        if ($PSCmdlet.ShouldProcess("$Path\$Name", "Set to $Value")) {
            if (-not $NoUndo) {
                Add-Undo 'Registry' @{
                    Path = $Path; Name = $Name; Type = $Type
                    OldValue = $current; Existed = ($null -ne $current)
                }
            }
            New-ItemProperty -Path $Path -Name $Name -Value $Value `
                -PropertyType $Type -Force -ErrorAction Stop | Out-Null
            Write-Ok $Description
            $script:Changes.Add($Description)
        }
    }
    catch {
        Write-Fail "$Description - $($_.Exception.Message)"
        $script:Failed.Add($Description)
    }
}

# Sets a service to delayed-auto. sc.exe returns error 87 on some
# services, so fall back to the registry flag directly.
function Set-ServiceDelayed {
    param([string]$ServiceName, [string]$Label)

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) { return }

    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (-not (Test-Path $key)) { return }

    $props     = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
    $startVal  = $props.Start
    $delayVal  = $props.DelayedAutostart

    # Start=2 is automatic. Only worth delaying those. 3=manual, 4=disabled.
    if ($startVal -ne 2) {
        Write-Skip "$Label (not auto-start)"
        return
    }
    if ($delayVal -eq 1) {
        Write-Skip "$Label (already delayed)"
        return
    }

    if ($PSCmdlet.ShouldProcess($ServiceName, 'Set delayed auto-start')) {
        try {
            Add-Undo 'Service' @{
                Name = $ServiceName; OldStart = $startVal; OldDelayed = $delayVal
            }
            New-ItemProperty -Path $key -Name 'DelayedAutostart' -Value 1 `
                -PropertyType DWord -Force -ErrorAction Stop | Out-Null
            Write-Ok "$Label -> delayed start"
            $script:Changes.Add("Delayed: $Label")
        }
        catch {
            Write-Fail "$Label - $($_.Exception.Message)"
            $script:Failed.Add("Service: $Label")
        }
    }
}

function Set-ServiceDisabled {
    param([string]$ServiceName, [string]$Label)

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) { return }

    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (-not (Test-Path $key)) { return }

    $props    = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
    $startVal = $props.Start

    if ($startVal -eq 4) { Write-Skip "$Label (already disabled)"; return }

    if ($PSCmdlet.ShouldProcess($ServiceName, 'Disable service')) {
        try {
            Add-Undo 'Service' @{
                Name = $ServiceName; OldStart = $startVal
                OldDelayed = $props.DelayedAutostart
            }
            Set-Service -Name $ServiceName -StartupType Disabled -ErrorAction Stop
            Write-Ok "$Label -> disabled"
            $script:Changes.Add("Disabled: $Label")
        }
        catch {
            Write-Fail "$Label - $($_.Exception.Message)"
            $script:Failed.Add("Service: $Label")
        }
    }
}

function Disable-BootTask {
    param([string]$TaskPathPattern, [string]$Label)

    try {
        $tasks = Get-ScheduledTask -ErrorAction SilentlyContinue |
                 Where-Object { $_.TaskName -like $TaskPathPattern -and $_.State -ne 'Disabled' }
        if (-not $tasks) { return }

        foreach ($t in $tasks) {
            if ($PSCmdlet.ShouldProcess($t.TaskName, 'Disable scheduled task')) {
                try {
                    Add-Undo 'Task' @{ Name = $t.TaskName; Path = $t.TaskPath }
                    Disable-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath `
                        -ErrorAction Stop | Out-Null
                    Write-Ok "$Label task disabled: $($t.TaskName)"
                    $script:Changes.Add("Task disabled: $($t.TaskName)")
                }
                catch { Write-Fail "$($t.TaskName) - $($_.Exception.Message)" }
            }
        }
    } catch { }
}

# --- Admin check ---
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "`n  Must be run as Administrator.`n" -ForegroundColor Red
    Write-Host "  Right-click PowerShell > Run as administrator, then:" -ForegroundColor Yellow
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`"`n" -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

#endregion

#region --- ROLLBACK MODE -----------------------------------------------------

if ($Rollback) {
    Clear-Host
    Write-Host "`n  === ROLLBACK MODE ===`n" -ForegroundColor Yellow

    if (-not (Test-Path $rollbackFile)) {
        Write-Host "  No rollback file found at:" -ForegroundColor Red
        Write-Host "  $rollbackFile`n" -ForegroundColor Red
        exit 1
    }

    $undoData = Get-Content $rollbackFile -Raw | ConvertFrom-Json
    Write-Info "Restoring $(@($undoData).Count) change(s) from $(Split-Path $rollbackFile -Leaf)"

    foreach ($rec in $undoData) {
        try {
            switch ($rec.Kind) {
                'Registry' {
                    if ($rec.Existed) {
                        New-ItemProperty -Path $rec.Path -Name $rec.Name -Value $rec.OldValue `
                            -PropertyType $rec.Type -Force -ErrorAction Stop | Out-Null
                        Write-Ok "Restored $($rec.Path)\$($rec.Name)"
                    } else {
                        Remove-ItemProperty -Path $rec.Path -Name $rec.Name -Force -ErrorAction SilentlyContinue
                        Write-Ok "Removed $($rec.Path)\$($rec.Name)"
                    }
                }
                'Service' {
                    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$($rec.Name)"
                    if (Test-Path $key) {
                        New-ItemProperty -Path $key -Name 'Start' -Value $rec.OldStart `
                            -PropertyType DWord -Force -ErrorAction Stop | Out-Null
                        if ($null -ne $rec.OldDelayed) {
                            New-ItemProperty -Path $key -Name 'DelayedAutostart' -Value $rec.OldDelayed `
                                -PropertyType DWord -Force -ErrorAction SilentlyContinue | Out-Null
                        } else {
                            Remove-ItemProperty -Path $key -Name 'DelayedAutostart' `
                                -Force -ErrorAction SilentlyContinue
                        }
                        Write-Ok "Restored service: $($rec.Name)"
                    }
                }
                'Task' {
                    Enable-ScheduledTask -TaskName $rec.Name -TaskPath $rec.Path -ErrorAction Stop | Out-Null
                    Write-Ok "Re-enabled task: $($rec.Name)"
                }
                'Startup' {
                    Remove-ItemProperty -Path $rec.Path -Name $rec.Name -Force -ErrorAction SilentlyContinue
                    Write-Ok "Re-enabled startup app: $($rec.Name)"
                }
                'Feature' {
                    Write-Info "Windows feature '$($rec.Name)' must be re-enabled manually"
                    Write-Info "  (Windows Features, or: Enable-WindowsOptionalFeature -Online -FeatureName $($rec.Name))"
                }
            }
        }
        catch { Write-Fail "$($rec.Kind) $($rec.Name) - $($_.Exception.Message)" }
    }

    Write-Host "`n  Rollback complete. Restart to apply.`n" -ForegroundColor Green
    exit 0
}

#endregion

Clear-Host
Write-Host @"

  +------------------------------------------------------+
  |            Windows Boot Optimisation                 |
  |                                                      |
  |   Fully automatic. Detects your CPU and GPU and      |
  |   applies the right tweaks. No BIOS changes needed.  |
  |   Anti-cheat prerequisites are never touched.        |
  +------------------------------------------------------+

"@ -ForegroundColor White

#region --- Hardware detection ------------------------------------------------

Write-Step "Hardware detection"

$os  = Get-CimInstance Win32_OperatingSystem
$cs  = Get-CimInstance Win32_ComputerSystem
$bb  = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1

# --- CPU vendor ---
# Manufacturer is authoritative; Name is a fallback for OEM images
# that leave Manufacturer blank.
$cpuVendor = 'Unknown'
$vendorRaw = "$($cpu.Manufacturer) $($cpu.Name)"
if     ($vendorRaw -match '(?i)GenuineIntel|\bIntel\b') { $cpuVendor = 'Intel' }
elseif ($vendorRaw -match '(?i)AuthenticAMD|\bAMD\b')   { $cpuVendor = 'AMD'   }

# --- GPU vendors. A machine can have several (iGPU + dGPU). ---
$gpus = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -and $_.Name -notmatch '(?i)basic display|remote|virtual|meta|parsec|citrix' }

$hasNvidia = $false; $hasAmdGpu = $false; $hasIntelGpu = $false
foreach ($g in $gpus) {
    if ($g.Name -match '(?i)nvidia|geforce|quadro|\brtx\b|\bgtx\b')     { $hasNvidia   = $true }
    if ($g.Name -match '(?i)\bamd\b|radeon|\brx\s?\d|vega|firepro')     { $hasAmdGpu   = $true }
    if ($g.Name -match '(?i)intel.*(graphics|\barc\b|iris|uhd|hd\s\d)') { $hasIntelGpu = $true }
}

$isLaptop       = ($cs.PCSystemType -eq 2)
$isDomainJoined = $cs.PartOfDomain

Write-Info "Machine     : $env:COMPUTERNAME"
Write-Info "OS          : $($os.Caption) (build $($os.BuildNumber))"
Write-Info "CPU         : $($cpu.Name.Trim())"
Write-Info "CPU vendor  : $cpuVendor"
if ($bb) { Write-Info "Motherboard : $($bb.Manufacturer) $($bb.Product)" }
foreach ($g in $gpus) { Write-Info "GPU         : $($g.Name)" }
$gpuList = @()
if ($hasNvidia)   { $gpuList += 'NVIDIA' }
if ($hasAmdGpu)   { $gpuList += 'AMD' }
if ($hasIntelGpu) { $gpuList += 'Intel' }
Write-Info "GPU vendor  : $(if ($gpuList) { $gpuList -join ' + ' } else { 'Unknown' })"
Write-Info "Form factor : $(if ($isLaptop) {'Laptop'} else {'Desktop'})"

if ($isDomainJoined) {
    Write-Warn "Domain-joined - logon network tweak will be skipped"
    $script:Warnings.Add("Domain-joined: logon network tweak skipped")
}

# --- Dual-boot detection ---
# Fast Startup leaves NTFS in a locked state. Another OS mounting
# those volumes can corrupt them, so this must be caught.
$dualBootSigns = @()
$bcdOut = & bcdedit /enum firmware 2>&1 | Out-String
if ($bcdOut -match '(?i)ubuntu|fedora|debian|linux|grub|bazzite|arch|manjaro|opensuse|pop!_os|mint|nobara|steamos') {
    $dualBootSigns += "Linux entry in firmware boot list"
}
try {
    $esp = Get-Volume -ErrorAction SilentlyContinue |
           Where-Object { $_.FileSystemType -eq 'FAT32' -and $_.Size -lt 2GB -and $_.DriveLetter }
    foreach ($v in $esp) {
        $efiPath = "$($v.DriveLetter):\EFI"
        if (Test-Path $efiPath) {
            Get-ChildItem $efiPath -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                if ($_.Name -match '(?i)ubuntu|fedora|debian|grub|bazzite|arch|systemd|opensuse|nobara') {
                    $dualBootSigns += "Linux bootloader on ESP: $($_.Name)"
                }
            }
        }
    }
} catch { }
try {
    $unk = Get-Partition -ErrorAction SilentlyContinue |
           Where-Object { $_.Type -eq 'Unknown' -and $_.Size -gt 8GB }
    if ($unk) { $dualBootSigns += "$(@($unk).Count) large unreadable partition(s) - possible ext4/btrfs" }
} catch { }

$isDualBoot = ($dualBootSigns.Count -gt 0)
if ($isDualBoot) {
    Write-Warn "POSSIBLE DUAL-BOOT:"
    $dualBootSigns | ForEach-Object { Write-Warn "    - $_" }
} else {
    Write-Ok "No dual-boot detected"
}

# --- Is virtualisation actually in use? ---
$virtInUse = @()
if (Test-Path "$env:LOCALAPPDATA\Docker")            { $virtInUse += 'Docker Desktop' }
if (Test-Path "$env:ProgramFiles\Docker")            { $virtInUse += 'Docker' }
if (Test-Path "$env:ProgramFiles\Oracle\VirtualBox") { $virtInUse += 'VirtualBox' }
if (Test-Path "${env:ProgramFiles(x86)}\VMware")     { $virtInUse += 'VMware' }

# wsl.exe ships with Windows and prints "no installed distributions",
# so its mere presence proves nothing. Check for real distros.
if (Get-Command wsl.exe -ErrorAction SilentlyContinue) {
    try {
        $wslClean = ((& wsl.exe --list --quiet 2>&1 | Out-String) -replace "`0", '').Trim()
        if ($wslClean -and $wslClean -notmatch '(?i)no installed distributions|has no installed') {
            $virtInUse += 'WSL distribution(s)'
        }
    } catch { }
}
if (Get-Service vmms -ErrorAction SilentlyContinue) {
    try {
        $vms = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem `
               -ErrorAction SilentlyContinue | Where-Object { $_.Caption -eq 'Virtual Machine' }
        if ($vms) { $virtInUse += "$(@($vms).Count) Hyper-V VM(s)" }
    } catch { }
}

#endregion

#region --- Fast Startup ------------------------------------------------------

Write-Step "Fast Startup"

$doFastStartup = $true

if ($isDualBoot -and -not $ForceFastStartup) {
    Write-Fail "BLOCKED: dual-boot detected - skipping to protect your filesystem"
    $script:Warnings.Add("Fast Startup skipped - dual-boot")
    $doFastStartup = $false
}

if ($doFastStartup) {
    $hibEnabled = ((& powercfg /a 2>&1 | Out-String) -match '(?m)^\s+Hibernate\s*$')

    if (-not $hibEnabled) {
        $freeGB = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
        $ramGB  = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
        $needGB = [math]::Round($ramGB * 0.4, 1)

        if ($freeGB -lt $needGB) {
            Write-Fail "Not enough free space ($freeGB GB free, hiberfil.sys needs ~$needGB GB)"
            $script:Warnings.Add("Fast Startup skipped - low disk space")
            $doFastStartup = $false
        }
        elseif ($PSCmdlet.ShouldProcess('Hibernation', 'Enable')) {
            & powercfg /hibernate on 2>&1 | Out-Null
            Start-Sleep -Milliseconds 500
            if ((& powercfg /a 2>&1 | Out-String) -match '(?m)^\s+Hibernate\s*$') {
                Write-Ok "Hibernation enabled"
                $script:Changes.Add("Hibernation enabled")
            } else {
                Write-Fail "Hibernation blocked by firmware or policy"
                $doFastStartup = $false
            }
        }
    } else {
        Write-Skip "Hibernation (already enabled)"
    }
}

if ($doFastStartup) {
    Set-RegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' `
                 -Name 'HiberbootEnabled' -Value 1 -Description 'Fast Startup enabled'

    $gp = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
    if (Test-Path $gp) {
        $blk = (Get-ItemProperty $gp -Name 'HiberbootEnabled' -ErrorAction SilentlyContinue).HiberbootEnabled
        if ($null -ne $blk -and $blk -eq 0 -and $PSCmdlet.ShouldProcess('Fast Startup policy', 'Remove block')) {
            Remove-ItemProperty $gp -Name 'HiberbootEnabled' -Force -ErrorAction SilentlyContinue
            Write-Ok "Removed policy block on Fast Startup"
            $script:Changes.Add("Removed Fast Startup policy block")
        }
    }
}

#endregion

#region --- Shutdown, logon, startup delay ------------------------------------

Write-Step "Shutdown timeouts"

Set-RegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control' `
             -Name 'WaitToKillServiceTimeout' -Value '2000' -Type String `
             -Description 'Service shutdown 5s -> 2s'

Set-RegValue -Path 'HKCU:\Control Panel\Desktop' `
             -Name 'WaitToKillAppTimeout' -Value '3000' -Type String `
             -Description 'App shutdown 20s -> 3s'

Set-RegValue -Path 'HKCU:\Control Panel\Desktop' `
             -Name 'HungAppTimeout' -Value '3000' -Type String `
             -Description 'Hung app detection 5s -> 3s'

Write-Step "Logon network wait"

if ($isDomainJoined) {
    Write-Skip "Domain-joined - skipped, would break Group Policy at logon"
} else {
    Set-RegValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' `
                 -Name 'SyncForegroundPolicy' -Value 0 `
                 -Description 'Do not block logon waiting for network'

    Set-RegValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' `
                 -Name 'DelayedDesktopSwitchTimeout' -Value 0 `
                 -Description 'Removed delayed desktop switch'
}

Write-Step "Startup delay and UI"

Set-RegValue -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize' `
             -Name 'StartupDelayInMSec' -Value 0 `
             -Description 'Removed 10s startup app delay'

Set-RegValue -Path 'HKCU:\Control Panel\Desktop' `
             -Name 'MenuShowDelay' -Value '20' -Type String `
             -Description 'Menu show delay 400ms -> 20ms'

#endregion

#region --- GPU vendor specific -----------------------------------------------

Write-Step "GPU vendor optimisation"

if (-not ($hasNvidia -or $hasAmdGpu -or $hasIntelGpu)) {
    Write-Skip "No recognised GPU vendor - nothing to do"
}

# ---------- NVIDIA ----------
if ($hasNvidia) {
    Write-Info "NVIDIA detected. Delaying the app containers and killing telemetry."
    Write-Info "Display driver, NVIDIA Control Panel and G-Sync are NOT touched."

    # NvContainerLocalSystem  = NVIDIA App / GeForce Experience backend.
    # NvContainerNetworkSvc   = its network component.
    # Both are app-layer only. Delaying costs nothing at boot.
    #
    # NVDisplay.ContainerLocalSystem is deliberately EXCLUDED. It backs
    # the Control Panel and display feature persistence.
    Set-ServiceDelayed -ServiceName 'NvContainerLocalSystem'   -Label 'NVIDIA App container'
    Set-ServiceDelayed -ServiceName 'NvContainerNetworkService' -Label 'NVIDIA network container'

    # Pure telemetry. No functional loss.
    Set-ServiceDisabled -ServiceName 'NvTelemetryContainer' -Label 'NVIDIA telemetry'
    Set-ServiceDisabled -ServiceName 'NvTelemetryContainerLocalSystem' -Label 'NVIDIA telemetry (LS)'

    # Telemetry and profile-updater scheduled tasks that fire at boot/logon.
    Disable-BootTask -TaskPathPattern 'NvTmRep*'          -Label 'NVIDIA telemetry'
    Disable-BootTask -TaskPathPattern 'NvTmMon*'          -Label 'NVIDIA telemetry'
    Disable-BootTask -TaskPathPattern 'NvTmRepOnLogon*'   -Label 'NVIDIA telemetry'
    Disable-BootTask -TaskPathPattern 'NvProfileUpdater*' -Label 'NVIDIA profile updater'
}

# ---------- AMD GPU ----------
if ($hasAmdGpu) {
    Write-Info "AMD GPU detected. Delaying helper services and the tray launcher."
    Write-Info "Display driver, FreeSync, Anti-Lag and Chill are NOT touched."

    # AMDRSServ / AMD Crash Defender: crash reporting only.
    # AMDLog: diagnostic logging.
    #
    # 'atiesrxx' (AMD External Events Utility) is deliberately EXCLUDED.
    # It handles display hotplug and hotkeys and must start early.
    Set-ServiceDelayed  -ServiceName 'AMD Crash Defender Service' -Label 'AMD Crash Defender'
    Set-ServiceDelayed  -ServiceName 'AMDRSServ'                  -Label 'AMD Radeon Settings service'
    Set-ServiceDelayed  -ServiceName 'AMD Crash Defender'         -Label 'AMD Crash Defender'
    Set-ServiceDisabled -ServiceName 'AMDLog'                     -Label 'AMD diagnostic logging'

    # Adrenalin's tray auto-launch runs as a scheduled task named StartCN.
    # Disabling it means no tray icon on boot. The driver, FreeSync and
    # all game profiles keep working; the app opens from the Start menu.
    Disable-BootTask -TaskPathPattern 'StartCN*'             -Label 'AMD Adrenalin tray'
    Disable-BootTask -TaskPathPattern 'AMDInstallLauncher*'  -Label 'AMD installer'
    Disable-BootTask -TaskPathPattern 'AMDLinkUpdate*'       -Label 'AMD Link updater'
    Disable-BootTask -TaskPathPattern 'AMDRyzenMasterSDK*'   -Label 'AMD Ryzen Master SDK'
}

# ---------- Intel GPU ----------
if ($hasIntelGpu) {
    Write-Info "Intel graphics detected."
    # igfxCUIService backs the Intel graphics control panel and hotkeys.
    # On many laptops it is the only display control path, so it is left
    # alone. Only the telemetry-style pieces are safe to touch.
    Set-ServiceDisabled -ServiceName 'Intel(R) TileWorker Service' -Label 'Intel TileWorker'
    Disable-BootTask -TaskPathPattern 'Intel*Telemetry*' -Label 'Intel telemetry'
}

#endregion

#region --- CPU vendor specific -----------------------------------------------

Write-Step "CPU vendor optimisation"

if ($cpuVendor -eq 'Intel') {
    Write-Info "Intel CPU detected. Delaying Management Engine helper services."

    # LMS  = Intel Management and Security Application Local Management.
    # jhi_service = Intel Dynamic Application Loader host interface.
    # Both are management-layer, not required for boot or for the CPU
    # to function. Delaying is safe on consumer machines.
    #
    # 'Intel(R) TPM Provisioning Service' is deliberately EXCLUDED -
    # touching anything TPM-adjacent risks breaking anti-cheat.
    Set-ServiceDelayed -ServiceName 'LMS'         -Label 'Intel Management Engine (LMS)'
    Set-ServiceDelayed -ServiceName 'jhi_service' -Label 'Intel DAL host interface'
    Set-ServiceDelayed -ServiceName 'cplspcon'    -Label 'Intel content protection'
}
elseif ($cpuVendor -eq 'AMD') {
    Write-Info "AMD CPU detected."
    # AMD ships no auto-start CPU services that are safe and worth
    # delaying. Ryzen Master's driver is on-demand already. Nothing
    # to do here rather than invent a tweak for symmetry.
    Write-Skip "No AMD CPU services worth changing (this is normal)"
}
else {
    Write-Skip "CPU vendor not recognised - skipping vendor step"
}

#endregion

#region --- Startup apps ------------------------------------------------------

if (-not $SkipStartupApps) {
    Write-Step "Startup apps"
    Write-Info "Launchers that cost 5-10s each and lose nothing by opening on demand."

    $targets = @(
        'Discord','discord','Steam','Battle.net','EpicGamesLauncher',
        'Epic Games Launcher','GOG Galaxy','Ubisoft Connect','EADesktop',
        'Origin','Riot Client','Rockstar Games Launcher',
        'Spotify','Spotify Web Helper',
        'Microsoft Teams','com.squirrel.Teams.Teams','MSTeams',
        'Skype','Zoom','Slack',
        'CCXProcess','Adobe CCXProcess','AdobeGCInvoker-1.0',
        'Adobe Creative Cloud','AcrobatUpdater',
        'iTunesHelper','QuickTime Task','SunJavaUpdateSched',
        'Mozilla-Firefox-308046B0AF4A39CB',
        'GoogleChromeAutoLaunch','Opera Browser Assistant',
        'uTorrent','qBittorrent','MicrosoftEdgeAutoLaunch'
    )

    $pairs = @(
        @{ Run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
           Approved='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' },
        @{ Run='HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
           Approved='HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' },
        @{ Run='HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'
           Approved='HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32' }
    )

    # Task Manager's own on-disk format: byte 0 is 2 (enabled), 3 (disabled).
    $blob  = [byte[]](3,0,0,0,0,0,0,0,0,0,0,0)
    $found = 0

    foreach ($p in $pairs) {
        if (-not (Test-Path $p.Run)) { continue }
        $entries = @(); try { $entries = (Get-Item $p.Run).Property } catch { continue }

        foreach ($name in $entries) {
            if ($targets -notcontains $name) { continue }
            $found++
            try {
                if (-not (Test-Path $p.Approved)) {
                    if ($PSCmdlet.ShouldProcess($p.Approved,'Create')) {
                        New-Item -Path $p.Approved -Force -ErrorAction Stop | Out-Null
                    } else { continue }
                }
                $ex = (Get-ItemProperty $p.Approved -Name $name -ErrorAction SilentlyContinue).$name
                if ($ex -and $ex[0] -eq 3) { Write-Skip "$name (already disabled)"; continue }

                if ($PSCmdlet.ShouldProcess($name,'Disable at startup')) {
                    Add-Undo 'Startup' @{ Path = $p.Approved; Name = $name }
                    New-ItemProperty -Path $p.Approved -Name $name -Value $blob `
                        -PropertyType Binary -Force -ErrorAction Stop | Out-Null
                    Write-Ok "Disabled at startup: $name"
                    $script:Changes.Add("Startup disabled: $name")
                }
            }
            catch { Write-Fail "$name - $($_.Exception.Message)"; $script:Failed.Add("Startup: $name") }
        }
    }
    if ($found -eq 0) { Write-Skip "No known slow launchers found" }
} else {
    Write-Step "Startup apps"
    Write-Skip "Skipped by -SkipStartupApps"
}

#endregion

#region --- Virtualisation (optional) -----------------------------------------

Write-Step "Virtualisation features"

if (-not $RemoveVirtualisation) {
    if ($virtInUse.Count -gt 0) { Write-Skip "Not requested (in use: $($virtInUse -join ', '))" }
    else { Write-Skip "Not requested. Add -RemoveVirtualisation if no WSL/Docker/VMs." }
}
elseif ($virtInUse.Count -gt 0) {
    Write-Fail "BLOCKED: virtualisation in active use"
    $virtInUse | ForEach-Object { Write-Info "    - $_" }
    $script:Warnings.Add("Virtualisation removal blocked - in use")
}
else {
    Write-Info "No WSL/Docker/VMs found. Removing container and Hyper-V features."
    foreach ($f in @('Containers-SDN','Containers-HNS','Containers',
                     'Containers-DisposableClientVM','Microsoft-Hyper-V-All',
                     'HypervisorPlatform','VirtualMachinePlatform')) {
        try {
            $st = Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction SilentlyContinue
            if (-not $st)                { Write-Skip "$f (not present)";      continue }
            if ($st.State -ne 'Enabled') { Write-Skip "$f (already disabled)"; continue }
            if ($PSCmdlet.ShouldProcess($f,'Disable')) {
                Add-Undo 'Feature' @{ Name = $f }
                Disable-WindowsOptionalFeature -Online -FeatureName $f -NoRestart -ErrorAction Stop | Out-Null
                Write-Ok "Removed: $f"
                $script:Changes.Add("Removed feature: $f")
            }
        }
        catch { Write-Fail "$f - $($_.Exception.Message)"; $script:Failed.Add("Feature: $f") }
    }
    try {
        $ics = Get-Service SharedAccess -ErrorAction SilentlyContinue
        if ($ics -and $ics.StartType -ne 'Disabled') {
            Set-ServiceDisabled -ServiceName 'SharedAccess' -Label 'Internet Connection Sharing'
        }
    } catch { }
    Write-Info "hypervisorlaunchtype left alone - changing it would disable VBS."
}

#endregion

#region --- Anti-cheat prerequisite check (read-only) -------------------------

Write-Step "Anti-cheat prerequisites (read-only)"

try {
    if (Confirm-SecureBootUEFI -ErrorAction Stop) { Write-Ok "Secure Boot: On" }
    else {
        Write-Warn "Secure Boot: OFF - some anti-cheats will refuse to run"
        $script:Warnings.Add("Secure Boot is off")
    }
} catch { Write-Info "Secure Boot: could not determine" }

try {
    $tpm = Get-Tpm -ErrorAction Stop
    if ($tpm.TpmPresent -and $tpm.TpmReady) { Write-Ok "TPM 2.0: Present and ready" }
    elseif ($tpm.TpmPresent) { Write-Warn "TPM present but not ready" }
    else {
        Write-Warn "TPM: Not detected - Valorant will not launch"
        $script:Warnings.Add("TPM not detected")
    }
} catch { Write-Info "TPM: could not query" }

try {
    $dg = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName 'Win32_DeviceGuard' -ErrorAction Stop
    switch ($dg.VirtualizationBasedSecurityStatus) {
        2 { Write-Ok   "VBS: Running (required by Riot Vanguard)" }
        1 { Write-Info "VBS: Enabled, reboot pending" }
        default { Write-Info "VBS: Not running. Only matters for Valorant." }
    }
} catch { Write-Info "VBS: could not query" }

if ((& bcdedit /enum "{current}" 2>&1 | Out-String) -match 'hypervisorlaunchtype\s+Off') {
    Write-Warn "hypervisorlaunchtype is Off - this disables VBS and breaks Valorant"
    $script:Warnings.Add("hypervisorlaunchtype is Off")
}

#endregion

#region --- Save rollback and summarise ---------------------------------------

if ($script:Undo.Count -gt 0 -and -not $WhatIfPreference) {
    try {
        $existing = @()
        if (Test-Path $rollbackFile) {
            $existing = @(Get-Content $rollbackFile -Raw | ConvertFrom-Json)
        }
        # Newest first, so rollback restores the most recent state.
        $merged = @($script:Undo) + $existing
        $merged | ConvertTo-Json -Depth 5 | Set-Content -Path $rollbackFile -Encoding UTF8
    }
    catch { Write-Fail "Could not write rollback file - $($_.Exception.Message)" }
}

Write-Host "`n"
Write-Host "  +------------------------------------------------------+" -ForegroundColor White
Write-Host "  |                      SUMMARY                         |" -ForegroundColor White
Write-Host "  +------------------------------------------------------+" -ForegroundColor White

if ($script:Changes.Count -gt 0) {
    Write-Host "`n  Applied ($($script:Changes.Count)):" -ForegroundColor Green
    $script:Changes | ForEach-Object { Write-Host "    + $_" -ForegroundColor Green }
} else {
    Write-Host "`n  Nothing changed - already optimised, or WhatIf mode." -ForegroundColor DarkGray
}

if ($script:Warnings.Count -gt 0) {
    Write-Host "`n  Attention ($($script:Warnings.Count)):" -ForegroundColor Magenta
    $script:Warnings | ForEach-Object { Write-Host "    ! $_" -ForegroundColor Magenta }
}

if ($script:Failed.Count -gt 0) {
    Write-Host "`n  Failed ($($script:Failed.Count)):" -ForegroundColor Red
    $script:Failed | ForEach-Object { Write-Host "    x $_" -ForegroundColor Red }
}

Write-Host "`n  --- WHAT TO DO NOW ---" -ForegroundColor Yellow
Write-Host "  Restart once. That's it." -ForegroundColor Yellow
Write-Host "  After that, use Shut down rather than Restart day to day -" -ForegroundColor Yellow
Write-Host "  Restart always does a full cold boot by design." -ForegroundColor Yellow

if ($script:Undo.Count -gt 0 -and -not $WhatIfPreference) {
    Write-Host "`n  --- IF YOU WANT TO UNDO ---" -ForegroundColor DarkGray
    # If this is running as a temp-extracted copy from the all-in-one
    # .bat, $PSCommandPath points at a random file in %TEMP% that won't
    # exist next time. Tell them to just re-run the .bat instead.
    if ($PSCommandPath -match [regex]::Escape($env:TEMP) -and $PSCommandPath -match 'OptimizeBoot_') {
        Write-Host "  Just double-click the .bat file again and choose Undo" -ForegroundColor DarkGray
        Write-Host "  when it asks." -ForegroundColor DarkGray
    } else {
        Write-Host "  powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Rollback" -ForegroundColor DarkGray
    }
    Write-Host "  Rollback data saved to: $(Split-Path $rollbackFile -Leaf)" -ForegroundColor DarkGray
}
Write-Host ""

#endregion
