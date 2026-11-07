# Reused TechLoungeTweaks Discord launcher

These files are unchanged copies of the existing TechLoungeTweaks launcher, read on 23 October 2026. The builder compiles them with Windows .NET Framework. No service, scheduled task or background updater is registered. Repair occurs when a configured shortcut opens Discord after a full quit. Discord internal update restarts may bypass the launcher.

- `Launcher.cs` SHA256 `62c3a1752c99d9b1950c454ebd968eb50af3aa94b51e7d16779422a5fbd08d4c`
- `ShellLinks.cs` SHA256 `c436eaf1f04fb666f87f804738937219272894142d1527b66039967bb4c58311`

- `Checks.cs` is the unchanged native fixture test from TechLoungeTweaks tools, SHA256 `2a17d8c49ca1accc212d92d72fae5c653962057bfdea2f67779fcbc865f8c944`. It is compiled only for Tests, not into the production launcher.
