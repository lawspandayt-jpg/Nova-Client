# Authentication guide

## Flow (what the player sees)

1. Type your **Microsoft email** into the launcher (used only as a `login_hint` so Microsoft's
   page pre-fills it — it is never treated as a credential).
2. Click **Continue with Microsoft** → a WebView2 window opens **Microsoft's official sign-in
   page** (`login.microsoftonline.com`). Password, 2FA, passkeys, Windows Hello, security codes
   and account recovery are all handled entirely by Microsoft.
3. On success the window closes and the launcher completes the chain:
   Microsoft token → Xbox Live token → XSTS token → Minecraft services token →
   **ownership check** → profile (name, UUID, skin).
4. The Launch button unlocks only after ownership is confirmed.

If WebView2 is unavailable, the launcher automatically falls back to your **default browser**
with a loopback redirect (RFC 8252).

## Security guarantees (enforced by code + tests)

- The launcher has **no password field** and never sees, stores, logs, or transmits the password.
- No JavaScript is injected into the sign-in page; no cookies are read; no keystrokes captured.
- OAuth uses **PKCE (S256)** + a random `state` verified on return (`MicrosoftOAuth.ExtractCode`).
- Tokens are cached **DPAPI-encrypted** (`%AppData%\NovaClient\config\secure\auth.bin`) — never
  plain text; only your Windows account can decrypt them.
- Every log line passes through `LogRedactor`; automated tests prove access/refresh/Xbox/XSTS
  tokens, JWTs, authorization codes and PKCE verifiers cannot appear in logs.
- Sign Out deletes the encrypted blob and clears the profile; Switch Account additionally returns
  you to the email screen for a different account.
- No fake success paths exist: entitlement/profile failures produce specific errors
  (`AuthErrorKind`) — including child-account (XErr 2148916238), no-Xbox-profile (2148916233),
  region restriction (2148916235), no ownership, missing Java profile, rate limiting, and offline.

## Endpoints used (all official)

| Step | Endpoint |
|---|---|
| Authorize | `https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize` |
| Token | `https://login.microsoftonline.com/consumers/oauth2/v2.0/token` |
| Xbox user token | `https://user.auth.xboxlive.com/user/authenticate` |
| XSTS | `https://xsts.auth.xboxlive.com/xsts/authorize` (RP `rp://api.minecraftservices.com/`) |
| Minecraft login | `https://api.minecraftservices.com/authentication/login_with_xbox` |
| Entitlements | `https://api.minecraftservices.com/entitlements/mcstore` |
| Profile | `https://api.minecraftservices.com/minecraft/profile` |

Game Pass note: such accounts can report an empty entitlement list while still owning Java access;
the launcher treats **an existing Java profile** as proof of access (never the reverse).
