# OptiFine setup guide

OptiFine's license forbids redistribution, so Nova Client never bundles or auto-downloads the jar.
Setup is a one-time, launcher-guided step:

1. Home screen → **OptiFine Setup** (shown automatically while OptiFine is missing — the Launch
   button stays disabled until OptiFine is configured).
2. Click **Open optifine.net/downloads** and download an official **Minecraft 1.8.9** build
   (recommended: `1.8.9_HD_U_M5`; the recommended edition is configurable in `branding.json`).
3. Click **Browse…** and select the downloaded jar.
4. The launcher validates it — real OptiFine tweaker + `Config` class present, readable zip, and
   the embedded version markers say **1.8.9**. Clear messages cover: wrong Minecraft version,
   corrupted file, missing file, not-OptiFine files.
5. Click **Install OptiFine**. The jar is copied to
   `%AppData%\NovaClient\libraries\optifine\OptiFine\<edition>\` and every future launch loads it
   automatically.

## How it loads (no Forge)

1.8.9 OptiFine jars contain complete classes and their own LaunchWrapper tweaker. The generated
`nova-1.8.9` version launches `net.minecraft.launchwrapper.Launch` with:

```
--tweakClass optifine.OptiFineTweaker --tweakClass dev.novaclient.bootstrap.NovaTweaker
```

OptiFine patches the game exactly as its own standalone installer would; Nova's tweaker/agent
never touches Minecraft classes, so all supported OptiFine functionality is preserved
(performance/video settings, zoom, dynamic lighting, custom skies, connected textures, shader
menu compatibility, fast render, animations, detail and quality settings).

To replace or remove OptiFine, use the OptiFine Setup screen again or delete the
`libraries\optifine` folder and press **Repair**.
