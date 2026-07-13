package dev.novaclient.core;

import dev.novaclient.render.Draw;
import dev.novaclient.render.GlFontRenderer;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

/** Small toast notifications rendered top-right (e.g. "Nova Client loaded"). */
public final class Notifications {

    private static final class Toast {
        final String text;
        final long shownAt;

        Toast(String text, long shownAt) {
            this.text = text;
            this.shownAt = shownAt;
        }
    }

    private static final long DURATION_MS = 3200;
    private static final long FADE_MS = 350;

    private final List<Toast> toasts = new ArrayList<Toast>();

    public void push(String text) {
        synchronized (toasts) {
            toasts.add(new Toast(text, System.currentTimeMillis()));
            if (toasts.size() > 5) toasts.remove(0);
        }
    }

    public void render(GlFontRenderer font, int screenWidth, int accent, Theme theme) {
        long now = System.currentTimeMillis();
        float y = 12;
        synchronized (toasts) {
            Iterator<Toast> iterator = toasts.iterator();
            while (iterator.hasNext()) {
                Toast toast = iterator.next();
                long age = now - toast.shownAt;
                if (age > DURATION_MS) {
                    iterator.remove();
                    continue;
                }
                float alpha = 1f;
                if (age < FADE_MS) alpha = age / (float) FADE_MS;
                else if (age > DURATION_MS - FADE_MS) alpha = (DURATION_MS - age) / (float) FADE_MS;

                float width = font.width(toast.text) + 26;
                float height = font.getHeight() + 12;
                float x = screenWidth - width - 12;
                Draw.roundedRect(x, y, width, height, 7, Draw.withAlpha(theme.background, alpha * 0.95f));
                Draw.rect(x, y + 3, 3, height - 6, Draw.withAlpha(accent, alpha));
                font.draw(toast.text, x + 14, y + 6, Draw.withAlpha(theme.text, alpha));
                y += height + 8;
            }
        }
    }
}
