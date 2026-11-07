# Engineering references and validation tools

- [Intel graphics driver installation guidance](https://www.intel.com/content/www/us/en/support/articles/000005629/graphics/processor-graphics.html) and [OEM-driver restrictions](https://www.intel.com/content/www/us/en/support/articles/000005469.html): vendor reference releases and OEM customisations need separate compatibility consideration.
- [AMD Auto-Detect tool](https://www.amd.com/en/resources/support-articles/faqs/GPU-131.html): latest recommended/optional packages, explicit install choices and factory-reset behaviour. The Builder does not invoke this utility or factory reset in the Windows Update path.
- [WUA driver properties](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/iwindowsdriverupdate-properties): hardware identity/provider/date are available but no structured version property. The version-title parser rejects unrecognised formats instead of guessing.

- [NVIDIA Windows driver installation guide](https://docs.nvidia.com/datacenter/tesla/driver-installation-guide/windows.html): quiet driver-only setup and success/restart exit codes. [Notebook OEM guidance](https://nvidia.custhelp.com/app/answers/detail/a_id/2085/) identifies laptop-specific compatibility limits.
- NVIDIA's live [product lookup](https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=1) and [driver catalogue service](https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php), inspected on 25 October 2026. Endpoint/schema changes must produce visible failure rather than a guessed GPU family.
- [Microsoft driver matching](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/step-2--a-driver-for-the-device-is-selected) and [Windows Update Agent API](https://learn.microsoft.com/en-us/windows/win32/api/_wua/): matching-only base-driver bootstrap without altering update policy. Windows Update's best match is not necessarily NVIDIA's newest release.
- [Profile Inspector startup code](https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/App.xaml.cs), inspected for `-exportCustomized` and `-silentImport`; actual bundled-tool/NVAPI execution remains a hardware test gate.

## tiny11builder

Reviewed [ntdevlabs/tiny11builder](https://github.com/ntdevlabs/tiny11builder), branch `winui-preview`, revision `8909da14b783e9625bfabc7ab2cf16e233b00a21`, on 21 October 2026.

Useful comparisons were native DISM inventory, WIM/ESD handling, separate servicing scratch directories, checking actual component states, and BIOS/UEFI Oscdimg arguments. These are implemented independently in TechLoungeOS using Microsoft tool contracts. No upstream source or assets were copied. Its NOTICE states there is no standalone upstream licence grant; do not bundle or redistribute upstream material without resolving permission. The upstream Core mode is outside our preservation requirements.

## Microsoft tools

ADK bootstrapper obtained from the current [Microsoft ADK download page](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install). Its Authenticode signature was valid and signed by Microsoft Corporation. Local validation extracted selected deployment-tool cabinets rather than installing the ADK. Cabinet bytes were checked against the bootstrapper's embedded payload manifest; the x64 Oscdimg and standalone DISM executables also had valid Microsoft signatures.

Oscdimg SHA-256 used for the test: `D2709F8099DDD202BC41BCB399ECD5638503C6466F416D1A321769AF83A70C3A`. Host DISM reported `10.0.26100.9549`; the separately extracted ADK DISM reported `10.0.26100.9457`. Both reproduced the observed host offline-initialisation failure. ADK tools are local ignored validation dependencies and are not bundled in the builder application or source diff.

## Isolated Windows PE validation

Portable QEMU binaries were obtained through the Windows download link on [qemu.org](https://www.qemu.org/download/), from Stefan Weil's linked distribution. The downloaded `qemu-w64-setup-20260811.exe` was checked against its published SHA-512 file and extracted locally, without running its installer. It reports QEMU 11.1.0.

Validation uses a newly created 64 GB virtual disk, a copied Windows PE boot image, no networking and no passed-through host disks. Host-side preparation resolves that specific virtual disk file to its disk object and rejects system, boot, existing-partition or unexpected-size targets before initialising it. A random marker identifies the validation volume. Guest startup performs no disk formatting, and the servicing worker requires Windows PE plus a marked volume. This is a validation harness, not an advertised automatic VM feature of the desktop builder.

Windows PE, Windows installation media, QEMU and ADK tools are not part of the distributable application. Their respective terms and notices continue to apply.

## App artwork, 22 October 2026

App search and selected rows use actual package artwork when available. An embedded exact-ID catalogue currently maps 191 WinGet IDs to the package-icons repository pinned at 5ce78130bf1d802ce3460c376a5fc76a7379e362. Downloaded PNGs must match the catalogue SHA256. Verified bytes are cached under LocalAppData/TechLoungeOS/AppIcons; cache failures do not block selection. Remaining apps try supported WinGet icon metadata, then show a neutral Windows app symbol, never a first-letter tile. This improves coverage, not a guarantee of artwork for every WinGet package. Icon requests are optional, bounded and limited to four concurrent loads. No installers are downloaded for icon extraction. The catalogue only supplies artwork; it does not limit search or app selection.

Artwork is for product identification and does not imply endorsement. Upstream third-party notices ship alongside the builder, including Icons8 attribution. Catalogue refreshes require a reviewed revision/hash update rather than silently following a mutable branch. Source: https://github.com/memstechtips/package-icons/tree/5ce78130bf1d802ce3460c376a5fc76a7379e362

- [Microsoft file attribute/tag information](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_attribute_tag_info) and [GetFileInformationByHandleEx](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex): distinguish native WIM-backed hive placeholders from name-surrogate links. Validated against a real mount on 23 October 2026.

## Blank-password validation references

- Microsoft LogonUserW parameter/result contract: https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-logonuserw
- Microsoft error1327 description: https://learn.microsoft.com/windows/win32/debug/system-error-codes--1300-1699-
- Chromium's existing blank-password handling also accounts for ERROR_ACCOUNT_RESTRICTION: https://chromium.googlesource.com/chromium/src/+/127.0.6533.88/chrome/browser/password_manager/password_manager_util_win.cc . Used as a behavioural cross-check, not copied implementation. The owned native1102 probe independently reproduced1327 on this image.

## Automatic ISO component in 0.1.38

The app obtains the exact x64 Oscdimg 2.56 executable from Microsoft's HTTPS symbol server and accepts a pinned whole-file SHA-256. The observed Microsoft blob redirect is restricted by host and exact path. Downloaded SHA-256: `F5129F313ED7EB46F2677CF522E64264A225F226307ED0DDB52BB14C46E7CFDD`. This file has no embedded Authenticode signature; acceptance relies on the exact checksum and restricted Microsoft source, not a claimed signature. The previously verified Microsoft-signed tool (`D2709F...A70C3A` above) is also accepted when supplied by an existing caller. Tooling is copied into the administrator-protected cache and rechecked before use. Preparation failure stops before USB erasure. No general unsigned-executable exception is introduced. The component is acquired directly by each installation, not redistributed in the ZIP or source. No tiny11builder source or assets were copied. Microsoft's terms continue to apply.
