package dev.novaclient.render;

import dev.novaclient.core.NovaClient;
import org.lwjgl.opengl.Display;
import org.lwjgl.opengl.GL11;

/**
 * Called once per frame from the instrumented Display.update(), on the render thread, after the
 * game has finished drawing and before the buffer swap. Sets up a pixel-space orthographic
 * projection, renders the client overlay, and restores ALL GL state so the game (and OptiFine)
 * are unaffected.
 */
public final class FrameHook {

    private static boolean initialized;
    private static boolean failed;

    private FrameHook() {
    }

    public static void onFrame() {
        if (failed || !Display.isCreated()) return;
        try {
            if (!initialized) {
                NovaClient.getInstance().initOnRenderThread();
                initialized = true;
            }

            int width = Display.getWidth();
            int height = Display.getHeight();

            GL11.glPushAttrib(GL11.GL_ALL_ATTRIB_BITS);
            GL11.glMatrixMode(GL11.GL_PROJECTION);
            GL11.glPushMatrix();
            GL11.glLoadIdentity();
            GL11.glOrtho(0, width, height, 0, -1, 1); // origin top-left, pixel units
            GL11.glMatrixMode(GL11.GL_MODELVIEW);
            GL11.glPushMatrix();
            GL11.glLoadIdentity();

            GL11.glDisable(GL11.GL_DEPTH_TEST);
            GL11.glDisable(GL11.GL_LIGHTING);
            GL11.glDisable(GL11.GL_CULL_FACE);
            GL11.glDisable(GL11.GL_ALPHA_TEST);
            GL11.glEnable(GL11.GL_BLEND);
            GL11.glBlendFunc(GL11.GL_SRC_ALPHA, GL11.GL_ONE_MINUS_SRC_ALPHA);
            GL11.glEnable(GL11.GL_TEXTURE_2D);
            GL11.glBindTexture(GL11.GL_TEXTURE_2D, 0);

            NovaClient.getInstance().onFrame(width, height);

            GL11.glMatrixMode(GL11.GL_PROJECTION);
            GL11.glPopMatrix();
            GL11.glMatrixMode(GL11.GL_MODELVIEW);
            GL11.glPopMatrix();
            GL11.glPopAttrib();
        } catch (Throwable t) {
            failed = true; // never risk crashing the game — disable the overlay instead
            System.err.println("[NovaClient] Overlay disabled after render error:");
            t.printStackTrace();
        }
    }
}
