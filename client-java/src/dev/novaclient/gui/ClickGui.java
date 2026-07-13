package dev.novaclient.gui;

import dev.novaclient.core.NovaClient;
import dev.novaclient.core.Theme;
import dev.novaclient.core.module.Module;
import dev.novaclient.core.module.ModuleCategory;
import dev.novaclient.input.GuiInput;
import dev.novaclient.render.Draw;
import dev.novaclient.render.GlFontRenderer;
import org.lwjgl.input.Keyboard;
import org.lwjgl.input.Mouse;
import org.lwjgl.opengl.Display;
import org.lwjgl.opengl.GL11;

import java.util.ArrayList;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;

/**
 * The Right-Shift click GUI. Original design: one draggable window per category, a search bar,
 * and a Settings window with live controls (keybind, scale, theme, accent, sounds, animation
 * speed, layout reset). Fully functional with an empty module registry.
 */
public final class ClickGui {

    private static final float WINDOW_WIDTH = 214;
    private static final float HEADER_HEIGHT = 30;
    private static final float ROW_HEIGHT = 26;
    private static final float BODY_MAX_HEIGHT = 240;

    private final NovaClient client;
    private final Map<ModuleCategory, Window> windows = new EnumMap<ModuleCategory, Window>(ModuleCategory.class);

    private float openProgress;          // 0..1 animated
    private boolean open;                // logical state (matches InputBridge.isGuiOpen())
    private long lastFrameNanos;

    private String searchText = "";
    private boolean searchFocused;

    private boolean capturingBind;
    private Window draggingWindow;
    private float dragOffsetX, dragOffsetY;
    private String draggingSlider;       // control id being dragged

    private float mouseX, mouseY;        // in GUI units (already divided by scale)
    private final List<int[]> clicks = new ArrayList<int[]>();  // {x, y, button} press events
    private int wheelDelta;

    private String tooltipText;
    private long hoverStartMillis;
    private String hoverId = "";

    private static final class Window {
        float x, y;
        float scroll, scrollTarget;

        Window(float x, float y) {
            this.x = x;
            this.y = y;
        }
    }

    public ClickGui(NovaClient client) {
        this.client = client;
        float x = 24;
        for (ModuleCategory category : ModuleCategory.values()) {
            int[] saved = client.settings.windowPositions.get(category.name());
            windows.put(category, saved != null && saved.length == 2
                    ? new Window(saved[0], saved[1])
                    : new Window(x, 60));
            x += WINDOW_WIDTH + 18;
        }
    }

    // ------------------------------------------------------------------ lifecycle

    public void onOpened() {
        open = true;
        searchFocused = false;
        searchText = "";
    }

    public void onClosed() {
        open = false;
        capturingBind = false;
        draggingWindow = null;
        draggingSlider = null;
        persistLayout();
    }

    public boolean isAnimating() {
        return openProgress > 0.01f;
    }

    public boolean isCapturingBind() {
        return capturingBind;
    }

    private void persistLayout() {
        for (Map.Entry<ModuleCategory, Window> entry : windows.entrySet()) {
            client.settings.windowPositions.put(entry.getKey().name(),
                    new int[]{Math.round(entry.getValue().x), Math.round(entry.getValue().y)});
        }
    }

    // ------------------------------------------------------------------ per-frame

    public void updateAndRender(int screenWidth, int screenHeight) {
        // Animation timing
        long now = System.nanoTime();
        float dt = lastFrameNanos == 0 ? 0.016f : Math.min(0.1f, (now - lastFrameNanos) / 1_000_000_000f);
        lastFrameNanos = now;
        float speed = 7f * client.settings.animationSpeed;
        openProgress = open
                ? Math.min(1f, openProgress + dt * speed)
                : Math.max(0f, openProgress - dt * speed);
        if (!open && openProgress <= 0f) {
            lastFrameNanos = 0;
            return;
        }

        float scale = client.settings.guiScale;
        collectInput(scale, screenHeight);

        float eased = openProgress * openProgress * (3 - 2 * openProgress); // smoothstep
        float guiWidth = screenWidth / scale;
        float guiHeight = screenHeight / scale;

        GL11.glPushMatrix();
        GL11.glScalef(scale, scale, 1f);

        // Dimmed backdrop
        Draw.rect(0, 0, guiWidth, guiHeight, Draw.withAlpha(0xC0000000, eased * 0.45f));

        tooltipText = null;
        renderSearchBar(guiWidth, eased);
        for (ModuleCategory category : ModuleCategory.values()) {
            renderWindow(category, windows.get(category), eased, guiHeight);
        }
        renderTooltip(guiWidth, guiHeight);

        GL11.glPopMatrix();
        clicks.clear();
        wheelDelta = 0;
    }

    // ------------------------------------------------------------------ input

    private void collectInput(float scale, int screenHeight) {
        mouseX = Mouse.getX() / scale;
        mouseY = (screenHeight - 1 - Mouse.getY()) / scale;

        GuiInput.MouseEvent mouseEvent;
        while ((mouseEvent = GuiInput.pollMouse()) != null) {
            if (mouseEvent.wheel != 0) wheelDelta += mouseEvent.wheel;
            if (mouseEvent.button == 0) {
                if (mouseEvent.pressed) {
                    clicks.add(new int[]{
                            Math.round(mouseEvent.x / scale),
                            Math.round((screenHeight - 1 - mouseEvent.y) / scale), 0});
                } else {
                    if (draggingWindow != null) persistLayout();
                    draggingWindow = null;
                    draggingSlider = null;
                }
            }
        }

        GuiInput.KeyEvent keyEvent;
        while ((keyEvent = GuiInput.pollKey()) != null) {
            if (!keyEvent.pressed) continue;
            if (capturingBind) {
                if (keyEvent.key != Keyboard.KEY_ESCAPE) {
                    client.settings.openGuiKey = keyEvent.key;
                    client.notifications.push("Menu key set to " + Keyboard.getKeyName(keyEvent.key));
                }
                capturingBind = false;
                continue;
            }
            if (keyEvent.key == Keyboard.KEY_ESCAPE) {
                client.closeGui();
                continue;
            }
            if (searchFocused) {
                if (keyEvent.key == Keyboard.KEY_BACK && searchText.length() > 0) {
                    searchText = searchText.substring(0, searchText.length() - 1);
                } else if (keyEvent.key == Keyboard.KEY_RETURN) {
                    searchFocused = false;
                } else if (keyEvent.character >= ' ' && searchText.length() < 32) {
                    searchText += keyEvent.character;
                }
            }
        }

        // Window dragging follows the cursor
        if (draggingWindow != null) {
            draggingWindow.x = mouseX - dragOffsetX;
            draggingWindow.y = mouseY - dragOffsetY;
        }
    }

    private boolean clicked(float x, float y, float w, float h) {
        for (int[] click : clicks) {
            if (click[0] >= x && click[0] <= x + w && click[1] >= y && click[1] <= y + h) return true;
        }
        return false;
    }

    private boolean hovered(float x, float y, float w, float h) {
        return mouseX >= x && mouseX <= x + w && mouseY >= y && mouseY <= y + h;
    }

    private void hoverTooltip(String id, float x, float y, float w, float h, String text) {
        if (!hovered(x, y, w, h)) {
            if (hoverId.equals(id)) hoverId = "";
            return;
        }
        if (!hoverId.equals(id)) {
            hoverId = id;
            hoverStartMillis = System.currentTimeMillis();
        }
        if (System.currentTimeMillis() - hoverStartMillis > 550) tooltipText = text;
    }

    // ------------------------------------------------------------------ search bar

    private void renderSearchBar(float guiWidth, float alpha) {
        Theme theme = client.getTheme();
        GlFontRenderer font = client.font;
        float width = 260, height = 32;
        float x = (guiWidth - width) / 2f, y = 16;

        boolean hover = hovered(x, y, width, height);
        Draw.roundedRect(x, y, width, height, 9, Draw.withAlpha(theme.background, alpha));
        Draw.roundedRectOutline(x, y, width, height, 9, 1.2f,
                Draw.withAlpha(searchFocused ? client.getAccent() : theme.border, alpha));

        // magnifier icon
        int iconColor = Draw.withAlpha(theme.textDim, alpha);
        Draw.roundedRectOutline(x + 11, y + 9, 10, 10, 5, 1.4f, iconColor);
        Draw.line(x + 20, y + 19, x + 24, y + 23, 1.6f, iconColor);

        String shown = searchText.isEmpty() && !searchFocused ? "Search modules…" : searchText;
        int textColor = searchText.isEmpty() && !searchFocused ? theme.textDim : theme.text;
        font.draw(shown + (searchFocused && (System.currentTimeMillis() / 500) % 2 == 0 ? "_" : ""),
                x + 32, y + (height - font.getHeight()) / 2f, Draw.withAlpha(textColor, alpha));

        if (clicked(x, y, width, height)) {
            searchFocused = true;
            client.playClick();
        } else if (!clicks.isEmpty() && !hover) {
            searchFocused = false;
        }
    }

    // ------------------------------------------------------------------ windows

    private void renderWindow(ModuleCategory category, Window window, float alpha, float guiHeight) {
        Theme theme = client.getTheme();
        GlFontRenderer font = client.font;
        int accent = client.getAccent();

        List<Row> rows = buildRows(category);
        float contentHeight = 0;
        for (Row row : rows) contentHeight += row.height;
        float bodyHeight = Math.min(BODY_MAX_HEIGHT, Math.max(46, contentHeight + 12));
        float height = HEADER_HEIGHT + bodyHeight;

        // Keep on screen
        window.x = Math.max(0, Math.min(window.x, Display.getWidth() / client.settings.guiScale - WINDOW_WIDTH));
        window.y = Math.max(0, Math.min(window.y, guiHeight - height));

        float x = window.x, y = window.y;

        // Smooth scrolling
        float maxScroll = Math.max(0, contentHeight + 12 - bodyHeight);
        if (hovered(x, y, WINDOW_WIDTH, height) && wheelDelta != 0) {
            window.scrollTarget = Math.max(0, Math.min(maxScroll, window.scrollTarget - wheelDelta / 4f));
        }
        window.scrollTarget = Math.max(0, Math.min(maxScroll, window.scrollTarget));
        window.scroll += (window.scrollTarget - window.scroll) * 0.35f;

        // Frame
        Draw.roundedRect(x, y, WINDOW_WIDTH, height, 10, Draw.withAlpha(theme.background, alpha));
        Draw.roundedRectOutline(x, y, WINDOW_WIDTH, height, 10, 1.2f, Draw.withAlpha(theme.border, alpha));
        // Header
        Draw.roundedRect(x, y, WINDOW_WIDTH, HEADER_HEIGHT, 10, Draw.withAlpha(theme.header, alpha));
        Draw.rect(x, y + HEADER_HEIGHT - 4, WINDOW_WIDTH, 4, Draw.withAlpha(theme.header, alpha));
        Draw.rect(x + 10, y + HEADER_HEIGHT - 1.5f, WINDOW_WIDTH - 20, 1.5f, Draw.withAlpha(accent, alpha));

        drawCategoryIcon(category, x + 12, y + 8, Draw.withAlpha(accent, alpha));
        font.draw(category.displayName, x + 32, y + (HEADER_HEIGHT - font.getHeight()) / 2f,
                Draw.withAlpha(theme.text, alpha));

        // Header interactions: dragging
        if (clicked(x, y, WINDOW_WIDTH, HEADER_HEIGHT) && draggingWindow == null) {
            draggingWindow = window;
            dragOffsetX = mouseX - window.x;
            dragOffsetY = mouseY - window.y;
        }

        // Body (scissored)
        float scale = client.settings.guiScale;
        GL11.glEnable(GL11.GL_SCISSOR_TEST);
        GL11.glScissor(Math.round(x * scale), Math.round(Display.getHeight() - (y + height) * scale),
                Math.round(WINDOW_WIDTH * scale), Math.round(bodyHeight * scale));

        float rowY = y + HEADER_HEIGHT + 6 - window.scroll;
        for (Row row : rows) {
            if (rowY + row.height >= y + HEADER_HEIGHT && rowY <= y + height) {
                row.render(this, x, rowY, alpha);
            }
            rowY += row.height;
        }
        GL11.glDisable(GL11.GL_SCISSOR_TEST);
    }

    private void drawCategoryIcon(ModuleCategory category, float x, float y, int color) {
        switch (category) {
            case HUD:
                Draw.roundedRectOutline(x, y + 2, 13, 10, 2, 1.4f, color);
                Draw.line(x + 2.5f, y + 9, x + 10.5f, y + 9, 1.4f, color);
                break;
            case VISUAL:
                Draw.roundedRectOutline(x, y + 4, 14, 7, 3.5f, 1.4f, color);
                Draw.circle(x + 7, y + 7.5f, 2, color);
                break;
            case PERFORMANCE:
                Draw.line(x + 8, y + 1, x + 3, y + 8, 1.6f, color);
                Draw.line(x + 3, y + 8, x + 8, y + 8, 1.6f, color);
                Draw.line(x + 8, y + 8, x + 5, y + 14, 1.6f, color);
                break;
            case UTILITY:
                Draw.circle(x + 4, y + 4, 3.2f, color);
                Draw.line(x + 6, y + 6, x + 12, y + 12, 2.4f, color);
                break;
            case SETTINGS:
                Draw.circle(x + 7, y + 7, 5.4f, color);
                Draw.circle(x + 7, y + 7, 2.4f, 0xFF000000 | (client.getTheme().header & 0xFFFFFF));
                for (int i = 0; i < 4; i++) {
                    double angle = Math.toRadians(i * 90 + 45);
                    Draw.line((float) (x + 7 + Math.cos(angle) * 5), (float) (y + 7 + Math.sin(angle) * 5),
                            (float) (x + 7 + Math.cos(angle) * 7.4), (float) (y + 7 + Math.sin(angle) * 7.4), 1.8f, color);
                }
                break;
        }
    }

    // ------------------------------------------------------------------ rows

    private abstract static class Row {
        final float height;

        Row(float height) {
            this.height = height;
        }

        abstract void render(ClickGui gui, float windowX, float y, float alpha);
    }

    private List<Row> buildRows(ModuleCategory category) {
        List<Row> rows = new ArrayList<Row>();
        if (category == ModuleCategory.SETTINGS) {
            buildSettingsRows(rows);
            return rows;
        }

        List<Module> modules = searchText.trim().isEmpty()
                ? client.modules.byCategory(category)
                : filterByCategory(client.modules.search(searchText), category);

        if (modules.isEmpty()) {
            final String message = searchText.trim().isEmpty()
                    ? "No modules have been added to this category yet."
                    : "No modules match \"" + searchText.trim() + "\".";
            rows.add(new Row(52) {
                @Override
                void render(ClickGui gui, float windowX, float y, float alpha) {
                    gui.drawWrapped(message, windowX + 12, y + 6, WINDOW_WIDTH - 24,
                            Draw.withAlpha(gui.client.getTheme().textDim, alpha));
                }
            });
        }
        // (Future) module rows would be added here — none exist in this release.
        return rows;
    }

    private static List<Module> filterByCategory(List<Module> modules, ModuleCategory category) {
        List<Module> result = new ArrayList<Module>();
        for (Module module : modules) {
            if (module.getCategory() == category) result.add(module);
        }
        return result;
    }

    private void drawWrapped(String text, float x, float y, float maxWidth, int color) {
        GlFontRenderer font = client.fontSmall;
        String[] words = text.split(" ");
        StringBuilder line = new StringBuilder();
        float lineY = y;
        for (String word : words) {
            String candidate = line.length() == 0 ? word : line + " " + word;
            if (font.width(candidate) > maxWidth && line.length() > 0) {
                font.draw(line.toString(), x, lineY, color);
                lineY += font.getHeight();
                line = new StringBuilder(word);
            } else {
                line = new StringBuilder(candidate);
            }
        }
        if (line.length() > 0) font.draw(line.toString(), x, lineY, color);
    }

    // ------------------------------------------------------------------ settings rows

    private void buildSettingsRows(List<Row> rows) {
        rows.add(buttonRow("Menu Key", new Runnable() {
            @Override
            public void run() {
                capturingBind = true;
            }
        }, new ValueText() {
            @Override
            public String get() {
                return capturingBind ? "press a key…" : Keyboard.getKeyName(client.settings.openGuiKey);
            }
        }, "Rebind the key that opens this menu. Escape cancels."));

        rows.add(buttonRow("GUI Scale", new Runnable() {
            @Override
            public void run() {
                float scale = client.settings.guiScale;
                client.settings.guiScale = scale >= 1.5f ? 0.75f : scale + 0.25f;
            }
        }, new ValueText() {
            @Override
            public String get() {
                return String.format("%.2fx", client.settings.guiScale);
            }
        }, "Cycle the size of the client GUI."));

        rows.add(buttonRow("Theme", new Runnable() {
            @Override
            public void run() {
                client.setTheme(Theme.next(client.getTheme()));
            }
        }, new ValueText() {
            @Override
            public String get() {
                return client.getTheme().name;
            }
        }, "Cycle between the built-in dark themes."));

        rows.add(new Row(ROW_HEIGHT + 6) { // accent swatches
            @Override
            void render(ClickGui gui, float windowX, float y, float alpha) {
                Theme theme = gui.client.getTheme();
                gui.client.fontSmall.draw("Accent", windowX + 12, y + 6, Draw.withAlpha(theme.textDim, alpha));
                float swatchX = windowX + 74;
                for (int accentOption : Theme.ACCENTS) {
                    boolean active = gui.client.getAccent() == accentOption;
                    Draw.circle(swatchX + 7, y + 13, active ? 8 : 6, Draw.withAlpha(accentOption, alpha));
                    if (active) Draw.circle(swatchX + 7, y + 13, 2.4f, Draw.withAlpha(0xFFFFFFFF, alpha));
                    if (gui.clicked(swatchX, y + 5, 16, 16)) {
                        gui.client.setAccent(accentOption);
                        gui.client.playClick();
                    }
                    swatchX += 21;
                }
                gui.hoverTooltip("accent", windowX, y, WINDOW_WIDTH, ROW_HEIGHT, "Pick the accent color.");
            }
        });

        rows.add(buttonRow("Click Sounds", new Runnable() {
            @Override
            public void run() {
                client.settings.clickSounds = !client.settings.clickSounds;
            }
        }, new ValueText() {
            @Override
            public String get() {
                return client.settings.clickSounds ? "On" : "Off";
            }
        }, "Play a soft click when using the GUI."));

        rows.add(sliderRow("Volume", "volume", 0f, 1f, new SliderBinding() {
            @Override
            public float get() {
                return client.settings.clickVolume;
            }

            @Override
            public void set(float value) {
                client.settings.clickVolume = value;
            }
        }, "Click sound volume."));

        rows.add(sliderRow("Anim Speed", "animspeed", 0.5f, 2f, new SliderBinding() {
            @Override
            public float get() {
                return client.settings.animationSpeed;
            }

            @Override
            public void set(float value) {
                client.settings.animationSpeed = value;
            }
        }, "Speed of open/close and hover animations."));

        rows.add(buttonRow("Reset Layout", new Runnable() {
            @Override
            public void run() {
                float x = 24;
                for (ModuleCategory category : ModuleCategory.values()) {
                    Window window = windows.get(category);
                    window.x = x;
                    window.y = 60;
                    window.scrollTarget = 0;
                    x += WINDOW_WIDTH + 18;
                }
                persistLayout();
                client.notifications.push("GUI layout reset");
            }
        }, new ValueText() {
            @Override
            public String get() {
                return "";
            }
        }, "Move all windows back to their default positions."));

        rows.add(new Row(ROW_HEIGHT) { // version info
            @Override
            void render(ClickGui gui, float windowX, float y, float alpha) {
                gui.client.fontSmall.draw(gui.client.clientName + " v" + gui.client.clientVersion + " · MC 1.8.9",
                        windowX + 12, y + 6, Draw.withAlpha(gui.client.getTheme().textDim, alpha));
            }
        });
    }

    private interface ValueText {
        String get();
    }

    private interface SliderBinding {
        float get();

        void set(float value);
    }

    private Row buttonRow(final String label, final Runnable action, final ValueText value, final String tooltip) {
        return new Row(ROW_HEIGHT) {
            @Override
            void render(ClickGui gui, float windowX, float y, float alpha) {
                Theme theme = gui.client.getTheme();
                boolean hover = gui.hovered(windowX + 6, y, WINDOW_WIDTH - 12, ROW_HEIGHT - 4);
                int background = Draw.lerpColor(theme.panel, gui.client.getAccent(), hover ? 0.25f : 0f);
                Draw.roundedRect(windowX + 8, y, WINDOW_WIDTH - 16, ROW_HEIGHT - 5, 6, Draw.withAlpha(background, alpha));
                gui.client.fontSmall.draw(label, windowX + 16, y + 4, Draw.withAlpha(theme.text, alpha));
                String text = value.get();
                if (!text.isEmpty()) {
                    gui.client.fontSmall.draw(text, windowX + WINDOW_WIDTH - 16 - gui.client.fontSmall.width(text),
                            y + 4, Draw.withAlpha(gui.client.getAccent(), alpha));
                }
                if (gui.clicked(windowX + 8, y, WINDOW_WIDTH - 16, ROW_HEIGHT - 5)) {
                    action.run();
                    gui.client.playClick();
                }
                gui.hoverTooltip(label, windowX + 8, y, WINDOW_WIDTH - 16, ROW_HEIGHT - 5, tooltip);
            }
        };
    }

    private Row sliderRow(final String label, final String id, final float min, final float max,
                          final SliderBinding binding, final String tooltip) {
        return new Row(ROW_HEIGHT + 4) {
            @Override
            void render(ClickGui gui, float windowX, float y, float alpha) {
                Theme theme = gui.client.getTheme();
                gui.client.fontSmall.draw(label, windowX + 16, y + 1, Draw.withAlpha(theme.textDim, alpha));
                String valueText = String.format("%.2f", binding.get());
                gui.client.fontSmall.draw(valueText,
                        windowX + WINDOW_WIDTH - 16 - gui.client.fontSmall.width(valueText), y + 1,
                        Draw.withAlpha(theme.text, alpha));

                float trackX = windowX + 16, trackY = y + 18, trackW = WINDOW_WIDTH - 32;
                float fraction = (binding.get() - min) / (max - min);
                Draw.roundedRect(trackX, trackY, trackW, 4, 2, Draw.withAlpha(theme.panel, alpha));
                Draw.roundedRect(trackX, trackY, trackW * fraction, 4, 2, Draw.withAlpha(gui.client.getAccent(), alpha));
                Draw.circle(trackX + trackW * fraction, trackY + 2, 5, Draw.withAlpha(0xFFFFFFFF, alpha));

                if (gui.clicked(trackX - 6, trackY - 8, trackW + 12, 20)) gui.draggingSlider = id;
                if (id.equals(gui.draggingSlider)) {
                    float newFraction = Draw.clamp01((gui.mouseX - trackX) / trackW);
                    binding.set(min + newFraction * (max - min));
                }
                gui.hoverTooltip(id, trackX, y, trackW, ROW_HEIGHT, tooltip);
            }
        };
    }

    // ------------------------------------------------------------------ tooltip

    private void renderTooltip(float guiWidth, float guiHeight) {
        if (tooltipText == null) return;
        GlFontRenderer font = client.fontSmall;
        Theme theme = client.getTheme();
        float width = font.width(tooltipText) + 16;
        float height = font.getHeight() + 8;
        float x = Math.min(mouseX + 14, guiWidth - width - 4);
        float y = Math.min(mouseY + 18, guiHeight - height - 4);
        Draw.roundedRect(x, y, width, height, 6, 0xF0101218);
        Draw.roundedRectOutline(x, y, width, height, 6, 1f, theme.border);
        font.draw(tooltipText, x + 8, y + 4, theme.text);
    }
}
