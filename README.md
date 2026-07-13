# Nova Client

A legitimate Minecraft: Java Edition **1.8.9** launcher + custom PvP client for Windows 10/11.

## 📥 Download (no building required)

Grab the latest version from the **[Releases page](https://github.com/lawspandayt-jpg/nova-client/releases/latest)**:

- **`NovaClientSetup-x.x.x.exe`** — installer (Start-menu shortcut, uninstaller) ← most people want this
- **`NovaClient.exe`** — portable single file, just run it from anywhere

Then: enter your Microsoft email → sign in on Microsoft's official page → follow the one-time
OptiFine setup → **Launch**. In game, press **Right Shift** for the client menu.
You need a Microsoft account that owns Minecraft: Java Edition.

> Windows SmartScreen may warn on first run because the exe isn't code-signed yet —
> click **More info → Run anyway**.

- Official **Microsoft account** sign-in (OAuth 2.0 + PKCE, WebView2, browser fallback) — no
  cracked/offline accounts, no password ever touches the launcher
- **Minecraft ownership verification** before the Launch button unlocks
- Official Minecraft **1.8.9** installation (hash-verified downloads from Mojang)
- **OptiFine** loaded through its own tweaker — **no Forge, no Fabric**
- Custom non-Forge client layer with a polished **Right Shift click GUI**
- **Zero gameplay modules** and zero cheats — only the client framework ships today
- Windows installer (Inno Setup) + portable build

> ⚠️ Before first sign-in you must create a (free) Microsoft app registration and put its client
> ID into `branding.json` — see [docs/microsoft-app-registration.md](docs/microsoft-app-registration.md).

## Build

Requirements: **.NET 8 SDK**, any **JDK 8+** (for the in-game client), **Inno Setup 6** (installer only).

```powershell
# 1) In-game client jar (downloads LWJGL/LaunchWrapper/ASM/Gson from official repos)
.\client-java\build.ps1

# 2) Launcher (all projects) + tests
dotnet build NovaClient.sln
dotnet test NovaClient.sln

# 3) Single-file launcher exe  →  dist\single\NovaClient.exe  (runs standalone)
dotnet publish src\NovaClient.Launcher -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist\single

# 4) Installer      →  dist\installer\NovaClientSetup-1.0.0.exe
ISCC.exe installer\NovaClient.iss
```

## Run

```powershell
dist\single\NovaClient.exe          # the launcher — one standalone exe
# or run from source:
dotnet run --project src\NovaClient.Launcher
```

The exe is fully self-contained: on first start it creates an editable `branding.json` next to
itself (put your Microsoft client ID there) and unpacks the embedded in-game client jar into
`%AppData%\NovaClient\client`.

First run: enter your Microsoft email → sign in on Microsoft's page → ownership check →
one-click OptiFine setup → Launch. In game, press **Right Shift** to open the client menu.

## Repository layout

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Guides: app registration, authentication,
Minecraft installation, OptiFine, Java runtime, installer/release, security, troubleshooting,
third-party notices — all in [docs/](docs/).

## Branding

Everything brandable lives in [src/NovaClient.Launcher/branding.json](src/NovaClient.Launcher/branding.json)
(name, title, accent color, URLs, versions, Microsoft client ID, recommended OptiFine edition,
default RAM). The file is copied next to the exe and read at startup — rebrand without recompiling.

## What must NOT be committed

- `client-java/lib/` and `client-java/dist/` (downloaded/compiled artifacts)
- Any **OptiFine jar** (no redistribution rights)
- Any **Minecraft** game files, assets, or libraries
- `dist/`, `bin/`, `obj/`, real client IDs you consider private, signing certificates

A ready `.gitignore` is included.
