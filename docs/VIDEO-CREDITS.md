# Video credits and evidence

The 93-second showcase is 1920 × 1080 at 60 fps. One persistent product frame keeps native pages, search results and USB windows aligned. The approved icon and product name identify the app on the first frame. The result shows all ten selected apps plus TechLoungeTweaks on a labelled desktop visualisation. A double-click opens the actual Tweaks Home interface, System expands using captured native animation frames, Resources is selected, and its existing Activate Windows option is hovered without running it. Tweaks closes back to the desktop before the icon-and-name ending.

Music: **Happy Beats & Business Moves Vol.10**, Sascha Ende, [ende.app](https://ende.app), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The original 60-second track is joined to a second copy with a three-second crossfade, level controlled and trimmed to 93 seconds, with the final fade at the end card.

Sound effects: [Kenney](https://kenney.nl/), CC0. The click is byte-identical to the earlier approved Shiftd showcase asset (SHA256 `2FA929138BC3A0F432696588F66EAC94B8F6D463905EC8808C6461CE4B054292`). The edit uses 38 click, selection, switch, slide and soft-impact cues. Typeface: Sora, SIL Open Font License 1.1; licence in `TechLoungeOS/FirstLogin/Fonts/OFL.txt`.

## What the footage demonstrates

- Actual compiled Builder 0.1.46 controls: component preset, recommended selection, typed app query, result selection, wallpapers and output choices. The OBS Studio result comes from a saved read-only exact WinGet search. The capture harness injects that result into the existing controls; no application is installed during capture.
- All fifteen packaged wallpapers and the approved product icon. Colour choices use contained changes and a single selected pill.
- A labelled animation of source-backed features: gaming defaults, networking tools available in TechLoungeTweaks, NVIDIA driver/app/profile queued for detected NVIDIA hardware, and Boot Optimizer after app installation. It is not a benchmark or proof of a new physical GPU test.
- The existing **Activate Windows** option in the actual Tweaks frontend, captured with its backend disconnected. No activation command is executed and no activation result is claimed.
- Native USB progress and completion controls driven by inert test events, placed over the Finish page. No physical drive is enumerated or written; the accelerated progress is an interface demonstration, not a speed claim.
- Native WinPE preview footage, trimmed past its initial desktop exposure. The desktop ending is an explicitly labelled HTML visualisation using the packaged wallpaper and real application artwork. It does not alter a VM recording or imply successful licensing.

## Checks on the delivered export

The native capture build passed. Browser checks covered all 5,580 timeline frames and verified all 22 pointer targets, one primary page, headings and shortcut labels inside their bounds, first-frame identity and hidden elements before their entrance. A second motion check used 300 samples and verified entrance order and framing. Hyperframes reported no runtime, layout, motion or contrast errors. Three media lint warnings remain for intentional artwork reuse across the two desktop shots, taskbar and identity cards. The first check used an ambiguous selector; it was corrected and the final motion check passed.

The encoded video fully decoded: 5,580 frames and 93-second stereo audio. All 420 encoded WinPE frames were checked for the exposed bright startup screen. Exported journey, feature, transition and ending contact sheets were visually reviewed. Audio measurements found no silence gaps of 0.25 seconds or longer at −60 dBFS, including after minute one; mean −26.4 dBFS, peak −8.1 dBFS. All 38 encoded cue windows matched the premix with correlation at least 0.996394 and zero measured timing offset. Audio was measured, not audibly auditioned.

Video SHA256: `653593F3195C4B8348CA8BD8EE1AFF0844CC5A5039741B6BE620D0573D919521`.

No original ISO, completed USB, application package or update manifest was changed for this video revision.
