# Java runtime guide

Minecraft 1.8.9 runs best on (and Nova Client requires) **64-bit Java 8**.

## Detection

The launcher probes, in order: a manually selected `java.exe`, the managed runtime folder
(`%AppData%\NovaClient\runtime`), standard install locations (Oracle, Eclipse Adoptium, Zulu,
Microsoft — both Program Files and per-user), and `JAVA_HOME`. Every candidate is validated by
running `java -version`: the version string and bitness are parsed and shown in the UI.
32-bit Java and non-8 majors are flagged as incompatible with a clear warning, and the Launch
button stays disabled until a compatible runtime exists.

## One-click install

If no 64-bit Java 8 is found, the home screen offers **Install Java 8**, which:

1. Queries the official **Adoptium API** (`api.adoptium.net`) for the latest
   **Eclipse Temurin 8 JRE (Windows x64)** — a legally redistributable OpenJDK build.
2. Downloads it (with progress + speed) and verifies the **SHA-256** checksum published by
   Adoptium; a mismatch deletes the file and aborts.
3. Unpacks it into `%AppData%\NovaClient\runtime\` and re-validates by running it.

No other download source is ever used. Temurin is licensed GPLv2 with Classpath Exception —
see THIRD-PARTY-NOTICES.md.

## Manual selection

Settings → Java → **Browse…** to point at any `java.exe`; **Auto-detect** clears the override.
The selected executable is validated before it is used.
