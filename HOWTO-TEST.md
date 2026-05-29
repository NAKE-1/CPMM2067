# CPMM2067 — quick test directions

Everything below works against the `testgame/` fixture in this repo. **Do not point at your real Cyberpunk 2077 install yet.**

## 0. Launch

Double-click `launch.bat` at the repo root. First run installs .NET 8 via winget if missing, then builds and starts the app.

## 1. Point CPMM2067 at the test folder (one time)

1. Dashboard → `[ pick folder… ]`
2. Pick `C:\Users\nakan\Downloads\CPMM2067\testgame\`
3. The dashboard should show:
   - storefront: `Manual`
   - install dir: `…\CPMM2067\testgame`
   - redmod dlc: `False` (expected — fixture has no real REDmod tooling)
4. Close the app and re-launch. The dashboard should reload the saved path automatically (no need to pick again).

If you ever need to go back to auto-detect, the `[ clear saved path ]` command exists in the view-model (UI button TBD).

## 2. Scan for existing mods

1. Sidebar → `Mods`
2. `[ SCAN ]`
3. You should see eight rows from the fixture:
   - `fake_hud_tweak` / `Another Fake Mod` (RedMod)
   - `fake_outfit` (LegacyArchive + ArchiveXL)
   - `FakeNotificationHelper` (Cet)
   - `FakeTweakXL` (Red4ext)
   - `fake_tweak` (TweakXL)
   - `FakeHUDExtension` (Redscript)

## 3. Install a mod from a .zip

1. Mods → `[ INSTALL FROM ZIP ]`
2. Pick `testgame\sample-mods\example_install_test.zip`
3. Status panel should report `Installed as RedMod: example_install_test`
4. The list auto-rescans; you should now see a 9th row for `example_install_test` under `mods/example_install_test/`
5. Try it with `sample_legacy_install.zip` too — that one installs as `LegacyArchive` into `archive/pc/mod/`

## 4. Delete a mod

1. Mods → click `[ DELETE ]` on any row
2. The button flips to `[ CONFIRM ] [ x ]` — click `[ CONFIRM ]` to actually delete, or `[ x ]` to cancel
3. The file/folder is removed from the install; for REDmod the entry is also stripped from `mods.json`

## 5. NXM protocol handler

1. Settings → `[ register ]`
2. Status flips to `Registered`
3. To verify: paste `nxm://cyberpunk2077/mods/1/files/1` into a browser address bar
4. Windows should ask "Open this with CPMM2067" — accept
5. Check `%TEMP%\cpmm2067-nxm.log` — you should see a line with the URL
6. Click `[ unregister ]` when done

## 6. Game logs viewer

If your testgame/ has no logs yet, this panel will be empty. Drop a couple of fake log files to test:

```powershell
mkdir testgame\red4ext\logs
"started ok" | Out-File -Encoding utf8 testgame\red4ext\logs\red4ext.log

mkdir testgame\r6\cache
"redscript compile OK" | Out-File -Encoding utf8 testgame\r6\cache\redscript_rCURRENT.log
```

Then in the app:
1. Sidebar → `Logs`
2. `[ refresh ]` under the `[ GAME LOGS ]` panel
3. Click any file in the left list — the tail (last ~256 KB) renders on the right

## 7. Game running status (bottom bar)

The bottom-right of the status bar polls every 2 seconds for a `Cyberpunk2077.exe` process. To test the transitions without owning the game:

```powershell
# Open Notepad and rename its process artificially won't work, but you can
# write a one-second loop in a different language. Simpler: run an actual
# Cyberpunk2077.exe placeholder using:
$src = 'C:\Windows\System32\notepad.exe'
$dst = "$env:TEMP\Cyberpunk2077.exe"
Copy-Item $src $dst -Force
Start-Process $dst
```

Within ~2 seconds the bottom bar should flip to `[ GAME: RUNNING ]` with the pid. Close the fake exe — the bar transitions through `STOPPING` to `NOT RUNNING`.

(In production this catches the real game process by name.)

## 8. Diagnostic bundle

1. Logs → `[ CREATE DIAGNOSTIC BUNDLE ]`
2. Status shows the saved path under `%AppData%\CPMM2067\cpmm2067-diag-<timestamp>.zip`
3. The zip contains: this session's log file, a metadata.json with OS / app version / game info, and settings.json (api key masked at write time — verify before sharing)

## 9. Cleanup between runs

- App data: `%AppData%\CPMM2067\` — `settings.json`, `logs\`, `backups\`, `cache\`. Safe to delete to reset.
- Test mods you installed end up under `testgame\mods\` or `testgame\archive\pc\mod\`. Delete them via the in-app `[ DELETE ]` button, or `Remove-Item` the directories directly.
