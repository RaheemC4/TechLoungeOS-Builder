#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param([Parameter(Mandatory)][string]$Workspace, [switch]$Discard)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Workspace).TrimEnd('\')
$journal = Join-Path $root 'state.json'
if (!(Test-Path -LiteralPath $journal -PathType Leaf)) { throw 'No TechLoungeOS state.json found.' }
$state = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
$mount = Join-Path $root 'mount'
$wim = Join-Path $root 'selected.wim'
if ($state.mount -ne $mount -or $state.wim -ne $wim) { throw 'Journal paths do not match this workspace.' }
$lock = [IO.File]::Open((Join-Path $root 'build.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $images = @(Get-WindowsImage -Mounted | Where-Object { $_.Path -eq $mount })
    if (!$images.Count) { Write-Output 'No image remains mounted at this workspace. Logs and media were retained.'; return }
    if ($images.Count -ne 1 -or $images[0].ImagePath -ne $wim) { throw 'Mounted image does not match the workspace journal. Inspect DISM manually.' }
    $images | Format-List Path, ImagePath, ImageIndex, MountStatus
    if ($Discard -and $PSCmdlet.ShouldProcess($mount, 'Discard only this workspace image and its uncommitted changes')) {
        & "$env:SystemRoot\System32\dism.exe" /English /Unmount-Image "/MountDir:$mount" /Discard
        if ($LASTEXITCODE -ne 0) { throw "DISM recovery failed: $LASTEXITCODE. Retain the workspace for manual investigation." }
    } elseif (!$Discard) { Write-Output 'Inspection only. Use -Discard to request targeted recovery with confirmation.' }
} finally { $lock.Dispose() }
