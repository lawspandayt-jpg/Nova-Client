package dev.novaclient.core;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

import java.io.File;
import java.io.FileReader;
import java.io.FileWriter;
import java.io.Reader;
import java.io.Writer;
import java.util.HashMap;
import java.util.Map;

/**
 * In-game client settings, persisted to %AppData%\NovaClient\config\client-settings.json
 * (the directory comes from -Dnova.dir, set by the launcher). Saved when the GUI closes and on
 * JVM shutdown, so layout/theme/keybinds survive restarts.
 */
public final class ClientSettings {

    public int openGuiKey = 54;          // LWJGL KEY_RSHIFT
    public float guiScale = 1.0f;        // 0.75 – 1.5
    public float animationSpeed = 1.0f;  // 0.5 – 2.0
    public boolean clickSounds = true;
    public float clickVolume = 0.5f;     // 0 – 1
    public String theme = "Nova Dark";
    public String accentColor = "#7C5CFF";
    public Map<String, int[]> windowPositions = new HashMap<String, int[]>();
    public Map<String, Integer> moduleKeybinds = new HashMap<String, Integer>();

    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();

    public static File configFile() {
        String dir = System.getProperty("nova.dir");
        if (dir == null || dir.isEmpty()) {
            dir = new File(System.getenv("APPDATA"), "NovaClient").getAbsolutePath();
        }
        File configDir = new File(dir, "config");
        //noinspection ResultOfMethodCallIgnored
        configDir.mkdirs();
        return new File(configDir, "client-settings.json");
    }

    public static ClientSettings load() {
        File file = configFile();
        if (file.isFile()) {
            Reader reader = null;
            try {
                reader = new FileReader(file);
                ClientSettings settings = GSON.fromJson(reader, ClientSettings.class);
                if (settings != null) return settings.sanitized();
            } catch (Exception e) {
                System.err.println("[NovaClient] Could not read client-settings.json, using defaults: " + e);
            } finally {
                closeQuietly(reader);
            }
        }
        ClientSettings defaults = new ClientSettings();
        String accent = System.getProperty("nova.accentColor");
        if (accent != null && accent.matches("#[0-9a-fA-F]{6}")) defaults.accentColor = accent;
        return defaults;
    }

    public void save() {
        Writer writer = null;
        try {
            writer = new FileWriter(configFile());
            GSON.toJson(this, writer);
        } catch (Exception e) {
            System.err.println("[NovaClient] Could not save client-settings.json: " + e);
        } finally {
            closeQuietly(writer);
        }
    }

    private ClientSettings sanitized() {
        if (guiScale < 0.75f || guiScale > 1.5f) guiScale = 1.0f;
        if (animationSpeed < 0.5f || animationSpeed > 2.0f) animationSpeed = 1.0f;
        if (clickVolume < 0f || clickVolume > 1f) clickVolume = 0.5f;
        if (openGuiKey <= 0 || openGuiKey > 255) openGuiKey = 54;
        if (accentColor == null || !accentColor.matches("#[0-9a-fA-F]{6}")) accentColor = "#7C5CFF";
        if (theme == null) theme = "Nova Dark";
        if (windowPositions == null) windowPositions = new HashMap<String, int[]>();
        if (moduleKeybinds == null) moduleKeybinds = new HashMap<String, Integer>();
        return this;
    }

    private static void closeQuietly(java.io.Closeable closeable) {
        if (closeable != null) {
            try {
                closeable.close();
            } catch (Exception ignored) {
            }
        }
    }
}
