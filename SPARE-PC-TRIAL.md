# Spare-PC trial

**Ready for a supervised trial on the spare PC. This is a pre-release candidate, not universal hardware or public-release certification.** ISO 1250 passed the full 16 GB virtual-USB readback, a fresh Secure Boot/TPM2 installation, all 27 installation checks, internal recovery with installer media absent, and an ordinary restart with unchanged once-only receipts. Builder 0.1.20 passed its actual local app update and 292-file readback. Physical USB and PC acceptance remain to be done.

## Files

- Tested ISO: `D:\TechLoungeOS-validation\export-1250\Ghost-full-1250.iso` (13,277,698,048 bytes).
- ISO SHA256: `CDBB0604EB3F625B7E923D05792F07E48C86A2601361F4B1905F16647A95CDB8`.
- Portable Builder 0.1.20: `D:\TechLoungeOS-validation\release-0.1.20\TechLoungeOS-Builder-win-x64.zip`. Extract the whole ZIP and open `TechLoungeOS.Builder.exe`.
- App ZIP SHA256: `FC0A33B6488C9403D241F905F6BF84F036E2A44D58E2C34A805B3F8B442ACB22`.

The original Ghost download was independently rehashed unchanged. This candidate retains the source Windows build and the tested source tweaks/tools. The approved additions include Edge, Store/Xbox, recovery, runtimes, Office and the Builder defaults. It does not restore arbitrary other stripped features. Changing Builder preferences requires building a new ISO and testing that new image.

## Create the USB

1. Back up the spare PC's files and anything on the 16 GB USB. Writing the USB erases it; a clean Windows installation can erase the PC's files.
2. Choose **Create USB** in Builder and select the tested ISO above. This separate flow accepts an existing ISO; rebuilding is unnecessary merely to make the USB.
3. Identify the USB by name, capacity and serial, confirm the exact drive and wait for full verification. No physical USB has been written by the automated tests.
4. Boot its UEFI entry on the i7-9700K PC. Disconnect unrelated data drives during installation if practical.

## Install and check

1. In the WinPE wizard, choose your username. Password and PC name are optional. With no PC name, Raheem becomes RaheemPC (subject to Windows naming limits). A nonempty password plus automatic sign-in passed the current VM test; an earlier image tested the blank-password path. Hello PIN enrolment and 1Password passkeys still need real-device acceptance and use Windows' enrolment flow.
2. Select only the intended Windows disk in Setup. Check its capacity before deleting partitions. This candidate uses UK region/keyboard settings; use Builder to choose others before a new build.
3. After Setup restarts, expect the created account's desktop without further account, network, region, keyboard or privacy questions. Record any unexpected question.
4. Keep internet connected. Selected user apps download at first sign-in and their desktop icons appear gradually. Wait for provisioning before treating a missing app as a failure. Status: `%LOCALAPPDATA%\TechLoungeOS\status.txt`; details: `first-login.log` in that folder. The VM recovered an initial transient catalogue error through its normal retry, without manual repair.
5. Check Discord, Brave, Chrome, Steam, MSI Afterburner, Stremio, Proton VPN, ChatGPT, HWiNFO64, Tweaks and Office. Discord uses the normal icon and stable OpenAsar/repair recipe. Office, runtimes and prepared Store/Xbox packages are bundled. Account sign-in, licensing, games and actual GPU operation remain separate checks. Office and Xbox may present their own first-run choices when manually opened.
6. Check the blue wallpaper, left taskbar without search/widgets/Store pin, no This PC/CPU-Z desktop icons, dark mode and input settings. Personalise retains all 15 presets. Native 1280x800 and 3840x2160 application/refresh passed; physical multi-monitor, portrait and display-driver changes still need hardware testing.
7. Restart normally. Apps should remain installed and post-install actions must not repeat. The supplied Standalone contains Ohook activation, separate from ordinary Office deployment; review that documented behaviour before using or sharing the image.
8. Check Advanced startup can enter Recovery, then choose Continue. Do not choose Reset this PC merely to test its menu. The VM tested internal boot, advanced tools and a read-only Command Prompt operation, not a destructive reset or every repair tool.

Record the actual PC/GPU, unexpected questions, failed apps, display issues and exact errors. Keep setup logs. Windows upgrades inside the ISO, generic non-Ghost custom WinPE and the hosted Builder update channel remain unfinished. Automatic app-update discovery needs the first separately approved GitHub release; the signed local update path has passed. Do not distribute this trial as a completed universal release.
