# Installer & release guide

## Build the installer

```powershell
.\client-java\build.ps1
dotnet publish src\NovaClient.Launcher -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist\single
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\NovaClient.iss
# → dist\installer\NovaClientSetup-1.0.0.exe   (installs the single-file exe)
```

`dist\single\NovaClient.exe` is also the **portable build**: it runs standalone from any folder,
generates its `branding.json` template on first start, and self-extracts the embedded in-game
client jar.

The installer is **per-user** (no admin/UAC), creates a Start-menu shortcut, offers an optional
desktop shortcut, supports clean uninstall, and asks before deleting `%AppData%\NovaClient`.
It contains no Minecraft or OptiFine files.

## Release checklist

1. Bump versions together: `branding.json` (launcherVersion/gameClientVersion),
   `NovaClient.Launcher.csproj` `<Version>`, `installer/NovaClient.iss` `#define AppVersion`.
2. `dotnet test NovaClient.sln` — all green.
3. Build client jar → publish → installer (commands above).
4. **Code-sign** `NovaClient.exe` and the setup exe with your Authenticode certificate
   (`signtool sign /fd SHA256 /tr <timestamp-url> ...`). Unsigned builds trigger SmartScreen.
5. Generate the update manifest (below), upload files + manifest to your HTTPS host
   (`branding.json` → `updateApiUrl`).

## Update system & local testing

The updater downloads a JSON manifest over HTTPS, compares semantic versions, stages files into a
temp folder, verifies **SHA-256 per file**, backs up current files, applies, and rolls everything
back on any failure. It never touches settings, screenshots, resource packs, or logs.

Manifest format:

```json
{
  "launcherVersion": "1.1.0",
  "clientVersion": "1.1.0",
  "notes": "What changed",
  "files": [
    { "path": "NovaClient.dll", "url": "https://host/updates/1.1.0/NovaClient.dll",
      "sha256": "<hex>", "size": 123456 }
  ]
}
```

For development, `UpdateService.WriteDevManifestAsync(...)` generates a manifest with correct
hashes for local files, and `file://` manifest URLs are accepted so the whole check path can be
tested without any server (see `UpdaterTests`). Settings → "Disable update checks (development)"
turns the startup check off. There is no fake production server — until you host a real manifest,
the home screen simply shows "Update check unavailable."

## Known limitations

- Ownership-verified sign-in requires your client ID to be **Mojang-approved** for the Minecraft
  services API (registration guide §5) — this is external to the code.
- The in-game client was verified by compilation, unit-level reasoning and code review; full
  in-game verification requires a real signed-in account on this machine (see docs/testing.md).
- Multi-account switching is sequential (sign out → sign in); no simultaneous account list yet.
- The GUI opens only from normal gameplay (by design, to never fight vanilla screens for input).
- No macOS/Linux support (Windows 10/11 only, per scope).
