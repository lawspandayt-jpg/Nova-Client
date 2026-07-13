# Security guide

## Threat model & guarantees

| Area | Guarantee | Enforced by |
|---|---|---|
| Microsoft password | Never entered in, seen by, or stored by the launcher | Sign-in only on Microsoft's page (WebView2/browser); no password field exists |
| OAuth | Public client + PKCE S256 + state check; no client secret in the binary | `MicrosoftOAuth` + tests |
| Tokens at rest | DPAPI (CurrentUser) encrypted blob, app-specific entropy | `SecureTokenStore` + test proving plaintext absent on disk |
| Tokens in logs | Redacted before any sink (launcher log, game log, UI) | `LogRedactor` + `LogRedactionTests` |
| Launch command | Access token replaced with `[REDACTED]` in logged command line | `LaunchArgumentBuilder.DescribeForLog` + test |
| Updates | HTTPS-only manifest, SHA-256 per file, staged install, backup + rollback, path-traversal rejection | `UpdateService` + tests |
| Java downloads | Adoptium API only, SHA-256 verified | `AdoptiumJavaProvider` |
| Game files | Mojang servers only, SHA-1 verified | `MinecraftInstaller` |
| Privileges | `asInvoker` manifest; per-user installer (`PrivilegesRequired=lowest`) | app.manifest / NovaClient.iss |
| Uninstall | Removes app files; asks before touching user data | installer `[Code]` section |

## Explicitly absent (verified by reading the code — grep is your friend)

No analytics, telemetry, tracking, or advertising. No browser-cookie or Discord-token access.
No file uploads of any kind — the launcher only ever talks to Microsoft, Xbox, Mojang, Adoptium
and (if configured) your own update host. No hidden background processes, scheduled tasks,
Defender exclusions, or persistence beyond the Start-menu shortcut the user chooses. No cheat,
macro, packet-modification, or automation code in the game client.

## In-game client scope

The java agent instruments exactly three stable LWJGL classes (`Display`, `Keyboard`, `Mouse`) via
mechanical rename+delegate patches (`LwjglTransformer` — the complete list is in that file).
It adds a render callback and an input gate for the GUI. It cannot read or write game memory
structures, does not touch netcode, and confers zero gameplay advantage.

## Logging policy

Logs contain: timestamps, download/validation progress, Java detection results, install progress,
game start/exit, sanitized errors. Logs never contain: Microsoft/Xbox/XSTS/Minecraft tokens,
passwords, OAuth codes, PKCE verifiers, WebView2 cookies, session/device codes, or Credential
Manager contents — enforced by `LogRedactor` with automated tests.

## Reporting

Set your security contact in `branding.json`'s support URL and publish a disclosure policy at
your privacy URL (template below).

# Privacy policy template

> **Nova Client Privacy Policy (template — review with counsel before publishing)**
>
> Nova Client authenticates you directly with Microsoft, Xbox Live and Minecraft services. Your
> password is entered only on Microsoft's own pages and is never visible to Nova Client. The
> launcher stores, on your device only: your Minecraft profile (name/UUID/skin), encrypted
> authentication tokens, your email address if you enable "Remember Email", launcher settings and
> logs. Nova Client sends no data to us — it communicates only with Microsoft, Xbox Live, Mojang,
> Eclipse Adoptium (optional Java download) and, when enabled, our update server (which receives
> only a standard HTTP request for the update manifest). Signing out deletes stored tokens.
> Uninstalling offers to delete all remaining local data.
