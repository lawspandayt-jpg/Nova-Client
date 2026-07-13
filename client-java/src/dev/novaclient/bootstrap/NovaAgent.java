package dev.novaclient.bootstrap;

import java.lang.instrument.Instrumentation;

/**
 * Java agent entry point (attached by the launcher with -javaagent:nova-client.jar).
 *
 * The agent registers a single {@link LwjglTransformer} that instruments three STABLE LWJGL 2
 * classes (Display, Keyboard, Mouse). It never touches Minecraft's own (obfuscated) classes,
 * never modifies packets, and changes no gameplay behavior — it only adds a per-frame render
 * callback and an input gate for the client GUI overlay.
 *
 * An agent is used (rather than a LaunchWrapper IClassTransformer) because LaunchWrapper's
 * class loader deliberately excludes org.lwjgl.* from transformation.
 */
public final class NovaAgent {

    private NovaAgent() {
    }

    public static void premain(String agentArgs, Instrumentation instrumentation) {
        System.out.println("[NovaClient] Agent attached — instrumenting LWJGL Display/Keyboard/Mouse.");
        instrumentation.addTransformer(new LwjglTransformer(), false);
    }
}
