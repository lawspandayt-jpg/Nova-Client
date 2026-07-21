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

    /** Replaces the 1.8.9 window/taskbar icon with the Nova logo (bundled in the jar). */
    private static void applyWindowIcon() {
        try
        {
            java.io.InputStream stream = FrameHook.class.getResourceAsStream("/dev/novaclient/logo.png");
            if (stream == null) return;
            java.awt.image.BufferedImage source = javax.imageio.ImageIO.read(stream);
            stream.close();
            if (source == null) return;
            Display.setIcon(new java.nio.ByteBuffer[] { toRgba(source, 16), toRgba(source, 32) });
        }
        catch (Throwable ignored)
        {
            // Icon is cosmetic — never let it break the game.
        }
    }

    private static java.nio.ByteBuffer toRgba(java.awt.image.BufferedImage source, int size)
    {
        java.awt.image.BufferedImage scaled =
                new java.awt.image.BufferedImage(size, size, java.awt.image.BufferedImage.TYPE_INT_ARGB);
        java.awt.Graphics2D g = scaled.createGraphics();
        g.setRenderingHint(java.awt.RenderingHints.KEY_INTERPOLATION,
                java.awt.RenderingHints.VALUE_INTERPOLATION_BILINEAR);
        g.drawImage(source, 0, 0, size, size, null);
        g.dispose();
        java.nio.ByteBuffer buffer = org.lwjgl.BufferUtils.createByteBuffer(size * size * 4);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int pixel = scaled.getRGB(x, y);
                buffer.put((byte) ((pixel >> 16) & 0xFF));
                buffer.put((byte) ((pixel >> 8) & 0xFF));
                buffer.put((byte) (pixel & 0xFF));
                buffer.put((byte) ((pixel >>> 24) & 0xFF));
            }
        }
        buffer.flip();
        return buffer;
    }

    public static void onFrame() {
        if (failed || !Display.isCreated()) return;
        try {
            if (!initialized) {
                NovaClient.getInstance().initOnRenderThread();
                applyWindowIcon();
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
