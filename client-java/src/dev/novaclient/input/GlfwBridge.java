package dev.novaclient.input;

import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.MethodType;

/**
 * Window-title branding for modern Minecraft (1.13+, LWJGL 3/GLFW). The instrumented
 * GLFW.glfwCreateWindow / glfwSetWindowTitle delegate here; the original title is kept but its
 * "Minecraft"/"Minecraft*" prefix becomes the client name ("Minecraft* 1.21.4 - Singleplayer"
 * → "Nova Client 1.21.4 - Singleplayer"). Cosmetic only — nothing else is touched.
 */
public final class GlfwBridge {

    private GlfwBridge() {
    }

    private static MethodHandle CREATE_WINDOW, SET_TITLE;
    private static boolean resolved;

    public static String brand(CharSequence original) {
        String name = System.getProperty("nova.clientName", "Nova Client");
        String s = original == null ? "" : original.toString();
        if (s.regionMatches(true, 0, "Minecraft", 0, 9)) {
            int i = 9;
            if (i < s.length() && s.charAt(i) == '*') i++;
            return name + s.substring(i);
        }
        return s.isEmpty() ? name : s;
    }

    private static void ensure() {
        if (resolved) return;
        try {
            MethodHandles.Lookup lookup = MethodHandles.publicLookup();
            Class<?> glfw = Class.forName("org.lwjgl.glfw.GLFW");
            CREATE_WINDOW = lookup.findStatic(glfw, "glfwCreateWindow$nova",
                    MethodType.methodType(long.class, int.class, int.class, CharSequence.class, long.class, long.class));
            SET_TITLE = lookup.findStatic(glfw, "glfwSetWindowTitle$nova",
                    MethodType.methodType(void.class, long.class, CharSequence.class));
            resolved = true;
        } catch (Throwable t) {
            throw new IllegalStateException("[NovaClient] Could not bind instrumented GLFW methods", t);
        }
    }

    public static long glfwCreateWindow(int width, int height, CharSequence title, long monitor, long share) {
        ensure();
        try {
            return (long) CREATE_WINDOW.invoke(width, height, (CharSequence) brand(title), monitor, share);
        } catch (Throwable t) {
            throw new RuntimeException(t);
        }
    }

    public static void glfwSetWindowTitle(long window, CharSequence title) {
        ensure();
        try {
            SET_TITLE.invoke(window, (CharSequence) brand(title));
        } catch (Throwable t) {
            throw new RuntimeException(t);
        }
    }
}
