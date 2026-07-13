# Third-party notices

Nova Client is not affiliated with Mojang, Microsoft, or OptiFine. "Minecraft" is a trademark of
Mojang Synergies AB.

## Distributed with / downloaded by the launcher

| Component | License | How obtained |
|---|---|---|
| .NET 8 runtime (self-contained publish) | MIT | bundled by `dotnet publish` |
| Microsoft.Web.WebView2 SDK | BSD-style (Microsoft) | NuGet |
| WebView2 Evergreen Runtime | Microsoft license | already present on Win10/11 |
| Eclipse Temurin (OpenJDK 8) | GPLv2 + Classpath Exception | downloaded from api.adoptium.net on user request |
| LaunchWrapper (net.minecraft:launchwrapper:1.12) | MIT-style (Mojang) | downloaded from libraries.minecraft.net |
| OW2 ASM 5.0.3 | BSD-3-Clause | downloaded from Maven Central |
| Gson 2.2.4 | Apache-2.0 | downloaded from libraries.minecraft.net |
| LWJGL 2 (compile-time only for client-java) | BSD-3-Clause | downloaded from libraries.minecraft.net |

## Not distributed

- **Minecraft game files** — downloaded from Mojang's official servers to the user's machine at
  install time, under the user's own Minecraft license. Never committed or repackaged.
- **OptiFine** — the user downloads it themselves from optifine.net; the launcher only validates
  and copies the user's file locally. OptiFine may not be redistributed.

## Licenses

Full license texts: ASM (BSD-3-Clause) — asm.ow2.io/license.html; Gson (Apache-2.0) —
apache.org/licenses/LICENSE-2.0; Temurin (GPLv2+CE) — adoptium.net/about; LWJGL (BSD-3-Clause) —
lwjgl.org/license; .NET (MIT) — github.com/dotnet/runtime/blob/main/LICENSE.TXT.
