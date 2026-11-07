# TechLoungeOS Builder

Build a Windows 11 installer with your apps, desktop and setup preferences already chosen.

[![Watch the 25-second preview](docs/preview.png)](docs/preview.mp4)

- Download Windows 11 from Microsoft or import an ISO, then choose an edition and components.
- Search for apps or select the recommended set, including Discord, Stremio, Proton VPN and HWiNFO64.
- Create a local account and device name in the redesigned WinPE setup.
- Include Shiftd, HatchZip, Microsoft 365, runtimes, Store/Xbox and TechLoungeTweaks integration.
- Reach the desktop first, then finish installs quietly with progress and retry controls.
- Run the Boot Optimizer after selected apps finish installing.
- Choose OLED-friendly wallpapers, colours and desktop defaults.
- Choose setup USB, saved ISO, or both with clear output buttons.
- Apply supported Windows updates with checks for retained components and settings.
- Check for signed Builder updates automatically, with installation after the app closes.

Your original ISO is retained unless you explicitly choose and confirm replacement. This is a separate app from TechLoungeTweaks. Windows, Office and third-party apps retain their own licensing requirements.

Requires Windows 11 x64, administrator permission, local NTFS working space and the Microsoft ADK Deployment Tools for ISO export. Selected online apps need internet at first sign-in.

Office uses the normal Microsoft deployment tools and your own account or licence. Activation tools are not included.

[Download the Windows x64 app](https://github.com/RaheemC4/TechLoungeOS-Builder/releases/latest). Extract the complete ZIP and open TechLoungeOS.Builder.exe.

Ghost and standard-image servicing/install checks passed for their recorded media, including selected apps, Office, TechLoungeTweaks and ordinary recovery. The current Ghost media passed full file verification and 16 GB virtual USB writing, BIOS boot and UEFI Secure Boot with TPM 2.0. Physical hardware, arbitrary modified images and unknown Windows upgrade routes are not certified. See [test results](TechLoungeOS/TEST-RESULTS.md) and [compatibility](TechLoungeOS/COMPATIBILITY.md). The video uses edited app controls and VM captures.

[Build and technical documentation](TechLoungeOS/README.md)
