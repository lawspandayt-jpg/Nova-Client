# Microsoft app registration (required once, free)

Nova Client signs players in with the official Microsoft identity platform. That requires an
**app registration** owned by you. No client secret is used anywhere — this is a **public client**
with PKCE, which is the correct model for a desktop launcher.

## 1. Create the registration

1. Go to <https://portal.azure.com> → **Microsoft Entra ID** → **App registrations** → **New registration**.
2. **Name**: e.g. `Nova Client Launcher`.
3. **Supported account types**: choose **"Personal Microsoft accounts only"**
   (Minecraft players sign in with personal MSA accounts).
4. Click **Register**.

## 2. Configure authentication

1. Open **Authentication** → **Add a platform** → **Mobile and desktop applications**.
2. Tick the built-in redirect URI:
   - `https://login.microsoftonline.com/common/oauth2/nativeclient`  (used by the WebView2 window)
3. Under the same platform, add a custom redirect URI:
   - `http://localhost`  (used by the system-browser fallback; the launcher listens on
     `http://127.0.0.1:<random-port>/callback/` which loopback rules accept)
4. Set **"Allow public client flows"** to **Yes** (this enables desktop authentication).
5. Save. **Do NOT create a client secret** — the launcher must never contain one.

## 3. API permissions

`XboxLive.signin` is requested dynamically through the scope; no additional Graph permissions are
needed. If sign-in reports a consent problem, add **Xbox Live → XboxLive.signin** under
**API permissions**.

## 4. Put the client ID into the launcher

Copy **Application (client) ID** from the Overview page into:

```
src/NovaClient.Launcher/branding.json   →   "microsoftClientId": "<your-guid-here>"
```

(or edit `branding.json` next to the installed `NovaClient.exe`). The placeholder
`REPLACE_WITH_REAL_CLIENT_ID` blocks sign-in with a clear error message until replaced.

## 5. Important: Minecraft API approval

Mojang gates the final `login_with_xbox` step: **new third-party launcher client IDs must be
approved by Mojang** before they may call the Minecraft services API. Request access via the
official form (search "Mojang API approval form" / <https://aka.ms/mce-reviewappid>). Until your
client ID is approved, the chain may fail at the Minecraft services step even though the
Microsoft/Xbox steps succeed.

## Why no client secret?

A secret embedded in a distributed exe is public by definition. The OAuth authorization-code flow
with **PKCE** (RFC 7636) is designed for exactly this case: each sign-in generates a one-time
verifier/challenge pair, so intercepted authorization codes are useless without the verifier that
never leaves the launcher process.
