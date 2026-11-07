# Private WebView2 for TechLoungeTweaks integration

Some modified sources keep WebView2 registration after removing its runtime files. The supplied Ghost Spectre SUPERLITE + DEF image does this. The existing Tweaks release accepts the registration and can remain hidden while its browser engine never starts.

The builder supports an app-private Microsoft Fixed Version runtime. Browser data lives under the signed-in user's LocalAppData\TechLoungeOS\WebView2. It does not repair the source component store or system runtime registration.

Current installer integration downloads the upstream installer using its published schema2 release metadata and verifies both the archive and extracted installer hashes. It stages the private runtime from the selected support folder rather than copying the legacy application bundle. Account setup prepares WebView2 policy values scoped specifically to TechLoungeTweaks.exe for BrowserExecutableFolder and UserDataFolder. This also covers the unchanged upstream updater and tray launch paths. Conflicting existing policy values stop preparation rather than being overwritten.

The final5November candidate uses the existing immutable machine task to configure only HKLM's BrowserExecutableFolder value for TechLoungeTweaks.exe. It retains matching policy types/values, rejects conflicts and never writes machine UserDataFolder or enters user registry hives. Existing initial-account data overrides remain intact. Additional accounts use the unchanged application's own per-user browser storage, as supported by [pywebview's documented storage behaviour](https://github.com/r0x0r/pywebview/blob/master/docs/api/README.md). Ordinary provisioning only reads these values; it does not request elevation or alter policy ACLs. Scoped registry fixtures pass, and the VM fixed task prepared the shared value without rerunning completed machine installers. Additional-user retry acceptance remains pending. This supersedes the earlier per-profile task-request candidate and is not yet in the earlier ISO/ZIP.

The retained portable-bundle mode copies the complete selected bundle and uses a separate launcher with process-scoped runtime environment variables. The desktop shortcut uses that launcher. This older path can still be blocked when antivirus rejects a bundled utility.

Automatic Recommended setup uses the Builder's reviewed positive list. Windows Update pauses and all Defender changes are excluded. NVIDIA settings and graphics drivers use separate hardware-gated workers. The original Tweaks release and its manual presets remain unchanged; automatic integration does not establish that every bundled manual utility will pass antivirus checks.

Prepare a separate integration bundle:

1. Copy a trusted extracted TechLoungeTweaks release into a new folder. Keep the original release intact.
2. Obtain Microsoft's complete x64 Fixed Version WebView2 package from its [official download page](https://developer.microsoft.com/en-us/microsoft-edge/webview2). Follow Microsoft's [distribution instructions](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution), verify the source and Microsoft signatures, and expand the entire package.
3. Copy the expanded version folder into the integration folder as `WebView2Runtime`. Its root must contain `msedgewebview2.exe`, `msedge.dll`, `icudtl.dat`, `resources.pak` and all accompanying package files/subfolders. Do not copy just these four entry files; their presence is only a sanity check, not authentication or proof of a complete runtime.
4. Select this integration folder in the builder's existing TechLoungeTweaks folder picker.

Layout:

```text
Integration bundle/
  TechLoungeTweaks.exe
  _internal/...
  WebView2Runtime/
    msedgewebview2.exe
    msedge.dll
    icudtl.dat
    resources.pak
    ...all other Microsoft package contents...
```

If no runtime executable exists at the supported image paths and no private runtime is supplied, the build stops before committing. An incomplete private runtime folder is also rejected. A private runtime does not repair the source component store or make a stripped image equivalent to OEM media.

Fixed runtimes do not update themselves. Replace the private runtime with a current verified x64 Microsoft package and rebuild/retest the bundle. Automatic private-WebView2 downloading/updating is not implemented. Keep this maintenance responsibility separate from the system's Evergreen runtime updates and the upstream Tweaks installer update.

The tested private runtime is156.0.4314.42x64. Targeted native tests of current2026.10.23.174632 installer integration opened Tweaks and completed the repaired first-login sequence. These do not replace a fresh install of the current993 candidate. Quarantined advanced resources remain a compatibility gate. The earlier portable test preserved2026.10.18.174440 byte-for-byte; that older release can reject a source that removes runtime registration as well as files. Standard-user/elevation combinations, other modified distributions and later private runtime versions need their own verification. Do not advertise universal modified-ISO compatibility.
