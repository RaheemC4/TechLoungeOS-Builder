# ISO and USB performance review - 8 November 2026

The app now avoids repeated work in USB-only, ISO-plus-USB and same-session saved-ISO-to-USB paths. This does not establish a hardware ceiling or guarantee speed on every PC.

| Stage | Evidence and finding | Result |
| --- | --- | --- |
| ISO import | WIM metadata is queried once, without mounting each edition. | Existing fast path retained. |
| Prerequisites and downloads | Office took about29seconds; sixteen runtime downloads about36seconds in build-3103. Current-version resolution and Microsoft signature checks are required. | Up to two independent prerequisite jobs overlap, backing off to one below2GiB available RAM. WinGet runtime downloads remain serial within their job. A checked same-session Office cache can seed a fresh Current-channel ODT resolution. |
| Source copying and edition export | Source ISO was copied before a roughly199second selected-edition export. Changing the temporary compression or ISO mount lifetime could change disk requirements and recovery behaviour. | Current compression retained; not changed without a representative native Windows-image comparison. |
| Main image servicing | Full before/after health scans and configuration checks occupy substantial time. | Reuse only the immediately preceding successful update transaction's full healthy scan, once on the same unchanged mount. Initial update and final servicing scans and preservation checks remain; no quick-health substitute or broad repair. |
| WinPE preparation | Interface pass plus separate compatibility pass previously mounted/saved the selected boot image twice. | One private transaction, one mount/commit per index, one shared before/after file snapshot per index. Services, original tools, compatibility values and boot metadata remain verified. |
| Payload staging | An intermediate complete hash inventory was repeated before the final inventory. | Removed the redundant intermediate inventory; final complete payload hashes remain. |
| Final ESD compression | Fixed two-thread setting underused this host. Pinned source documents about640MiB per default solid-compression thread. | Select1-8threads from CPU count and current available RAM, with reserved headroom; fail before compression below1GiB free. Same format, chunk size, integrity checks and16GB budget. |
| USB-only output | Previously exported, read back and extracted a temporary ISO. | Writes checked build files directly. No temporary ISO or second media copy. |
| ISO plus USB | Previously extracted the ISO just produced. | Uses the completed private media. Saved ISO is checked against its accepted hash before reuse. |
| Saved ISO then USB in the same session | Previously re-extracted unchanged exported media. | Reuses its recorded workspace/hash while the user's choices still match; changed ISO is rejected. Unrelated existing ISOs retain normal extraction. |
| FAT32 splitting | Necessary on small drives with large WIMs, but wasteful for full-capacity NTFS data volumes. | Large devices retain the WIM and use native FAT32 boot plus maximum-size NTFS data. Small devices retain checked WIM splitting. |
| Physical USB writing | Live samples showed16-20MB/s writing and around83MB/s verification reads. No comparative device benchmark or negotiated-link query establishes the exact hardware ceiling. | Sequential chunked copying and real byte progress retained. No global write-cache, antivirus or buffer-flush changes. |
| Verification | Source hashes before erase, target hashes after writing, complete ISO readback and protected replacement cost time. | Preserved. No success before verification. |
| Diagnostics and UI | Earlier logs lacked consistent per-command/stage durations. Completion was buried among controls. | Stage timings in state/log plus command durations/exit codes; dedicated completion screen, large checkmark and bounded background taskbar flash. |

## Results actually measured

- Original user run: about26minutes preparing/customising/exporting Windows,4.5minutes additional USB preparation, then about18minutes writing/verifying after confirmation. Those intervals have different workloads and are not isolated hardware measurements.
- The cached0.1.43 full-capacity rewrite finished in20:19 in the actual UI. It reused the earlier Windows build; this is not a fresh-build speed comparison. All1,117files plus66boot-file copies verified. Physical read-only partition inspection confirmed one visible61,198,041,088byte NTFS volume and an active792,723,456byte unlettered EFI boot partition(type239). Replug/firmware boot remains separate.
- Native256MiB synthetic archive, alternating order, two samples each:2threads averaged12.612seconds, selected8threads averaged7.839seconds, about37.8% less compression time. All four exports were135,539,802bytes; every extracted file matched its SHA256. This is warm local archive evidence, not a Windows or whole-USB speed claim.
- Native inert two-image WIM: actual DISM, Offline Registry and WIMGAPI preparation passed with exactly2mounts/2commits in18.74seconds. Independent native extraction verified all original tools/services, all three compatibility values on both images, the interface only on boot index2 and the unchanged original backup. This tiny fixture was never booted and contains no installable Windows.
-1,027ordinary regression checks passed before the final native-fixture CLI addition, which compiled separately. Final WPF completion/activity/minimum-layout and Unicode checks passed. No fresh installation or automated physical USB write was performed.

Evidence: artifacts/pipeline3024-baseline.json, pipeline3024-compression.json, archive-perf3018/results.json, pe-native3023/result.json, perf3021-regression.log, output3022-checks.log, completion3022-checks.log, copy3022-checks.log and physical3017-full-capacity.json.

A fresh real Windows build with0.1.44 has not been timed. The new timings provide that comparison on the next requested build without repeating installation tests. No universal compatibility or bottleneck-free claim is made.

## 8 November: cached physical rewrite observation

The repaired cached-media rewrite completed verification in about 19 minutes 20 seconds after writing began. Bounded observations found approximately16-20MB/s writes and87MB/s verification reads; source SSD activity was low while writing. This locates the observed wait on the USB write path, but does not establish a hardware-only cause, negotiated USB mode or maximum achievable speed. No antivirus, write-cache or removal-policy settings were changed. The prepared payload is13.56GB including mandatory Office files, rather than just the5.91GB source ISO. Skipping repeat customisation/export/extraction saves preparation work; the physical copy engine itself was unchanged for this rewrite.

## 8 November: actual 26H2 compression and larger-USB capacity

The native build-3103 updated Ghost COMPACT + DEF from 26200.9457 to 26300.9725 and passed servicing, health and preservation checks. Solid ESD compression then took 822.558 seconds with eight threads, producing 9,414,583,416 bytes from a 12,071,782,285-byte accepted WIM. This host has an i9-14900K and had about13.8GiB available memory when compression began; CPU model alone does not establish usable compressor concurrency.

A read-only native LZX export comparison took193.128seconds and produced11,981,695,507bytes. That is not a whole-build comparison. Keeping the accepted WIM avoids solid recompression but increases the bytes to copy; small USBs and conservative saved-ISO output can still require compression.

USB-only preparation now passes the selected device's actual capacity through to the packing decision. It retains existing image compression when the complete media and boot-layout allowance fit. Saved ISO output retains the conservative16GB target. Required payloads, health checks, preservation checks and file verification are retained.

The completed3103 payload exceeded the conservative15.5GB budget:14,183,706,101 media bytes require15,717,072,142bytes including layout/headroom. No ISO was exported. A guarded private recovery retained that failure journal and reused the committed media for the actual61,991,813,120-byte Kingston. Normal preparation checked all1,115files and created prepared.json; the original ISO hash was unchanged. This establishes prepared-media readiness for that larger USB, not a physical write or fresh installation pass. Evidence: artifacts/handoff3114-proof.json and build-3103/image-packing.json, windows-update/result.json and fast-export3113-result.json.

## 8 November: 0.1.46 performance candidate

The prerequisite batch now runs at most two independent jobs together. Each writes to a separate workspace folder. Runtime WinGet downloads remain serial; servicing, image commit, compression and USB writes remain serial. Failure/cancellation joins active peers before returning. Below2GiB free RAM only one download job runs. Four inert150ms jobs measured approximately632ms serial and315ms paired; this demonstrates scheduling, not a real network or whole-build improvement.

Within the same app session, a completed Office workspace may seed a later build. Every cached Office file is checked against its Builder receipt and held read-only until copying finishes; corruption, incomplete data or an invalid receipt falls back to fresh acquisition. No extra persistent cache is created. The signed Microsoft ODT is refreshed and unpinned Current-channel resolution runs again, so reuse does not pin an old Office release. A cache on the selected USB volume is excluded. Real ODT network reuse was not retimed for this candidate.

After a verified Windows update, the next servicing stage consumes that exact mount's healthy full scan once. Only read-only recovery/feature validation occurs between the scan and consumption. Before-update and final post-provisioning scans and every preservation gate remain. The redundant scan took98.990seconds in build-3103; that is avoided work from the recorded run, not a newly timed whole build.

Large-file checksums now reuse the1MiB sequential reader, with cancellation between chunks. Export readback uses a1MiB stream for files of16MiB or larger. Alternating warm256MiB fixture samples measured C# checksum averages201.47ms before versus126.88ms after (about37% less time), and Windows PowerShell5.1 averages194.24ms versus133.92ms (about31%). Digests matched. Real PowerShell readback also rejected a one-bit mutation beyond16MiB. Every source/target byte still gets hashed; no verification was removed.

Increasing LZMS from8 to12threads did not help the native1GiB inert archive:8 averaged23.197seconds,12 averaged23.664seconds. All four outputs were542,254,538bytes and extracted file digests matched. Production remains capped at8 with its existing memory budget. Larger USB-only builds can avoid the822.558second solid recompression when their complete uncompressed media fits; small USB/saved ISO budgets may still require it, and larger files add transfer time.

The user's already prepared26H2 Kingston USB subsequently completed full file readback (build-3103/usb-write.log). It was produced before these performance changes and is not a fresh0.1.46 timing result. A compression comparison briefly shared D: with its write and may have added contention; further native benchmarks were stopped while it finished. No original ISO, physical partition, write-cache setting or antivirus setting was changed by these performance checks.

Validation:1,078 ordinary regression checks; compiled WPF output-flow, Unicode and minimum-size USB completion/activity checks; Release and self-contained publish with zero warnings/errors. Evidence: artifacts/perf3126-*, hash3123-dotnet.log, hash3123-proof.json, and compression-scaling3121/results.json. These are logic, native inert-file and interface checks, not a fresh Windows installation or proof of faster physical USB writes on every machine.