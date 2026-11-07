## 0.1.36

Download Windows 11 x64 directly from Microsoft with UK English first, a compact scroll-free dialogue, progress, cancellation and safe save handling. Successful lookup hides website/retry recovery actions. Downloads retain existing ISOs, validate size/format and calculate SHA-256; a Microsoft comparison hash is verified when supplied.

ISO import now reads the Windows image metadata directory once instead of reopening every edition. The measured 11-edition Microsoft image improved from22.72s to0.94s with identical edition metadata; the six-edition Ghost image loaded in0.83s. Results depend on storage and source media. Build-time reinspection and preservation gates remain intact.

Replaced the output dropdown with three icon buttons: Setup USB, Save ISO, USB+ISO. Selection is highlighted with a checkmark; other choices remain clickable with muted styling, hover feedback and keyboard focus. Reduced motion and high contrast are respected. New approved app icon and updated25second product video.

964 ordinary automated checks, compiled WPF usability/layout/copy checks and the signed298file package passed. A full9,061,271,552byte Microsoft UK ISO download completed; the user confirmed its editions appear in the packaged app. Main executable remains Authenticode unsigned; update-envelope signing is separate. No new Windows installation was required for this app-only release.

## 0.1.35

Restored a single opening-screen action for copying an already-finished ISO to USB, without rebuilding it. It hides after selecting an edition, keeping customisation in Finish. Existing erase confirmation and file verification remain unchanged. The native-validated Windows enablement route is now described as reviewed; unknown future routes remain blocked.

The final Ghost ISO passed all 1,116 exported-file checks. The actual packaged USB flow passed cancelled-erasure/retry handling, full write/readback and the selected label on a 16 GB virtual USB. That same USB booted WinPE in BIOS and UEFI with Secure Boot enabled and TPM 2.0 attached; the launcher animation and inherited tools were observed. All VMs shut down normally. Existing installation and ordinary recovery evidence was retained without another full install. Hosted update delivery awaits publication approval; physical hardware remains unverified.

# 0.1.33 working candidate, 7 November 2026, unreleased

Discord installs quietly without launching its first-run browser handoff. Its installer database is initialised from the verified vendor seed before OpenAsar repair and shortcut integration. Existing databases remain untouched; incomplete, ambiguous or linked seed layouts fail visibly. OpenAsar settings now use UTF-8 without a BOM and preserve non-ASCII existing settings. Other app installers retain their catalogue arguments and checksum checks.

Owned wallpaper frames can refresh under Windows' Fill mode as well as Centre, preserving placement and custom backgrounds. All933 ordinary automated checks and25 separate personalisation checks passed, along with compiled WPF copy checks. The live changed-resolution helper check was blocked by automatic approval and remains unverified.

The completed standard26H2 installation passed all seven machine steps, ten selected apps, Office/Tweaks opening and post-app ordering. Ordinary WinRE booted, read the installed Windows loader through Command Prompt and returned to automatic sign-in on the dark desktop without selected apps opening. The current signed-envelope0.1.33 package passed independent verification of all298 ZIP entries and extracted files. Main app Authenticode status is NotSigned; update-envelope signing is a separate verification.

Final refreshed ISO/USB readback and approved hosted-update delivery remain outstanding. Original downloads remain untouched. Nothing has been published.

--- Earlier evidence below ---
# 0.1.32 working candidate, 6 November 2026, unreleased

Fixed standard Windows Setup stopping at product-key validation when the WinPE account wizard supplied an answer file without a ProductKey instruction. Images with no inherited key now retain Windows Setup's normal key/skip screen. Existing installation keys, key-display policy and edition selection are preserved; ambiguous source licensing settings stop preparation. No key or activation step is supplied. All921 automated checks passed and Builder built with zero warnings/errors.

The corrected source-generated account template matched the complete native-tested trial. The current standard26H2 image then installed to the owned disposable target and reached StockFinal's desktop without post-reboot account/network/privacy answers, with networking disabled until the desktop appeared. Actual Secure Boot, TPM, default PC naming, automatic sign-in, region and recovery registration passed the read-only audit. Background required-app installation remains in progress; the blue wallpaper is visible at native display dimensions. Windows selected Fill mode afterwards, so the audit's stricter centring-mode check remains unresolved until completion/restart. Ordinary WinRE boot/return, corrected final ISO readback and current16GB USB acceptance remain pending.

The signed private0.1.32 package independently passed its signature and all298 extracted-file checks; FirstLogin remains byte-exact C4. A raw-publish packaging failure correctly detected missing WebView2 support before creating an archive;260 unchanged support files were verified against the previous signed manifest before successful packaging. Nothing has been published. A custom maintenance-carrier preparation was blocked by tool policy; a smaller data-only XML/checksum disc was accepted instead. The blocked custom launcher was not run. These are separate from Windows' default script policy, changed only for the authorised diagnostic PowerShell process in the disposable VM.

--- Earlier evidence below ---
# 0.1.31 working candidate, 6 November 2026, unreleased

Fixed a stale restart notice for additional users: a completed setup now compares the installer receipt with the current Windows boot time. Receipts from an earlier boot no longer request another restart; an unavailable boot-time query conservatively retains the notice. Four inert generated-script regressions cover missing, old, new and unverifiable receipts. All916 automated checks passed; no host Windows servicing was performed by these tests.

The standard26H2 update committed successfully after its healthy scan and preservation acceptance. Its final12,993,331,200-byte ISO passed full file verification and independent read-only Windows-image inspection at17:54UTC, including all709 configuration fingerprints and the reviewed full task-data migration. The current Ghost payload refresh also passed full ISO/read-only image verification at19:30UTC, preserving all recorded source settings, original system-app identities and WinPE/tools. Standard installation/recovery remains pending. The additional administrator completed its first sign-in without repeating completed machine installers or changing the primary account's automatic sign-in. The updated private0.1.31 ZIP passed independent signature verification and all298 extracted-file checks. Nothing has been published.

--- Earlier evidence below ---
# 0.1.30 working candidate, 6 November 2026, unreleased

Added a separately reviewed standard26200.8037-to26300.9725 migration alongside the existing Ghost9457 route. Source service/task/framework prerequisites, exact Microsoft package identities and full retained settings remain mandatory. The catalogue permits only measured service, internal framework and three Windows metadata transitions. Unexpected settings, task commands/triggers, removed security payload restoration or lost source apps still stop export. The standard native continuation passed its post-camera-correction health scan and is committing; final exported media and standard installation acceptance remain pending. The original failed experiment is retained separately from this qualified continuation.

The task migration now requires the complete reviewed semantic fingerprint, including triggers, even when the offline task-definition directory is empty. All912 automated checks passed before the final readback callback addition; current app/UI builds and media validation are being completed. The FirstLogin executable retains the exact previously Defender-scanned0.1.29 identity; the main app advances to0.1.30 so earlier private app packages can update correctly. Nothing is published.

--- Earlier evidence below ---
# 0.1.29 local candidate, 6 November 2026, unreleased

Standardised the first-login helper build with the SDK deterministic compiler and normal version identity. App and test builds now emit identical helper bytes. The exact helper passed the owned VM's current Defender scan in both normal and inactive Startup-folder paths, without security exclusions or protection changes. This addresses today's observed block; future antivirus verdicts are not guaranteed. Existing setup behaviour is retained. New ISO exports keep a recognised .iso extension while staged; native corruption/release-retry checks and all867 automated checks passed. Large-ISO finalisation performance remains unverified. Quiet launch, Sora rendering, rounded controls and retry checks passed afterwards.

The private ZIP and signed envelope passed independent signature and all 298 extracted-file checks. ZIP SHA256 61D2B9D361DB883070D396D558FD89B9E9D865759192DF07F2A780484DA54ECF. Updated-media acceptance, standard-image installation and hosted delivery are still pending. Supersedes the private 0.1.28 package; nothing is published. The archive recorded below predates the staging-name correction and must be repackaged before distribution.

--- Earlier evidence below ---
# 0.1.28 local candidate, 5 November 2026, unreleased

Packaged and independently verified the signed update manifest, all298 extracted files, executable version0.1.28.0 and readable Sora font licence. The local archive is432,711,249bytes, SHA2563A47DD8DB0B01A4C9CA2EF9DDA424D869C89A6161B4AD018432522A12CAFF4D8. No remote repository or release was created.

Includes additional-user WebView2 and ChatGPT setup fixes, SID-specific post-app processing, missing desktop shortcuts for retained machine apps, the Sora progress window and its corrected rounded-button corners/descenders. The reviewed supported-update caption no longer says experimental; unknown Windows upgrade routes and unexplained preservation differences still stop the build.

On the existing Ghost26H2 installation, the additional standard user completed its corrected-payload retry; Hello PIN sign-in, internal WinRE boot/Command Prompt and automatic return to the primary desktop passed. Fast user switching can still leave a black desktop despite valid wallpaper frames. The latest payload has not yet completed refreshed-media and standard-image installation acceptance. Full release and hosted update delivery remain pending.

A stopped test disk passed structure checking and full logical comparison before atomic replacement, reclaiming19.76GiB net. Original ISO metadata remains unchanged. See artifacts/package2316-proof.json and compact2300-revision1250.json.

--- Earlier evidence below ---
Fixed clipped g/y/p strokes in the Sora setup heading by letting the title rows size to the font. A failing native glyph regression now covers the original defect and four font sizes; all 857 automated checks passed after the fix.

## Unreleased working-tree polish, 4 November 2026

Redesigned the first-login setup window with bundled Sora Regular/SemiBold, rounded controls, smooth hover/press and progress feedback, and a cleaner failure-only Retry action. Setup keeps running when hidden. The font licence is included in generated provisioning. Builds and native harmless-worker checks passed; the previously verified ZIP and ISO still contain the older window until the next validated package refresh.

# 0.1.27 release candidate, 4 November 2026, unreleased

Includes the pinned wimlib compressor, matching source/licences and a FAT32 boot plus NTFS data USB layout for large ESD images. DISM remains responsible for Windows servicing. Ordinary large WIM images retain the split-WIM route. The conservative 16 GB budget includes duplicated boot files and headroom; no components are removed to force a fit. The app version is newer than the previously supplied 0.1.26 testing build so a future signed release can be offered through its updater.

856 automated checks passed. The real Ghost 26H2 ESD is 8,030,545,778 bytes; projected media is 12,943,772,644 bytes and the two-volume USB requirement is 14,417,239,164 bytes. Full ISO file readback matched all 1,116 files, and independent Windows-image readback passed. A real write to the exact owned 16,000,000,000-byte virtual USB verified every copied file and its custom label; no physical drive was erased. The isolated native 0.1.26-to-0.1.27 updater also passed, and the updated app was opened and used. USB boot, fresh installation/recovery, current standard-image acceptance and hosted update delivery remain release gates. This is not yet a public-release acceptance result. Original downloads and the existing Tweaks release remain unchanged.

--- Earlier evidence below ---
## 4 November build/package corrections, unreleased

- Made the account-setup helper reproducible using the SDK compiler, with unchanged program functionality. Independent app/test builds now produce the same artifact. The fixed artifact passed the owned VM's Windows Security file checks; universal antivirus compatibility is not claimed.
- Added wallpaper-library validation before real builds and to packaged-resource checks. A missing preset now stops before costly servicing. The complete package passed; a deliberately missing preset was rejected and restored in the fixture.
- Added a post-update visible-artwork/security check after the exact approved internal correction. Targeted file-change/attribute/missing-file checks and affected update regressions passed.
- Resumed final 26H2 integration from the accepted WIM and completed WinPE. A missing wallpaper folder in the native test carrier caused a clean discard, without commit/update reapplication/original-ISO changes. The corrected carrier includes all15 variants; native continuation and final ISO/USB/install/recovery acceptance remain in progress.
## 3 November native saved-image acceptance, unreleased

The recovered private Ghost25H2 image passed reviewed preservation and full ScanHealth, committed successfully and independently reopened as Windows11 26H2 build26300.9725 at20:35UTC. The original ISO remains detached and untouched. The four-disk recovery checkpoint and permanent TPM/UEFI backups were verified before restart; RAM and volatile TPM state were not captured. Integrated current-Builder ISO/16GB USB/install/recovery acceptance, automatic repair from the original ISO and wider public release gates remain open. No update packages or fresh installations were repeated for this continuation.

# Unreleased, 3 November 2026

- Added exact reviewed Windows service-migration rules and disabling the new Camera-CCX framework, with its driver registration kept absent during the matching 26H2 update.
- Bound replacement services to their original predecessor so updates cannot recreate a previously stripped service.
- Added independent protection for global performance and Internet preferences, plus a complete preservation-difference report before rejection.
- Integrated the user-approved, checksum-bound internal wallpaper correction while retaining Ghost artwork, recovery bytes and permissions.
- Fixed file-attribute restoration after Windows security writes; real NTFS success, refusal and failure-recovery checks passed.
- Native acceptance, final packaging and updated boot/install validation are in progress. This remains an unreleased candidate.

# 0.1.26 guarded enablement-upgrade candidate, 1 November 2026

The experimental Windows update option now resolves supported Windows 11 24H2/25H2 x64 images to the latest stable 26H2 target through Microsoft KB5121794, after its matching cumulative prerequisite. Microsoft release metadata remains live; a versioned embedded compatibility catalogue authorises the exact upgrade route, source editions, prerequisite revision and pinned enablement bytes. Future releases can be detected without authorising unknown package routes. New routes require a reviewed Builder update; this cannot guarantee compatibility with unreleased Windows versions. LTSC/server/unknown editions are not converted. Actual offline edition identity, target build, DisplayVersion and retained edition must agree.

The native update transaction uses separate cumulative/enablement folders so checkpoint discovery cannot apply enablement out of order. The cumulative stage keeps PreventPending; the enablement stage uses standard applicability checks after the prerequisite has been verified. Inherited pending servicing is rejected. There is no stock-image replacement, automatic component repair, IgnoreCheck or ResetBase. Private committed WIM metadata is refreshed to the independently verified OS version, retains custom identity/counts and is read back before ISO export.

Preservation now also checks originally absent Defender/security payloads and newly added system-app folders. Offline settings snapshots include machine Run/RunOnce (both registry views) and scheduled-task cache, alongside existing services, policies, default-user settings and optional-feature/Appx removals. A difference prevents commit/export. This is an export rejection guarantee, not proof that every modified ISO can successfully upgrade. Existing intentional Builder inclusions remain separate. Original ISO files were not modified.

Verification: 809 ordinary source checks passed, followed by focused final cumulative and feature-upgrade regressions. The new feature fixtures cover package order and folder separation, required versions, source previews, future unknown releases, LTSC/edition conversion, cancellation, inherited pending state, restored Defender/system apps, changed settings, bad hashes, redirects and byte budgets. Real Microsoft release discovery and a small enablement download passed; the 177535-byte package SHA256 is BF43EB13B7EBDE920C2201473BFD5C531B45E0F46ACD8E0843F27F9744FB4B43, with a Valid Microsoft Corporation Authenticode signature. This smoke assumed an already-updated prerequisite and did not service Windows. Compiled UI release/edition fixtures and five-page/three-size WPF checks passed. An attempted test rebuild hit the still-running test process's file lock; it stopped normally and the subsequent build passed without warnings/errors. No lock was bypassed.

Native 26H2 servicing, private-WIM metadata API readback, export/boot, recovery and first-login acceptance remain unverified. The focused VM resumed safely. The corrected offline test helper reuses the retained private Ghost WIM and existing cached Microsoft packages; all 191 runtime files were independently verified inside the guest. The fresh source scan completed: one altered stock wallpaper payload, no manifest/metadata corruption. The update was rejected before any package application and the private mount discarded successfully. A narrowly approved internal-picture repair is now under native test; Ghost’s visible picture, permissions, apps, services and settings must remain unchanged. The shipped Builder still performs no automatic source repair. The VM NIC remains disconnected, and no new multi-GB payload copy was created. Post-install process/startup comparisons must use matching app selections, hardware and network state; a Task Manager count alone is not preservation proof. No fresh install, physical drive write, original-ISO alteration or publication occurred. This is a testing candidate, not a public-release compatibility claim.

## Targeted native repair, 1 November 2026, 22:33 UTC

Native repair1619 now passed on the retained private mount: the internal img100.jpg matches the canonical Microsoft SHA256 while Ghost’s visible image retains its original SHA256. Both source file objects and the parent directory retained owner/access/audit semantics; temporary access rules were removed. The native app/feature/configuration and removed-payload comparisons passed immediately after repair. Ten security-equivalence fixtures reject actual changes to permissions, ownership, audits, integrity labels and unprotected inheritance, while accepting only the observed protected-inheritance marker and NULL/absent audit-list representation difference. Earlier staging/replace attempts stopped safely before updating packages. The production updater completed its full source ScanHealth at22:38:51 UTC: No component store corruption detected, operation successful. It is proceeding to verify/apply cached packages. Upgrade/commit/export/boot acceptance remains pending. The repair is confined to the explicitly approved native test, not shipped automatic repair.

--- Earlier evidence below ---

# 0.1.25 personal testing build, 1 November 2026

Update feedback now reserves its header space, keeps the button caption/appearance steady, holds instant results briefly in the checking state and crossfades text over 180 ms. A finite loading-icon animation stops when the check finishes; Windows reduced-animation and high-contrast preferences are respected. Retry transitions can reverse without resetting the text opacity.

App search shows icon-bearing suggestions as the user types, with immediate catalogue/recent matches and a 450 ms pause before online WinGet lookup. Older requests cannot publish over newer text; source requests are serialised. Read-only stale processes may finish before the queued newest search runs. Click a suggestion again to deselect it; Down reaches the first result and Escape dismisses suggestions. Required apps remain hidden and unsupported Discord variants remain excluded. Artwork uses packaged/verified sources and retains a placeholder when unavailable.

Finish is the only output route: the duplicate sidebar and post-export USB buttons are removed. The completed-file summary remains informational. One final action continues to drive selection or saves an ISO according to the selected output mode. Existing device identity, exact erase confirmation, staged replacement and readback protections remain.

Verification: source/UI builds pass with zero warnings/errors. WPF animation checks verify fixed geometry, minimum checking duration, combined crossfade opacity, settling/stopping and repeated errors, with timed rendered frames from an offscreen non-activating window. Suggestion fixtures cover debounce, stale completion, icons, selection toggling, offline fallback and identity rules. A real read-only WinGet query successfully populated Steam suggestions through the app; no app was installed. Five-page/three-size WPF navigation/layout and recovery gating checks passed. GPU frame pacing and live pointer/keyboard typing are not certified by these checks. No fresh install, VM resume, physical drive write or original-ISO change was performed.

This remains a personal testing build. Cross-release Windows upgrades, native acceptance of the latest combined USB workflow and the hosted public update release remain incomplete. Nothing was published.

--- Earlier evidence below ---

# 0.1.24 personal testing build, 1 November 2026

The app-update result now reads "No app updates available" when no public release exists. Normal activity uses one readable card feed with a separate diagnostic-log window. XPS and Work Folders have inline explanations, and the source-app scan panel is removed. Wallpaper modes show only their relevant controls. Region and keyboard use one searchable native dropdown each, defaulting to UK English with US English second. Advanced download sources are removed; mandatory packages remain automatic, including .NET 8 compatibility.

Finish is the single output decision: USB only, ISO only, or ISO and USB. The saved ISO destination is entered once on Finish; the USB dialog shows the plan and accepts only drive/label choices. Existing staged replacement, fresh drive identity checks, erase confirmation and temporary-ISO cleanup protections remain. Windows metadata shows release names, exact Microsoft publication dates when found, the compatible stable update date and check time. A newer feature release is shown separately and is not applied automatically.

Verification: source/UI builds have zero warnings/errors. Targeted WPF usability, compiled-copy, update-feedback, region-picker and inert output-transaction checks passed. Metadata parser/download fixtures passed, including current-build date, stable-update selection, newer release ordering and malformed/download failure paths. Five-page WPF rendering at 1160x860, 800x650 and 720x500 passed before the final small activity-completion/caption edits; affected usability/render checks passed again afterwards. These are UI and fixture checks, not live pointer typing, ISO servicing, USB writing or installation acceptance.

The VMs remain paused. Original ISOs and the prior testing app are retained unchanged. No physical drive was written and nothing was published. The combined customise-to-USB pipeline still needs native end-to-end acceptance; Windows feature-release upgrading and a hosted update release remain unfinished. This is a personal testing build, not a public release.

--- Earlier evidence below ---

# 0.1.23 personal testing build, unreleased

- Simplify fixed defaults, app browsing, activity text, update feedback and wallpaper layout.
- Add region/keyboard search and automatic temporary storage.
- Use current ISO/customisations directly for USB creation, optional ISO saving/replacement, USB labels and verified temporary-ISO cleanup.
- Enforce HatchZip and required inclusions in real builds; retain source tools and the reviewed Tweaks exclusions.
- Focused fixtures and WPF checks passed. Native combined-workflow acceptance and wider release gates remain open.

# 0.1.22 draft, unreleased

- Add separate Windows-image version checking and an experimental same-release cumulative-update build option with strict preservation checks.
- Keep normal Office deployment and account/licence activation; exclude the owner-supplied activation action from app/media assets.
- Verify first-start progress hide/close/failure/retry behaviour using actual Framework message-loop fixtures.
- Correct setup-timing and generic WinPE captions, wrap long options, and verify mandatory final idle-power defaults.
- Source and interface checks passed. Current-image export, fresh desktop-first acceptance and the documented wider Windows-update scope remain incomplete.

## Unreleased, 1 November 2026

- Apply Never screen, sleep and hibernate idle timeouts to AC and battery across every available power plan. Apply during setup and finalise after apps/tweaks; retain backup values, verify results and report failures. No manual Sleep/Hibernate removal or permanent background monitor.
- Prevent idle sleep while a quiet installer is active, then release the temporary request.
- Add guarded cumulative-update and Microsoft metadata discovery foundations. These are not yet a completed automatic Windows version upgrade feature or a release acceptance result.
- Focused native power testing and existing source/UI checks passed. The active frozen ISO build predates these source additions. Keep unreleased until the documented wider-support gates are complete.

# 0.1.21 draft, unreleased

Added draft desktop-first provisioning: protected deferred machine installation, a dark Manrope progress window, retained successes, retry controls and restart notices. App and graphics-driver ordering is enforced before final optimisation. Host fixtures cover real harmless child-process failures and interrupted observers; native progress interaction and fresh-install timings still need acceptance. Preparing the VM interface interaction test was rejected by automatic approval review and did not execute.

The frozen standard Windows11Pro export passed native app servicing and independent preservation readback, including all21,239 original WinPE files. Its13.45GB ISO and evidence archive were transferred with independently verified hashes. It uses the earlier provisioning schedule and is not a fresh-install or desktop-first pass. ISO verification and filename finalisation now have visible app phases.

Fixed Unicode decoding of WinGet and Builder-owned PowerShell output on PCs using legacy Windows code pages. Other native-command encodings remain unchanged. All700 source checks passed; the full standard-image build and installation gates remain separate.

Wider Windows ISO support is under test. The draft detects the marked x64 Windows PE boot index and adds a guarded standard-startup integration with a separate file-manager fallback. Ghost tools and startup entries retain their existing integration. A standard WinPE candidate booted with Secure Boot enabled and a detected TPM2 device. Runtime tests found and fixed missing basic-tool entries and incorrect font fallback. The final fixes are included in the accepted standard export, but still need a full-image installation test. Safe Windows-image updating and hosted release delivery remain open. The verified0.1.20 package is retained separately and has not been published.

# TechLoungeOS Builder development notes

## Complete ISO checksum verification, 31 October 2026

- Builder 0.1.20 records every source-media file before export, then mounts its own output read-only and checks every file, including the full installation WIM. The final ISO filename is published only after verification and mount cleanup succeed. Missing, extra and equal-length damaged files fail the build.
- All 663 source checks and the complete five-page/three-size WPF run passed. Native Windows tests used the real Microsoft ISO tool and optical mounts: successful readback, deliberate checksum mismatch, failed-output cleanup and a transient unmount retry passed. The 292-file signed portable package passed independent ZIP CRC, length and checksum checks. Its installation helpers are unchanged from accepted ISO1250.
- A discard-only Windows update experiment reached 25H2 build 26200.9725, but failed preservation: a camera feature appeared and 20 tracked service configurations changed. The private WIM was discarded successfully. This does not implement or approve Windows-image updating; the accepted ISO retains its original build and configuration.
- The actual 0.1.19-to-0.1.20 app update reopened its window, matched all 292 signed files, preserved a user file and left no unfinished journal. Complete accepted-ISO readback passed all 1,115 files, including the full WIM, and released its read-only mount. No hosted feed, publication or physical USB write was performed.

## Queued-update storage fix and fresh installation, 31 October 2026

- App0.1.19 keeps the current verified update when Install downloaded update is clicked again, with clear close-to-install guidance. Repeated selection no longer loses its queued download reference. 655 source checks and the full interface run passed; the new interface regression invokes the actual handler three times with a synthetic pending state.
- ISO1250 passed all27 fresh-install checks from the verified16GB virtual USB, internal recovery boot without any installer media, automatic return and an ordinary restart with unchanged post-app receipt data. Its installation helpers remain byte-identical in app0.1.19. Office basic editing/slideshow, Tweaks Home/update panel and Store/Xbox browsing also passed native checks. Physical-PC acceptance remains separate.
- App0.1.17-to0.1.18 actual UI update passed292-file signed readback with a retained user file. The final0.1.18-to0.1.19 actual UI update passed all292 files and retained the user note; its pending-selection regression also passed inside Windows. No publication, push or physical drive write.

## Update download and wallpaper hardening, 31 October 2026

- App0.1.18 discards only its own failed update-download/extraction stage. Path boundaries and the complete tree are checked before removal; other staged updates and rollback evidence remain untouched. Cleanup failures preserve the original error and identify retained files.
- Current source655 checks and WPF five-page/three-size checks pass. The signed app-only ZIP was independently inspected, with292 manifest files, no developer/private files and the exact native-tested installation helpers. Native failure-path/update-app smoke verification remains pending.
- Candidate1250 exported and independently preserved11 apps,136 optional features,705 tracked settings and all media outside install.wim. It contains the final4K-tested wallpaper refresh helpers. Current16GB USB/fresh-install tests remain in progress; previous1178 acceptance is recorded separately below.

## Previous accepted candidate, 31 October 2026

- App0.1.16 fixes corrupted interface punctuation and protects the shared post-install task folder against ordinary-user replacement. Actual standard-user security checks and native app launch passed.
- Source and clean-patch reconstructions each passed651 checks. WPF checks passed all five pages at three sizes, including nine-app selection/search. The local portable package is verified and excludes the developer test harness.
- ISO1178 passed read-only preservation, all1,117 files on the owned16GB virtualUSB and25 fresh-install checks with Secure Boot/TPM2. Internal recovery tools booted without the installer USB and returned to the desktop. All25 checks passed again after a normal boot, with unchanged post-app receipts. Physical-PC and public-release acceptance remain open.

## Startup and post-app boot tuning, 30 October 2026

- Boot Optimizer now runs automatically after all selected apps succeed. Failed apps defer tuning until their successful retry. The existing reviewed Tweaks script, actual user registry hive and rollback data are retained; no visible elevation prompt or startup watcher is added.
- New Edge installations skip the first-run welcome wizard when no source policy overrides that choice. HWiNFO installs in machine setup so its elevated post-install window cannot appear on the user's first desktop.
- All642 automated checks passed. Targeted VM tests passed for the new Edge/HWiNFO paths and unelevated Boot Optimizer invocation with protected task/code. Fresh ISO1120 passed24 functional checks; Tweaks and Word opened. These new startup fixes require a revised ISO and another clean-install test before handoff.

## Blank-password installation fix, 30 October 2026

- Account setup now handles Windows' console-only restriction result for an intentionally blank password, retaining that policy and the existing current-user guard. Other failures remain errors.
- The exact repaired helper passed a targeted native test. Revised ISO1120 independently retained11provisioned apps,136feature states and705tracked settings; its WinPE is byte-identical to1057, and read-only export checking confirmed the repaired helper and manifest entry.
- ISO1120 SHA256:F955F012F4E98CE57AF7DEE9691E47AC375D05EFE18D5497C8A48BB7627E7ACF,13232726016bytes. The16GBvirtualUSB and clean installation checks remain in progress. No physical disk write or public release.
## Usability and recovery fixes, 30 October 2026

- Standalone USB creation now asks for an ISO explicitly. The post-export action uses only the last completed image, with a reminder to rebuild after changing choices.
- App search results stay visible with all nine recommendations selected, and the result count excludes blocked Discord channels and duplicate packages. The failing layout regression and final three-size WPF run1090 were checked.
- Disabled USB actions now use readable neutral text/icons and explain when they become available. Both standalone and post-export entry points retain the same operation/recovery protections.
- Recovery now consistently blocks media inputs, navigation and direct action handlers until an interrupted Builder update is restored.
- WinPE is enabled initially. Complete portable packages select their bundled integration support automatically; missing support is reported before a build starts.
- Active operations request that Windows stay awake, then restore the previous request without changing the user's power plan or forcing the display on.
- Current source suite1071 passed623 checks; final WPF run1077 passed all five pages at three sizes. The new full Ghost1057 export, nine-app fresh install and current16GBUSB tests remain in progress. Existing993 VM proof remains separately recorded.

## Nine-app recommendation and release preparation, 29 October 2026

- Added HWiNFO64 (`REALiX.HWiNFO`) to Recommended apps, retaining individual deselection and existing choices. Its publisher icon is packaged for offline display. The first-login queue uses the current WinGet installer, quiet cleanup and an ordinary desktop shortcut.
- Full source suite1040 passed622 checks. Updated packaged resources, all nine recommendation icons and WPF selection/layout checks passed. Fresh installation of HWiNFO64 remains pending in the new build1041.
- The user requested a separate GitHub repository, concise feature README and Brag demo video once ready. The configured update source already points to `RaheemC4/TechLoungeOS-Builder`, with automatic startup checks and signed updates applied after exit. Hosted discovery still needs a real dedicated release; no repository or release has been published in this continuation.

## Desktop cleanup defaults, 29 October 2026

- All new Builder builds hide This PC on the desktop and remove CPU-Z desktop shortcuts. CPU-Z remains installed, and removed shortcuts are backed up for recovery.
- Microsoft Store stays installed and enabled but is unpinned from the taskbar. Other taskbar pins and unrelated desktop shortcuts are preserved.
- The common offline/default-profile and first-login paths apply these settings without a selectable preset. Current-user cleanup retries independently if Store registration is not ready.
- Native Ghost VM tests and a normal restart confirmed the requested desktop layout. The existing exported ISO993 predates these changes and must be rebuilt before the spare-PC handoff. Other ISO distributions have not received fresh installation coverage for this change.

## Current continuation, 28 October 2026

Native807 exported and independently preserved the audited Ghost image state; its fresh Secure Boot/TPM install is in progress. Recommended apps now select Discord, Brave, Chrome, Steam, MSI Afterburner, Stremio and ChatGPT with whole-card highlight/toggle interactions and individual deselection. The preset skips new additions already detected in the source, retaining explicit existing choices. Stable Discord always uses the TechLounge OpenAsar/repair recipe under the normal Discord name/icon; other channels are blocked. Official icons are embedded for the seven recommendations. ChatGPT uses its official Microsoft Store product. Source also fixes regional settings for the initial WindowsPE language page while retaining source Setup commands. These newest source changes still require installation coverage and are not all present in807. Publication remains unapproved.

## 26 October acceptance continuation

- Recovered export 347 from the successfully committed WIM after the authorised overnight cancellation; the cancelled UI run remains recorded separately. ISO size is 12,077,404,160 bytes, within the conservative 16 GB media budget.
- Verified all 1,114 virtual USB files, booted the actual animated Ghost PE and original tools, and completed a fresh Secure Boot/TPM installation. Source configuration, removals and tools pass preservation checks.
- All twelve provisioning steps completed. Word, Tweaks/private WebView2, SumatraPDF and Discord sign-in were exercised. Wallpaper and all fourteen reviewed settings match. Both GPU queues skipped nonmatching hardware correctly. OpenAsar remained present after the observed Discord update to 1.0.9266.
- Store/Xbox restoration remains unresolved. Discord's browser handoff still warns on this stripped browserless source. Its inherited update pause is retained. Physical devices, standard-user provisioning, real GPUs and hosted updates remain release gates. No production code or package changed in this continuation; earlier automated results retain their original scope.

Detailed receipts and limitations are in TechLoungeOS/TEST-RESULTS.md. This image is not yet recommended for a physical reinstall.

## Conditional AMD/Intel graphics queue (unpackaged continuation)

- Added default-on AMD/Intel display-driver setup independent of the Tweaks bundle. Target PCI display identities and full hardware IDs gate it, including integrated/hybrid graphics. CPU branding, chipset/network devices and stale vendor names do not select graphics drivers.
- Both graphics queues now detect target hardware before registering a task. NVIDIA-only PCs do not queue AMD/Intel work, and AMD/Intel-only PCs do not queue NVIDIA work. No matching adapter means no download task.
- AMD/Intel currently use explicitly labelled hardware-matched Windows Update packages. Numeric version selection retains newer signed drivers and rejects unknown version formats. This is not a guarantee of the absolute newest AMD/Intel vendor reference release; hardware-matched Windows Update is the user-confirmed AMD/Intel source.
- Hybrid vendor phases retain successes, record individual package results, verify active signed drivers and defer activation across user-controlled restarts. A shared exclusive gate prevents NVIDIA and AMD/Intel installers running concurrently. No forced reboot, factory reset, update policy change or vendor auto-detect utility is added.
- AMD/Intel add only scripts/manifests to the ISO. Drivers download after installation. Real hardware and fresh ISO/task lifecycle validation remain release gates.

345 backend assertions and WPF checks passed. These are controlled hardware/update/task tests, not physical driver-install proof. No original ISO or existing Tweaks release was changed.

## Cancellation boundaries, local 0.1.15.3

- Honour cancellation after the current mount, inventory, health or provisioning operation and before commit. Leave DISM running until that operation finishes, then discard the private mount.
- Add regression coverage for mounting and both health scans. The earlier defect is reproduced in a retained failing test; all 353 final backend assertions pass.
- Prepare and verify the signed local 0.1.15.3 package. The real VM update/open/close proof remains 0.1.14.1 to 0.1.15.2. Neither package has been published.

## Quiet first-login setup and native continuation checks

- Replaced the Startup CMD entry and Tweaks PowerShell shortcut with a small Windows GUI executable. PowerShell starts without a console; output and failures remain logged, and duplicate launches leave the active setup alone. Installer permission prompts remain package-specific.
- Validate the source's built-in Windows Framework prerequisite before component removal; preserve retry markers and existing successful application steps.
- In the resumed Ghost VM, verified real wallpaper application/readback, quiet provisioning, private WebView2 launch, active Defender, the reviewed 14 registry values and native task registration/SYSTEM execution/cleanup. Vendor-positive registration used controlled PCI identities; physical graphics installation remains unverified.

350 final backend checks passed. The local signed 0.1.15.1 package passed pinned-key signature, archive and extracted helper verification; it has not been applied as an in-place update or published. Bundle validation now explains its read-only check in the log, and preparation/copy journal phases are accurate. A newly exported ISO and blank-disk installation of these changes are still separate acceptance gates. The original ISO checksum is unchanged. Store/Xbox global restoration remains unresolved. A lost temporary runtime was recovered; retained disks passed integrity checks, but normal shutdown of that interrupted run was not verified.

## NVIDIA-only setup and 16 GB media target (unpackaged continuation)

- Added a separate default-on option with the supplied Tweaks bundle: detect NVIDIA PCI hardware on the installed PC, resolve an exact model against NVIDIA's current catalogue, download a compatible WHQL driver after setup and import the TechLounge profile after driver readiness/restart. No driver package is stored in the ISO. AMD/Intel-only PCs skip the operation.
- Validate download hosts and NVIDIA signatures, keep newer installed drivers, reject unknown/ambiguous models and conflicting multi-GPU driver branches, back up profiles and verify requested settings. Bootstrap unidentified NVIDIA adapters through matching-only Windows Update drivers without changing update/security policy.
- Queue quiet SYSTEM work at first sign-in/startup, keep retryable status and unregister the task after completion. Never force restarts or kill a running driver installer. Download/extraction needs 8 GB temporary space on installed Windows. Driver-only setup does not add the NVIDIA App.
- Correct the original profile's mismatched encoding and missing global name in a private copy; preserve the supplied release.
- Default-on 16 GB target checks media bytes plus filesystem reserve/headroom against conservative 15.5-billion-byte capacity. Actual selected-disk/split-WIM checks remain mandatory. Oversized selections stop without silently removing anything.

309 backend assertions and WPF checks passed. Live catalogue lookup and actual-payload staging passed; the NVIDIA tools/profile add about 2 MB. Real NVIDIA hardware, fresh ISO/VM installation and extractor redistribution review remain release gates. Earlier full VM/USB results predate this path.

## Reviewed Recommended installation profile (unpackaged continuation)

- With a TechLoungeTweaks bundle selected, stage ten reviewed privacy/suggestions tweaks. Explicitly exclude Windows Update pauses, all Defender changes and GPU settings. Keep the supplied Tweaks application and its manual presets unchanged.
- Apply machine settings through the existing setup hook and user settings at first sign-in without elevation. Keep pre-change value/type backups, verify writes, retain successes and report skipped/failed machine setup.
- Update build review and integration copy to show this behaviour. The profile does not invoke Apply All, import NVIDIA profiles, or modify AMD/Intel graphics.

The new profile has isolated registry regression coverage and original-source comparison. It is not yet in a newly exported ISO or verified in a fresh VM. Earlier full-install results below apply to build 252, which did not automatically apply this profile. Standalone NVIDIA Profile Inspector application has no hardware guard in the unchanged supplied Tweaks source; harmless behaviour on non-NVIDIA hardware is not established.

## 0.1.14.2 local USB usability fix

- Added a native temporary-working-folder chooser to USB preparation. A user with limited C: space can choose another local NTFS drive; each operation still creates its own new folder and rejects the USB target as its workspace.
- Reused the existing storage policy so low-space errors report required and available capacity, with non-NTFS/network working storage rejected before preparation.

278 backend checks passed. WPF bounds checks cover normal/minimum dialog sizes. In the owned VM, the packaged app displayed the dark native dialog, selected S: with the native picker and retained S: after reopening/cancelling it. No second USB operation was started by that UI test. The separate native backend write passed all 1,114 file checks. Fresh VM 254 completed installation and all seven provisioning steps, including automatic missing-definition recovery, with Secure Boot/TPM ready. The browserless-source Discord handoff warning and global Store/Xbox restoration remain unresolved. The signed self-contained 0.1.14.2 package passed extraction/hash verification; it has not been published or applied as another updater test.

## 0.1.14.1 local continuation

- Shared the running-installation guard between the app and updater, with process IDs in refusal messages and handling for processes that exit during inspection. The normal signed 0.1.12 to 0.1.14 UI update retained user data.
- Allowed ordinary window closure when another copy blocks an update, while keeping an explicit Install/restart request open with an explanation. An owned-VM check exercised both paths and verified that the staged update and installed files were retained.
- Passed 278 backend assertions, WPF checks and signed self-contained 0.1.14.1 package verification. A further local updater-harness apply to 0.1.14.1 retained user data and left no pending journal.
- Completed another native full Ghost ISO build. The preservation audit found all 11 existing Appx packages, 136 features and 705 service/configuration entries unchanged. WinPE retained its original files, with only the intended startup wrapper change and two additions. Temporary VM test exceptions were removed with protection enabled.

USB preparation initially refused insufficient temporary space before any disk write. The latest ISO's fresh installation remains pending. Global Store/Xbox restoration and hosted updates remain open; no publication occurred.

## 0.1.13 local continuation

- Fixed the activity panel opening at old buffered output until another message arrived. It now scrolls to the latest buffered line after the expanded viewport is laid out.
- Added actual WPF viewport checks for opening and reopening a populated activity panel at all three supported test sizes. The new check failed before the fix and passed afterwards.

Self-contained local packaging and signed archive extraction/hash verification passed (artifacts/release-256). Servicing/provisioning code is unchanged from 0.1.12; that native ISO run remains in progress. No publication occurred. The Store's official consumer installer now has current-user launch proof on the modified VM, but global/offline Store/Xbox restoration is still unverified.

## 0.1.12 local continuation

- Confirmed the fresh Ghost install's initial Defender engine and signatures were absent. Its normal Microsoft definition update restored real-time protection while retaining preferences and an empty exclusion list.
- Added an idempotent missing-definition check to the default setup pack's first-login steps. Existing disabled/stopped/removed Defender is retained; permission or update failures are reported without automatic elevation or security-setting changes.
- Verified actual launches of Tweaks, 7-Zip, SumatraPDF, Discord and Word's licence sign-in UI. No activation or account sign-in was performed.
- Passed a real local signed 0.1.5 to 0.1.12 update with retained user data, plus normal packaged import/navigation/picker-cancellation checks. Recovered an interrupted VM test before servicing, verified the saved disks and removed its temporary exact-file test exceptions. A new clean rebuild is in progress; this is not a fresh-install acceptance result.

274 backend assertions, WPF checks at three sizes and local self-contained 0.1.12 signed-package extraction/hash verification passed (artifacts/release-228). The latest source fixes still need another fresh exported-ISO installation. Store/Xbox acquisition, an intermittent browser warning, standard-user coverage, physical USB and hosted update/interruption tests remain open. No publication occurred.

## 0.1.11 local continuation

- Completed a fresh full Ghost installation from the verified virtual USB. Office and runtimes completed during setup; all selected app steps completed after Ghost's own first-login restart. Tweaks opened with its private WebView2 runtime, SumatraPDF opened and Discord reached sign-in with verified OpenAsar bytes. Secure Boot is enabled and TPM reports ready.
- Added a single named catalog refresh/retry for WinGet's missing-source-data startup error. Other errors remain failures; successful steps remain preserved.
- Fixed newer-runtime detection for .NET entries whose internal installer version is 80.x. It now reads the actual runtime version while retaining case-insensitive VC matching.

268 backend assertions passed. Local self-contained 0.1.11 publish and signed archive extraction/hash checks passed under artifacts/release-223. These source fixes are newer than the installed test ISO and have not been through another full fresh installation. Physical readiness remains blocked by Store/Xbox acquisition, Defender real-time readiness and an observed browser-association warning. Standard-user coverage, physical USB and hosted update/recovery tests remain open. See the latest TEST-RESULTS.md section for precise evidence.

## 0.1.10 local continuation

- Tested the visible packaged app in the Ghost VM with normal file selection, app search, Add/Remove, page changes and a cancelled wallpaper picker. Search results now appear before setup options. Explicitly labelled ARM app variants cannot be selected for an x64 image.
- Fixed the page jumping when clicking Add in nested search results. Mouse selection retains its position; keyboard Tab still brings the next eligible result into view. The normal window now centres within the available desktop.
- Read the entire supplied Tweaks integration before downloads or servicing. Unreadable/security-blocked files stop early with guidance to retain security protection. No files are silently excluded.
- Fixed numeric USB bus inventory, erase-time validation and removable-drive partition-table handling. The real virtual USB passed format/copy/hash verification, booted with Secure Boot and TPM 2.0 enumeration, and its original Setup reached disk selection. Physical USB compatibility remains untested.
- The signed 0.1.2 to 0.1.5 VM update passed after correcting replacement of read-only package files. Failed replacement retained the old version, and unrelated user data survived the successful update. The new 0.1.8 UI package has been published locally only, not released online.
- Moved optional WinPE preparation ahead of Windows servicing. A native-renderer animated GIF is available; the subsequent actual USB boot also showed the background, working original tools and Setup.

Checks: 264 backend assertions and five-page UI checks at three sizes passed. Normal clicks confirmed the search fixes and keyboard focus behaviour in packaged 0.1.8; the latest local signed package is 0.1.10. The initial full rebuild stopped because Defender blocked four supplied Tweaks files. At the user's request, exact-file compatibility probes and then a complete build ran in the disposable VM. The full export passed, and all eight source/staging exceptions were removed with restoration verified. Antivirus, real-time protection and tamper protection remained enabled. No blocked utility was launched, host security setting changed or automatic whitelist added to created ISOs. The full-image USB verified all 1,114 files and booted the retained Ghost tools/background. The original Ghost ISO hash is unchanged. Fresh full installation/first login, Store/Xbox restoration, physical USB compatibility and hosted updates remain release gates. See TechLoungeOS/USER-JOURNEY-TESTS.md and TEST-RESULTS.md.

Source continuation reuses the existing WIM-backed file validator for PE and adds scoped backup reads for protected image folders, without changing ACLs or ownership. A harness logger race was fixed, then the PE-only export and the complete original-bundle export passed. Both native ScanHealth passes retained the source's inherited repairable component-store state; no repair or component removal ran. The fresh VM's empty-CD/IDE startup configuration was corrected before Windows booted, with blank-disk/fresh-firmware checks before retry. These source fixes are included in the local 0.1.10 package.

## 0.1.2 development package

- Fixed startup failure caused by assigning Administrators as the update-cache owner from a token that could not assign that owner.
- Startup now uses Windows kernel coordination and never creates or opens the protected update cache. Cache failures affect updates rather than preventing ordinary startup.
- Let Windows choose the valid storage owner; retain restricted access and inherited owner-rights protection. Accept the SYNCHRONIZE flag Windows adds to allow rules.
- Added regression checks for conflicting startup/update ownership, releasing coordination, cache independence and default ACL ownership.
- Signed update staging, atomic replacement, rollback journal and recovery action are implemented. Updates wait for app exit and do not interrupt active work. Live packaged upgrade/recovery remains unverified.
- Added UEFI USB preparation and guarded writing using Windows storage tools, with split WIM support and read-back hashes. Real virtual-USB writing/boot remains unverified. No physical disk was written.
- Builder is now an independent local repository on feature/bootable-media-and-updates. The separate GitHub repository and first hosted release await approval; update checks report unavailable until publication.

Checks: 231 automated backend assertions passed; WPF checks passed for all five pages at three sizes. Self-contained x64 publish and signed ZIP extraction/hash verification passed. Earlier Ghost installation evidence is historical and predates these update/USB additions. A fresh corrected ISO install and Store/Xbox restoration remain open release gates.

## 0.1.4 development continuation

- Added optional native ambient WinPE background, with static/high-contrast fallback, retained launcher entries, original startup backup and mounted-file preservation checks. Experimental/off by default pending a live PE boot.
- Live protected update storage passed in the Ghost VM. 239 backend assertions, renderer checks and WPF layout checks passed.
- Automatic approval review blocked further VM test preparation with only blocked by policy reported. Full USB/update, rebuilt ISO and fresh installation remain outstanding. Original ISO hash matches; disposable VM shut down and state saved.
# Unreleased: offline Edge and Store/Xbox recovery, 26 October

- Bundle the current Microsoft Edge Stable x64 enterprise MSI, verify its published SHA-256 and Microsoft signature, and install quietly during Windows setup. Use supported default associations for new users, preserving the original file and unrelated associations. Mark Edge included in the app picker. Fix the setup-payload allowlist and avoid waiting for persistent updater descendants after MSI completion.
- Add Microsoft component-catalogue acquisition for missing Store/Xbox packages, with original signed archive validation and all-user provisioning. Keep normal licensing/deployment failures visible. See TechLoungeOS/STORE-XBOX.md for service limits and integrity details.
- Actual targeted Ghost VM checks: Edge installed, a new standard account uses Edge for HTTP/HTTPS, Discord hands off to Edge, and all six Store/Xbox families register healthily. The original ISO remains unchanged. The next full rebuilt ISO/Windows Setup/SYSTEM test is outstanding; this is not release readiness.

## 27 October development continuation

- Correct final configuration/readback validation for the reviewed offline user-policy change, using its independently prepared hive hash while rejecting unrelated mutations. Retain earlier source-preservation evidence.
- Extend actual WPF checks to prevent duplicate Edge installations through app search. 386 backend assertions and the packaged UI checks passed from a verified local source mirror.
- New native test media is prepared. A cloud-backed test work disk stalled the hypervisor before Windows started; a surviving I/O worker still owns the disk lock. The fresh installation is pending approval to restart the Linux environment shared with decomp. No release, push, merge or physical disk write was performed.

The blocked I/O worker later exited without an environment restart. Image checks passed and the native rebuild resumed on local storage; it has passed servicing and final configuration preservation and is committing. The portable 0.1.16 candidate compiled successfully and its Builder/first-login/Discord-launcher hashes match the tested binaries. Fresh installation remains pending. No publication occurred.

The native0.1.16 pipeline has now completed its Ghost ISO export/re-import and independent read-only verification, with all captured component/configuration state preserved except the reviewed user-policy preparation. Both ISO and evidence transferred with matching checksums. Temporary VM test exceptions were removed; Defender protection remained enabled. Fresh Windows installation remains the next gate.

27 October development: guarded BIOS/UEFI USB writer, separate requested desktop defaults, account-answer preparation and deferred Gaming Services checks added. Backend tests passed 439 assertions; fresh installation of this source revision, wizard UI and native USB writes remain pending. No default wallpaper applied.


## WinPE preview refinements, 27 October

Added a separate native interface preview with bundled, licensed Manrope static
fonts, restrained rounded controls, animated hover/press/focus and checkbox
feedback, full-row checkbox targets and native text fields. Password confirmation
is conditional on entry and clears/hides after an empty password loses focus.
Standard selection/copy/paste/cut and word-delete shortcuts work in password
fields too. Rounded keyboard-focus outlines and current-value transition drawing
address the reported square highlights and filled-text flash. Native prototype
checks and desktop interaction checks passed; account submission and boot-image
integration remain open and this prototype is not included as an installer.

Added required source-recovery presence/DISM checks and quiet setup registration
verification, preserving already registered recovery locations. Missing WinRE
payloads now stop the build. Ghost recovery boot, Advanced options, Command Prompt
and return to Windows were verified. The full controlled suite passed 461 checks;
the final worker passed separately in the installed VM with unchanged recovery
configuration. New-ISO integration and destructive repair/reset testing remain
unverified.

WinPE interface preview motion now uses reusable drawing buffers, cached ambient
frames, velocity-preserving control feedback and optional display-paced frames up
to 120 Hz with a legacy timer fallback. Local timing and native motion/resource
checks passed, with occasional long samples still observed. Fixed focus traversal
when conditional password confirmation hides. This remains a separate interface
preview pending production WinPE/action integration and boot testing.

The integrated WinPE shell now replaces the recognised Ghost toolbar in place and
presents its original shortcuts through the themed tools panel. A real Secure
Boot/TPM boot-only probe opened 7-Zip, CPU-Z and Task Manager and shut down
normally. This replaces the earlier preview-only status for those shell/tool
launch checks. Actual account creation, persistent automatic sign-in and a fresh
full ISO installation are still required before a spare-PC test release.

Fixed silent omission of WinPE fonts from a published build. Required font assets
now fail compilation if absent; the published-package check validates fonts,
licence, embedded WinPE payload and setup helper executables. Corrected package
checks passed on the host and in the builder VM. Fresh ISO installation remains
pending.

Fixed atomic build/update journal replacement when another correctly sharing
reader has the previous journal open. Added native filesystem regressions and
preserved original servicing errors when failure-journal recording is blocked.
471 automated checks and corrected self-contained package checks passed; fresh
native export/install validation remains required.

The main build status now displays the actual saving and ISO-creation stages.
Queued progress cannot replace cancellation or the final result.472 automated
checks and packaged WPF/resource checks passed; fresh install/media acceptance is
still required before the spare-PC trial.

Default booted Windows Setup compatibility now bypasses TPM,Secure Boot and minimum
RAM eligibility checks across the boot images, independently of the custom toolbar.
Firmware security and signed boot files stay unchanged. This does not emulate
missing CPU instructions or provide an in-place upgrade wrapper.479checks and native
boot-image/Windows-image preservation passed; full installation gates remain open.

USB preparation now accepts Microsoft's MUI-backed bootsect version metadata while retaining Microsoft signature and runnable x86/x64 executable checks. Resource-only DLLs and unrelated utilities remain rejected before erase.

Fixed USB activation using Windows' supported Set-Partition parameters; rechecks the selected disk before formatting. Storage regression tests now validate generated parameters against real Windows command metadata.

Region/date-format and keyboard choices are now available beside the WinPE account wizard. They retain the image display language, preserve ISO defaults when requested and reject selected keyboard payloads absent from stripped images. Both generated account templates contain the four unattended regional fields. 501 fixture/regression checks and interface checks passed; a new exported image still needs a fresh installation to verify regional OOBE suppression.
# Unreleased validation update, 28 October 2026

- Verified current TechLoungeTweaks installer integration retains supplied-release mode and upstream installer/updater bytes. Runtime policies are scoped to its executable; conflicting values are retained and reported. No antivirus exclusions are applied.
- Missing advanced Tweaks tools are reported separately. Targeted VM testing opened the GUI and update screen with Defender enabled; four activation/Defender-removal files remain blocked. Full functionality and future antivirus compatibility are not established.
- An explicit WinPE keyboard now applies before the account wizard, with target layout/utility checks. Keeping ISO defaults preserves its input configuration. Fresh installation is pending.
- Recovery boot, installed-Windows access and automatic return sign-in passed on fresh736. Export780 preserved recorded apps/features/configuration. Revision807 and another fresh-install test are pending.


29 October: first-login app installs refresh a stale checksum catalogue once, retaining checksum verification and actionable retry status. Persistent mismatch never records success. Hello invitation tests updated; latest source suite560 checks. Native seven-app completion remains pending Chrome retry; new ISO export not yet performed.

29 October: Stremio and Proton VPN included in8-app recommendations with offline publisher icons. Known fresh installs clean up new autostart entries/processes and create desktop shortcuts; preserve source apps. SMB guest/client/server-signing compatibility and SMB1 client are now mandatory in every build, with source payload preflight, backups and readback. These reduce network security.602 automated checks passed; final native source/server-preservation/fresh export gates remain open.

## Account helper repair,30October2026

The blank-password native test found a console-only account-check failure that also prevented initial Tweaks runtime preparation. AccountSetup now handles the specific1327 result without weakening Windows policy; native repair1104 and a normal first-login retry succeeded. All23targeted acceptance gates passed1107, with623source checks. The current ISO still needs this helper integrated and a fresh-install retest. The observed website window needs an uninterrupted replay to separate installer behaviour from test-input focus loss.
## Current validation candidate,30October2026

- Fixed corrupted Unicode captions in the compiled interface, including Browse and Create USB. Added a regression check against the actual WPF controls and explicit UTF-8 source guidance.
- Protected the shared post-install parent folder as well as both fixed SYSTEM-task payloads. A real ordinary-user token could neither overwrite a worker nor replace its folder; a benign fixed SYSTEM action completed. Temporary test tasks/account were removed.
- Extended exported-image verification to compare exact post-app files and registration scripts, selected-app manifests, apps/Boot/Standalone execution order, absence of baked execution state and the exported WinPE hash.
- Source1178 passed651 checks; WPF1179 passed the three-size/five-page, selection, navigation, caption, wallpaper and USB-bounds checks. Published0.1.13 passed payload/font/helper and caption checks. These are separate from current-image installation proof.
- ISO1120 remains the retained known-good export. Its accepted installation passed all24 desktop gates again1163 after normal restart, with Defender/RTP enabled and exclusion/DisableRealtimeMonitoring preferences unchanged. The new full build1164 and final revised ISO/USB/fresh-install acceptance remain in progress.
- Reclaimed34,978,172,928bytes of obsolete exports/RAM state after ownership, fallback-hash and image-release checks. The original source ISO and active test dependencies were retained. No publication, push, merge or physical USB write occurred.
### 4 November local release preparation

- Validate setup helpers and wallpapers before servicing; include generated launchers in native acceptance packages.
- Prepare and verify the self-contained app, updater and signed local update archive. Publication and final26H2 media/install acceptance remain pending.
## Unreleased working-tree fixes, 5 November 2026

Final runtime fix uses one app-specific shared runtime path, retaining per-user app storage and existing policies. Additional users can register an already staged ChatGPT package without reinstalling it. All864 automated checks pass; actual VM runtime preparation preserves completed machine installs. Multi-user retry, PIN/automatic-login, recovery and updated media/package acceptance remain open.

Additional-user validation reproduced and addressed protected WebView2 policy access and cross-user Discord process inspection. The candidate prepares only fixed, executable-specific runtime values through the existing protected task. Discord retains conservative deferral when a running process cannot be identified safely. All863 automated checks passed; the final policy-preservation refinement passed focused checks. Native multi-user retries and packaging remain pending.

Fixed missing HatchZip desktop shortcuts, with checks for retries, missing files and existing user shortcuts. Updated post-app targeting for multiple signed-in users; native multi-user validation is in progress. All860 automated checks pass, with zero build warnings/errors. Existing VM app launches now confirm the Tweaks dashboard, Word home/licensing, Discord login and live Microsoft Store. The previously verified ISO/USB/ZIP predates these fixes and the Sora window; public release gates remain open.


## 5 November 19:22 UTC: additional-user retry results

The standard account now installs TechLoungeTweaks successfully through the unchanged upstream installer and registers the already staged official ChatGPT family for itself. HatchZip's native desktop shortcut is present. Only Discord/OpenAsar and its correctly deferred post-app Boot Optimizer remain incomplete. Native inspection confirms Windows denies even limited-query process paths and owner SID queries for the other account's running Discord processes. The helper conservatively defers rather than changing an archive while its ownership is unknown; the process-path refinement alone does not resolve this case. The finite test will close only the test account's own Discord before retrying. No access restrictions are weakened.

Both accounts showed a black desktop after fast user switching despite the per-monitor COM API retaining the correct 1280x800 Waterfall Ice image, a nonblack central pixel and the selected preset receipt. The render exists and is not blank; the visible shell discrepancy remains unresolved. Do not count the wallpaper gate as passed after switching. Evidence: artifacts/inspect2256-proof.json, receipt1161-1793906252512431980.json and vm1585-primary2260.png. No fresh installation or large image was allocated.

## 5 November 19:29 UTC: standard-user completion and PIN sign-in

The same standard account completed all selected apps and its own SID-specific Boot Optimizer after closing only FinalTest's test-launched Discord. No existing successful app was reinstalled by the final retry. The actual UI Retry button was used, and the new Sora progress window closed after success. Its progress reports restart-needed, complete=true and bootComplete=true; ChatGPT is registered for that profile. Primary automatic-login target remains FinalTest. This is a targeted corrected-payload retry, not a fresh installation pass. Evidence: artifacts/standard2268-proof.json. Unknown-access Discord processes still conservatively defer repair until fully quit; this is not a concurrent-running-Discord pass.

PIN enrolment and actual PIN sign-in have passed on FinalTest. Normal recovery/restart is now requested using the installed partition4 WinRE, with BitLocker protection Off and no provisioning process running. Internal recovery boot/return and post-restart automatic sign-in remain pending until observed. Evidence: artifacts/recovery2273-request.json.

## 5 November 19:36 UTC: internal recovery roundtrip and automatic login passed

The installed recovery partition booted to WinRE, Advanced options and a usable Command Prompt. No reset or repair was run. Continue returned to FinalTest's normal desktop without entering either the password or enrolled PIN. Winlogon still reports AutoAdminLogon=1/FinalTest, build26300.9725, completed provisioning and its original machine-completion timestamp. The Waterfall Ice background is visibly restored at1280x800. This proves recovery and automatic-login coexistence with enrolled Hello on this disposable local account, not real1Password use or every device. The separate fast-user-switch black-background discrepancy remains open.

Fixed missing per-user shortcuts for already installed selected machine apps: retained installs receive only a missing normal desktop link, without process/startup changes; conflicting links are preserved. Seven targeted quiet-app checks and all865 automated checks passed. Builder compiled with zero warnings/errors. Native shortcut verification in the standard profile is next. Evidence: artifacts/recovery2287-proof.json, recovery2286-after.json, shortcuts2278-tests.log, regression2279.log and shortcuts2281-builder.log.

## 5 November 19:39 UTC: retained machine-app shortcut verified

The compiled shortcut-only path created MSI Afterburner's normal icon for TlosStd2156, retaining its existing machine installation and successful receipts. The app was not launched. That account's normal post-restart sign-in retained all completed provisioning and the correct1280x800 Waterfall Ice background. Primary automatic login target remains FinalTest. Evidence: artifacts/shortcut2293-proof.json and vm1585-shortcut2294.png. Fast-switch behaviour remains separately unresolved; normal sign-in is now observed for both accounts.

## 5 November: rounded setup-button paint correction

Actual native rendering reproduced transparent action corners (ARGB0) appearing black in the VM. Button's native paint path can skip OnPaintBackground, so its custom foreground paint now clears to the parent surface before drawing the rounded face. The same native check now returns the exact parent colour at the corner; Sora descenders and hide/close/retry/duplicate-click fixtures also passed after the change. No installer ran. Full865-check regression preceded this two-line paint correction; affected native checks passed afterwards. Evidence: artifacts/corner2301.txt, corner2303.txt, corners2304-proof.json and setup-progress2304.png. The initial WinExe harness returned without waiting; completion was subsequently confirmed from its result files and exited process, without repeating the fixture.

## Next local candidate:0.1.28

Version advanced for the next local package so users of0.1.27 can receive the corrected build later. The supported-update checkbox no longer says experimental after the exact approved Ghost25H2-to26H2 route completed servicing, boot, installation and recovery acceptance. This caption change does not expand the supported routes: unknown enablement paths, failed health checks and unexplained preservation differences remain hard stops. Full app release, broader standard-image acceptance and hosted update delivery remain unfinished. Current0.1.28 source is not yet a signed downloadable release.
