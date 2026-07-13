package dev.novaclient.core;

import dev.novaclient.core.module.ModuleRegistry;
import dev.novaclient.gui.ClickGui;
import dev.novaclient.input.InputBridge;
import dev.novaclient.render.GlFontRenderer;

/**
 * Client singleton: settings, theme, module registry, notifications and the click GUI.
 * Created lazily on the render thread by FrameHook.
 */
public final class NovaClient {

    private static NovaClient instance;

    public static synchronized NovaClient getInstance() {
        if (instance == null) instance = new NovaClient();
        return instance;
    }

    public final String clientName = System.getProperty("nova.clientName", "Nova Client");
    public final String clientVersion = System.getProperty("nova.clientVersion", "1.0.0");

    public final ClientSettings settings;
    public final ModuleRegistry modules = new ModuleRegistry();
    public final Notifications notifications = new Notifications();
    public GlFontRenderer font;
    public GlFontRenderer fontSmall;

    private ClickGui gui;
    private Theme theme;
    private int accent;
    private boolean openKeyWasDown;

    private NovaClient() {
        settings = ClientSettings.load();
        theme = Theme.byName(settings.theme);
        accent = Theme.parseAccent(settings.accentColor, 0xFF7C5CFF);
        Runtime.getRuntime().addShutdownHook(new Thread(new Runnable() {
            @Override
            public void run() {
                settings.save();
            }
        }, "NovaClient-SettingsSave"));
        System.out.println("[NovaClient] " + clientName + " v" + clientVersion + " initialized (0 modules registered).");
    }

    /** First frame with a GL context: build font atlases, greet the player. */
    public void initOnRenderThread() {
        font = new GlFontRenderer("Segoe UI", java.awt.Font.PLAIN, 17);
        font.init();
        fontSmall = new GlFontRenderer("Segoe UI", java.awt.Font.PLAIN, 14);
        fontSmall.init();
        gui = new ClickGui(this);
        notifications.push(clientName + " v" + clientVersion + " — press Right Shift for the menu");
    }

    public Theme getTheme() {
        return theme;
    }

    public void setTheme(Theme newTheme) {
        theme = newTheme;
        settings.theme = newTheme.name;
    }

    public int getAccent() {
        return accent;
    }

    public void setAccent(int argb) {
        accent = argb;
        settings.accentColor = Theme.accentToHex(argb);
    }

    public ClickGui getGui() {
        return gui;
    }

    /** Per-frame entry point (render thread, ortho pixel projection already set up). */
    public void onFrame(int width, int height) {
        handleToggleKey();

        if (InputBridge.isGuiOpen() || gui.isAnimating()) {
            gui.updateAndRender(width, height);
        }
        modules.onFrame(width, height);
        notifications.render(font, width, accent, theme);
    }

    private void handleToggleKey() {
        boolean down = InputBridge.realIsKeyDown(settings.openGuiKey);
        if (down && !openKeyWasDown) {
            if (InputBridge.isGuiOpen()) {
                if (!gui.isCapturingBind()) closeGui();
            } else if (InputBridge.realIsGrabbed()) {
                // Only open from normal gameplay (never over chat/inventory) so vanilla screens
                // keep exclusive input.
                openGui();
            }
        }
        openKeyWasDown = down;
    }

    public void openGui() {
        InputBridge.openGui();
        gui.onOpened();
        playClick();
    }

    public void closeGui() {
        InputBridge.closeGui();   // input + mouse grab return to Minecraft immediately
        gui.onClosed();           // the closing animation is purely visual
        settings.save();
        playClick();
    }

    public void playClick() {
        if (settings.clickSounds) Sounds.click(settings.clickVolume);
    }
}
