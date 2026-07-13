# Minecraft installation guide

Everything installs into `%AppData%\NovaClient` — the vanilla `.minecraft` folder is never read,
written, or damaged.

## What the launcher does on Prepare/Repair

1. Downloads Mojang's official version manifest (`piston-meta.mojang.com`) and locates **1.8.9**
   (a cached copy is used when offline).
2. Downloads the client jar, all Windows-applicable libraries, natives, the asset index and every
   asset object — from Mojang's servers only.
3. Verifies **SHA-1** for every file that publishes one; valid files are skipped, corrupted files
   are re-downloaded (up to 3 attempts), interrupted downloads resume from `.part` files via HTTP
   Range requests. Up to 8 parallel downloads with live progress + speed in the UI.
4. Extracts native libraries (excluding `META-INF`) into `natives\1.8.9`.
5. Downloads the bootstrap libraries (LaunchWrapper from Mojang's server, ASM from Maven Central)
   and deploys the bundled `nova-client.jar` into the local libraries folder.
6. Generates `versions\nova-1.8.9\nova-1.8.9.json` (inherits vanilla 1.8.9, adds the tweakers).

## Folder layout

```
%AppData%\NovaClient\
  assets\  libraries\  versions\  natives\  logs\  screenshots\  resourcepacks\
  config\ (launcher + client settings, DPAPI token store)  cache\  runtime\ (managed Java)
  client\  crash-reports\
```

## Launching

The launcher builds the classpath and 1.8.9-style arguments itself, passing your real username,
UUID, access token and `--userType msa`, plus `-javaagent:nova-client.jar` and the branding
`-Dnova.*` properties. Game output is streamed (token-redacted) to `logs\game-*.log`; crashes are
detected via exit code and fresh `crash-reports\*.txt`, and surfaced on the home screen with a
**Repair** button. RAM is clamped so more than (total physical − 2 GB) can never be allocated.

No copyrighted Minecraft files are in the repository or installer — everything is downloaded from
official servers at setup time.
