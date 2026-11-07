# TechLoungeOS Builder

Create a customised Windows 11 x64 ISO or a bootable setup USB. This is a separate app from TechLoungeTweaks.

**Unreleased.** Current native acceptance and remaining limitations are recorded in [TEST-RESULTS.md](TEST-RESULTS.md) and [COMPATIBILITY.md](COMPATIBILITY.md). An earlier successful installation does not certify a newer image.

## Requirements

Use Windows 11 x64 with administrator permission and local NTFS working space. The Builder automatically downloads, verifies and protects the small Microsoft ISO creation component on first use; no separate ADK setup or executable selection is required. Keep the PC online for this initial preparation. Cached verified tooling is reused. Extract the complete Builder package, including its integration folder. A self-contained package includes the Builder runtime; the development build needs the .NET 10 Desktop Runtime. Required downloads and selected online apps need internet access. Free-space checks run before servicing or USB erasure; actual requirements depend on the source and selected output.

## Create your installer

1. **Windows image:** import an ISO and choose the edition you are licensed to use. The Windows-version section checks updates for that image. The header's **Check for updates** checks the Builder app.
2. **Components:** choose a preset or select optional components to remove. Existing removals and source configuration are the preservation baseline.
3. **Apps & setup:** search for apps or choose the recommended selection. Click a selected app again to deselect it. Required software is packaged automatically. Discord selections use the TechLounge Discord/OpenAsar recipe and its update repair launcher.
4. **Personalisation:** choose a wallpaper and colour, or keep the source background/use a custom image. Choose region and keyboard; UK English is the default.
5. **Finish:** save an ISO, create a setup USB, or do both. Your imported image and choices carry through automatically. Give the USB a label and choose the correct target drive. USB erasure requires explicit confirmation.

To copy a finished ISO without customising it, use **Create setup USB** on the opening page. This copies its existing contents; it does not add Builder customisations.

USB-only output uses managed temporary files and keeps no additional ISO after a successful verified write. Saving over an existing ISO requires explicit replacement confirmation; a new image is staged and checked first. Automated tests always retain original downloads.

Boot the installer and use its WinPE account wizard to choose a local username and optional password. The PC name is optional and defaults to a valid name derived from the username followed by `PC`. Automatic sign-in and a later Windows Hello setup invitation are separate choices. A Hello PIN still authenticates passkey/security prompts; it is not generated or bypassed by the Builder. Existing source setup hooks are retained and can affect installation behaviour.

## Included setup

Microsoft 365, Edge, runtimes, Store/Xbox provisioning, HatchZip and TechLoungeTweaks integration are required inclusions. Microsoft 365 uses Microsoft's deployment tools and requires the user's own account/licence. Activation launchers are excluded from deployment and release assets.

The desktop opens before Builder-owned long installations. Installs then run quietly with progress and retry information. Completed steps are retained on retry. Boot Optimizer and final power defaults wait for required apps/installers to finish. Arbitrary publisher installers, existing source scripts, firmware or driver initialisation can still show UI or cause delays.

Recommended ISO tweaks exclude Windows Update pauses and Defender changes. Graphics work is gated by the installed target's display hardware: NVIDIA receives its compatible driver/profile route; AMD/Intel use hardware-matched Windows Update drivers. Drivers are downloaded on the target, not bundled into the ISO.

Generated installations also apply the requested dark desktop, left-aligned clean taskbar, mouse defaults and Never idle timeouts. Deployment defaults include Never notify UAC, relaxed Internet launch prompts and legacy SMB client/guest/signing settings; account for these security-reducing choices when deciding where to deploy the installer. Enforced organisation policy is not overridden. [Setup packs](SETUP-PACKS.md) and [recovery guidance](../SPARE-PC-TRIAL.md) describe the relevant setup files and logs.

## Updates and compatibility

Windows updating uses Microsoft's compatible cumulative packages and explicitly reviewed enablement routes. It does not replace a modified image with stock Windows. Component, app, service, policy and relevant removed-payload changes outside the reviewed rules stop export. The exact approved internal-wallpaper correction has a checksum-bound donor prerequisite and must retain the visible source artwork. Other source corruption is not automatically repaired. Detecting a future Windows release does not establish a supported upgrade path.

Supported input layout is Windows 11 x64 `sources\install.wim` or `install.esd`. Split-SWM import, ARM64, unknown custom layouts and already integrated Builder payloads are not supported. Secure Boot depends on the source signing chain and target firmware. Bypassing setup checks cannot add missing CPU instructions or unavailable hardware drivers. Missing required recovery, framework or servicing infrastructure stops the build rather than silently restoring unrelated stripped features. A particular Ghost, tiny11 or ReviOS image needs its own compatibility evidence.

The automatic 16 GB media budget never removes requested or inherited content to make an oversized image fit. USB preparation checks actual capacity. It splits ordinary WIMs for FAT32 or uses FAT32 boot plus NTFS data for large compressed images. Selected USB partitions are erased only after confirmation.

Builder updates use a signed manifest and package checks, apply after the app closes, and preserve rollback/recovery evidence. Hosted delivery needs a published release in the dedicated Builder repository; local update fixtures are not proof of hosted delivery.

## Logs and recovery

Activity presents readable progress and retains technical output for diagnosis. Workspace state, preservation reports and logs identify incomplete operations. First-login progress and per-step logs retain failures for retry; machine installers also keep protected receipts under `%ProgramData%\TechLoungeOS` and `%WINDIR%\Setup`. A completed setup marker is not a substitute for an actual recovery boot test.

Cancellation waits for the current DISM operation before discarding an uncommitted mount. Keep the PC powered on until cancellation/cleanup completes. Use the supplied [workspace recovery script](Recover-Workspace.ps1) only after reviewing the recorded mount and state; do not remove live mounts or bypass image locks.

## Development

Use the .NET 10 SDK on Windows:

```powershell
dotnet build .\TechLoungeOS\Builder\Builder.csproj -c Release
dotnet run --project .\TechLoungeOS\Tests\Tests.csproj
dotnet publish .\TechLoungeOS\Builder\Builder.csproj -c Release -r win-x64 --self-contained true -o .\TechLoungeOS\artifacts\app
```

Account-setup compilation uses the SDK's deterministic C# compiler against the installed .NET Framework libraries. Run packaged-resource checks on the exact output being distributed. Read `AGENTS.md` before implementation, keep development on a feature branch, and obtain approval before publishing, pushing, merging or creating a remote release.