# Releasing CPMM2067

There are two release paths: **automated** (CI on a Git tag) and **manual** (local Inno Setup build).

---

## Automated release (recommended)

Triggered by pushing a tag matching `v*` to GitHub.

```bash
git tag v0.2.0
git push origin v0.2.0
```

The `.github/workflows/release.yml` workflow then:
1. Sets up .NET 8, restores, builds in Release, runs all 22 unit tests
2. Publishes a self-contained single-file `CPMM2067.App.exe` (Windows x64)
3. Installs Inno Setup via Chocolatey + compiles `packaging/install.iss` → `CPMM2067-<version>-setup.exe`
4. Installs the Velopack CLI (`vpk`) + packs a Velopack release feed → `publish/velopack/`
5. Zips the portable build → `CPMM2067-<version>-portable.zip`
6. Creates a GitHub Release named `CPMM2067 <version>` with auto-generated release notes and all three asset types attached

Result: every tag is immediately downloadable from the GitHub Releases page with both the installer (for end users) and the portable zip (for dev / sandbox).

### Versioning

Use semver. The tag is `v<major>.<minor>.<patch>`. The workflow strips the `v` and passes the version to:
- `/p:Version=...` on `dotnet build` (sets `AssemblyVersion` + `FileVersion`)
- `/DMyAppVersion=...` on `ISCC.exe` (drives the Inno Setup install dir, registry entries, uninstall display name)
- `--packVersion` on `vpk pack`

So a single `git tag` propagates through everything.

---

## Manual release (no CI required)

1. Ensure clean `git status` and `dotnet test` passes.
2. Publish single-file build:
   ```pwsh
   dotnet publish src\CPMM2067.App -c Release -r win-x64 --self-contained true `
       /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
       -o publish\win-x64
   ```
3. Build the installer:
   ```pwsh
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
       /DMyAppVersion=0.2.0 packaging\install.iss
   ```
   Output lands in `packaging\Output\CPMM2067-0.2.0-setup.exe`.
4. (Optional) Build a Velopack release:
   ```pwsh
   dotnet tool install -g vpk
   vpk pack --packId CPMM2067 --packVersion 0.2.0 `
            --packDir publish\win-x64 --mainExe CPMM2067.App.exe `
            --outputDir publish\velopack
   ```
5. Smoke-test the installer on a clean Win 11 VM (no .NET preinstalled — single-file publish bundles the runtime).

---

## Signing

Currently **unsigned**. SmartScreen will show "Unrecognized app" on first launch until the binary builds reputation (a few hundred downloads).

To sign:
- **Free**: apply to [SignPath Foundation](https://signpath.org/) for OSS code signing. Adds 1–2 weeks to the release process for the human-review step.
- **Paid**: buy a Sectigo / DigiCert OV cert (~$80–200/yr) and add `signtool sign /fd SHA256 /tr http://timestamp.sectigo.com /td SHA256 /f cert.pfx /p $PFX_PASSWORD path\to.exe` to both the published exe and the installer before upload.

Either way, store the cert + password as GitHub repo secrets (`SIGNING_CERT_PFX_B64`, `SIGNING_CERT_PASSWORD`) and add a sign step to `release.yml` between Build and Upload.

---

## Update channel

Velopack reads its update feed from the URL configured in `src/CPMM2067.Update/UpdaterService.cs` (`DefaultReleaseFeedUrl`). Currently pointed at `https://github.com/cpmm2067/cpmm2067/releases`. Update that constant to your actual repo before tagging v0.2.0 if the repo is at a different path (e.g. `NAKE-1/CPMM2067`).

Once a tag is published, in-app `[ CHECK FOR UPDATE ]` on the About page will discover it and offer to apply.

---

## Pre-release checklist

- [ ] `dotnet test` clean (22/22)
- [ ] `dotnet build CPMM2067.sln` clean (warnings tolerated, errors blocking)
- [ ] Manual smoke: `launch.bat`, walk through Dashboard → Mods → Downloads → Saves → Collections at least once
- [ ] `HOWTO-TEST.md` scenarios pass
- [ ] Update `MainWindowViewModel._statusBar` version string if it diverges from the tag
- [ ] Update `Views\AboutView.axaml` feature checklist (`[*]` vs `[ ]`) to reflect what actually ships
- [ ] Bump `UpdaterService.DefaultReleaseFeedUrl` if repo moved
- [ ] Tag with `git tag v<major>.<minor>.<patch>` then push the tag
