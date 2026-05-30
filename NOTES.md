# CPMM2067 — working notes

Scratchpad so future-you (or Claude) can pick up cold.

## Current state (2026-05-29)

- **Latest tag:** `v0.3.0` on `35fac81`
- **Latest changes:** Save editor + CyberCAT integration removed entirely (user scrapped it).
- **GitHub:** https://github.com/NAKE-1/CPMM2067
- **Release workflow:** triggers on `v*` tag push. Watch at `/actions`. Last known issue was a Velopack pack failure that needed `VelopackApp.Build().Run()` inlined as literal first line of `Program.Main` — fixed.

## How to cut a new release

When code changes are merged to `main` and you want a downloadable build + auto-update delta:

1. **Bump the version in all four places:**
   - `src/CPMM2067.App/CPMM2067.App.csproj` line 9 — `<Version>...</Version>`
   - `src/CPMM2067.App/Views/MainWindow.axaml` — sidebar ASCII `v0.X` (currently line 62)
   - `src/CPMM2067.App/ViewModels/MainWindowViewModel.cs` — `StatusBar` default string `"CPMM2067 v0.X :: ..."`
   - (No other source-of-truth — `assemblyinfo`/etc. are generated from csproj)

2. **Commit + push + tag:**
   ```
   git add -A
   git commit -m "Bump to vX.Y.Z"
   git push origin main
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

3. **Watch the workflow** at https://github.com/NAKE-1/CPMM2067/actions — if it dies on Velopack pack, the verifier is complaining about the entry-exe IL; do not move `VelopackApp.Build().Run()` out of `Program.Main`.

4. **Re-tagging the same version** (if first attempt failed):
   ```
   git tag -d vX.Y.Z
   git push origin :refs/tags/vX.Y.Z
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

## Gotchas / footguns

- **`Edit` tool silently failing** when a file was modified by source generators since last `Read`. If a Grep after edit shows "no matches" for the thing you just added — re-Read and re-Edit (or Write whole file).
- **`.gitignore` is case-insensitive on Windows.** `backups/` ate `src/CPMM2067.Core/Backups/`. Anchor with `/backups/` and `/data/`.
- **Velopack** scans entry-exe IL for the literal call `VelopackApp.Build().Run()`. Must be in `Program.Main`, not in a helper or another assembly.
- **NEVER commit:** `testgame/`, `data/`, `.env`, `settings.json` (holds API key), `*.pfx` signing certs. Already gitignored — keep them that way.
- **`PreferredBrowserExe` setting** stays empty by default. Auto-detect on startup got reverted because Edge was being picked up. Empty = use system default.

## Project layout (quick map)

- `src/CPMM2067.App/` — Avalonia UI entry point, ViewModels, Views, Services (`AppHost`, `SettingsStore`, etc.)
- `src/CPMM2067.Core/` — domain types, paths, install journal, backup records
- `src/CPMM2067.Frameworks/` — `RedModHandler`, `LegacyArchiveHandler`, `Red4extHandler`, `TweakXLHandler`, `CetHandler`, `RedscriptHandler`, `FomodHandler`, `ModScanner`, `ModInstaller`, `InstallQueue`
- `src/CPMM2067.GameDetect/` — Steam/GOG/Epic detection
- `src/CPMM2067.Backup/` — `FileBackupStore` (vanilla-file rescue on uninstall)
- `src/CPMM2067.Nexus/` — REST + GraphQL clients, NXM router, rate-limit tracker, Collections
- `src/CPMM2067.Compat/` — version verdict engine
- `src/CPMM2067.Saves/` — `SaveModInspector` (heuristic fingerprint; **no** editor anymore)
- `src/CPMM2067.Launch/` — storefront launcher (`steam://`, GOG Galaxy, Epic), `redMod.exe deploy`
- `src/CPMM2067.Update/` — Velopack wiring
- `src/CPMM2067.Diagnostics/` — Serilog bootstrap, game log reader, diagnostic-bundle zipper
- `tests/CPMM2067.Tests/` — xUnit, 22 tests at last check

## Nav order (MainWindowViewModel)

Dashboard, Mods, Downloads, Collections, Load order, Conflicts, Load report, Saves, Logs, Settings, About

(Save editor was here between Saves and Logs — removed.)

## Things deliberately NOT done (don't re-add by accident)

- Save editor / CyberCAT-SimpleGUI integration — user said no
- Native CR2W save parser — out of scope
- In-app Nexus browser — NXM-only by design
- Auto-detect preferred browser on startup — picks Edge wrong; stay empty by default
- Background mod-update polling — manual "Check for updates" only

## Pending / nice-to-have

- Drag-drop into Collections page
- SignPath Foundation OSS cert (apply during alpha)
- Linux/Steam Deck port (Avalonia ready, blocker is Proton compat-data layout)
- High-DPI / 4K pass
- Accessibility audit
