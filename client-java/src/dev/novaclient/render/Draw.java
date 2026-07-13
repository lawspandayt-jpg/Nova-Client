package dev.novaclient.render;

import org.lwjgl.opengl.GL11;

/**
 * Minimal immediate-mode drawing helpers for the overlay. Colors are 0xAARRGGBB.
 * All original rendering — no Minecraft or third-party client code involved.
 */
public final class Draw {

    private Draw() {
    }

    public static void color(int argb) {
        GL11.glColor4f(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f,
                (argb & 0xFF) / 255f, ((argb >>> 24) & 0xFF) / 255f);
    }

    public static void rect(float x, float y, float w, float h, int argb) {
        GL11.glDisable(GL11.GL_TEXTURE_2D);
        color(argb);
        GL11.glBegin(GL11.GL_QUADS);
        GL11.glVertex2f(x, y);
        GL11.glVertex2f(x, y + h);
        GL11.glVertex2f(x + w, y + h);
        GL11.glVertex2f(x + w, y);
        GL11.glEnd();
        GL11.glEnable(GL11.GL_TEXTURE_2D);
    }

    public static void roundedRect(float x, float y, float w, float h, float radius, int argb) {
        float r = Math.min(radius, Math.min(w, h) / 2f);
        GL11.glDisable(GL11.GL_TEXTURE_2D);
        color(argb);
        GL11.glBegin(GL11.GL_POLYGON);
        arcVertices(x + r, y + r, r, 180, 270);
        arcVertices(x + w - r, y + r, r, 270, 360);
        arcVertices(x + w - r, y + h - r, r, 0, 90);
        arcVertices(x + r, y + h - r, r, 90, 180);
        GL11.glEnd();
        GL11.glEnable(GL11.GL_TEXTURE_2D);
    }

    public static void roundedRectOutline(float x, float y, float w, float h, float radius, float thickness, int argb) {
        float r = Math.min(radius, Math.min(w, h) / 2f);
        GL11.glDisable(GL11.GL_TEXTURE_2D);
        color(argb);
        GL11.glLineWidth(thickness);
        GL11.glBegin(GL11.GL_LINE_LOOP);
        arcVertices(x + r, y + r, r, 180, 270);
        arcVertices(x + w - r, y + r, r, 270, 360);
        arcVertices(x + w - r, y + h - r, r, 0, 90);
        arcVertices(x + r, y + h - r, r, 90, 180);
        GL11.glEnd();
        GL11.glLineWidth(1f);
        GL11.glEnable(GL11.GL_TEXTURE_2D);
    }

    private static void arcVertices(float cx, float cy, float r, int startDeg, int endDeg) {
        for (int angle = startDeg; angle <= endDeg; angle += 10) {
            double rad = Math.toRadians(angle);
            GL11.glVertex2d(cx + Math.cos(rad) * r, cy + Math.sin(rad) * r);
        }
    }

    public static void circle(float cx, float cy, float r, int argb) {
        GL11.glDisable(GL11.GL_TEXTURE_2D);
        color(argb);
        GL11.glBegin(GL11.GL_TRIANGLE_FAN);
        GL11.glVertex2f(cx, cy);
        for (int angle = 0; angle <= 360; angle += 10) {
            double rad = Math.toRadians(angle);
            GL11.glVertex2d(cx + Math.cos(rad) * r, cy + Math.sin(rad) * r);
        }
        GL11.glEnd();
        GL11.glEnable(GL11.GL_TEXTURE_2D);
    }

    public static void line(float x1, float y1, float x2, float y2, float thickness, int argb) {
        GL11.glDisable(GL11.GL_TEXTURE_2D);
        color(argb);
        GL11.glLineWidth(thickness);
        GL11.glBegin(GL11.GL_LINES);
        GL11.glVertex2f(x1, y1);
        GL11.glVertex2f(x2, y2);
        GL11.glEnd();
        GL11.glLineWidth(1f);
        GL11.glEnable(GL11.GL_TEXTURE_2D);
    }

    /** Simple vertical gradient. */
    public static void gradient(float x, float y, float w, float h, int topArgb, int bottomArgb) {
        GL11.glDisable(GL11.GL_TEXTURE_2D);
        GL11.glShadeModel(GL11.GL_SMOOTH);
        GL11.glBegin(GL11.GL_QUADS);
        color(topArgb);
        GL11.glVertex2f(x, y);
        GL11.glVertex2f(x + w, y);
        color(bottomArgb);
        GL11.glVertex2f(x + w, y + h);
        GL11.glVertex2f(x, y + h);
        GL11.glEnd();
        GL11.glShadeModel(GL11.GL_FLAT);
        GL11.glEnable(GL11.GL_TEXTURE_2D);
    }

    public static int withAlpha(int argb, float alpha) {
        int a = Math.round(((argb >>> 24) & 0xFF) * clamp01(alpha));
        return (a << 24) | (argb & 0xFFFFFF);
    }

    public static int lerpColor(int from, int to, float t) {
        t = clamp01(t);
        int a = lerp((from >>> 24) & 0xFF, (to >>> 24) & 0xFF, t);
        int r = lerp((from >> 16) & 0xFF, (to >> 16) & 0xFF, t);
        int g = lerp((from >> 8) & 0xFF, (to >> 8) & 0xFF, t);
        int b = lerp(from & 0xFF, to & 0xFF, t);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static int lerp(int from, int to, float t) {
        return Math.round(from + (to - from) * t);
    }

    public static float clamp01(float v) {
        return v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
