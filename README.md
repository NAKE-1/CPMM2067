# CPMM2067

A Cyberpunk 2077 mod manager —fast, portable, and built specifically around the CP2077 framework stack (REDmod, RED4ext, ArchiveXL, TweakXL, CET, Codeware/redscript, legacy `.archive`).

 CPMM2067 covers all seven CP2077 mod frameworks, ships a working NXM protocol handler, surfaces real conflict detection (not just filename overlap), and reads game logs to tell you whether your mods actually loaded.

---

## Quick start

1. Download the latest **`CPMM2067--setup.exe`** from the [Releases page](https://github.com/NAKE-1/CPMM2067/releases), or grab the **`-portable.zip`** if you don't want an installer.
2. Run it. First launch auto-detects your Cyberpunk install (Steam/GOG/Epic) and registers the `nxm://` protocol so Nexus's "Mod Manager Download" buttons route into the app.
3. (Optional) Paste your Nexus API key in **Settings → [ NEXUS MODS API KEY ]** if you want one-click NXM downloads.

For a developer-mode launch: clone this repo, run **`launch.bat`** — it auto-installs the .NET 8 SDK via winget if missing, builds the solution, and starts the app.

---

## What's in it

| Sidebar | What it does |
|---|---|
| **Dashboard** | Game detection (Steam / GOG / Epic / manual), version, REDmod DLC state, [ DEPLOY + PLAY ], [ BACKUP SAVE ] |
| **Mods** | Drop-in mod scanner across all 7 frameworks. Install from `.zip` / `.7z` / `.rar` (or drag-drop). Per-row compatibility pill (green / yellow / red). Search + framework filter. Export to JSON. |
| **Downloads** | Install journal — every download + install op recorded with file list. Per-row revert. Active transfers panel with MB/s, ETA, cancellable. |
| **Collections** | Load Nexus collections from `.collection`/`.zip` bundles or by URL slug (needs JWT for online fetch). Per-mod download + install status. Open mod page in browser as escape hatch for free-tier rate-limit cases. |
| **Load order** | Per-framework load-order UI. REDmod tab has drag-via-arrow reorder writing to `mods.json`. |
| **Conflicts** | Two scanners: filename collisions across installed mods (from journal) + semantic conflicts (TweakXL key collisions, redscript hook collisions on `@addMethod`/`@replaceMethod`/`@wrapMethod`). |
| **Load report** | Parses CET / RED4ext / redscript / REDmod load logs and cross-references with the disk scan. Green = on disk and confirmed loaded; yellow = present but never loaded; red = stale log reference. |
| **Saves** | Browse CP2077 saves with thumbnail + last-played. Per-row backup (to `<exe>\backups\saves\<timestamp>\`), inspect (heuristic mod-fingerprint), rename, duplicate, and delete (with backup-first option). |
| **Logs** | App live log + game log viewer (red4ext, r6, CET, redmod). [ CREATE DIAGNOSTIC BUNDLE ] zips logs + install manifest + game version for bug reports. |
| **Settings** | Nexus API key + JWT, NXM protocol handler register/unregister, preferred browser (bypass Edge), Nexus API usage tracker (daily + hourly remaining), testing mode (dry-run installs), telemetry opt-in. |
| **About** | Version, build date, data dir, manual update check via Velopack, full feature checklist, credits. |

---

## Highlights

- **Portable by default** — everything lives in `<exe-dir>\data\` (or wherever a `datadir.cfg` next to the exe points). No registry pollution beyond the optional NXM handler.
- **Install journal** — every install op records the full file list, so revert always works and conflicts have a ground truth.
- **NXM single-instance** — second launches forward via named pipe; the running app handles the URL.
- **Live game-process monitor** — bottom bar tracks whether `Cyberpunk2077.exe` is running.
- **Background mode** — close-while-busy dialog offers `[ HIDE TO TRAY ]` so downloads keep running with a system tray icon to restore from.
- **Game-version-change alert** — pops a modal when CP2077's binary version differs from last launch, listing your scanned mods so you can sanity-check compat after a patch.
- **TUI-style theme** — port of [refact0r/system24](https://github.com/refact0r/system24) — monochrome dark + purple accent, monospace, 2px borders, panel labels like `[ ACTIVE TRANSFERS ]`.

---

## Build instructions

### Prerequisites

- Windows 10/11 x64 (the app is Windows-only at v1; Avalonia keeps a Linux port realistic later)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — installable via `winget install Microsoft.DotNet.SDK.8`
- (Optional) [Inno Setup 6](https://jrsoftware.org/isdl.php) if you want to build the installer locally — `choco install innosetup`

### Dev build

```pwsh
git clone https://github.com/NAKE-1/CPMM2067.git
cd CPMM2067
dotnet restore
dotnet build CPMM2067.sln
dotnet test tests\CPMM2067.Tests
```

Or just double-click **`launch.bat`** — it handles the SDK install, build, and launch in one shot.

### Release build (single-file self-contained exe)

```pwsh
dotnet publish src\CPMM2067.App -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish\win-x64
```

Output: `publish\win-x64\CPMM2067.App.exe` (~80MB, runs without .NET installed on the target machine).

### Installer

After the publish step above:

```pwsh
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=0.2.0 packaging\install.iss
```

Output: `packaging\Output\CPMM2067-0.2.0-setup.exe`.

### Tag-driven release (CI)

```bash
git tag v0.2.0
git push origin v0.2.0
```

`.github/workflows/release.yml` then builds + tests + publishes + packs Inno Setup + packs Velopack + creates a GitHub Release with all assets. See [HOWTO-RELEASE.md](HOWTO-RELEASE.md) for the full procedure.

---

## Architecture

```
CPMM2067.sln
├── src/
│   ├── CPMM2067.App/            Avalonia 11/12 UI + view-models, single-instance gate, NXM router, DI container
│   ├── CPMM2067.Core/           Domain models (ModId, ModManifest, GameInstallation, AppPaths)
│   ├── CPMM2067.GameDetect/     Steam / GOG / Epic registry probes + Cyberpunk2077.exe version read
│   ├── CPMM2067.Frameworks/     Per-framework installers: RedMod / Red4ext / LegacyArchive / TweakXL / Cet / Redscript / Fomod
│   ├── CPMM2067.Archives/       Zip / 7z / Rar extraction (SharpCompress), FOMOD ModuleConfig.xml parser
│   ├── CPMM2067.Backup/         Vanilla-file shadow-copy store, saves snapshot
│   ├── CPMM2067.Nexus/          REST API client, NXM:// protocol handler, Collections GraphQL client, manifest parser, rate-limit tracker, NxmDownloadService
│   ├── CPMM2067.Conflicts/      Path-overlap scanner + deep TweakXL/redscript hook scanners
│   ├── CPMM2067.Compat/         Game-version vs mod compat verdict engine
│   ├── CPMM2067.Saves/          Save mod-fingerprint inspector (heuristic CR2W scan)
│   ├── CPMM2067.Launch/         Storefront launcher (steam:// / GOG Galaxy / Epic URI) + REDmod deploy invocation
│   ├── CPMM2067.Diagnostics/    Serilog wiring, in-memory log sink, game-log parser (CET / RED4ext / redscript / REDmod), diagnostic-bundle zipper
│   └── CPMM2067.Update/         Velopack self-update wrapper
├── tests/CPMM2067.Tests/        xUnit (22 tests)
├── packaging/                   Inno Setup install.iss
├── testgame/                    Fake CP2077 install for local testing (gitignored — fixture only)
├── data/                        Portable data dir (settings.json, logs, journal, cache, backups) — gitignored
├── launch.bat                   Dev launcher (auto-installs .NET, builds, runs)
└── rebuild.bat                  Clean rebuild + test runner
```

Key design choices documented in the plan file at `~/.claude/plans/i-want-to-make-soft-phoenix.md` (private to dev — gitignored).

---

## Configuration paths

- **Settings file**: `<exe-dir>\data\settings.json` (or wherever `datadir.cfg` redirects to)
- **API key / JWT**: stored in `settings.json` (gitignored)
- **Logs**: `<exe-dir>\data\logs\cpmm2067-<date>.log` (Serilog rolling daily, 14-day retention)
- **Install journal**: `<exe-dir>\data\journal\<timestamp>-<id>.json` per install op
- **Downloaded mods**: `<exe-dir>\data\cache\archives\`
- **Save backups**: `<exe-dir>\backups\saves\<timestamp>-<savename>\`
- **Dry-run test extracts**: `<exe-dir>\data\testing\<timestamp>-<id>\`

Override with a one-line `datadir.cfg` next to the exe containing an alternative absolute path (env vars expanded).

---

## Credits / dependencies

- **[WopsS/RED4ext](https://github.com/WopsS/RED4ext)** — the framework loader we target
- **[adamhathcock/SharpCompress](https://github.com/adamhathcock/SharpCompress)** — zip / 7z / rar reader
- **[velopack/velopack](https://github.com/velopack/velopack)** — auto-update plumbing
- **[AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia)** — cross-platform XAML UI
- **[CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)** — MVVM source generators
- **[serilog/serilog](https://github.com/serilog/serilog)** — structured logging

---

## Contributing

PRs welcome once the repo flips public. Things on the queue:

- Drag-drop into Collections page
- Code-signing through SignPath Foundation
- Linux port (Avalonia ready; the hard part is Proton compat-data layout for game detection)
- FOMOD multi-step wizard refinement (currently picks defaults if you cancel)

---

## License

MIT — see [LICENSE](LICENSE).
