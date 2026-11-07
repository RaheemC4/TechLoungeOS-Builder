# Current USB flow, unreleased

The USB entry uses the imported source and current choices, or a matching completed export. Users choose the drive and label, optionally save an ISO, and explicitly confirm any replacement. USB-only removes its owned temporary ISO after verified writing. Automatic working storage excludes connected USB volumes. Original ISOs are only replaced through the explicit verified-save transaction. The opening screen also offers copying an already-finished ISO without rebuilding it; that action hides during imported customisation.

On 7 November the actual packaged .34 app passed source-picker cancellation, eligible-disk selection, custom label, cancelled erasure without writes, prepared-file reuse, full write and readback on the owned 16 GB virtual USB. That same USB booted its WinPE in BIOS and UEFI with Secure Boot enabled and TPM 2.0 attached. No physical disk was tested. Version .35 changes version identity and upgrade wording only; the USB engine is unchanged. See TEST-RESULTS.md for the exact media and receipts. Public hosting and live hosted-update verification still require approved publication.

--- Earlier implementation notes below ---

# Updates and bootable USB development

## Independent app releases

The source is isolated in the standalone TechLoungeOS-Builder repository. Its configured future release source is RaheemC4/TechLoungeOS-Builder, separate from TechLoungeTweaks. No remote repository, push, PR or release was created. GitHub 404 is reported as unavailable, not up to date.

The app checks on opening. Only a newer, product/channel/architecture-matched manifest signed by the embedded P-256 key can authorise package installation. Package size, paths, hashes and executable version are checked before staging and again before applying. Verified updates install after app exit; an ISO/USB operation prevents closure. Another running copy blocks replacement. Replacement retains unrelated user files, atomically swaps existing files, and journals rollback state. An interrupted transaction exposes a recovery action and blocks media work. The package also contains Restore previous version.cmd for recovery when the main executable cannot open.

Startup uses a per-install Windows kernel mutex. It does not access ProgramData update storage. Storage creation retains the Windows-selected owner and restricts access to administrators/SYSTEM with inherited owner-rights protection. Failure to secure the cache prevents an update, rather than blocking startup or weakening access control.

ReleaseTool packages a complete published folder and verifies the archive. Before packing, include the trusted x64 private runtime under integration/WebView2Runtime and the NVIDIA inspector/profile under integration/resources. The inspector licence and matching wimlib source/licences are included by the publish project. A raw dotnet publish alone is incomplete; packaging now stops before creating an archive when these required files are missing. Operator commands:

    dotnet publish TechLoungeOS/Builder -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=0.1.27 -p:AssemblyVersion=0.1.27.0 -p:FileVersion=0.1.27.0 -o <new-publish-folder>
    dotnet run --project TechLoungeOS/ReleaseTool -c Release -- pack <publish-folder> <new-output-folder> 0.1.27.0 <private-key-metadata-or-encrypted-p8>

The local signing identity remains in the current user's Windows CNG keystore. private/release-key.json identifies it and is ignored by Git. It is not a substitute for a recoverable backup. Before publishing, the operator must use ReleaseTool backup-key with an interactive passphrase and store the encrypted backup and passphrase separately. No backup passphrase was chosen or plaintext private key exported. Never include private/ in releases. Loss of the signing key requires a deliberate trust migration. Older builds without this updater cannot gain automatic updates retroactively.

A real local signed admin-context update from 0.1.2 to 0.1.5 passed, including retention of unrelated files. Native test 1066 interrupted an actual replacement after one committed move; the real signed recovery helper restored 435 original files and reopened the app while retaining an unrelated read-only file. This verifies that process-interruption boundary, not every power-loss case. Hosted release discovery remains an acceptance gate.

## USB implementation and limits

Create setup USB prepares a private copy of a Windows 11 x64 ISO before confirming erasure. It uses Windows storage tools and DISM, preserves source EFI/WinPE files, retains compressed ESDs and splits large ordinary WIMs into Setup-compatible SWMs. SHA256 verifies input stability, prepared files and every copied file.

Only identified USB-bus disks with 512-byte logical sectors are eligible. System, boot, pagefile, read-only and offline disks are excluded. The user must select a drive and type its exact ERASE disk-number confirmation. Identity is rechecked immediately before erase and afterwards. The writer uses a volume GUID to avoid copying to a reused drive letter. This is destructive to the confirmed target; no physical drive was used during development tests.

The writer uses the source Microsoft boot chain and signed bootsect. Ordinary WIM media uses an active MBR/FAT32 partition capped at31GiB. Large ESD/data files use an active FAT32 boot partition plus an NTFS data partition on the same USB. Both volumes receive full file readback, and planning includes duplicated boot files and filesystem headroom. The two-partition route currently awaits native boot/install acceptance; the historical checks below cover the earlier single-partition route. Native 1058 wrote and verified all 1,117 files on the owned 16,000,000,000-byte virtual USB from ISO 1057. Fresh1060 booted that exact read-only USB using Microsoft Secure Boot firmware and TPM 2, without an installation CD fallback. Legacy BIOS boot and physical USB/firmware testing remain open. Pre-split source SWMs and4Kn devices remain unsupported. Boot files above FAT32's file-size limit are rejected before erasure; separate boot/data layouts require USB drives no larger than2TiB. Firmware compatibility and Secure Boot depend on retained source boot files; the builder does not certify altered third-party bootloaders.

Rufus source was reviewed at commit 65f7402a33d02c31243d118196ecd04407608845. Its GPL source served as reference; no Rufus code or UEFI:NTFS bootloader was copied into this original C#/Windows-tools implementation. Source: https://github.com/pbatard/rufus . Microsoft's split-image deployment guidance: https://learn.microsoft.com/windows-hardware/manufacture/desktop/split-a-windows-image--wim--file-to-span-across-multiple-dvds?view=windows-11 .

A real owned virtual USB passed format/copy and verification of all 1,093 files, then booted in a separate fresh VM. PE reported Secure Boot enabled and a TPM 2.0 device; its original tools and new Setup launcher worked. Full rebuilt image installation and physical-device compatibility remain separate gates. Do not describe this development package as ready for physical reinstall testing.
