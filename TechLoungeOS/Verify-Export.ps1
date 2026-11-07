#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Config)
$ErrorActionPreference = 'Stop'
$options = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
$workspace = [IO.Path]::GetFullPath($options.Workspace)
$mount = Join-Path $workspace 'verify-mount'
if (Test-Path -LiteralPath $mount) { throw 'Use a new verification mount directory.' }
$buildLock = [IO.File]::Open((Join-Path $workspace 'build.lock'), 'Open', 'ReadWrite', 'None')
$attached = [Collections.Generic.List[string]]::new()
$mounted = $false
function Open-Iso([string]$path) {
    $disk = Get-DiskImage -ImagePath $path
    if (!$disk.Attached) { $disk = Mount-DiskImage -ImagePath $path -PassThru; $attached.Add($path) }
    $volume = $disk | Get-Volume
    if (!$volume.DriveLetter) { throw "No accessible volume: $path" }
    return "$($volume.DriveLetter):\"
}
try {
    $source = Open-Iso $options.Iso
    $output = Open-Iso $options.Output
    $bootHashes = @{}
    foreach ($relative in @('boot\etfsboot.com','efi\microsoft\boot\efisys.bin','efi\boot\bootx64.efi','sources\boot.wim')) {
        $before = (Get-FileHash -LiteralPath (Join-Path $source $relative) -Algorithm SHA256).Hash
        $after = (Get-FileHash -LiteralPath (Join-Path $output $relative) -Algorithm SHA256).Hash
        if ($before -ne $after) { throw "Boot/setup file changed: $relative" }
        $bootHashes[$relative] = $after
    }
    $image = Join-Path $output 'sources\install.wim'
    $editions = @(Get-WindowsImage -ImagePath $image)
    if ($editions.Count -ne 1 -or $editions[0].ImageIndex -ne 1) { throw 'Expected one installation image at index 1.' }
    New-Item -ItemType Directory -Path $mount | Out-Null
    $mounted = $true
    & "$env:SystemRoot\System32\dism.exe" /English /Mount-Image "/ImageFile:$image" /Index:1 "/MountDir:$mount" /ReadOnly /CheckIntegrity
    if ($LASTEXITCODE -ne 0) { throw "Verification mount failed: $LASTEXITCODE" }
    $packages = @(Get-AppxProvisionedPackage -Path $mount)
    $features = @(Get-WindowsOptionalFeature -Path $mount)
    foreach ($component in $options.Remove) {
        if ($component.Kind -eq 'Appx' -and @($packages | Where-Object DisplayName -eq $component.Name).Count) {
            throw "Selected app remains provisioned: $($component.Name)"
        }
        if ($component.Kind -eq 'Feature' -and @($features | Where-Object { $_.FeatureName -eq $component.Name -and "$($_.State)" -notlike 'Disabled*' }).Count) {
            throw "Selected feature is not fully disabled: $($component.Name)"
        }
    }
    $baseline = @(Get-Content -LiteralPath (Join-Path $workspace 'appx-before.json') -Raw | ConvertFrom-Json)
    $retained = @($baseline | Where-Object { $_.DisplayName -notin @($options.Remove | Where-Object Kind -eq 'Appx' | ForEach-Object Name) })
    foreach ($package in $retained) {
        if ($package.PackageName -notin $packages.PackageName) { throw "Unselected provisioned package disappeared: $($package.PackageName)" }
    }
    $payload = Join-Path $mount 'ProgramData\TechLoungeOS'
    $hashes = Get-Content -LiteralPath (Join-Path $payload 'payload-sha256.json') -Raw | ConvertFrom-Json
    $checked = 0
    foreach ($entry in $hashes.PSObject.Properties) {
        $file = [IO.Path]::GetFullPath((Join-Path $payload $entry.Name))
        if (!$file.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload hash path escapes its folder.' }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Value) { throw "Payload checksum mismatch: $($entry.Name)" }
        $checked++
    }
    $tokens=$null; $errors=$null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $payload 'first-login.ps1'),[ref]$tokens,[ref]$errors) | Out-Null
    if ($errors.Count) { throw ($errors | Out-String) }
    if (!(Test-Path -LiteralPath (Join-Path $mount 'Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\TechLoungeOS.cmd'))) { throw 'Default-profile provisioning entry missing.' }
    [pscustomobject]@{
        status='Verified exported ISO contents'; edition=$editions[0].ImageName
        checkedPayloadFiles=$checked; retainedProvisionedPackages=$retained.Count
        selectedComponents=@($options.Remove); bootFilesUnchanged=$bootHashes
        installationValidated=$false; firstLoginExecuted=$false
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($Config + '.verified.json')
} finally {
    if ($mounted) {
        & "$env:SystemRoot\System32\dism.exe" /English /Unmount-Image "/MountDir:$mount" /Discard
        if ($LASTEXITCODE -ne 0) { Write-Warning "Verification mount requires targeted recovery: $mount" }
    }
    foreach ($path in $attached) { Dismount-DiskImage -ImagePath $path | Out-Null }
    $buildLock.Dispose()
}
