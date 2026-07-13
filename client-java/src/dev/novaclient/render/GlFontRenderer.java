package dev.novaclient.render;

import org.lwjgl.BufferUtils;
import org.lwjgl.opengl.GL11;

import java.awt.Color;
import java.awt.Font;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.font.FontRenderContext;
import java.awt.geom.Rectangle2D;
import java.awt.image.BufferedImage;
import java.nio.ByteBuffer;

/**
 * Original OpenGL font renderer: rasterizes a system font (AWT) into a glyph atlas texture once,
 * then draws strings as textured quads. Anti-aliased, supports ASCII + Latin-1.
 */
public final class GlFontRenderer {

    private static final int FIRST_CHAR = 32;
    private static final int LAST_CHAR = 255;

    private final float[] glyphU = new float[LAST_CHAR + 1];
    private final float[] glyphV = new float[LAST_CHAR + 1];
    private final float[] glyphW = new float[LAST_CHAR + 1];
    private final float[] glyphH = new float[LAST_CHAR + 1];
    private final float[] glyphAdvance = new float[LAST_CHAR + 1];

    private int textureId = -1;
    private int atlasSize;
    private float lineHeight;
    private float ascent;
    private final Font font;

    public GlFontRenderer(String family, int style, int size) {
        this.font = new Font(family, style, size);
    }

    /** Must be called on the render thread with a current GL context. */
    public void init() {
        if (textureId != -1) return;

        atlasSize = 512;
        BufferedImage atlas = new BufferedImage(atlasSize, atlasSize, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = atlas.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS, RenderingHints.VALUE_FRACTIONALMETRICS_ON);
        g.setFont(font);
        g.setColor(Color.WHITE);

        FontRenderContext frc = g.getFontRenderContext();
        java.awt.FontMetrics metrics = g.getFontMetrics();
        ascent = metrics.getAscent();
        lineHeight = metrics.getHeight();

        int x = 1, y = 1;
        int rowHeight = metrics.getHeight() + 2;
        for (int c = FIRST_CHAR; c <= LAST_CHAR; c++) {
            String s = String.valueOf((char) c);
            Rectangle2D bounds = font.getStringBounds(s, frc);
            int w = Math.max(1, (int) Math.ceil(bounds.getWidth()) + 2);
            if (x + w >= atlasSize) {
                x = 1;
                y += rowHeight;
            }
            g.drawString(s, x, y + metrics.getAscent());
            glyphU[c] = x / (float) atlasSize;
            glyphV[c] = y / (float) atlasSize;
            glyphW[c] = w;
            glyphH[c] = rowHeight;
            glyphAdvance[c] = metrics.charWidth((char) c);
            x += w;
        }
        g.dispose();

        // Upload as RGBA
        int[] pixels = atlas.getRGB(0, 0, atlasSize, atlasSize, null, 0, atlasSize);
        ByteBuffer buffer = BufferUtils.createByteBuffer(atlasSize * atlasSize * 4);
        for (int pixel : pixels) {
            buffer.put((byte) ((pixel >> 16) & 0xFF));
            buffer.put((byte) ((pixel >> 8) & 0xFF));
            buffer.put((byte) (pixel & 0xFF));
            buffer.put((byte) ((pixel >>> 24) & 0xFF));
        }
        buffer.flip();

        textureId = GL11.glGenTextures();
        GL11.glBindTexture(GL11.GL_TEXTURE_2D, textureId);
        GL11.glTexParameteri(GL11.GL_TEXTURE_2D, GL11.GL_TEXTURE_MIN_FILTER, GL11.GL_LINEAR);
        GL11.glTexParameteri(GL11.GL_TEXTURE_2D, GL11.GL_TEXTURE_MAG_FILTER, GL11.GL_LINEAR);
        GL11.glTexImage2D(GL11.GL_TEXTURE_2D, 0, GL11.GL_RGBA8, atlasSize, atlasSize, 0,
                GL11.GL_RGBA, GL11.GL_UNSIGNED_BYTE, buffer);
    }

    public float getHeight() {
        return lineHeight;
    }

    public float width(String text) {
        float w = 0;
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if (c >= FIRST_CHAR && c <= LAST_CHAR) w += glyphAdvance[c];
        }
        return w;
    }

    public void draw(String text, float x, float y, int argb) {
        if (textureId == -1) return;
        GL11.glEnable(GL11.GL_TEXTURE_2D);
        GL11.glBindTexture(GL11.GL_TEXTURE_2D, textureId);
        Draw.color(argb);
        GL11.glBegin(GL11.GL_QUADS);
        float cx = x;
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if (c < FIRST_CHAR || c > LAST_CHAR) continue;
            float u0 = glyphU[c], v0 = glyphV[c];
            float u1 = u0 + glyphW[c] / atlasSize, v1 = v0 + glyphH[c] / atlasSize;
            float w = glyphW[c], h = glyphH[c];
            GL11.glTexCoord2f(u0, v0); GL11.glVertex2f(cx, y);
            GL11.glTexCoord2f(u0, v1); GL11.glVertex2f(cx, y + h);
            GL11.glTexCoord2f(u1, v1); GL11.glVertex2f(cx + w, y + h);
            GL11.glTexCoord2f(u1, v0); GL11.glVertex2f(cx + w, y);
            cx += glyphAdvance[c];
        }
        GL11.glEnd();
    }

    public void drawCentered(String text, float centerX, float y, int argb) {
        draw(text, centerX - width(text) / 2f, y, argb);
    }
}
