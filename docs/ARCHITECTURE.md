# Nova Client — Architecture

Nova Client is a legitimate Minecraft Java Edition 1.8.9 launcher + custom (non-Forge) PvP client
for Windows 10/11. It uses official Microsoft authentication only, verifies Minecraft ownership,
and ships **zero** gameplay modules — only the client framework (branding, settings, Right-Shift
click GUI, module framework, notifications, themes).

## Solution layout

```
NovaClient.sln
├── src/
│   ├── NovaClient.Core            # Branding config, paths, settings, logging + redaction, DPAPI storage, utils
│   ├── NovaClient.Authentication  # Microsoft OAuth (PKCE) → Xbox Live → XSTS → Minecraft Services → entitlement → profile
│   ├── NovaClient.Minecraft       # Version manifest, downloads (SHA-1, resume), assets, natives, Java detection, launch
│   ├── NovaClient.GameClient      # nova-1.8.9 version JSON, LaunchWrapper bootstrap wiring, OptiFine setup/validation
│   ├── NovaClient.Updater         # HTTPS manifest, semver compare, SHA-256 verify, staged install + rollback
│   └── NovaClient.Launcher        # WPF (.NET 8, MVVM) UI: login, account, home, settings, OptiFine setup, WebView2 auth
├── client-java/                   # Java 8 in-game client (LaunchWrapper tweaker + scoped ASM transformers + click GUI)
├── tests/NovaClient.Tests         # xUnit tests (redaction, PKCE, settings, updater, OptiFine validation, …)
├── installer/                     # Inno Setup script
└── docs/                          # Guides
```

## Data directory

Everything lives in `%AppData%\NovaClient` (never touches `.minecraft`):

```
assets/  libraries/  versions/  natives/  logs/  screenshots/  resourcepacks/
config/  cache/  runtime/  client/  crash-reports/
```

## Authentication chain

1. Launcher validates the typed Microsoft email (used only as `login_hint`).
2. WebView2 window loads `https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize`
   (official page, PKCE S256, public client, no secret, scope `XboxLive.signin offline_access`).
   Fallback: system browser + loopback (`http://127.0.0.1:{port}/callback`).
3. Authorization code → token endpoint → MSA access/refresh tokens.
4. Xbox Live user token (`user.auth.xboxlive.com`) → XSTS (`xsts.auth.xboxlive.com`,
   relying party `rp://api.minecraftservices.com/`).
5. Minecraft Services login (`api.minecraftservices.com/authentication/login_with_xbox`).
6. Entitlement check (`/entitlements/mcstore`) — launch is blocked without ownership.
7. Profile (`/minecraft/profile`) → username, UUID, skin; head rendered locally from the skin PNG.
8. Tokens cached with **DPAPI (CurrentUser)** in `config/auth.bin`; refresh on expiry; logout wipes it.

The launcher never sees, stores, or logs the Microsoft password. No JavaScript is injected into the
login page. All token-bearing strings pass through the `LogRedactor` before hitting any log sink.

## Game bootstrap (non-Forge)

The generated version `nova-1.8.9` inherits vanilla 1.8.9 and sets
`mainClass = net.minecraft.launchwrapper.Launch` with tweak classes:

```
optifine.OptiFineTweaker   (only when OptiFine is installed)
dev.novaclient.bootstrap.NovaTweaker
```

Libraries added: `net.minecraft:launchwrapper:1.12`, `org.ow2.asm:asm-debug-all:5.0.3`
(both from libraries.minecraft.net), the user-supplied OptiFine jar (copied into `libraries/`),
and `dev.novaclient:nova-client` (built from `client-java/`).

### In-game client design — no obfuscated Minecraft internals

The client deliberately avoids depending on obfuscated Minecraft class/member names. It hooks only
**stable LWJGL 2 classes** via three small, documented ASM transformers:

| Transformer | Target | Purpose |
|---|---|---|
| `DisplayTransformer`  | `org.lwjgl.opengl.Display.update()`   | Per-frame callback on the render thread (GL context current) — renders the click GUI overlay before buffer swap |
| `KeyboardTransformer` | `org.lwjgl.input.Keyboard` (`next`, `isKeyDown`, `getEvent*`) | While the GUI is open, the game sees no keyboard input (synthetic release events unpress held keys); the GUI reads real state |
| `MouseTransformer`    | `org.lwjgl.input.Mouse` (`next`, `isButtonDown`, `getDX/getDY`, `getEvent*`) | While the GUI is open, the game sees no clicks/camera movement; the GUI reads real cursor state |

Opening the GUI un-grabs the mouse cursor; closing restores grab. Because input is suppressed at
the LWJGL layer (exactly what the game experiences when its window loses focus), the GUI cannot
break chat, inventory, or multiplayer, and gameplay is never paused. There is **no** packet
modification, no server interaction, and no gameplay behavior change of any kind.

Text is rendered with an original OpenGL font renderer (AWT-rasterized glyph atlas). All GL state
is saved/restored around overlay rendering.

## Security posture

- Public-client OAuth with PKCE; client ID comes from `branding.json` (`REPLACE_WITH_REAL_CLIENT_ID`).
- Tokens: DPAPI-encrypted at rest; never written to logs (automated redaction tests).
- No telemetry, analytics, tracking, hidden processes, or Defender exclusions.
- Updates: HTTPS manifest, SHA-256 verification, staged install with backup + rollback.
- Java runtime offered only from Adoptium (Temurin 8, x64) with checksum validation.
