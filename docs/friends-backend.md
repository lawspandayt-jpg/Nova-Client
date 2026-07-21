# Friends & online status — deploy the presence backend (free)

Nova Client's friends system (add by username → **pending** → **accept**, live online status, and the
global "N Online" counter) needs one tiny shared server. It's a free **Cloudflare Worker** with a
free **KV** store. It stores only Minecraft UUIDs + usernames — no emails, tokens, or personal data.
Until it's deployed, the friends panel still works as a local list (no requests / no online status).

## What you'll deploy
`cloudflare-worker/worker.js` — ~180 lines. Endpoints: heartbeat, status, count, request, inbox,
respond, friends, outbox.

## Steps (about 5 minutes)

1. **Create the KV store**
   - dash.cloudflare.com → **Storage & Databases → KV** → **Create instance**
   - Name it `NOVA` → Create.

2. **Create the Worker**
   - **Workers & Pages → Create → Workers → Create Worker**
   - Give it a name like `nova-friends` → **Deploy** (the default hello-world is fine for now).
   - Click **Edit code**, delete everything, paste the entire contents of
     `cloudflare-worker/worker.js`, then **Deploy**.

3. **Bind the KV store to the Worker**
   - Open the Worker → **Settings → Bindings → Add → KV namespace**
   - **Variable name:** `NOVA`  ·  **KV namespace:** the `NOVA` store from step 1 → **Deploy**.
   - (The variable name must be exactly `NOVA` — the code uses `env.NOVA`.)

4. **Copy the Worker URL**
   - It looks like `https://nova-friends.<your-subdomain>.workers.dev`.

5. **Point the launcher at it**
   - Edit `branding.json` (the one next to `NovaClient.exe`, or `src/NovaClient.Launcher/branding.json`
     before building):
     ```json
     "presenceApiUrl": "https://nova-friends.<your-subdomain>.workers.dev"
     ```
   - Rebuild/republish the launcher (or just edit the shipped `branding.json` and restart).

That's it. Every copy of the launcher pointed at that URL now shares one friends network.

## How it behaves once live
- **Add a friend** by username → they get a request; your side shows **"Request sent · pending"**.
- They see **"wants to be your friend"** with **✓ / ✕** → Accept makes you friends both ways.
- Only people who have **used Nova Client** can receive requests (their UUID must be known to the
  backend) — the launcher tells you if a name hasn't used the client yet.
- **Online status**: the launcher sends a heartbeat every 45s while open; friends who've beaten in
  the last 90s show a green dot, and the header shows the total **N Online**.

## Free-tier limits
Cloudflare's free plan allows 100,000 Worker requests/day and generous KV reads/writes — plenty
for a friends network in the thousands. If you ever outgrow it, the Worker code is unchanged; you
just bump the Cloudflare plan.

## Privacy
The backend stores: `online:<uuid>` (expires after 90s), `name:<uuid>`, `req:<to>:<from>`, and
`friend:<a>:<b>`. No emails, no tokens, no IPs, no chat. Removing the KV store wipes everything.
