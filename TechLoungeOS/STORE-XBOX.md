# Store and Xbox component acquisition

This is an explicit restoration option for six free Microsoft Windows components. It grants no account, game, subscription or purchase entitlement. Missing Appx/licensing/service infrastructure remains a compatibility failure. Unrelated stripped components remain the preservation baseline.

WinGet's official Store download route is tried without interactive authentication. Where unavailable, the builder uses Microsoft's package-retrieval service. Empty responses fall back to Microsoft's public product catalogue and Windows Update FE3 service. Store Purchase uses that catalogue directly. Product ID, package family and category must match the builder's positive list. Catalogue requests use the selected image's Windows version and Retail attributes, not the build host's Windows version. The numerical platform prerequisite IDs describe catalogue applicability; they do not represent updates installed on the target and may need revision if Microsoft's service changes.

Only original APPX/MSIX archives and bundles are accepted. Encrypted archives, third-party mirrors, reconstructed packages, account tickets, TLS exceptions and developer-mode installation are not used. All packages must pass manifest identity, size and Microsoft Authenticode checks before staging. Their actual SHA-256 hashes are recorded and checked again during Windows setup.

The package-retrieval service currently returns the main bundle's digest for its dependencies too. Dependency downloads therefore require its exact HTTPS host and a valid Microsoft signature; that inaccurate dependency digest is not treated as a SHA-256. Main-bundle and FE3 archive digests are checked against HTTPS catalogue metadata. Some FE3 archive URLs are HTTP-only Microsoft CDN locations; those require an exact catalogue SHA-256 as well as Microsoft signature verification. Redirects and arbitrary hosts/ports are rejected.

Windows setup stages the original signed archives through PackageManager.StagePackageAsync, then uses ProvisionPackageForAllUsersAsync. Existing provisioned families are retained. User registration remains a separate first-login check. This replaces the earlier Add-AppxProvisionedPackage/SkipLicense deployment path. Normal Windows licensing and deployment errors remain failures.

These Microsoft service interfaces can change independently of the builder. A changed schema, identity, URL, signature or integrity result stops acquisition. Offline packages remain an alternative. This is not a promise of permanent API availability or support for every stripped image.

## Evidence, 26 October 2026

The production downloader acquired all six component sets through Microsoft endpoints. Purchase, Xbox Identity Provider and Game Bar used the catalogue route where required. Production Prepare then verified all six sets, including dependencies (`artifacts/store-verify-543.log`). The first attempted service dependency hash failed closed, exposing the repeated-digest service response. A separate Xbox Identity response was empty; the catalogue fallback resolved it. The earlier failed download folder remains because automatic approval review rejected its cleanup.

Native targeted recovery on the existing Ghost VM provisioned all six families. A newly created standard account registered all six with healthy status, Store signatures and development mode off (`receipt-purchase-install-516.json`, `receipt-user-edge-528.json`). This is targeted recovery evidence. The exact generated deployment script's Windows Setup/SYSTEM path and another fresh ISO installation still need testing.

## Sources

- [Microsoft PackageManager provisioning API](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager.provisionpackageforallusersasync)
- [Microsoft WinGet download documentation](https://learn.microsoft.com/windows/package-manager/winget/download)
- [StoreDev/StoreLib reference](https://github.com/StoreDev/StoreLib), Public branch, MPL-2.0. Used to understand the FE3 wire format. The builder contains an original standard-library implementation, with no StoreLib source, dependency, device credentials or account ticket included.
