# Setup packs and TechLounge recommendations

## Office in every app-created ISO

The app always includes Microsoft 365 Apps for enterprise (`O365ProPlusRetail`, x64, en-US). The production build engine rejects attempts to disable it, including calls outside the UI. Users activate with their own eligible account. No product keys or activation commands are added. The builder downloads Microsoft's current signed Office Deployment Tool and uses download mode only on the build computer. A complete existing Office data cache can be reused; the tool is refreshed even with cached data. Download or verification failures stop the build rather than exporting an ISO without Office.

The layout follows the user's supplied guide excerpt:

```text
sources\$OEM$\$$\Setup\Scripts\
  SetupComplete.cmd
  setup.exe
  Configuration.xml
  Office\Data\...
```

The later desktop-first choice changes execution timing: SetupComplete registers the fixed machine queue, and `TechLoungeOS-Office.cmd` retains the administrator guard, `cd /d "%~dp0"` and literal `setup.exe /configure Configuration.xml` command. After a stable desktop exists, the queue copies and verifies Office into its protected cache and runs that command there. Additional checks validate the installed product, architecture and Word/Excel/PowerPoint executables. The local source and Office version are pinned, with CDN fallback disabled. Office updates stay enabled. No application shutdown or reboot is forced. The configuration excludes OneDrive, Teams, new Outlook and Publisher and does not remove conflicting existing Office installations.

Existing source setup commands retain their original bytes and OEM-over-image precedence. Conflicting Office payload paths stop the build rather than being overwritten. Status and transcripts now live in `ProgramData\TechLoungeOS\MachineSetup\payloads\Office\TechLoungeOS-Office-State`. The progress window offers retry and keeps successes. Automatic activation launchers are excluded from generated deployment and release assets.

Microsoft documents SetupComplete restrictions for OEM keys and custom deployment paths. The WinPE-created administrator's elevated first-logon callback also registers Builder-owned deferred tasks, covering skipped SetupComplete for that supported wizard flow. It does not force inherited source scripts to run or change Windows keys. Actual OEM-channel acceptance remains a separate test gate. The guide page could not be retrieved during validation; implementation follows the commands and layout quoted by the user, checked against Microsoft's [Office Deployment Tool documentation](https://learn.microsoft.com/microsoft-365-apps/deploy/overview-office-deployment-tool).

## App choices

The Apps & setup page keeps WinGet search and reversible recommended selections. Clicking a highlighted app deselects it. Discord is shown with its normal name/icon, while only the approved OpenAsar/repair recipe is selectable. Plain/PTB/other-channel Discord choices are excluded.

## Offline runtime pack

Included in every build. During a build, WinGet downloads the Microsoft installers for VC++ 2005, 2008, 2010, 2012, 2013 and the current v14 family, both x86 and x64, and .NET Desktop 10 for both architectures. WinGet verifies its catalog hashes; the builder additionally requires valid Microsoft Authenticode signatures. The full DirectX June 2010 redist is downloaded from Microsoft and signature-checked. No installer executes on the build host. Per-build receipts retain resolved versions, hashes and silent switches. Patch versions are resolved at build time, rather than embedded permanently in the app.

The pack is staged under `Windows\Setup\TechLoungeOS-Runtimes` and copied into the protected machine cache after desktop readiness. SetupComplete performs registration only. Existing/newer runtime registrations are retained where identifiable; installer already-installed/newer-version results are accepted. There is no uninstall, forced restart or Windows feature restoration. Successful steps are retained for recovery. Legacy DirectX adds side-by-side game libraries; it does not replace the OS DirectX version. The DirectX package cannot generally be uninstalled.

.NET 8 compatibility is also included while supported, through 10 November 2026, and omitted from new builds afterwards. .NET 10 does not replace applications' .NET 8 dependency. Changing a support deadline or adding a new runtime family requires a reviewed builder update; dynamic patch downloads do not make all future versions automatically compatible.

## Machine-wide Microsoft Store and Xbox provisioning

Edge Stable is also included in every app-created ISO. Its current x64 enterprise MSI is downloaded during configuration, checked against Microsoft's size/SHA-256 metadata and verified as Microsoft-signed. Windows setup runs it quietly with no forced restart. Default browser associations are merged through DISM for new users; PDF/text/other associations and existing user choices are retained. The original association file is backed up. Edge's own updater remains installed, and ordinary first-run/account/permission prompts are retained. The app picker marks Edge Stable included, while other browser channels and WebView2 remain separate choices. Offline setup status is under Windows\Setup\TechLoungeOS-Edge. This is an explicit user-authorised exception to browser stripping.

Included in every build. This is an explicit exception to retaining stripped components. The intended package set is Microsoft Store, Store Purchase App, Xbox, Gaming Services, Xbox Identity Provider and Game Bar. Unrelated source removals remain the baseline. Setup stages the original signed archives and uses PackageManager's all-user provisioning API; each user's first-login step registers and checks the packages for that profile. This is distinct from installing Xbox only in the builder's own account.

The acquisition path tries WinGet's official `msstore` source in silent authentication mode, then Microsoft component catalogues. It does not sign in automatically or obtain paid licences. Production acquisition/validation of all six sets and targeted native provisioning/new-standard-user registration passed on 26 October. The complete generated Windows Setup/SYSTEM path in a rebuilt ISO remains unverified; restoration is not guaranteed for every stripped image. Failed acquisition stops export. See STORE-XBOX.md for service, integrity and licensing limits.

The internal build/test interface can accept a trusted offline package cache; this is not an ordinary app control. Create a subfolder for each product ID below. Each must contain one matching signed main APPX/MSIX package or bundle, together with its signed dependencies. Dependencies may be nested. Only the listed Microsoft framework families are accepted: VCLibs, UI.Xaml, .NET Native, WindowsAppRuntime and Store Engagement. Manifest identities, DTD restrictions, counts, size bounds, Microsoft signatures and hashes are checked. No paid entitlement or licence bypass is provided.

| Product folder | Package identity |
| --- | --- |
| 9WZDNCRFJBMP | Microsoft.WindowsStore |
| 9NBLGGH4LS1F | Microsoft.StorePurchaseApp |
| 9MV0B5HZVK9Z | Microsoft.GamingApp |
| 9MWPM2CQNLHN | Microsoft.GamingServices |
| 9WZDNCRD1HKW | Microsoft.XboxIdentityProvider |
| 9NZKPSTSNW4P | Microsoft.XboxGamingOverlay |

When explicitly enabled, the machine script enables existing Appx/licensing/update dependencies if disabled, removes the machine Store block values, provisions the packages and enables existing Xbox/Gaming Services. First login removes the corresponding source user Store blocks and registers the packages. Missing service infrastructure, incompatible packages, deployment restrictions or licensing errors remain failures. No registry service fabrication, RestoreHealth, unknown Store mirror, domain-policy bypass or user sign-in is attempted. Domain policy can reapply restrictions. Existing accounts baked into custom media remain outside the default-profile Startup guarantee.

## Setup and recovery constraints

SetupComplete can be skipped with OEM product keys outside supported Enterprise/Server cases, and custom deployment tools may bypass it. The builder preserves the existing batch body byte-for-byte at its original path and prefixes its own invocation. It saves the previous body alongside each setup pack. Inherited scripts with NUL or UTF-8 BOM are rejected rather than rewritten into a different encoding. Scripts which inspect their own bytes may still need a separately verified integration. Original ISO files are never edited.

Setup scripts run as SYSTEM or through an explicit administrator recovery action. Their files, status, transcript, success markers and completion marker stay under `Windows\Setup\TechLoungeOS-Runtimes` or `Windows\Setup\TechLoungeOS-StoreXbox`. No reboot is forced. If a hook was skipped or a step failed, first login reports it in `%LOCALAPPDATA%\TechLoungeOS\status.txt` while allowing independent app steps to proceed. An administrator can run the corresponding `install.ps1` to retry failures, then sign out and back in. Standard-user provisioning does not elevate automatically.

## Discord and direct app recipes

Discord + OpenAsar uses the unchanged TechLoungeTweaks launcher sources in `DiscordLauncher`, compiled into the builder package. The builder downloads the official OpenAsar nightly asset, requires GitHub's SHA-256 digest and bundles the verified cache. At first login, it installs or reuses stable Discord with WinGet no-upgrade, merges the existing TechLounge OpenAsar settings with a settings backup, and integrates the repair launcher into current-user shortcuts. A fresh installer-created Discord process may be closed; an existing Discord session is left running and the step asks for a full quit/retry.

The short-lived launcher repairs a replacement stock ASAR from its verified local cache when Discord opens through its configured shortcuts after a full quit. It retains stock backups. There is no extra autostart service or scheduled updater. Discord's own internal update restart or an unconfigured shortcut can bypass it. The OpenAsar cache updates when a new build is prepared; it is not an automatic OpenAsar updater. The helper requires .NET Framework 4 retained in the source. New Discord layouts and future Discord/OpenAsar incompatibilities are not guaranteed.

HatchZip uses its official GitHub release asset and digest; Shiftd uses the existing TechLounge pinned release and SHA-256. They are bundled only when selected, run at first login and retain identifiable existing installations. Other recommendations use the same exact WinGet/no-upgrade path as search results. Recommendations do not install automatically, and their publisher licences and permissions still apply.

Primary references: [Setup scripts and OEM restrictions](https://learn.microsoft.com/windows-hardware/manufacture/desktop/add-a-custom-script-to-windows-setup?view=windows-11), [WinGet offline download](https://learn.microsoft.com/windows/package-manager/winget/download), [.NET support policy](https://dotnet.microsoft.com/platform/support/policy), [DirectX offline redist](https://www.microsoft.com/download/details.aspx?id=8109), [Store registration troubleshooting](https://learn.microsoft.com/troubleshoot/windows-client/shell-experience/troubleshooting-microsoft-store-apps-download-failure). Microsoft does not support arbitrary Store removal/reinstallation as a general recovery method; see [its recovery boundary](https://learn.microsoft.com/troubleshoot/windows-client/shell-experience/cannot-remove-uninstall-or-reinstall-microsoft-store-app). This experimental custom-image provisioning path does not change that support policy.
