## 6 November: standard-source migration, current acceptance boundary

The reviewed catalogue now has separate routes for sources26200.9457 (Ghost) and26200.8037 (standard) to26300.9725 using the exact previously verified Microsoft package sequence. It is not a general permission to upgrade every source or future version. Changed source task definitions, required source services or missing/changed source framework manifests reject the standard-specific route before servicing. The existing Ghost route retains its19 transitions and does not acquire stock security/AI framework restoration permissions.

The standard route records65 measured service transitions, three internal metadata values and two exact internal framework changes. All other values in the affected default-profile, memory-management and policy trees must match. Task GUID rebinding is normalised, but full reviewed task data (including actions, triggers and stored permissions), the exact original task cache and both file/content/NTFS-access fingerprints are required. The measured offline Tasks directories are empty, so their matching fingerprints alone are insufficient; an unexpected full task fingerprint stops export. Custom disabled services and unrelated lost/added system apps remain failures.

The original public native experiment applied the packages and reported a healthy target, then correctly stopped on the new camera feature. After independent source-hive comparison and migration review, the retained private candidate passed the corrected production preservation checks and its post-correction health scan. Commit/export/installation gates are recorded separately in TEST-RESULTS.md. No packages were reapplied, no stock image replaced the customised image and the original user ISO remains detached.

## 3 November: client camera correction and source-service dependency protection

The retained image reports the new Camera-CCX feature as Disabled after standard DISM Disable-Feature /Remove, and its newly introduced Ccx01000 driver registration is absent again. Microsoft documents that Windows client editions retain feature installation files for recovery even with /Remove; Disabled with Payload Removed is not a valid universal client requirement. The exact reviewed migration now accepts fully Disabled or Disabled with Payload Removed for this one newly introduced feature. Enabled, pending and unknown states still fail. The Ccx01000 addition was removed from the service exception catalogue, so a restored driver registration is rejected. Existing source feature states and removed app/security payload gates remain strict. This is an explicitly bounded new-release migration, not a promise of byte-identical Windows servicing-cache contents.

Service replacements now require the matching predecessor's original full hash and its verified retirement. This prevents recreating an isolation service that a different custom ISO had already stripped, or replacing custom disabled predecessor settings with defaults. Independent native before/after hashes confirm the tested Ghost predecessor and replacement meet this rule. The source retains19 reviewed service transitions after camera correction. Unknown changes still stop export.

Final source verification:32 update regressions,45 feature-upgrade regressions and31 migration/preference regressions passed, plus security-descriptor checks. Builder and Tests compiled without warnings/errors. A second real NTFS fixture also passed with administrators given read access only, proving attribute restoration works after the temporary write permission is removed. Its receipt SHA256 matches tool5DB64BCBBE83FCB5E3A0D83A5CCBD8FE95B28BF809D83387C6818F3A38C2C0EC. The production repair helper was unchanged between these two native fixtures.

Native continuation1726 verified the recovered exact mount, rehashed the previously signed package bytes, verified the actual26300.9725 release and retained edition, and passed its app/feature/configuration/global-preference/removed-payload/artwork comparisons. Its final full ScanHealth completed successfully with no component-store corruption. At20:35:05 UTC the same private WIM committed successfully and independent Get-WindowsImage readback verified10.0.26300.9725. The mount is released, failure is null, and the exact source edition remains IoTEnterprise. Evidence: artifacts/accept1751.json and health-commit1752.txt. This passes native saved-image servicing acceptance for this exact source/package route; integrated ISO, USB, installation and recovery acceptance remain pending. No update packages were reapplied and no fresh install was repeated. The additional predecessor dependency was added to production source during this scan; independent recorded native hashes prove the tested image also meets it. Boot/USB, installation, recovery and complete Builder-path acceptance remain pending. Keep unreleased.

Servicing-host prerequisite verified3November: the exact approved donor exists on the physical Windows host with SHA25661643221D2F14BE9E180E507D98ABEA29EEBFB7472797342EC3E25073B7E4CAD. The Ghost-installed servicing VM instead has the altered9363F9E568986C3B9BB02C210FA98C0B08A0C8EC301A7C798FA2B5E6AC723584 file. Automatic repair must stop on that guest rather than import another donor or change live Windows. Two native fixtures verify the production replacement helper, but the original-ISO automatic repair and full Builder transaction remain a separate unverified acceptance gate. The current native transaction used the previously approved manual repair.

Reference: https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/enable-or-disable-windows-features-using-dism?view=windows-11 (Windows client feature-payload behaviour). Evidence: artifacts/camera1724-data.json, inspect1653.json, repair1715.json, repair1722.json and *1730-tests.log.


--- Earlier records below ---

# 0.1.26 guarded enablement-upgrade candidate, 1 November 2026

The experimental Windows update option now resolves supported Windows 11 24H2/25H2 x64 images to the latest stable 26H2 target through Microsoft KB5121794, after its matching cumulative prerequisite. Microsoft release metadata remains live; a versioned embedded compatibility catalogue authorises the exact upgrade route, source editions, prerequisite revision and pinned enablement bytes. Future releases can be detected without authorising unknown package routes. New routes require a reviewed Builder update; this cannot guarantee compatibility with unreleased Windows versions. LTSC/server/unknown editions are not converted. Actual offline edition identity, target build, DisplayVersion and retained edition must agree.

The native update transaction uses separate cumulative/enablement folders so checkpoint discovery cannot apply enablement out of order. The cumulative stage keeps PreventPending; the enablement stage uses standard applicability checks after the prerequisite has been verified. Inherited pending servicing is rejected. There is no stock-image replacement, automatic component repair, IgnoreCheck or ResetBase. Private committed WIM metadata is refreshed to the independently verified OS version, retains custom identity/counts and is read back before ISO export.

Preservation now also checks originally absent Defender/security payloads and newly added system-app folders. Offline settings snapshots include machine Run/RunOnce (both registry views) and scheduled-task cache, alongside existing services, policies, default-user settings and optional-feature/Appx removals. A difference prevents commit/export. This is an export rejection guarantee, not proof that every modified ISO can successfully upgrade. Existing intentional Builder inclusions remain separate. Original ISO files were not modified.

Earlier verification: 809 ordinary source checks passed, followed by focused final cumulative and feature-upgrade regressions. The new feature fixtures cover package order and folder separation, required versions, source previews, future unknown releases, LTSC/edition conversion, cancellation, inherited pending state, restored Defender/system apps, changed settings, bad hashes, redirects and byte budgets. Real Microsoft release discovery and a small enablement download passed; the 177535-byte package SHA256 is BF43EB13B7EBDE920C2201473BFD5C531B45E0F46ACD8E0843F27F9744FB4B43, with a Valid Microsoft Corporation Authenticode signature. This smoke assumed an already-updated prerequisite and did not service Windows. Compiled UI release/edition fixtures and five-page/three-size WPF checks passed. An attempted test rebuild hit the still-running test process's file lock; it stopped normally and the subsequent build passed without warnings/errors. No lock was bypassed.

Native 26H2 servicing, private-WIM metadata API readback, export/boot, recovery and first-login acceptance remain unverified. Focused native validation1586 resumed the exact saved VM/disks/TPM. The retained stock build passed its 1088-file media check and continued a separate read-only WIM audit. Original Ghost edition2 (COMPACT + DEF, 26200.9457) exported and mounted successfully, with the source ISO attached read-only. CheckHealth reported repairable; that cached flag stopped the preliminary harness before any Windows update package download/application. Private discard began. This is not a fresh ScanHealth result or a successful upgrade. Corrected harness1591 is prepared to reuse the existing WIM and let the production updater run its full scan, with bounded CBS diagnostic tails. Source app/configuration baselines now survive a failed source scan; 45 targeted feature checks and the affected cumulative regressions passed, and Builder compiled without warnings/errors. The four-file test patch is prepared, not deployed. VM CPU/network were temporarily paused for the user's internet-performance comparison. No 26H2 update, fresh installation or physical USB write is accepted. Post-install process/startup comparisons must use matching app selections, hardware and network state; a Task Manager count alone is not preservation proof. No fresh install, physical drive write, original-ISO alteration or publication occurred. This is a testing candidate, not a public-release compatibility claim.

--- Earlier evidence below ---

# Current interface integration, 1 November 2026

The Windows image page now has a separate metadata-only Check Windows version action and an explicit experimental cumulative-update choice. Build-time discovery rechecks Microsoft release metadata and downloads into that build workspace, then uses the existing strict transaction. The default retains the source version: automatic feature upgrades and preservation of arbitrary modified images remain unverified. The top-right Check for updates remains exclusively for the Builder app. Cancellation, late results, source changes and unsupported architectures have compiled-interface fixture coverage. No native updated ISO has been accepted.

# Windows image updates: unreleased backend

The Builder app updater and Windows image servicing are separate systems. The top-right **Check for updates** action still checks Builder releases.

The image-update source now supports a guarded, explicitly supplied cumulative-update plan through `BuildOptions.WindowsUpdateFolder`. The experimental Windows image-page option performs Microsoft discovery during a build; a prepared folder can also be supplied through the API. The selected source version must match the plan exactly; the target must be newer within the same Windows build family. Feature-release enablement and full feature upgrades remain unimplemented.

`WindowsUpdateDownload.Check` reads Microsoft's release history and selects a stable B or out-of-band cumulative update for the source build. It excludes previews, future-dated rows and unrelated build families. `Prepare` resolves one exact x64 catalogue entry, downloads only permitted Microsoft HTTPS payloads, validates their SHA256 values and writes a source-bound plan. The image-page option now calls these APIs during a build; the separate version-check button reads only metadata. Unexpected provider layouts, redirects, payloads or ambiguous matches fail rather than guessing.

Example plan structure, with deliberately non-usable placeholder values:

```json
{
  "Schema": 1,
  "SourceVersion": "10.0.26200.9457",
  "TargetVersion": "10.0.26200.9725",
  "Packages": [
    {
      "File": "windows11.0-kb5134133-x64_<Microsoft filename digest>.msu",
      "Bytes": 0,
      "Sha256": "<Microsoft SHA256>"
    }
  ]
}
```

Required checkpoint packages come first; the target MSU comes last. This example is not a current-version recommendation or an installable manifest. Actual filenames, sizes and checksums must match Microsoft metadata. At most eight packages, 8 GiB per file and 20 GiB total are accepted.

The update transaction snapshots source apps, optional features, services, policies and default-user settings, and requires a healthy component store. It creates private package copies, holds them against writes, verifies their checksum and Microsoft signature, and invokes DISM without `IgnoreCheck`, `ResetBase` or automatic component repair. Afterwards, it checks the exact Windows version, component-store health and the existing strict preservation rules. A failed check prevents the build from committing or exporting the modified Windows image; diagnostic snapshots remain in the workspace. Cancellation waits for servicing to finish before discard.

The strict gate intentionally still rejects unreviewed Microsoft migrations as well as unwanted restored components. The earlier private Ghost cumulative-update experiment remains rejected. This backend has passed inert command/HTTP fixtures and actual Microsoft metadata parsing, but not a native cumulative-update export, boot or fresh-install acceptance run. It cannot be described as preserving every possible custom ISO through every future Windows update.

Still required for the complete requested feature: supported feature-release transitions, a reviewed approach to legitimate update migrations without undoing customisations, coordinated Setup/WinPE/WinRE updates, native servicing/readback, recovery acceptance and final frozen-candidate installation checks. Keep unreleased.

Microsoft references: [media Dynamic Update](https://learn.microsoft.com/en-us/windows/deployment/update/media-dynamic-update), [DISM package servicing](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-operating-system-package-servicing-command-line-options?view=windows-11), [Windows release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information).
