# Native WinPE setup and tools

`SetupPreview.c` now builds the embedded `PeBackground.exe`: a persistent native Win32 shell with an animated background, account form and original-image tools panel. Normal desktop mode previews the interface without deploying Windows or accounts. Deployment requires explicit `--pe` and genuine WinPE checks. The PE shell has no browser, network, service or .NET dependency; the installed Windows account helper separately requires retained .NET Framework 4.

The Builder setting starts enabled and currently supports the recognised Ghost layout. It replaces a recognised single Ghost LaunchBar entry in `[LaunchApps]` at boot.wim index 2, at its original foreground launch point. Do not detach this shell: allowing winpeshl to exit can reboot WinPE. Unrelated startup bytes and the original utilities stay unchanged, with an exact INI backup. Unknown layouts, encodings, missing prerequisites and existing integrations stop the private build. Generic Microsoft/OEM shells are not yet supported by this option.

The copied boot image gains the executable, original INI backup, two inert account templates, media identity and three licensed font files. A matching marker is added to copied installation media. Per-install credentials are rendered only on the WinPE RAM drive when the user continues. All inherited tool files must match their captured hashes; the source ISO is never opened for writing. The themed catalogue resolves existing shortcuts, including their arguments and valid working folders. Third-party tool windows retain their own interfaces.

Build with MinGW-w64 GCC (tested compiler 14-posix, Debian package 14.2.0-19+27+b1):

    x86_64-w64-mingw32-gcc -Os -Wall -Wextra -Werror -mwindows -municode -static -s SetupPreview.c -o PeBackground.exe -lmsimg32 -lgdi32 -luser32 -lshell32 -ladvapi32 -lole32 -luuid -lm
    x86_64-w64-mingw32-gcc -Os -Wall -Wextra -Werror -municode -static -s RenderChecks.c -o PeRenderChecks.exe -lgdi32 -luser32 -lm
    PeRenderChecks.exe <new-empty-local-output-folder>

Recompile the embedded asset after native source changes. Place the Fonts directory beside it for desktop checks. The older `Background.c` and RenderChecks exercise the shared ambient renderer only. Fresh1263 booted the exact ISO1250/USB1255 with Secure Boot/TPM, discovered 21 original shortcuts, exercised the live account form and completed installation without the remaining setup questionnaires. Automatic sign-in passed after recovery and an ordinary restart. Legacy1300 separately booted that USB without UEFI/TPM and opened the original CPU-Z at 1024x768; it was not a full legacy installation. See ../TEST-RESULTS.md and ../WINPE-TOOLS.md for evidence and limits.

No Ghost Spectre executable, tool, archive or branding was copied into this program. No third-party shader/image assets were used. The compiler was downloaded/extracted locally; the WSL system package database was not modified.

## Interface checks

Desktop preview and check modes never install Windows, execute source tools or
change account/security settings. Full accessible labelling and fresh-install
acceptance remain incomplete. `--handofftest` checks account validation, generated
PC names, XML escaping and Windows password encoding without installing Windows.

The revision uses privately loaded, bundled Manrope Regular and Semibold static
fonts, softly rounded controls, one persistent icon toolbar and a restrained blue
accent. `Fonts/OFL.txt` retains the SIL Open Font License; `Fonts/REVISION.txt`
identifies the upstream googlefonts/manrope source revision. No system fonts are
installed. Native `GetTextFaceW` readback verifies the selected family.

Build from this directory with MinGW-w64, placing the executable and a copy of
the `Fonts` directory together in the output directory:

    PeBackground.exe --handofftest
    PeBackground.exe --selftest
    PeBackground.exe --formtest
    PeBackground.exe --motiontest

`--render <new-home.bmp> <new-account.bmp>` renders 1280x800 views;
`--compact <new-account.bmp>` renders 800x600. Output files must not already
exist. `--selftest` checks transition reversal, zero-time continuity and compact
control bounds. A missing bundled font causes a visible error; a substituted
family causes render verification to fail.

`--formtest` creates the real native edit controls in a hidden test window and
checks confirmation initially hidden, immediate appearance on password entry,
retention while an emptied password still has focus, hiding/clearing after blur,
and clean reappearance. Confirm password is absent until password entry; clearing
the password then leaving its field hides the confirmation and clears its value.
Ctrl+A selects all text, and clicking the panel background releases input focus.
No typed values are logged or persisted by this prototype.

`InputEditing.h` adds consistent Ctrl+A/C/V/X and Ctrl+Backspace handling to
all four edit fields. Password text remains masked, but explicitly copying a
selection puts that selected text on the Windows clipboard as requested.
Temporary password buffers are cleared. Copy retries brief clipboard contention;
failed cut never removes the selection. Word deletion handles whitespace,
punctuation, accented letters and UTF-16 pairs without inserting DEL glyphs.

Checkboxes toggle from their whole 34-pixel-high label row, not just the visible
18-pixel box. The preview animates checkbox state, row hover, button hover/press
and field hover/focus/typing feedback from their current pose. Text entry itself
is immediate; it does not delay or reveal password characters. Rounded keyboard
focus rings follow button/row corners and remain distinct from pointer feedback.
Transitions read current field values (masked for passwords); they never paint
sample credentials before swapping to native edit controls. No fields are
prefilled when opening a new live preview.

Normal mode supports Install Windows, Back, Tab/Shift+Tab, Enter/Space and Escape
for preview navigation. Text entry is interactive; other commands and account
choices remain visual examples. Hover and press feedback are native drawing; panel transitions fade
through the background to avoid overlapping text, with exponential state-following
that retargets from the current pose. Double buffering prevents erase flashes.
Background drawing stops when minimized/hidden. Windows animation/high-contrast
preferences suppress motion. `--demo` cycles the two views for recording;
`--demo-static` exercises the same layout with reduced motion without changing
host preferences. An optional second argument to either mode writes a new JSON
paint-timing receipt on normal close. Timings measure desktop painting, not
compositor presentation, booted WinPE frame pacing or low-end hardware support.

Desktop captures and compact renders are design evidence only. Native Ghost
setup handoff and automatic sign-in have the installation evidence above. Generic
boot integration, full accessible field/button semantics, physical-device behaviour
and Windows Hello/passkey enrolment remain separate gates.

### Fluid motion refinement

`Motion.h` uses exact critically damped motion, retaining velocity when an action
reverses. Hover, focus, checkbox and button-pressure feedback share this treatment;
typing remains immediate. Panel opacity has a smooth start/finish without drawing
outgoing and incoming text over each other. Current values remain masked during
transitions. Tab/Shift+Tab now follows visible fields, checkbox rows, footer and
toolbar controls; hidden password confirmation is skipped. Opening setup focuses
the username field once it is visible.

`PreviewSurface.h` retains drawing surfaces until the window size changes and
releases them on exit. The ambient image is cached at 30 updates/second, while
controls can update faster. `FrameClock.h` optionally uses a high-resolution
waitable timer, paced to the display up to 120 Hz, through the existing UI thread.
Input continues to be processed between frames. It changes no global timer or
power policy. Unsupported timer creation/arming retains the WM_TIMER fallback.
Window dragging/resizing uses the fallback during the native modal move loop.

`--motiontest` checks analytic spring response, frame subdivision, reversal,
bounded repeated retargeting, actual pixel preservation, surface reuse, resize
and GDI handle cleanup. `--measure <new.json>` cycles the same pages for 16 seconds
and closes itself. `--measure-fallback` forces the legacy timer;
`--measure-static` exercises reduced motion. Receipts include drawing durations
and paint-start intervals. They measure desktop paint work, not physical display
presentation or booted WinPE performance. Reduced motion suppresses continuous
painting while preserving input feedback; hidden/minimized windows stop drawing.
