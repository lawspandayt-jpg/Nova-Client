package dev.novaclient.bootstrap;

import net.minecraft.launchwrapper.ITweaker;
import net.minecraft.launchwrapper.Launch;
import net.minecraft.launchwrapper.LaunchClassLoader;

import java.io.File;
import java.util.ArrayList;
import java.util.List;

/**
 * LaunchWrapper tweaker. All LWJGL instrumentation happens in the java agent ({@link NovaAgent});
 * this tweaker only supplies the launch target and (when it is the primary tweaker, i.e. OptiFine
 * is not installed) reconstructs the game arguments the way vanilla expects.
 */
public final class NovaTweaker implements ITweaker {

    private final List<String> launchArguments = new ArrayList<String>();

    @Override
    public void acceptOptions(List<String> args, File gameDir, File assetsDir, String profile) {
        launchArguments.addAll(args);
        if (!args.contains("--version") && profile != null) {
            launchArguments.add("--version");
            launchArguments.add(profile);
        }
        if (!args.contains("--gameDir") && gameDir != null) {
            launchArguments.add("--gameDir");
            launchArguments.add(gameDir.getAbsolutePath());
        }
        if (!args.contains("--assetsDir") && assetsDir != null) {
            launchArguments.add("--assetsDir");
            launchArguments.add(assetsDir.getAbsolutePath());
        }
    }

    @Override
    public void injectIntoClassLoader(LaunchClassLoader classLoader) {
        System.out.println("[NovaClient] Tweaker active (transformations are handled by the java agent).");
    }

    @Override
    public String getLaunchTarget() {
        return "net.minecraft.client.main.Main";
    }

    @Override
    public String[] getLaunchArguments() {
        // LaunchWrapper concatenates every tweaker's arguments. When OptiFine's tweaker is present
        // it already returns the full argument list, so returning ours too would duplicate them.
        if (isOptiFinePresent()) {
            return new String[0];
        }
        return launchArguments.toArray(new String[0]);
    }

    private static boolean isOptiFinePresent() {
        try {
            Object tweaks = Launch.blackboard.get("Tweaks");
            if (tweaks instanceof List<?>) {
                for (Object tweaker : (List<?>) tweaks) {
                    if (tweaker != null && tweaker.getClass().getName().toLowerCase().contains("optifine")) {
                        return true;
                    }
                }
            }
        } catch (Throwable ignored) {
        }
        return false;
    }
}
