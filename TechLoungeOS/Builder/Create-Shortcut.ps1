$ErrorActionPreference = 'Stop'
$builderExecutablePath = Join-Path $PSScriptRoot 'TechLoungeOS.Builder.exe'
if (-not (Test-Path -LiteralPath $builderExecutablePath -PathType Leaf)) {
    throw 'Keep this script beside TechLoungeOS.Builder.exe before creating its Start menu shortcut.'
}
$builderShortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'TechLoungeOS Builder.lnk'
$builderShortcutShell = New-Object -ComObject WScript.Shell
try {
    $builderShortcut = $builderShortcutShell.CreateShortcut($builderShortcutPath)
    try {
        if ((Test-Path -LiteralPath $builderShortcutPath) -and $builderShortcut.TargetPath -ne $builderExecutablePath) {
            throw 'A TechLoungeOS Builder shortcut already points elsewhere. Move that shortcut before creating this one.'
        }
        $builderShortcut.TargetPath = $builderExecutablePath
        $builderShortcut.WorkingDirectory = $PSScriptRoot
        $builderShortcut.IconLocation = "$builderExecutablePath,0"
        $builderShortcut.Description = 'Customise a Windows 11 installation image.'
        $builderShortcut.Save()
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($builderShortcut) }
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($builderShortcutShell) }
Write-Output "Start menu shortcut created: $builderShortcutPath"
