# Troubleshooting

| Symptom | Cause / fix |
|---|---|
| "no valid Microsoft client ID configured" | Put your app registration GUID into `branding.json` → `microsoftClientId` (see microsoft-app-registration.md) |
| Sign-in window closes, "Microsoft sign-in failed" | Check the app registration: personal accounts enabled, nativeclient redirect URI added, public client flows = Yes |
| Chain fails at "Signing in to Minecraft services" (403) | Your client ID likely isn't Mojang-approved yet (see registration guide §5) |
| "This account has no Xbox profile" | Sign in once at xbox.com with that Microsoft account |
| "child account" error | An adult in the Microsoft family must permit third-party sign-in |
| "does not own Minecraft: Java Edition" | The account has no Java Edition; Game Pass accounts must have created their Java profile in the official launcher once |
| WebView2 error → browser opens instead | Normal fallback. To fix WebView2, install the Evergreen runtime from Microsoft |
| Launch button disabled | All gates must pass: signed in + ownership + files verified + 64-bit Java 8 + OptiFine installed + client jar present |
| "missing client/nova-client.jar" | Run `client-java\build.ps1`, then rebuild/republish the launcher |
| No compatible Java | Home screen → **Install Java 8** (official Temurin build) |
| Downloads keep failing | Check firewall/proxy for piston-meta.mojang.com, libraries.minecraft.net, resources.download.minecraft.net; then **Repair** |
| Game crashes on start | Home screen shows the crash report path; `logs\game-*.log` has full output; try Repair, stock JVM args, and 2048 MB RAM |
| Right Shift does nothing in game | GUI opens only during normal gameplay (not in chat/menus). Check `logs\game-*.log` for `[NovaClient] Agent attached` |
| GUI opened but game keeps moving | Should be impossible (input gate). Capture `game-*.log` and file a bug |
| Reset everything | Settings → Reset Launcher Settings / Reset Client Settings; or delete `%AppData%\NovaClient\config` |

Launcher logs: `%AppData%\NovaClient\logs\launcher-*.log` (always token-redacted).
