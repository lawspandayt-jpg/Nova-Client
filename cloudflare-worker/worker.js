/**
 * Nova Client presence + friend-request backend (Cloudflare Worker).
 *
 * Stores only Minecraft UUIDs + usernames — no emails, tokens, IPs, or personal data.
 * Presence is ephemeral (a heartbeat with a 90-second TTL). Friend requests are small records.
 *
 * Bind a KV namespace called NOVA to this Worker (Settings → Variables → KV Namespace Bindings).
 * See docs/friends-backend.md for step-by-step deployment.
 *
 * Endpoints (all POST, JSON):
 *   /heartbeat  {uuid,name}                     → marks the player online for 90s
 *   /status     {uuids:[...]}                    → {online:[uuids currently online]}
 *   /count                                       → {count: total online now}
 *   /request    {fromUuid,fromName,toName}       → sends a friend request (validated names)
 *   /inbox      {uuid}                           → {requests:[{fromUuid,fromName,ts}]} pending for me
 *   /respond    {uuid,fromUuid,accept}           → accept/decline a request
 *   /friends    {uuid}                           → {friends:[{uuid,name}]} accepted friends
 *   /outbox     {uuid}                            → {pending:[{toUuid,toName}]} my sent-but-unaccepted
 */

const HEARTBEAT_TTL = 90;        // seconds a player is considered "online" after a heartbeat
const CORS = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "content-type" };

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS") return new Response(null, { headers: CORS });
    if (request.method !== "POST") return json({ error: "POST only" }, 405);

    const url = new URL(request.url);
    let body = {};
    try { body = await request.json(); } catch { body = {}; }

    try {
      switch (url.pathname) {
        case "/heartbeat": return await heartbeat(env, body);
        case "/status":    return await status(env, body);
        case "/count":     return await count(env);
        case "/request":   return await sendRequest(env, body);
        case "/inbox":     return await inbox(env, body);
        case "/respond":   return await respond(env, body);
        case "/friends":   return await friends(env, body);
        case "/outbox":    return await outbox(env, body);
        default:           return json({ error: "unknown endpoint" }, 404);
      }
    } catch (e) {
      return json({ error: "server error" }, 500);
    }
  }
};

function json(obj, statusCode = 200) {
  return new Response(JSON.stringify(obj), {
    status: statusCode,
    headers: { "content-type": "application/json", ...CORS }
  });
}

const clean = (s) => (typeof s === "string" ? s.trim() : "");
const validUuid = (s) => /^[0-9a-fA-F]{32}$/.test(clean(s));
const validName = (s) => /^[A-Za-z0-9_]{3,16}$/.test(clean(s));

async function heartbeat(env, { uuid, name }) {
  if (!validUuid(uuid)) return json({ error: "bad uuid" }, 400);
  // online:<uuid> auto-expires; also cache the name for lookups.
  await env.NOVA.put(`online:${uuid}`, clean(name), { expirationTtl: HEARTBEAT_TTL });
  await env.NOVA.put(`name:${uuid}`, clean(name));
  return json({ ok: true });
}

async function status(env, { uuids }) {
  if (!Array.isArray(uuids)) return json({ online: [] });
  const online = [];
  await Promise.all(uuids.slice(0, 200).map(async (u) => {
    if (validUuid(u) && (await env.NOVA.get(`online:${u}`)) !== null) online.push(u);
  }));
  return json({ online });
}

async function count(env) {
  // KV list is eventually consistent but fine for a live-ish counter.
  const list = await env.NOVA.list({ prefix: "online:" });
  return json({ count: list.keys.length });
}

// ---- friend requests ----
// Keys:  req:<toUuid>:<fromUuid> = {fromName,ts}   (pending, seen in recipient's inbox)
//        friend:<uuid>:<otherUuid> = otherName     (accepted, symmetric)

async function nameToUuid(env, name) {
  // Resolve a username to a UUID we already know about (they must have used the client).
  const list = await env.NOVA.list({ prefix: "name:" });
  for (const key of list.keys) {
    const uuid = key.name.slice("name:".length);
    const stored = await env.NOVA.get(key.name);
    if (stored && stored.toLowerCase() === name.toLowerCase()) return uuid;
  }
  return null;
}

async function sendRequest(env, { fromUuid, fromName, toName }) {
  if (!validUuid(fromUuid) || !validName(fromName) || !validName(toName))
    return json({ error: "bad input" }, 400);
  await env.NOVA.put(`name:${fromUuid}`, clean(fromName));

  const toUuid = await nameToUuid(env, clean(toName));
  if (!toUuid)
    return json({ status: "not_found", message: "That player hasn't used Nova Client yet, so they can't receive requests." });
  if (toUuid === fromUuid) return json({ error: "cannot add yourself" }, 400);

  // Already friends?
  if ((await env.NOVA.get(`friend:${fromUuid}:${toUuid}`)) !== null)
    return json({ status: "already_friends" });

  // If they already requested me, accept automatically.
  if ((await env.NOVA.get(`req:${fromUuid}:${toUuid}`)) !== null) {
    await makeFriends(env, fromUuid, clean(fromName), toUuid, clean(toName));
    await env.NOVA.delete(`req:${fromUuid}:${toUuid}`);
    return json({ status: "accepted" });
  }

  await env.NOVA.put(`req:${toUuid}:${fromUuid}`, JSON.stringify({ fromName: clean(fromName), ts: Date.now() }));
  return json({ status: "pending", toUuid });
}

async function inbox(env, { uuid }) {
  if (!validUuid(uuid)) return json({ requests: [] });
  const list = await env.NOVA.list({ prefix: `req:${uuid}:` });
  const requests = [];
  for (const key of list.keys) {
    const fromUuid = key.name.split(":")[2];
    const rec = await env.NOVA.get(key.name, "json");
    if (rec) requests.push({ fromUuid, fromName: rec.fromName, ts: rec.ts });
  }
  return json({ requests });
}

async function respond(env, { uuid, fromUuid, accept }) {
  if (!validUuid(uuid) || !validUuid(fromUuid)) return json({ error: "bad input" }, 400);
  const rec = await env.NOVA.get(`req:${uuid}:${fromUuid}`, "json");
  await env.NOVA.delete(`req:${uuid}:${fromUuid}`);
  if (accept && rec) {
    const myName = (await env.NOVA.get(`name:${uuid}`)) || "";
    await makeFriends(env, uuid, myName, fromUuid, rec.fromName);
    return json({ status: "accepted" });
  }
  return json({ status: "declined" });
}

async function makeFriends(env, a, aName, b, bName) {
  await env.NOVA.put(`friend:${a}:${b}`, bName || "");
  await env.NOVA.put(`friend:${b}:${a}`, aName || "");
}

async function friends(env, { uuid }) {
  if (!validUuid(uuid)) return json({ friends: [] });
  const list = await env.NOVA.list({ prefix: `friend:${uuid}:` });
  const out = [];
  for (const key of list.keys) {
    const otherUuid = key.name.split(":")[2];
    const name = (await env.NOVA.get(key.name)) || (await env.NOVA.get(`name:${otherUuid}`)) || "";
    out.push({ uuid: otherUuid, name });
  }
  return json({ friends: out });
}

async function outbox(env, { uuid }) {
  // Requests I've sent that aren't accepted yet (still sitting in someone's inbox).
  if (!validUuid(uuid)) return json({ pending: [] });
  const list = await env.NOVA.list({ prefix: "req:" });
  const pending = [];
  for (const key of list.keys) {
    const parts = key.name.split(":"); // req:<to>:<from>
    if (parts[2] === uuid) {
      const toUuid = parts[1];
      const toName = (await env.NOVA.get(`name:${toUuid}`)) || "";
      pending.push({ toUuid, toName });
    }
  }
  return json({ pending });
}
