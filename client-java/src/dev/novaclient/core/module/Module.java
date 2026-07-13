package dev.novaclient.core.module;

/**
 * Base class for future client modules. This release ships with ZERO modules — no gameplay
 * features exist yet, and none may confer an unfair advantage when they are added.
 */
public abstract class Module {

    private final String name;
    private final String description;
    private final ModuleCategory category;
    private boolean enabled;
    private int keybind; // 0 = unbound (LWJGL key code)

    protected Module(String name, String description, ModuleCategory category) {
        this.name = name;
        this.description = description;
        this.category = category;
    }

    public String getName() {
        return name;
    }

    public String getDescription() {
        return description;
    }

    public ModuleCategory getCategory() {
        return category;
    }

    public boolean isEnabled() {
        return enabled;
    }

    public int getKeybind() {
        return keybind;
    }

    public void setKeybind(int keybind) {
        this.keybind = keybind;
    }

    public final void toggle() {
        setEnabled(!enabled);
    }

    public final void setEnabled(boolean enabled) {
        if (this.enabled == enabled) return;
        this.enabled = enabled;
        if (enabled) onEnable();
        else onDisable();
    }

    protected void onEnable() {
    }

    protected void onDisable() {
    }

    /** Called every frame while enabled (render-thread). */
    public void onFrame(int screenWidth, int screenHeight) {
    }
}
