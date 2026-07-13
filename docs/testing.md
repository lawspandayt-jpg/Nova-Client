# Testing

## Automated (74 tests, all passing — `dotnet test NovaClient.sln`)

- **Log redaction**: MSA access/refresh tokens, JWTs, Bearer headers, XBL3.0 identity tokens,
  OAuth codes, PKCE verifiers, device codes, cookies — proven absent from output; harmless text
  proven untouched.
- **OAuth/PKCE**: S256 challenge correctness, RFC 7636 verifier length, per-attempt uniqueness,
  authorize-URL parameters (and proven absence of any client secret), code extraction, state
  mismatch rejection, user-cancel mapping, user message for every error kind.
- **Secure storage**: DPAPI round-trip, ciphertext-on-disk check, delete.
- **Settings**: save/load round-trip, corrupted-file recovery, RAM clamping (below min, above
  physical-minus-reserve, small systems).
- **Versions/launch**: manifest library OS rules, Maven path layout, parent/child version merge
  order, full argument build (identity substitution, `-javaagent`, `-D` props, JVM-args-before-
  main-class ordering, width/height), access-token redaction in the logged command line.
- **Java**: version string parsing for both `1.8.0_x` and modern schemes.
- **OptiFine**: accepts a correct 1.8.9 jar (edition parsed), rejects wrong MC version, non-
  OptiFine jars, corrupted files, missing files; install + re-detect round-trip.
- **Updater**: newer/equal/older semver decisions, HTTPS-only enforcement, path-traversal
  rejection, dev-manifest generation with real SHA-256.
- **Nova version JSON**: tweaker ordering (OptiFine before Nova), OptiFine fully absent when not
  installed, library lists.
- **Paths**: all 12 data folders created.

## Manual — performed during this build

- Clean first run of the published `NovaClient.exe`: window opens (980×640), dark login screen,
  no dispatcher errors, launcher log created with redaction active, `%AppData%\NovaClient`
  folder tree created. Verified the placeholder client ID produces the correct guidance error.
- `client-java` compiles to Java 8 bytecode (major 52) and the jar carries the correct
  `Premain-Class` manifest.
- Installer compiles and installs/uninstalls per-user (no UAC).

## Manual — requires a real Microsoft account / Mojang-approved client ID (checklist)

Sign-in happy path · wrong email shape (validated inline) · cancel at Microsoft page · WebView2
removed → browser fallback · account without Minecraft → blocked with clear error · child
account · token refresh after expiry · Sign Out / Switch Account · full 1.8.9 download on a clean
machine · interrupted download resume (kill mid-download, relaunch) · corrupted file repair
(truncate a library, press Repair) · OptiFine wrong-version jar rejected · launch to main menu ·
multiplayer server join · Right Shift GUI open/close/Escape · keybind rebind · GUI scale/theme/
accent persistence across restarts · window drag positions persist · crash screen (kill the JVM)
· update apply + forced rollback (tamper a staged hash) · uninstall keeping/deleting user data.
