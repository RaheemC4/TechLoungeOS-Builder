# Normal user journey checks

24 October 2026. Actual packaged WPF app on the isolated Ghost Spectre Windows 11 VM, 1280x800 desktop, administrator account. No presets, activation utilities or user app installers were run by these UI checks.

| Journey | Observed result |
| --- | --- |
| Open Builder | Window fits the work area, with native caption and accessible footer actions. |
| Browse to the copied Ghost ISO using the native file dialog | Six editions imported. Renamed `source.iso` still receives the customised-image indicator and preservation wording. |
| Search for Discord with Enter or Search apps | Thirty live WinGet results appear directly below the query/list, with the real standard Discord icon. |
| Inspect Discord ARM64 | Labelled ARM variant is disabled for this x64-only milestone. |
| Add standard Discord | Exactly one selected app; its result says Added. Nothing installs on the builder computer. |
| Navigate to another page and return | Query, result list and selected Discord survive. |
| Remove Discord | Selected count returns to zero and the result becomes selectable again. |
| Open wallpaper picker, then cancel | Original-background choice and blank wallpaper path remain. |
| Reach wallpaper action with Tab | Windows scrolls the focused Choose image button into view; Enter opens the native dialog. |
| Click Add in 0.1.7 | Defect reproduced: page jumps down and hides the search query. |
| Repeat in corrected 0.1.8 | Query, headings and selected row stay visible. Subsequent Tab skips disabled ARM64 and scrolls to the x86 result's Add button with visible focus. |

Evidence: `artifacts/journey-129-*`, `journey-132-*` and `journey-133-*`. The before/after scroll captures are `journey-132-added.png`, `journey-133-search-before.png`, `journey-133-add-after.png` and `journey-133-keyboard.png`. Automated checks complement these clicks; they are not substitutes for installed-image acceptance.

Two automation typing attempts dropped path characters. Selecting the actual file normally avoided the problem; the QMP test input wrapper was corrected for uppercase characters and file-based command text. These errors were not attributed to the product.

Subsequent native acceptance completed a real signed update, the complete integration-bundle export, both virtual USB writes and actual animated WinPE boot/tool click-through. Those checks are recorded separately in TEST-RESULTS.md; the driver bypassed build pickers/review dialogs and does not certify that ordinary-user journey. Still required: normal review/cancel and recovery journeys with a complete compatible integration bundle, fresh full installation/first-login acceptance and physical display/accessibility testing. Project choices are not currently saved across app restarts.

The later blank-disk installation completed and exercised Ghost's own first-login restart and Builder's retained successful-step retry. Actual installed-app checks opened Tweaks from its desktop shortcut, SumatraPDF and Discord from their shortcuts, and 7-Zip/Word through Windows launch commands. No app account or activation was entered. Word reached its licence sign-in screen. An unexpected Internet Explorer retirement warning appeared during launch checks on this browser-stripped source; dismissing it left Discord functional at sign-in, but its origin remains unconfirmed. See the fresh-install section of TEST-RESULTS.md for the missing-definition diagnosis and native correction. Latest automated recovery changes still need another fresh-image acceptance run.

Packaged 0.1.12 normal clicks retained SUPERLITE + DEF across navigation and a cancelled source picker (journey-234-*). During the later actual build, opening activity showed old buffered output until the next message. The corrected 0.1.13 source passed a failing-before/passing-after WPF viewport regression for opening/reopening logs at all three sizes. Actual consumer Store installation on the owned modified VM reached its home page without signing in, but its registration was current-user only. Neither result establishes global Store/Xbox provisioning or the outstanding latest-image installation gates.

Normal packaged 0.1.12 update selection/verification and Install/restart completed the signed 0.1.14 upgrade, retaining an unrelated user file. A preceding 0.1.13 attempt refused a running-copy check without altering installed files. The shared guard now exposes the blocking process ID. WPF/owned-VM handler checks cover ordinary close versus explicit update restart, so two windows with queued updates can still close. The final signed 0.1.14.1 apply used the local updater harness, rather than another full human click journey.

The latest full build 252 completed fresh Setup and Ghost's own restart. All seven provisioning steps finished automatically. Normal desktop shortcuts opened Tweaks/private WebView2, SumatraPDF and Discord; Word reached its licence prompt and the retained 7-Zip file manager opened. The long 100% Setup stage ended normally after bundled setup work. A nontechnical-user quirk remains: Discord's optional browser handoff launches the retired IE stub on this browserless source, although manual sign-in is available. Process-parent evidence identifies Discord as the launcher; no supported-browser restoration or broad RPC workaround was imposed on the source. No accounts or activation were entered. The test record separates this source limitation from successful install/provisioning and from controller quoting/drive-letter mistakes.
## 26 October: new standard account and Edge

Actual VM interactions: normal first sign-in to a disposable standard account; Edge first-run without account login, browser data import or automatic startup; web-page launch via Windows association; Discord launch through its repair shortcut; native browser handoff and retained permission prompt; return to Discord sign-in; normal Windows shutdown. HTTP/HTTPS defaults are Edge. All six Store/Xbox families registered for the account. No account sign-in or purchase was performed.

The final audit found selected apps/wallpaper/OpenAsar succeeded, but the Recommended search policy failed on the protected HKCU key. Code now prepares that reviewed default-profile policy offline and recognises an exact preconfigured DWORD without rewriting it. The actual test account is still incomplete; corrected fresh-install acceptance is pending. Manual diagnostic PowerShell windows in screenshots are the test harness, not evidence of the packaged quiet launcher flashing.
